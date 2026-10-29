package com.auditlog.camera;

import com.auditlog.server.SyslogMessage;
import com.google.gson.JsonObject;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/**
 * 定时轮询各相机的 {@code /api/audit-log}，把新事件交给 {@link Consumer}（入库 + 刷新界面）。
 *
 * <p>增量策略：每台相机记住 {@code lastSync}（最新事件时间），下次带 {@code after} 参数；
 * 只有严格大于 {@code lastSync} 的事件才会被接收，避免边界重复。首次添加相机时拉全量历史。</p>
 */
public final class CameraPoller {

    private static final Logger log = LoggerFactory.getLogger(CameraPoller.class);

    private final CameraStore store;
    private final ScheduledExecutorService exec = Executors.newScheduledThreadPool(2, r -> {
        Thread t = new Thread(r, "camera-poller");
        t.setDaemon(true);
        return t;
    });
    private final List<CameraConfig> cameras = new CopyOnWriteArrayList<>();
    private final Map<String, Instant> nextDue = new ConcurrentHashMap<>();
    private Consumer<SyslogMessage> sink = m -> {};

    public CameraPoller(CameraStore store) {
        this.store = store;
    }

    /** 启动轮询（间隔 1 秒检查一次到期相机）。 */
    public synchronized void start(List<CameraConfig> initial, Consumer<SyslogMessage> sink) {
        this.sink = sink == null ? m -> {} : sink;
        reload(initial);
        exec.scheduleAtFixedRate(this::tick, 1, 1, TimeUnit.SECONDS);
    }

    /** 相机列表变化后调用（新增/编辑/删除）。 */
    public void reload(List<CameraConfig> list) {
        cameras.clear();
        if (list != null) cameras.addAll(list);
        nextDue.clear();
    }

    private void tick() {
        Instant now = Instant.now();
        for (CameraConfig c : cameras) {
            if (!c.enabled || c.ip == null || c.ip.isBlank()) continue;
            Instant due = nextDue.getOrDefault(c.id, Instant.MIN);
            if (now.isBefore(due)) continue;
            nextDue.put(c.id, now.plusSeconds(Math.max(5, c.pollSeconds)));
            exec.submit(() -> poll(c));
        }
    }

    /** 立即同步一次（不等待结果）。 */
    public void syncNow(CameraConfig c) {
        exec.submit(() -> poll(c));
    }

    private void poll(CameraConfig c) {
        synchronized (c) {
            try {
                List<JsonObject> entries = CameraClient.fetchAuditLog(c, c.lastSync);
                Instant last = CameraClient.parseTimestamp(c.lastSync);
                Instant max = last;
                int added = 0;
                for (JsonObject e : entries) {
                    Instant ts = CameraClient.parseTimestamp(CameraPoller.str(e, "timestamp"));
                    if (last != null && ts != null && !ts.isAfter(last)) continue;
                    sink.accept(CameraClient.toMessage(e, c.ip));
                    added++;
                    if (ts != null && (max == null || ts.isAfter(max))) max = ts;
                }
                if (max != null && !max.equals(last)) {
                    c.lastSync = max.toString();
                    store.save(cameras);
                }
                if (added > 0) log.info("相机 {} 拉取到 {} 条审计日志", c.label(), added);
            } catch (Exception e) {
                log.warn("相机 {} 拉取失败: {}", c.label(), e.getMessage());
            }
        }
    }

    public synchronized void stop() {
        exec.shutdownNow();
    }

    private static String str(JsonObject o, String key) {
        var el = o.get(key);
        return el == null || el.isJsonNull() ? "" : (el.isJsonPrimitive() ? el.getAsString() : el.toString());
    }
}
