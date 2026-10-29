package com.auditlog.config;

import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Centralised logic for where the app puts its data files.
 *
 * <p>Resolution order (Windows):</p>
 * <ol>
 *   <li>{@code D:\JustSmart\CognexLoggingServer\config\app.properties} + {@code D:\JustSmart\CognexLoggingServer\db\auditlog}</li>
 *   <li>If D: does not exist → {@code %APPDATA%\CognexLoggingServer\config\app.properties} + {@code %APPDATA%\CognexLoggingServer\db\auditlog}</li>
 *   <li>On non-Windows platforms → falls back to {@code ~/.config/cognex-log-server/...}</li>
 * </ol>
 *
 * <p>Also honours the {@code COGNEX_LOG_HOME} system property / env var — if set, that
 *    directory is used directly, overriding both strategies above.</p>
 */
public final class PathsUtil {

    public static final String APP_NAME = "CognexLoggingServer";

    private PathsUtil() {}

    /** Base directory holding everything (config + db + future logs). */
    public static Path baseDir() {
        // 0) Explicit override via sys prop / env var
        String override = System.getProperty("cognex.log.home");
        if (override == null || override.isBlank()) override = System.getenv("COGNEX_LOG_HOME");
        if (override != null && !override.isBlank()) {
            return Path.of(override);
        }

        // 1) D:\JustSmart\CognexLoggingServer
        try {
            Path d = Path.of("D:\\JustSmart", APP_NAME);
            if (Files.exists(d.getRoot())) return d; // D: exists — use it
        } catch (Exception ignored) {}

        // 2) %APPDATA%\CognexLoggingServer (falls to Linux/Mac too, but that's rare)
        String appData = System.getenv("APPDATA");
        if (appData == null || appData.isBlank()) appData = System.getProperty("user.home");
        return Path.of(appData, APP_NAME);
    }

    /** Directory containing app.properties. */
    public static Path configDir() {
        Path d = baseDir().resolve("config");
        ensure(d);
        return d;
    }

    /** Location of app.properties. */
    public static Path configFile() {
        return configDir().resolve("app.properties");
    }

    /** Directory holding H2 MV / LOB files. */
    public static Path dbDir() {
        Path d = baseDir().resolve("db");
        ensure(d);
        return d;
    }

    /**
     * Value written to {@code h2DbPath} so H2 resolves the .mv.db inside dbDir().
     * Kept as a plain string because H2 JDBC URL accepts it directly.
     */
    public static String h2DbPathValue() {
        return dbDir().resolve("auditlog").toString();
    }

    private static void ensure(Path d) {
        try { Files.createDirectories(d); } catch (Exception ignored) {}
    }
}
