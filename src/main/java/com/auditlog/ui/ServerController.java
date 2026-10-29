package com.auditlog.ui;

import com.auditlog.config.AppConfig;
import com.auditlog.db.AuditRow;
import com.auditlog.db.H2Store;
import com.auditlog.server.SyslogMessage;
import com.auditlog.server.SyslogServer;
import com.google.gson.Gson;

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

    private static final ServerController INSTANCE = new ServerController();

    public static ServerController get() { return INSTANCE; }

    private final AtomicReference<SyslogServer> server = new AtomicReference<>();
    private ScheduledExecutorService livePusher;
    private MainFrame frame;
    private H2Store store;
    private final Gson gson = new Gson();
    private final java.util.concurrent.ConcurrentLinkedQueue<SyslogMessage> inbound = new java.util.concurrent.ConcurrentLinkedQueue<>();

    public void bindFrame(MainFrame frame, H2Store store) {
        this.frame = frame;
        this.store = store;
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
        if (livePusher != null) {
            livePusher.shutdownNow();
            livePusher = null;
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

    private void flushInbound() {
        SyslogMessage m;
        int batch = 0;
        while ((m = inbound.poll()) != null && batch < 200) {
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
        // also refresh host list occasionally
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
