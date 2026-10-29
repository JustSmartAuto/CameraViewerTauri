package com.auditlog.ui;

import com.auditlog.AppShutdown;
import com.auditlog.config.AppConfig;
import com.auditlog.i18n.I18n;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.swing.*;
import java.awt.*;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * System-tray integration. Wraps {@link SystemTray} so the app can:
 * <ul>
 *   <li>Show a tray icon (using the same window icon)</li>
 *   <li>Auto-hide the main frame on launch when {@code minimizeToTray=true}</li>
 *   <li>Restore the frame when the tray icon is double-clicked</li>
 * </ul>
 * Popup menu labels honour the current {@link I18n} locale and rebuild on
 * {@link #onLocaleChanged()}.
 */
public class TrayManager {

    private static final Logger log = LoggerFactory.getLogger(TrayManager.class);

    private final MainFrame frame;
    private final AppConfig config;
    private final Image icon;
    private TrayIcon trayIcon;
    private SystemTray tray;
    private boolean installed;

    public TrayManager(MainFrame frame, AppConfig config, Image icon) {
        this.frame = frame;
        this.config = config;
        this.icon = icon;
    }

    public static boolean isSupportedStatic() { return SystemTray.isSupported(); }
    public boolean isSupported() { return SystemTray.isSupported(); }

    /** Install the tray icon and menu. Idempotent — safe to call twice. */
    public synchronized void install() {
        if (installed || !isSupported()) return;
        tray = SystemTray.getSystemTray();

        Image img = icon != null ? icon : placeholderIcon();
        trayIcon = new TrayIcon(img, TRAY_TITLE_EN, buildMenu());
        trayIcon.setImageAutoSize(true);

        trayIcon.addActionListener(e -> SwingUtilities.invokeLater(this::restoreFrame));
        trayIcon.addMouseListener(new MouseAdapter() {
            @Override public void mouseClicked(MouseEvent e) {
                if (e.getClickCount() == 2) SwingUtilities.invokeLater(TrayManager.this::restoreFrame);
            }
        });

        try {
            tray.add(trayIcon);
            installed = true;
            log.info("Tray icon installed");
        } catch (AWTException e) {
            log.warn("Failed to install tray icon: {}", e.getMessage());
        }
    }

    public synchronized void uninstall() {
        if (installed && tray != null && trayIcon != null) {
            tray.remove(trayIcon);
            installed = false;
        }
    }

    public boolean isInstalled() { return installed; }

    /** Hide main frame into tray (just icon, no taskbar). */
    public void hideFrame() {
        if (installed) frame.setVisible(false);
    }

    /** Bring the main frame back on screen. */
    public void restoreFrame() {
        frame.setVisible(true);
        frame.setState(Frame.NORMAL);
        frame.toFront();
        frame.requestFocus();
    }

    /** Minimize to tray on first launch. */
    public void showAccordingToPreference() {
        if (config.minimizeToTray && installed) hideFrame();
        else frame.setVisible(true);
    }

    /**
     * Called when the user switches locale at runtime. The tray popup menu and
     * tooltip intentionally stay in English (see class-level comment), but we
     * rebuild to keep references fresh.
     */
    public synchronized void onLocaleChanged() {
        if (trayIcon != null) {
            trayIcon.setToolTip(TRAY_TITLE_EN);
            trayIcon.setPopupMenu(buildMenu());
        }
    }

    /**
     * Rebuild the tray popup menu. NOTE: Strings here are deliberately hard-coded
     * in English — Windows SystemTray uses a heavyweight native HWND popup that
     * sometimes fails to render CJK glyphs depending on JDK build / OS code page.
     * The tooltip also uses English for the same reason.
     */
    private static final String TRAY_TITLE_EN = "Cognex Camera Log Server";
    private static final String TRAY_SHOW_EN  = "Show / Display";
    private static final String TRAY_HIDE_EN  = "Hide";
    private static final String TRAY_EXIT_EN  = "Exit";

    private PopupMenu buildMenu() {
        PopupMenu m = new PopupMenu();

        MenuItem showItem = new MenuItem(TRAY_SHOW_EN);
        showItem.addActionListener(e -> SwingUtilities.invokeLater(this::restoreFrame));
        m.add(showItem);

        MenuItem hideItem = new MenuItem(TRAY_HIDE_EN);
        hideItem.addActionListener(e -> hideFrame());
        m.add(hideItem);

        m.addSeparator();

        MenuItem exitItem = new MenuItem(TRAY_EXIT_EN);
        exitItem.addActionListener(e -> {
            // The confirm dialog IS a Swing lightweight JOptionPane — safe to
            // localize, so we keep I18n.t() here (unlike the tray menu).
            int r = JOptionPane.showConfirmDialog(frame,
                    I18n.t("tray.exit.confirm"),
                    I18n.t("dlg.confirm"),
                    JOptionPane.YES_NO_OPTION, JOptionPane.QUESTION_MESSAGE);
            if (r == JOptionPane.YES_OPTION) {
                // 走统一的有序退出：先停服务器、冲刷日志、关库、释放锁，再结束进程
                frame.setVisible(false);
                AppShutdown.exit(0);
            }
        });
        m.add(exitItem);

        return m;
    }

    /** Bubble notification via tray. */
    public void notify(String title, String body) {
        if (!installed) return;
        trayIcon.displayMessage(title, body, TrayIcon.MessageType.INFO);
    }

    /** Return the jar we're running from, if discoverable. */
    public static Path resolveJarPath() {
        String codeSrc = TrayManager.class.getProtectionDomain()
                .getCodeSource().getLocation().toExternalForm();
        if (codeSrc.startsWith("jar:")) codeSrc = codeSrc.substring(4);
        if (codeSrc.startsWith("file:")) {
            codeSrc = codeSrc.substring(5);
            if (codeSrc.contains("!/")) codeSrc = codeSrc.substring(0, codeSrc.indexOf("!/"));
            return Path.of(codeSrc);
        }
        return null;
    }

    /** Minimal 16×16 placeholder drawn programmatically. */
    private static Image placeholderIcon() {
        int size = 16;
        java.awt.image.BufferedImage img = new java.awt.image.BufferedImage(size, size, java.awt.image.BufferedImage.TYPE_INT_ARGB);
        java.awt.Graphics2D g = img.createGraphics();
        g.setColor(new java.awt.Color(0x2B579A));
        g.fillRoundRect(1, 1, size - 2, size - 2, 4, 4);
        g.setColor(java.awt.Color.WHITE);
        g.setFont(new java.awt.Font(Font.SANS_SERIF, Font.BOLD, 10));
        FontMetrics fm = g.getFontMetrics();
        String txt = "A";
        int tw = fm.stringWidth(txt);
        g.drawString(txt, (size - tw) / 2, (size + fm.getAscent() - fm.getDescent()) / 2);
        g.dispose();
        return img;
    }
}
