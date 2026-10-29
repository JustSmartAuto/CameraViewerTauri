package com.auditlog.ui;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.file.Path;

/**
 * Manages the Windows HKCU Run registry entry so that the application
 * can start silently at user login. No-op on non-Windows platforms.
 */
public final class WinAutostart {

    private static final Logger log = LoggerFactory.getLogger(WinAutostart.class);

    /** Registry key under HKCU that Windows reads at login. */
    private static final String RUN_KEY = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    /** Value name written for our app. Prefix so we can update/remove later. */
    private static final String VALUE_NAME = "AuditLogServer";

    private WinAutostart() {}

    /** True if we are actually on Windows (registry approach only works there). */
    public static boolean isWindows() {
        return System.getProperty("os.name", "").toLowerCase().contains("win");
    }

    /** Query whether the Run key contains our value. */
    public static boolean isEnabled() {
        if (!isWindows()) return false;
        try {
            // reg query returns exit 0 if found, 1 if not
            Process p = new ProcessBuilder("cmd.exe", "/c",
                    "reg", "query", RUN_KEY, "/v", VALUE_NAME)
                    .redirectErrorStream(true)
                    .start();
            p.waitFor();
            return p.exitValue() == 0;
        } catch (Exception e) {
            log.warn("reg query failed: {}", e.getMessage());
            return false;
        }
    }

    /**
     * Add (or update) the Run value. Uses javaw.exe with --autostart so
     * the UI is still hidden when booting — combined with minimizeToTray
     * the app will appear only in the tray on login.
     *
     * @param jarPath absolute path to the fat jar
     */
    public static void enable(Path jarPath) throws IOException, InterruptedException {
        if (!isWindows()) return;
        // Launch command: javaw -jar "abs-path.jar" --autostart
        // Use javaw so there is no console window
        String javaw = findJavaw();
        String cmd = "\"" + javaw + "\" -jar \"" + jarPath.toAbsolutePath() + "\" --autostart";

        Process p = new ProcessBuilder("cmd.exe", "/c",
                "reg", "add", RUN_KEY, "/v", VALUE_NAME,
                "/t", "REG_SZ", "/d", cmd, "/f")
                .inheritIO()
                .start();
        int code = p.waitFor();
        if (code != 0) {
            throw new IOException("reg add failed (exit=" + code + ") — are you running as restricted user?");
        }
        log.info("Autostart enabled: {}", cmd);
    }

    /** Remove our value from the Run key. */
    public static void disable() throws IOException, InterruptedException {
        if (!isWindows()) return;
        Process p = new ProcessBuilder("cmd.exe", "/c",
                "reg", "delete", RUN_KEY, "/v", VALUE_NAME, "/f")
                .inheritIO()
                .start();
        int code = p.waitFor();
        // exit 1 if value wasn't there — that's fine
        if (code != 0 && code != 1) {
            throw new IOException("reg delete failed (exit=" + code + ")");
        }
        log.info("Autostart disabled");
    }

    /** Resolve the javaw.exe that sits next to the running java.exe. */
    private static String findJavaw() {
        // java.home points to JDK/JRE root on both Zulu and Oracle builds
        String home = System.getProperty("java.home");
        Path p = Path.of(home, "bin", "javaw.exe");
        if (java.nio.file.Files.exists(p)) return p.toString();
        // Fall back to plain java — better than nothing (will show a console)
        Path javaP = Path.of(home, "bin", "java.exe");
        if (java.nio.file.Files.exists(javaP)) return javaP.toString();
        return "java";
    }
}
