package com.auditlog;

import com.auditlog.db.H2Store;
import com.auditlog.ui.ServerController;
import com.auditlog.ui.TrayManager;
import com.auditlog.util.SingleInstanceLock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * 统一的有序退出入口。
 *
 * <p>背景：此前所有退出入口（窗口关闭、托盘退出、菜单退出）都直接
 * {@code System.exit(0)}，JVM 立刻进入关闭流程，清理只在 shutdown hook 里
 * 异步执行 —— hook 还没停完服务器 / 写完队列，JVM 就已经退出，表现为：
 * <ul>
 *   <li>入站队列中尚未落库的日志丢失</li>
 *   <li>H2 文件没有正常 SHUTDOWN（可能留下残留状态或占用锁）</li>
 *   <li>退出后仍有隐藏的 javaw 残留进程</li>
 * </ul>
 *
 * <p>现在所有退出请求都走 {@link #exit(int)}：在独立线程里按
 * 「停服务器 → 冲刷队列入库 → 关闭 H2 → 释放单实例锁 → 卸载托盘」
 * 的顺序同步完成清理，最后才调用 {@code System.exit()}。
 * shutdown hook 只作兜底（Ctrl+C / 被系统结束），且会等待主动清理线程完成。
 */
public final class AppShutdown {

    private static final Logger log = LoggerFactory.getLogger(AppShutdown.class);

    private static final AtomicBoolean exitRequested = new AtomicBoolean();
    private static final AtomicBoolean cleaned = new AtomicBoolean();
    private static final CountDownLatch cleanupDone = new CountDownLatch(1);

    private static volatile Thread exitThread;
    private static volatile SingleInstanceLock lock;
    private static volatile H2Store store;
    private static volatile TrayManager tray;
    private static boolean hookInstalled;

    private AppShutdown() {}

    /** 抢到单实例锁后立即调用：登记锁并安装兜底 shutdown hook。 */
    public static synchronized void init(SingleInstanceLock instanceLock) {
        lock = instanceLock;
        if (!hookInstalled) {
            Runtime.getRuntime().addShutdownHook(new Thread(AppShutdown::onJvmShutdown, "app-exit-hook"));
            hookInstalled = true;
        }
    }

    public static void setStore(H2Store h2Store) { store = h2Store; }

    public static void setTray(TrayManager trayManager) { tray = trayManager; }

    /** 是否已经进入退出流程（用于忽略重复的关闭请求）。 */
    public static boolean isExitRequested() { return exitRequested.get(); }

    /**
     * 请求有序退出。线程安全且幂等：重复调用只有第一次生效。
     */
    public static void exit(int code) {
        if (!exitRequested.compareAndSet(false, true)) return;
        Thread t = new Thread(() -> {
            try {
                cleanup();
            } finally {
                System.exit(code);
            }
        }, "app-exit");
        t.setDaemon(false);
        exitThread = t;
        t.start();
        log.info("orderly exit requested");
    }

    /** 有序清理。幂等 —— 主动退出与 shutdown hook 都可能调用。 */
    private static void cleanup() {
        if (!cleaned.compareAndSet(false, true)) return;
        try {
            try {
                ServerController.get().stopAndFlush();
                log.info("server stopped, inbound queue flushed");
            } catch (Exception e) {
                log.warn("stop server failed: {}", e.getMessage());
            }

            H2Store s = store;
            store = null;
            if (s != null) {
                try {
                    s.close();
                    log.info("H2 store closed");
                } catch (Exception e) {
                    log.warn("close H2 failed: {}", e.getMessage());
                }
            }

            SingleInstanceLock l = lock;
            lock = null;
            if (l != null) {
                try {
                    l.close();
                    log.info("single-instance lock released");
                } catch (Exception e) {
                    log.warn("release lock failed: {}", e.getMessage());
                }
            }

            TrayManager t = tray;
            tray = null;
            if (t != null) {
                try {
                    t.uninstall();
                } catch (Exception e) {
                    log.warn("uninstall tray failed: {}", e.getMessage());
                }
            }
        } finally {
            cleanupDone.countDown();
        }
    }

    /**
     * JVM 兜底关闭（Ctrl+C、系统结束进程）。若有主动退出线程正在清理，
     * 等它完成（最多 20 秒）再返回，避免 System.exit 抢占导致清理被中断。
     */
    private static void onJvmShutdown() {
        Thread t = exitThread;
        if (t != null && t != Thread.currentThread()) {
            try {
                cleanupDone.await(20, TimeUnit.SECONDS);
            } catch (InterruptedException ignored) {}
            if (cleanupDone.getCount() == 0) return;
        }
        cleanup();
    }
}
