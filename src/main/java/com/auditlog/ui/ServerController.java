package com.auditlog.ui;

import com.auditlog.config.AppConfig;
import com.auditlog.db.AuditRow;
import com.auditlog.db.H2Store;
import com.auditlog.server.SyslogMessage;
import com.auditlog.server.SyslogServer;
import com.google.gson.Gson;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.swing.*;
import java.io.BufferedWriter;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Glue between the Swing UI, the Syslog server, and the H2 store.
 * Singleton so the MainFrame button handler can reach it without
 * passing references up/down.
 */
public class ServerController {

    private static final Logger log = LoggerFactory.getLogger(ServerController.class);

    private static final ServerController INSTANCE = new ServerController();

    public static ServerController get() { return INSTANCE; }

    private final AtomicReference<SyslogServer> server = new AtomicReference<>();
    private ScheduledExecutorService livePusher;
    private MainFrame frame;
    private H2Store store;
    private final Gson gson = new Gson();
    private final java.util.concurrent.ConcurrentLinkedQueue<SyslogMessage> inbound = new java.util.concurrent.ConcurrentLinkedQueue<>();
    private volatile boolean shuttingDown;

    public void bindFrame(MainFrame frame, H2Store store) {
        this.frame = frame;
        this.store = store;
    }

    /**
     * 相机拉取的审计日志走与 syslog 完全相同的入库 + 界面刷新链路。
     * 若服务器未启动（没有定时写库线程），这里按需拉起一个。
     */
    public void ingest(SyslogMessage m) {
        if (m == null) return;
        ensurePusher();
        inbound.offer(m);
    }

    private synchronized void ensurePusher() {
        if (shuttingDown) return;
        if (livePusher == null || livePusher.isShutdown()) {
            livePusher = Executors.newSingleThreadScheduledExecutor();
            livePusher.scheduleAtFixedRate(this::flushInbound, 250, 250, TimeUnit.MILLISECONDS);
        }
    }

    public synchronized void start(AppConfig config, H2Store store) throws Exception {
        this.store = store;
        SyslogServer s = new SyslogServer(config, msg -> {
            inbound.offer(msg);
        });
        s.start();
        server.set(s);

        // flush inbound queue every 250ms → DB + UI
        livePusher = Executors.newSingleThreadScheduledExecutor();
        livePusher.scheduleAtFixedRate(this::flushInbound, 250, 250, TimeUnit.MILLISECONDS);
    }

    public synchronized void stop() {
        SyslogServer s = server.getAndSet(null);
        if (s != null) s.stop();
        stopPusher(2);
    }

    /**
     * 退出流程专用：停服务器 → 等写库线程结束 → 把入站队列里剩余日志
     * 全部落库。必须在 {@code store.close()} 之前调用，否则退出时丢日志。
     */
    public synchronized void stopAndFlush() {
        shuttingDown = true;
        SyslogServer s = server.getAndSet(null);
        if (s != null) s.stop();
        stopPusher(5);
        int flushed = flushInbound(Integer.MAX_VALUE);
        if (flushed > 0) log.info("flushed {} pending log events on shutdown", flushed);
        if (!inbound.isEmpty()) log.warn("{} log events could not be persisted before exit", inbound.size());
    }

    /** 停止定时写库线程；超时未结束则强制中断。 */
    private void stopPusher(long awaitSeconds) {
        ScheduledExecutorService p = livePusher;
        livePusher = null;
        if (p == null) return;
        p.shutdown();
        try {
            if (!p.awaitTermination(awaitSeconds, TimeUnit.SECONDS)) {
                p.shutdownNow();
                p.awaitTermination(1, TimeUnit.SECONDS);
            }
        } catch (InterruptedException e) {
            p.shutdownNow();
            Thread.currentThread().interrupt();
        }
    }

    /** stop() + start() — called by the "重启服务器" button. */
    public synchronized void restart(AppConfig config, H2Store store) throws Exception {
        stop();
        start(config, store);
    }

    public boolean isRunning() {
        SyslogServer s = server.get();
        return s != null && s.isRunning();
    }

    private void flushInbound() { flushInbound(200); }

    /** 按批取队列写入 DB/UI，返回实际落库条数；maxBatch 为 Integer.MAX_VALUE 时全量冲刷。 */
    private int flushInbound(int maxBatch) {
        SyslogMessage m;
        int batch = 0;
        while (batch < maxBatch && (m = inbound.poll()) != null) {
            batch++;
            try {
                String sdJson = gson.toJson(m.structuredData());
                long id = store.insert(m, sdJson);
                if (frame != null) {
                    AuditRow row = new AuditRow(id,
                            m.timestamp() == null ? Instant.now() : m.timestamp(),
                            m.hostname(), m.severity(), m.severityName(),
                            m.appName(), m.procId(), m.msgId(),
                            m.facility(), m.sourceIp(), m.message(), sdJson);
                    frame.appendRow(row);
                }
            } catch (Exception ignored) {}
        }
        return batch;
    }

    public void exportJson(Path target) {
        try {
            long n = store.totalCount();
            int chunk = 10000;
            try (BufferedWriter w = Files.newBufferedWriter(target)) {
                w.write("[\n");
                boolean first = true;
                for (long off = 0; off < n; off += chunk) {
                    for (var row : store.query(null, null, null, (int) off, chunk)) {
                        if (!first) w.write(",\n");
                        first = false;
                        w.write(gson.toJson(row));
                    }
                }
                w.write("\n]\n");
            }
        } catch (IOException | java.sql.SQLException e) {
            JOptionPane.showMessageDialog(frame, "Export failed: " + e.getMessage());
        }
    }
}
