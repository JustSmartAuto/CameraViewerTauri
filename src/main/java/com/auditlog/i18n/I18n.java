package com.auditlog.i18n;

import java.text.MessageFormat;
import java.util.*;

/**
 * Minimal i18n wrapper over {@link ResourceBundle}.
 * Defaults to Simplified Chinese (zh_CN); falls back to English (en_US).
 *
 * <p>Usage: {@code I18n.t("btn.start")}, with optional varargs arguments
 *     for placeholders like {@code I18n.t("status.listening", 5140)}.</p>
 *
 * <p>Locale can be changed at runtime via {@link #setLocale(Locale)} — new
 *     lookups immediately use the new bundle (swapped-on-the-fly).</p>
 */
public final class I18n {

    /** Default locale — Simplified Chinese, as requested by product owner. */
    public static final Locale DEFAULT_LOCALE = Locale.SIMPLIFIED_CHINESE;
    /** Fallback locale when no translation is found. */
    public static final Locale FALLBACK_LOCALE = Locale.US;

    private static Locale current = DEFAULT_LOCALE;
    private static ResourceBundle bundle;

    static { reload(); }

    private I18n() {}

    /** Set the current locale and reload the bundle. */
    public static void setLocale(Locale loc) {
        current = loc != null ? loc : DEFAULT_LOCALE;
        reload();
    }

    public static Locale getLocale() { return current; }

    /** Reload bundles (needed if ResourceBundle.Control changed on disk). */
    public static synchronized void reload() {
        try {
            // ENGLISH is US locale (en_US). Resource name suffix automatically.
            bundle = ResourceBundle.getBundle("messages", current, new UTF8Control());
        } catch (MissingResourceException e) {
            bundle = ResourceBundle.getBundle("messages", FALLBACK_LOCALE, new UTF8Control());
        }
    }

    /**
     * Look up a translation for {@code key}. If not found, the key itself is returned
     * so the UI does not show "???". Placeholders {0}, {1} ... are substituted with
     * {@code args} per {@link MessageFormat}.
     */
    public static String t(String key, Object... args) {
        if (key == null) return "";
        String raw;
        try {
            raw = bundle.getString(key);
        } catch (MissingResourceException e) {
            return key; // return the key so layout does not collapse
        }
        if (args == null || args.length == 0) return raw;
        try {
            return new MessageFormat(raw, current).format(args);
        } catch (IllegalArgumentException e) {
            return raw;
        }
    }

    /** Supported display languages for the Settings dialog. */
    public static final String[][] SUPPORTED = {
            {"zh_CN", "简体中文"},
            {"en_US", "English"},
    };

    /** Simple UTF-8 capable Control for ResourceBundle (Java default is ISO-8859-1). */
    private static class UTF8Control extends ResourceBundle.Control {
        @Override
        public ResourceBundle newBundle(String baseName, Locale locale, String format, ClassLoader loader, boolean reload)
                throws IllegalAccessException, InstantiationException, java.io.IOException {
            String bundleName = toBundleName(baseName, locale);
            String resourceName = toResourceName(bundleName, "properties");
            java.io.InputStream stream = loader.getResourceAsStream(resourceName);
            if (stream == null) return null;
            try (stream) {
                return new PropertyResourceBundle(new java.io.InputStreamReader(stream, java.nio.charset.StandardCharsets.UTF_8));
            }
        }
    }
}
