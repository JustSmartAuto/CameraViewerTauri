package com.auditlog.util;

import com.auditlog.config.PathsUtil;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.channels.FileChannel;
import java.nio.channels.FileLock;
import java.nio.file.*;

/**
 * 跨进程单实例锁。
 *
 * <p>实现：在 {@link PathsUtil#baseDir()} 下创建一个 lock 文件（独占文件锁）。
 * 第一个实例调用 {@link #tryAcquire()} 成功拿到锁后，整个 JVM 持有。
 * 后续启动的 javaw.exe 调用 {@link #tryAcquire()} 会拿到 {@code null}，
 * 上层据此弹 "已有实例在运行" 提示并退出。
 *
 * <p>相比 JVM 内 Mutex，文件锁能覆盖以下场景：
 * <ul>
 *   <li>便携 Launcher 双击多次（修复 4 个 javaw 幽灵进程的根因）</li>
 *   <li>直接 {@code javaw -jar audit-log-server.jar} 启动多个实例</li>
 *   <li>Windows / Linux 进程间互斥行为一致</li>
 * </ul>
 */
public final class SingleInstanceLock implements AutoCloseable {

    private static final Logger log = LoggerFactory.getLogger(SingleInstanceLock.class);

    private static final String LOCK_FILE = "audit-log-server.lock";

    private FileChannel channel;
    private FileLock lock;

    private SingleInstanceLock(FileChannel channel, FileLock lock) {
        this.channel = channel;
        this.lock = lock;
    }

    /**
     * 尝试获取锁。返回 {@code null} 表示已有实例持有锁 —— 调用方应退出。
     * 返回非空 {@link SingleInstanceLock} 表示本进程拿到了唯一实例。
     */
    public static SingleInstanceLock tryAcquire() {
        Path lockPath = PathsUtil.baseDir().resolve(LOCK_FILE);
        try {
            Files.createDirectories(lockPath.getParent());
        } catch (IOException e) {
            log.warn("cannot ensure lock dir: {}", e.getMessage());
            return null;
        }
        try {
            FileChannel ch = FileChannel.open(lockPath,
                    StandardOpenOption.CREATE,
                    StandardOpenOption.WRITE);
            // tryLock（非阻塞）— 已被占则立刻返回 null
            FileLock lk = ch.tryLock();
            if (lk == null) {
                try { ch.close(); } catch (IOException ignored) {}
                log.info("another instance already holds {}", lockPath);
                return null;
            }
            // 成功持有 —— 写入 PID + 启动时间戳（调试信息）
            ch.truncate(0);
            ch.write(java.nio.ByteBuffer.wrap((
                    "pid=" + ProcessHandle.current().pid() + "\n" +
                    "started=" + java.time.Instant.now() + "\n"
            ).getBytes(java.nio.charset.StandardCharsets.UTF_8)));
            ch.force(true);
            log.info("single-instance lock acquired: {}", lockPath);
            return new SingleInstanceLock(ch, lk);
        } catch (IOException e) {
            log.warn("tryLock failed: {}", e.getMessage());
            return null;
        }
    }

    @Override
    public synchronized void close() {
        try {
            if (lock != null) { try { lock.release(); } catch (IOException ignored) {} }
            if (channel != null) { try { channel.close(); } catch (IOException ignored) {} }
        } finally {
            lock = null;
            channel = null;
        }
    }
}
