package com.auditlog.config;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

/**
 * Application configuration loaded from app.properties (or built-in defaults).
 */
public class AppConfig {

    public int syslogPort = 5140;
    public boolean tlsEnabled = false;
    public String tlsKeystorePath = "";
    public String tlsKeystorePassword = "";
    public String tlsTruststorePath = "";
    public String tlsTruststorePassword = "";
    public String h2DbPath = "./auditlog/auditlog";
    public int maxTableRows = 100_000;
    public String flatlafTheme = "FlatIntelliJLaf";

    public static AppConfig load(Path file) {
        AppConfig cfg = new AppConfig();
        if (file != null && Files.exists(file)) {
            try (var in = Files.newInputStream(file)) {
                var p = new Properties();
                p.load(in);
                cfg.syslogPort = parseInt(p, "syslog.port", cfg.syslogPort);
                cfg.tlsEnabled = Boolean.parseBoolean(p.getProperty("syslog.tls.enabled", String.valueOf(cfg.tlsEnabled)));
                cfg.tlsKeystorePath = p.getProperty("syslog.tls.keystore", cfg.tlsKeystorePath);
                cfg.tlsKeystorePassword = p.getProperty("syslog.tls.keystore.password", cfg.tlsKeystorePassword);
                cfg.tlsTruststorePath = p.getProperty("syslog.tls.truststore", cfg.tlsTruststorePath);
                cfg.tlsTruststorePassword = p.getProperty("syslog.tls.truststore.password", cfg.tlsTruststorePassword);
                cfg.h2DbPath = p.getProperty("h2.db.path", cfg.h2DbPath);
                cfg.maxTableRows = parseInt(p, "ui.max.rows", cfg.maxTableRows);
                cfg.flatlafTheme = p.getProperty("ui.theme", cfg.flatlafTheme);
            } catch (IOException e) {
                // use defaults
            }
        }
        return cfg;
    }

    private static int parseInt(Properties p, String key, int def) {
        try { return Integer.parseInt(p.getProperty(key, String.valueOf(def))); }
        catch (NumberFormatException e) { return def; }
    }
}
