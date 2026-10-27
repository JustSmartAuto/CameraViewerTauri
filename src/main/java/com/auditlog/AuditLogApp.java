package com.auditlog;

import com.auditlog.config.AppConfig;
import com.auditlog.db.H2Store;
import com.auditlog.ui.MainFrame;
import com.auditlog.ui.ServerController;
import com.formdev.flatlaf.FlatDarkLaf;
import com.formdev.flatlaf.FlatIntelliJLaf;
import com.formdev.flatlaf.FlatLaf;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.swing.*;
import java.nio.file.Path;

/** Application entry point. */
public final class AuditLogApp {

    private static final Logger log = LoggerFactory.getLogger(AuditLogApp.class);

    public static void main(String[] args) {
        // Load config
        Path cfgPath = Path.of("app.properties");
        AppConfig cfg = AppConfig.load(cfgPath);

        // Theme
        switch (cfg.flatlafTheme) {
            case "FlatDarkLaf" -> FlatDarkLaf.setup();
            case "FlatIntelliJLaf" -> FlatIntelliJLaf.setup();
            default -> FlatLaf.setup(new FlatIntelliJLaf());
        }

        // Open H2
        H2Store store;
        try {
            store = new H2Store(cfg.h2DbPath);
            store.open();
        } catch (Exception e) {
            JOptionPane.showMessageDialog(null, "Failed to open H2: " + e.getMessage(),
                    "AuditLog Server", JOptionPane.ERROR_MESSAGE);
            return;
        }

        // Frame
        try {
            UIManager.setLookAndFeel(UIManager.getLookAndFeel());
        } catch (Exception ignored) {}

        MainFrame frame = new MainFrame(cfg, store);
        ServerController.get().bindFrame(frame, store);

        // Auto-start server if asked
        if (args.length > 0 && "--autostart".equals(args[0])) {
            try { ServerController.get().start(cfg, store); }
            catch (Exception e) { log.warn("autostart failed: {}", e.getMessage()); }
        }

        SwingUtilities.invokeLater(frame::setVisible);

        // Shutdown hook
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            try { ServerController.get().stop(); store.close(); } catch (Exception ignored) {}
        }, "auditlog-shutdown"));
    }

    private AuditLogApp() {}
}
