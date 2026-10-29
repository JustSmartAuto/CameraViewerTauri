package com.auditlog;

import com.auditlog.config.AppConfig;
import com.auditlog.config.PathsUtil;
import com.auditlog.db.H2Store;
import com.auditlog.i18n.I18n;
import com.auditlog.ui.MainFrame;
import com.auditlog.ui.ServerController;
import com.auditlog.ui.TrayManager;
import com.auditlog.util.SingleInstanceLock;
import com.auditlog.util.TlsCertManager;
import com.formdev.flatlaf.FlatDarkLaf;
import com.formdev.flatlaf.FlatIntelliJLaf;
import com.formdev.flatlaf.FlatLaf;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.imageio.ImageIO;
import javax.swing.*;
import java.awt.*;
import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Path;

/** Application entry point. */
public final class AuditLogApp {

    private static final Logger log = LoggerFactory.getLogger(AuditLogApp.class);

    public static void main(String[] args) {
        SingleInstanceLock lock = SingleInstanceLock.tryAcquire();
        if (lock == null) {
            JOptionPane.showMessageDialog(null,
                    I18n.t("app.already-running"),
                    I18n.t("dlg.warning"),
                    JOptionPane.WARNING_MESSAGE);
            return;
        }
        // 登记单实例锁 + 安装兜底 shutdown hook（Ctrl+C / 系统结束进程时也能有序清理）
        AppShutdown.init(lock);

        // Load config from D:\JustSmart\CognexLoggingServer\config\app.properties
        // (or %APPDATA%\CognexLoggingServer\config\app.properties if D: absent).
        Path cfgPath = PathsUtil.configFile();
        AppConfig cfg = AppConfig.load(cfgPath);

        // ---- TLS 证书：首次启动自动在 config/tls 下生成 CA 公钥/私钥与服务器证书（PEM，无口令）----
        try {
            TlsCertManager.Paths tls = TlsCertManager.ensure(PathsUtil.configDir());
            if (cfg.tlsCertPath.isBlank() || cfg.tlsKeyPath.isBlank()) {
                cfg.tlsCertPath = tls.serverCert().toString();
                cfg.tlsKeyPath = tls.serverKey().toString();
                cfg.save(cfgPath);
            }
            log.info("相机端需导入的 CA 公钥: {}", tls.caCert());
        } catch (Exception e) {
            log.warn("生成/加载 TLS 证书失败: {}", e.getMessage());
        }

        // --- Locale: honour saved preference before any UI is built ---
        I18n.setLocale(cfg.asLocale());

        // Theme
        switch (cfg.flatlafTheme) {
            case "FlatDarkLaf" -> FlatDarkLaf.setup();
            case "FlatIntelliJLaf" -> FlatIntelliJLaf.setup();
            default -> FlatLaf.setup(new FlatIntelliJLaf());
        }

        // Load icon (packed inside the jar / available from root dir while in IDE)
        Image icon = loadIcon();

        // Open H2
        H2Store store;
        try {
            store = new H2Store(cfg.h2DbPath);
            store.open();
            AppShutdown.setStore(store);
        } catch (Exception e) {
            JOptionPane.showMessageDialog(null,
                    "Failed to open H2: " + e.getMessage(),
                    I18n.t("app.title"),
                    JOptionPane.ERROR_MESSAGE);
            return;
        }

        try {
            UIManager.setLookAndFeel(UIManager.getLookAndFeel());
        } catch (Exception ignored) {}

        MainFrame frame = new MainFrame(cfg, store, cfgPath);
        if (icon != null) {
            frame.setIconImage(icon);
            // Also update JVM default so child dialogs inherit the same icon
            try {
                Taskbar.getTaskbar().setIconImage(icon);
            } catch (UnsupportedOperationException ignored) {}
        }
        ServerController.get().bindFrame(frame, store);

        // Tray
        TrayManager tray = new TrayManager(frame, cfg, icon);
        frame.setTrayManager(tray);
        tray.install();
        AppShutdown.setTray(tray);

        // Auto-start server if either:
        //   a) the "启动软件后自动启动服务器" setting is true (default), or
        //   b) --autostart was passed (WinAutostart / tray menu explicit request).
        boolean autoStart = cfg.autoStartServer || (args.length > 0 && "--autostart".equals(args[0]));
        if (autoStart) {
            try {
                ServerController.get().start(cfg, store);
                // Mirror the same feedback the Start button would show
                SwingUtilities.invokeLater(() -> {
                    frame.appendWelcomeRow("welcome.started");
                    frame.refreshStartStopButton();
                });
            } catch (Exception e) {
                log.warn("autostart failed: {}", e.getMessage());
            }
        }

        SwingUtilities.invokeLater(tray::showAccordingToPreference);
    }

    /** Try icon from the packaged resource first, fall back to a root-dir file. */
    private static Image loadIcon() {
        // PNG (converted from data.ico) — JDK handles natively.
        // ICO fallback: extractor supports classic BMP-in-ICO and modern PNG-in-ICO.
        // candidates order: classpath first (packed inside fat jar), then filesystem (repo root when running from IDE).
        String[] candidates = {
                "/data.png",            // classpath — always present in fat jar
                "/data.ico",            // classpath — always present in fat jar
                "data.png",             // filesystem — legacy root
                "data.ico",             // filesystem — legacy root
                "icons/data.png",       // filesystem — new archive location
                "icons/data.ico",       // filesystem — new archive location
        };
        byte[] bytes = null;
        for (String loc : candidates) {
            try (InputStream in = AuditLogApp.class.getResourceAsStream(loc)) {
                if (in != null) { bytes = in.readAllBytes(); break; }
            } catch (Exception ignored) {}
            try {
                var p = Path.of(loc);
                if (java.nio.file.Files.exists(p)) { bytes = java.nio.file.Files.readAllBytes(p); break; }
            } catch (Exception ignored) {}
        }
        if (bytes == null) {
            log.warn("data.png / data.ico not found — icon will be OS default");
            return null;
        }
        try {
            // Try PNG first (JDK handles natively)
            BufferedImage img = ImageIO.read(new ByteArrayInputStream(bytes));
            if (img != null) return img;
            // Try ICO via manual extraction
            byte[] extracted = extractLargestImageFromIco(bytes);
            if (extracted != null) {
                img = ImageIO.read(new ByteArrayInputStream(extracted));
                if (img != null) return img;
            }
        } catch (Exception e) {
            log.warn("icon decode failed: {} — icon will be OS default", e.getMessage());
        }
        return null;
    }

    /**
     * Extract the largest embedded image from a valid ICO blob.
     * Returns either a full-fledged BMP (BITMAPFILEHEADER + BITMAPINFOHEADER + pixels)
     * or a PNG (returned verbatim). Returns null on header errors.
     */
    private static byte[] extractLargestImageFromIco(byte[] ico) {
        if (ico.length < 22) return null;
        if (ico[0] != 0 || ico[1] != 0 || (ico[2] & 0xFF) != 1) return null; // not ICO
        int count = (ico[4] & 0xFF) | ((ico[5] & 0xFF) << 8);
        int bestSize = -1, bestOff = -1, bestIdx = -1;
        for (int i = 0; i < count; i++) {
            int off = 6 + i * 16;
            if (off + 16 > ico.length) break;
            int size = (ico[off + 8] & 0xFF) | ((ico[off + 9] & 0xFF) << 8) |
                       ((ico[off + 10] & 0xFF) << 16) | ((ico[off + 11] & 0xFF) << 24);
            int foff = (ico[off + 12] & 0xFF) | ((ico[off + 13] & 0xFF) << 8) |
                       ((ico[off + 14] & 0xFF) << 16) | ((ico[off + 15] & 0xFF) << 24);
            if (size > bestSize) { bestSize = size; bestOff = foff; bestIdx = i; }
        }
        if (bestOff < 0 || bestOff + bestSize > ico.length) return null;
        byte[] raw = java.util.Arrays.copyOfRange(ico, bestOff, bestOff + bestSize);
        // Detect PNG vs BMP
        if (raw.length >= 8 && raw[0] == (byte) 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47) {
            return raw; // PNG-in-ICO — JDK ImageIO handles PNG natively
        }
        if (raw.length >= 40 && raw[0] == 0x28 && raw[1] == 0x00) {
            // BITMAPINFOHEADER (40 bytes) — prepend BITMAPFILEHEADER (14 bytes)
            byte[] full = new byte[14 + raw.length];
            full[0] = 'B'; full[1] = 'M';
            int total = 14 + raw.length;
            full[2] = (byte) total; full[3] = (byte) (total >> 8);
            full[4] = (byte) (total >> 16); full[5] = (byte) (total >> 24);
            // bytes 6..11 reserved
            // bytes 12..13 offset to pixels (= 14 + 40 = 54)
            full[10] = (byte) 54; full[11] = (byte) 0;
            System.arraycopy(raw, 0, full, 14, raw.length);
            return full;
        }
        return null;
    }

    private AuditLogApp() {}
}
