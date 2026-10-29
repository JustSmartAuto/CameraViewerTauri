package com.auditlog.config;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Locale;
import java.util.Properties;

/**
 * Application configuration loaded from app.properties (or built-in defaults).
 */
public class AppConfig {

    // ---- Network / Syslog ----
    public int syslogPort = 5140;
    public boolean tlsEnabled = false;
    public String tlsKeystorePath = "";
    public String tlsKeystorePassword = "";
    public String tlsTruststorePath = "";
    public String tlsTruststorePassword = "";

    // ---- Storage ----
    /** Default resolves to {@code D:\JustSmart\CognexLoggingServer\db\auditlog} (or %APPDATA% fallback). */
    public String h2DbPath = PathsUtil.h2DbPathValue();

    // ---- UI ----
    public int maxTableRows = 100_000;
    public String flatlafTheme = "FlatIntelliJLaf";
    /** Locale tag, e.g. "zh_CN" or "en_US". Default zh_CN. */
    public String locale = "zh_CN";

    // ---- Behavior ----
    /** Startup on Windows boot via HKCU Run registry key. Default false. */
    public boolean autostartOnBoot = false;
    /** Auto-minimize to system tray on launch. Default false. */
    public boolean minimizeToTray = false;
    /** Start the Syslog server automatically when the app launches. Default true. */
    public boolean autoStartServer = true;

    /** Convenience accessor — splits the stored tag back into a Locale. */
    public Locale asLocale() {
        String[] parts = locale.split("_");
        if (parts.length == 2) return new Locale(parts[0], parts[1]);
        return new Locale(locale);
    }

    public static AppConfig load(Path file) {
        AppConfig cfg = new AppConfig();
        if (file != null && Files.exists(file)) {
            try (InputStream in = Files.newInputStream(file)) {
                Properties p = new Properties();
                p.load(in);
                cfg.syslogPort         = parseInt(p, "syslog.port", cfg.syslogPort);
                cfg.tlsEnabled         = Boolean.parseBoolean(p.getProperty("syslog.tls.enabled", String.valueOf(cfg.tlsEnabled)));
                cfg.tlsKeystorePath    = p.getProperty("syslog.tls.keystore", cfg.tlsKeystorePath);
                cfg.tlsKeystorePassword = p.getProperty("syslog.tls.keystore.password", cfg.tlsKeystorePassword);
                cfg.tlsTruststorePath  = p.getProperty("syslog.tls.truststore", cfg.tlsTruststorePath);
                cfg.tlsTruststorePassword = p.getProperty("syslog.tls.truststore.password", cfg.tlsTruststorePassword);
                cfg.h2DbPath           = p.getProperty("h2.db.path", cfg.h2DbPath);
                cfg.maxTableRows       = parseInt(p, "ui.max.rows", cfg.maxTableRows);
                cfg.flatlafTheme       = p.getProperty("ui.theme", cfg.flatlafTheme);
                cfg.locale             = p.getProperty("ui.locale", cfg.locale);
                cfg.autostartOnBoot    = Boolean.parseBoolean(p.getProperty("behavior.autostart.boot", String.valueOf(cfg.autostartOnBoot)));
                cfg.minimizeToTray     = Boolean.parseBoolean(p.getProperty("behavior.minimize.tray", String.valueOf(cfg.minimizeToTray)));
                cfg.autoStartServer    = Boolean.parseBoolean(p.getProperty("behavior.auto-start-server", String.valueOf(cfg.autoStartServer)));
            } catch (IOException ignored) {
                // use defaults
            }
        }
        return cfg;
    }

    /** Save current values back to the properties file. Creates parent dirs if needed. */
    public void save(Path file) throws IOException {
        Properties p = new Properties();
        p.setProperty("syslog.port", String.valueOf(syslogPort));
        p.setProperty("syslog.tls.enabled", String.valueOf(tlsEnabled));
        p.setProperty("syslog.tls.keystore", tlsKeystorePath);
        p.setProperty("syslog.tls.keystore.password", tlsKeystorePassword);
        p.setProperty("syslog.tls.truststore", tlsTruststorePath);
        p.setProperty("syslog.tls.truststore.password", tlsTruststorePassword);
        p.setProperty("h2.db.path", h2DbPath);
        p.setProperty("ui.max.rows", String.valueOf(maxTableRows));
        p.setProperty("ui.theme", flatlafTheme);
        p.setProperty("ui.locale", locale);
        p.setProperty("behavior.autostart.boot", String.valueOf(autostartOnBoot));
        p.setProperty("behavior.minimize.tray", String.valueOf(minimizeToTray));
        p.setProperty("behavior.auto-start-server", String.valueOf(autoStartServer));

        if (file.getParent() != null) Files.createDirectories(file.getParent());
        try (OutputStream out = Files.newOutputStream(file)) {
            p.store(out, "Audit Log Server — auto-generated");
        }
    }

    private static int parseInt(Properties p, String key, int def) {
        try { return Integer.parseInt(p.getProperty(key, String.valueOf(def))); }
        catch (NumberFormatException e) { return def; }
    }
}
