package com.auditlog.ui;

import com.auditlog.config.AppConfig;
import com.auditlog.db.AuditRow;
import com.auditlog.db.H2Store;
import com.auditlog.i18n.I18n;

import javax.swing.*;
import javax.swing.table.AbstractTableModel;
import javax.swing.table.DefaultTableCellRenderer;
import java.awt.*;
import java.awt.event.WindowAdapter;
import java.awt.event.WindowEvent;
import java.io.IOException;
import java.nio.file.Path;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Main Swing frame. Uses FlatLaf theming. All UI strings come from {@link I18n}.
 */
public class MainFrame extends JFrame {

    private static final DateTimeFormatter FMT = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")
            .withZone(ZoneId.systemDefault());

    private final AppConfig config;
    private final H2Store store;
    private final Path configFile;    // where to save()
    private TrayManager trayManager;   // set externally after construction

    private final LogTableModel model = new LogTableModel();
    private final JTable table = new JTable(model);
    private final JLabel statusLabel = new JLabel(I18n.t("status.stopped"));
    private final JLabel countLabel = new JLabel();
    private final JComboBox<String> hostCombo = new JComboBox<>();
    private final JSpinner sevMin = new JSpinner(new SpinnerNumberModel(0, 0, 7, 1));
    private final JSpinner sevMax = new JSpinner(new SpinnerNumberModel(7, 0, 7, 1));
    private final JCheckBox autoScroll = new JCheckBox(I18n.t("chk.auto-scroll"), true);
    private final JButton refreshBtn = new JButton(I18n.t("btn.refresh"));
    private final JButton startStopBtn = new JButton(I18n.t("btn.start"));
    private final JButton exportBtn = new JButton(I18n.t("btn.export"));
    private final JButton pruneBtn = new JButton(I18n.t("btn.prune"));
    private final JButton settingsBtn = new JButton(I18n.t("btn.settings"));
    private final JSpinner pruneKeep = new JSpinner(new SpinnerNumberModel(100_000, 1000, 10_000_000, 1000));

    public MainFrame(AppConfig config, H2Store store, Path configFile) {
        super(I18n.t("app.title"));
        this.config = config;
        this.store = store;
        this.configFile = configFile;

        addWindowStateListener(new WindowAdapter() {
            @Override public void windowStateChanged(WindowEvent e) {
                if ((e.getNewState() & Frame.ICONIFIED) != 0 && trayManager != null && trayManager.isInstalled()) {
                    trayManager.hideFrame();
                }
            }
        });

        setDefaultCloseOperation(WindowConstants.HIDE_ON_CLOSE);
        setSize(1280, 780);
        setLocationRelativeTo(null);

        buildUi();
        refreshHosts();
        refreshData();
    }

    public void setTrayManager(TrayManager tm) {
        this.trayManager = tm;
        addWindowListener(new WindowAdapter() {
            @Override public void windowClosing(WindowEvent e) {
                if (trayManager != null && trayManager.isInstalled()) {
                    trayManager.hideFrame();
                } else {
                    System.exit(0);
                }
            }
        });
    }

    private void buildUi() {
        JPanel top = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 6));
        top.setBorder(BorderFactory.createMatteBorder(0, 0, 1, 0, UIManager.getColor("Separator.foreground")));

        startStopBtn.addActionListener(e -> onStartStop());
        refreshBtn.addActionListener(e -> refreshData());
        exportBtn.addActionListener(e -> onExportJson());
        pruneBtn.addActionListener(e -> onPrune());
        settingsBtn.addActionListener(e -> onOpenSettings());

        top.add(startStopBtn);
        top.add(Box.createHorizontalStrut(12));
        top.add(new JLabel(I18n.t("lbl.host")));
        hostCombo.setEditable(true);
        hostCombo.setPreferredSize(new Dimension(180, 26));
        hostCombo.addActionListener(e -> refreshData());
        top.add(hostCombo);
        top.add(Box.createHorizontalStrut(6));
        top.add(new JLabel(I18n.t("lbl.severity.min")));
        top.add(sevMin);
        top.add(new JLabel(I18n.t("lbl.severity.max")));
        top.add(sevMax);
        top.add(refreshBtn);
        top.add(Box.createHorizontalStrut(12));
        top.add(autoScroll);
        top.add(Box.createHorizontalGlue());
        top.add(countLabel);
        top.add(Box.createHorizontalStrut(12));
        top.add(new JLabel(I18n.t("lbl.keep.rows")));
        top.add(pruneKeep);
        top.add(pruneBtn);
        top.add(Box.createHorizontalStrut(8));
        top.add(settingsBtn);
        top.add(exportBtn);

        table.setAutoCreateRowSorter(true);
        table.setRowHeight(22);
        table.setShowGrid(true);
        table.setGridColor(UIManager.getColor("Table.gridColor"));
        table.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        table.getColumnModel().getColumn(0).setPreferredWidth(80);
        table.getColumnModel().getColumn(1).setPreferredWidth(120);
        table.getColumnModel().getColumn(2).setPreferredWidth(70);
        table.getColumnModel().getColumn(3).setPreferredWidth(110);
        table.getColumnModel().getColumn(4).setPreferredWidth(90);
        table.getColumnModel().getColumn(5).setPreferredWidth(90);
        table.getColumnModel().getColumn(6).setPreferredWidth(400);
        table.getColumnModel().getColumn(7).setPreferredWidth(600);
        table.setDefaultRenderer(Object.class, new SevRenderer());

        JScrollPane scroll = new JScrollPane(table, JScrollPane.VERTICAL_SCROLLBAR_AS_NEEDED,
                JScrollPane.HORIZONTAL_SCROLLBAR_AS_NEEDED);

        JPanel bottom = new JPanel(new BorderLayout(8, 0));
        bottom.setBorder(BorderFactory.createMatteBorder(1, 0, 0, 0, UIManager.getColor("Separator.foreground")));
        statusLabel.setBorder(BorderFactory.createEmptyBorder(4, 8, 4, 0));
        bottom.add(statusLabel, BorderLayout.WEST);
        JLabel portLabel = new JLabel(I18n.t("status.port", config.syslogPort) +
                (config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
        portLabel.setBorder(BorderFactory.createEmptyBorder(4, 0, 4, 8));
        bottom.add(portLabel, BorderLayout.EAST);

        JSplitPane split = new JSplitPane(JSplitPane.VERTICAL_SPLIT, scroll, bottom);
        split.setDividerLocation(680);
        split.setResizeWeight(1.0);

        add(top, BorderLayout.NORTH);
        add(split, BorderLayout.CENTER);
    }

    private void onStartStop() {
        var controller = ServerController.get();
        if (controller.isRunning()) {
            controller.stop();
            startStopBtn.setText(I18n.t("btn.start"));
            statusLabel.setText(I18n.t("status.stopped"));
        } else {
            try {
                controller.start(config, store);
                startStopBtn.setText(I18n.t("btn.stop"));
                statusLabel.setText(I18n.t("status.listening",
                        config.syslogPort,
                        config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
            } catch (Exception e) {
                JOptionPane.showMessageDialog(this, I18n.t("dlg.server-fail", e.getMessage()),
                        I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
            }
        }
    }

    private void refreshHosts() {
        SwingUtilities.invokeLater(() -> {
            try {
                String current = (String) hostCombo.getSelectedItem();
                hostCombo.removeAllItems();
                hostCombo.addItem(I18n.t("filter.all"));
                for (String h : store.distinctHostnames(100)) {
                    hostCombo.addItem(h);
                }
                if (current != null) hostCombo.setSelectedItem(current);
            } catch (Exception ignored) {}
        });
    }

    public void refreshData() {
        SwingUtilities.invokeLater(() -> {
            try {
                String hostFilter = (String) hostCombo.getSelectedItem();
                // key-insensitive: 判断 "全部" / "All" / "(all)" 的稳定标识 —— 空 / 当前 locale 显示值
                String all = I18n.t("filter.all");
                if (hostFilter == null || hostFilter.isBlank() || all.equals(hostFilter)) {
                    hostFilter = null;
                } else if (!hostFilter.contains("%")) {
                    hostFilter = hostFilter + "%";
                }

                int sevMinV = (Integer) sevMin.getValue();
                int sevMaxV = (Integer) sevMax.getValue();

                long total = store.totalCount();
                var rows = store.query(hostFilter, sevMinV, sevMaxV, 0, config.maxTableRows);
                model.setRows(rows);
                countLabel.setText(I18n.t("status.events", total, rows.size()));

                if (autoScroll.isSelected() && !rows.isEmpty()) {
                    table.scrollRectToVisible(table.getCellRect(0, 0, true));
                }
            } catch (Exception e) {
                countLabel.setText(I18n.t("status.error", e.getMessage()));
            }
        });
    }

    private void onExportJson() {
        JFileChooser fc = new JFileChooser();
        fc.setSelectedFile(new java.io.File("audit-log-export-" +
                java.time.LocalDateTime.now().format(java.time.format.DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss")) + ".json"));
        if (fc.showSaveDialog(this) != JFileChooser.APPROVE_OPTION) return;
        var controller = ServerController.get();
        controller.exportJson(fc.getSelectedFile().toPath());
        JOptionPane.showMessageDialog(this, I18n.t("dlg.export-done", fc.getSelectedFile().getAbsolutePath()),
                I18n.t("dlg.info"), JOptionPane.INFORMATION_MESSAGE);
    }

    private void onPrune() {
        int keep = (Integer) pruneKeep.getValue();
        try {
            int removed = store.prune(keep);
            refreshData();
            JOptionPane.showMessageDialog(this, I18n.t("dlg.prune-done", removed, store.totalCount()),
                    I18n.t("dlg.info"), JOptionPane.INFORMATION_MESSAGE);
        } catch (Exception e) {
            JOptionPane.showMessageDialog(this, I18n.t("dlg.prune-fail", e.getMessage()),
                    I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
        }
    }

    private void onOpenSettings() {
        // ---- 语言下拉 ----
        JComboBox<String> langCombo = new JComboBox<>();
        for (var pair : I18n.SUPPORTED) langCombo.addItem(pair[1]); // display name
        // 匹配当前 config.locale → SUPPORTED[i][0]
        int selIdx = 0;
        for (int i = 0; i < I18n.SUPPORTED.length; i++) {
            if (I18n.SUPPORTED[i][0].equals(config.locale)) { selIdx = i; break; }
        }
        langCombo.setSelectedIndex(selIdx);

        JCheckBox bootCheck = new JCheckBox(I18n.t("chk.boot-autostart"), config.autostartOnBoot);
        JCheckBox trayCheck = new JCheckBox(I18n.t("chk.minimize-to-tray"), config.minimizeToTray);
        if (!TrayManager.isSupportedStatic()) {
            trayCheck.setEnabled(false);
            trayCheck.setToolTipText(I18n.t("chk.minimize-to-tray.tooltip"));
        }
        if (!WinAutostart.isWindows()) {
            bootCheck.setEnabled(false);
            bootCheck.setToolTipText(I18n.t("chk.boot-autostart.tooltip"));
        }

        JPanel box = new JPanel(new GridBagLayout());
        GridBagConstraints gbc = new GridBagConstraints();
        gbc.insets = new Insets(6, 6, 6, 6);
        gbc.anchor = GridBagConstraints.WEST;
        gbc.gridx = 0; gbc.gridy = 0;
        gbc.gridwidth = 2;
        box.add(new JLabel(I18n.t("dlg.settings.behavior")), gbc);

        gbc.gridwidth = 1;
        gbc.gridy++;
        box.add(new JLabel(I18n.t("dlg.language")), gbc);
        gbc.gridx = 1;
        gbc.gridwidth = 1;
        box.add(langCombo, gbc);

        gbc.gridx = 0; gbc.gridwidth = 2; gbc.gridy++;
        box.add(bootCheck, gbc);
        gbc.gridy++;
        box.add(trayCheck, gbc);

        int res = JOptionPane.showConfirmDialog(this, box,
                I18n.t("dlg.settings.title"),
                JOptionPane.OK_CANCEL_OPTION, JOptionPane.PLAIN_MESSAGE);
        if (res != JOptionPane.OK_OPTION) return;

        config.autostartOnBoot = bootCheck.isSelected();
        config.minimizeToTray  = trayCheck.isSelected();
        // 语言：取 SUPPORTED[selIdx][0]
        config.locale = I18n.SUPPORTED[langCombo.getSelectedIndex()][0];

        boolean bootResult = applyAutostartSetting();
        if (config.minimizeToTray && trayManager != null && !trayManager.isInstalled()) {
            trayManager.install();
        }

        // ---- 立即切换 locale 并整页刷新 ----
        I18n.setLocale(config.asLocale());
        applyLocaleToFrame();
        trayManager.onLocaleChanged();

        // ---- 持久化 ----
        try {
            config.save(configFile);
        } catch (IOException ex) {
            JOptionPane.showMessageDialog(this, I18n.t("dlg.save-fail", ex.getMessage()),
                    I18n.t("dlg.warning"), JOptionPane.WARNING_MESSAGE);
        }

        String on   = I18n.t("lbl.on");
        String off  = I18n.t("lbl.off");
        String bootState  = config.autostartOnBoot ? on : off;
        String trayState  = config.minimizeToTray  ? on : off;

        if (!bootResult) {
            JOptionPane.showMessageDialog(this,
                    I18n.t("dlg.autostart-warn", bootState),
                    I18n.t("app.title"), JOptionPane.WARNING_MESSAGE);
        } else {
            JOptionPane.showMessageDialog(this,
                    I18n.t("dlg.settings-saved", bootState, trayState),
                    I18n.t("app.title"), JOptionPane.INFORMATION_MESSAGE);
        }
    }

    /** Refresh all visible UI text when locale changes at runtime. */
    private void applyLocaleToFrame() {
        setTitle(I18n.t("app.title"));
        startStopBtn.setText(ServerController.get().isRunning() ? I18n.t("btn.stop") : I18n.t("btn.start"));
        refreshBtn.setText(I18n.t("btn.refresh"));
        exportBtn.setText(I18n.t("btn.export"));
        pruneBtn.setText(I18n.t("btn.prune"));
        settingsBtn.setText(I18n.t("btn.settings"));
        autoScroll.setText(I18n.t("chk.auto-scroll"));

        // status label
        if (ServerController.get().isRunning()) {
            statusLabel.setText(I18n.t("status.listening",
                    config.syslogPort,
                    config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
        } else {
            statusLabel.setText(I18n.t("status.stopped"));
        }

        // host combo: 需要重建 all 项 — 已选中的具体 hostname 保持选中
        String currentHost = (String) hostCombo.getSelectedItem();
        hostCombo.removeAllItems();
        hostCombo.addItem(I18n.t("filter.all"));
        try {
            for (String h : store.distinctHostnames(100)) hostCombo.addItem(h);
        } catch (Exception ignored) {}
        if (currentHost != null) hostCombo.setSelectedItem(currentHost);

        // count label 由 refreshData 会重填
        refreshData();

        // 列标题
        model.fireTableStructureChanged();
    }

    /** Enable or disable the HKCU Run entry to match config.autostartOnBoot. */
    private boolean applyAutostartSetting() {
        if (!WinAutostart.isWindows()) return true;
        try {
            Path jarPath = TrayManager.resolveJarPath();
            if (jarPath == null) return false; // running from IDE
            if (config.autostartOnBoot) WinAutostart.enable(jarPath);
            else                         WinAutostart.disable();
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    public void appendRow(AuditRow row) {
        model.addRow(row);
    }

    // ---- table model ----
    class LogTableModel extends AbstractTableModel {
        private final String[] colKeys = {"col.time", "col.host", "col.sev", "col.app", "col.msgid", "col.srcip", "col.message", "col.structured"};
        private List<AuditRow> rows = new ArrayList<>();

        void setRows(List<AuditRow> r) { rows = r; fireTableDataChanged(); }
        void addRow(AuditRow r) {
            rows.addFirst(r);
            if (rows.size() > 5000) rows.removeLast();
            fireTableRowsInserted(0, 0);
        }

        @Override public int getRowCount() { return rows.size(); }
        @Override public int getColumnCount() { return colKeys.length; }
        @Override public String getColumnName(int c) { return I18n.t(colKeys[c]); }
        @Override public boolean isCellEditable(int r, int c) { return false; }
        @Override
        public Object getValueAt(int rowIndex, int columnIndex) {
            AuditRow r = rows.get(rowIndex);
            return switch (columnIndex) {
                case 0 -> FMT.format(r.ts());
                case 1 -> r.hostname();
                case 2 -> r.severity() + " " + r.severityName();
                case 3 -> r.appName();
                case 4 -> r.msgId();
                case 5 -> r.sourceIp();
                case 6 -> r.message();
                case 7 -> r.structuredDataJson();
                default -> null;
            };
        }
    }

    // ---- severity color renderer ----
    static class SevRenderer extends DefaultTableCellRenderer {
        private static final Color CRIT = new Color(220, 53, 69);
        private static final Color WARN = new Color(255, 193, 7);
        private static final Color INFO = new Color(40, 167, 69);
        @Override public Component getTableCellRendererComponent(JTable t, Object v, boolean sel, boolean foc, int r, int c) {
            Component comp = super.getTableCellRendererComponent(t, v, sel, foc, r, c);
            if (c == 2 && v != null && v.toString().chars().allMatch(Character::isDigit)) {
                try {
                    int sev = Integer.parseInt(v.toString().split(" ")[0]);
                    comp.setForeground(switch (sev) {
                        case 0, 1, 2, 3 -> CRIT;
                        case 4 -> WARN;
                        case 5, 6, 7 -> INFO;
                        default -> UIManager.getColor("Table.foreground");
                    });
                } catch (NumberFormatException ignored) {}
            } else {
                comp.setForeground(UIManager.getColor("Table.foreground"));
            }
            return comp;
        }
    }
}
