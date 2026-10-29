package com.auditlog.ui;

import com.auditlog.AppShutdown;
import com.auditlog.config.AppConfig;
import com.auditlog.db.AuditRow;
import com.auditlog.db.H2Store;
import com.auditlog.i18n.I18n;

import javax.swing.*;
import javax.swing.table.AbstractTableModel;
import javax.swing.table.DefaultTableCellRenderer;
import java.awt.*;
import java.awt.event.InputEvent;
import java.awt.event.WindowAdapter;
import java.awt.event.WindowEvent;
import java.io.IOException;
import java.nio.file.Files;
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
    private final JLabel portLabel = new JLabel();
    private final JComboBox<String> hostCombo = new JComboBox<>();
    private final JSpinner sevMin = new JSpinner(new SpinnerNumberModel(0, 0, 7, 1));
    private final JSpinner sevMax = new JSpinner(new SpinnerNumberModel(7, 0, 7, 1));
    private final JCheckBox autoScroll = new JCheckBox(I18n.t("chk.auto-scroll"), true);
    private final JButton refreshBtn = new JButton(I18n.t("btn.refresh"));
    private final JButton startStopBtn = new JButton(I18n.t("btn.start"));
    private final JButton settingsBtn = new JButton(I18n.t("btn.settings"));

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

        setDefaultCloseOperation(WindowConstants.DO_NOTHING_ON_CLOSE);
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
                onWindowClosingRequest();
            }
        });
    }

    /**
     * 关闭窗口时二选一：最小化到托盘 / 退出程序（默认最小化，可取消）。
     * 托盘不可用时直接走有序退出。
     */
    private void onWindowClosingRequest() {
        if (AppShutdown.isExitRequested()) return;   // 已在退出流程中，忽略重复关闭
        if (trayManager == null || !trayManager.isInstalled()) {
            requestExit();
            return;
        }
        Object[] options = {
                I18n.t("dlg.close.minimize-to-tray"),
                I18n.t("dlg.close.exit"),
                I18n.t("dlg.close.cancel")
        };
        int choice = JOptionPane.showOptionDialog(this,
                I18n.t("dlg.close.message"),
                I18n.t("dlg.close.title"),
                JOptionPane.YES_NO_CANCEL_OPTION,
                JOptionPane.QUESTION_MESSAGE,
                null, options, options[0]);
        switch (choice) {
            case 0 -> trayManager.hideFrame();
            case 1 -> requestExit();
            default -> { /* 取消 / 关闭对话框 —— 保持窗口打开 */ }
        }
    }

    /** 有序退出：先隐藏界面给用户即时反馈，再在后台完成清理后才结束进程。 */
    private void requestExit() {
        setVisible(false);
        AppShutdown.exit(0);
    }

    private void buildUi() {
        JPanel top = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 6));
        top.setBorder(BorderFactory.createMatteBorder(0, 0, 1, 0, UIManager.getColor("Separator.foreground")));

        startStopBtn.addActionListener(e -> onStartStop());
        refreshBtn.addActionListener(e -> refreshData());
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
        top.add(settingsBtn);

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
        portLabel.setText(I18n.t("status.port", config.syslogPort) +
                (config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
        portLabel.setBorder(BorderFactory.createEmptyBorder(4, 0, 4, 8));
        bottom.add(portLabel, BorderLayout.EAST);

        JSplitPane split = new JSplitPane(JSplitPane.VERTICAL_SPLIT, scroll, bottom);
        split.setDividerLocation(680);
        split.setResizeWeight(1.0);

        // ---- Menu bar ----
        setJMenuBar(buildMenuBar());

        add(top, BorderLayout.NORTH);
        add(split, BorderLayout.CENTER);
    }

    private JMenuBar buildMenuBar() {
        JMenuBar bar = new JMenuBar();

        JMenu mFile = new JMenu(I18n.t("menu.file"));
        mFile.setMnemonic(I18n.t("menu.file.mnemonic").charAt(0));

        JMenuItem miOpenCfg = new JMenuItem(I18n.t("menu.open-config-dir"));
        miOpenCfg.addActionListener(e -> openInDesktop(configFile.toAbsolutePath().getParent()));
        mFile.add(miOpenCfg);

        JMenuItem miOpenDb = new JMenuItem(I18n.t("menu.open-db-dir"));
        miOpenDb.addActionListener(e -> {
            // h2DbPath = "./auditlog/auditlog" — parent dir is where .mv.db actually lives
            Path dbFile = Path.of(config.h2DbPath).toAbsolutePath();
            openInDesktop(dbFile.getParent());
        });
        mFile.add(miOpenDb);

        JMenuItem miSettings = new JMenuItem(I18n.t("menu.settings"));
        miSettings.setAccelerator(KeyStroke.getKeyStroke('S', InputEvent.CTRL_DOWN_MASK));
        miSettings.addActionListener(e -> onOpenSettings());
        mFile.add(miSettings);

        JMenuItem miExport = new JMenuItem(I18n.t("menu.export"));
        miExport.addActionListener(e -> onExportJson());
        mFile.add(miExport);

        mFile.addSeparator();

        JMenuItem miExit = new JMenuItem(I18n.t("menu.exit"));
        miExit.addActionListener(e -> {
            int r = JOptionPane.showConfirmDialog(this,
                    I18n.t("tray.exit.confirm"),
                    I18n.t("dlg.confirm"),
                    JOptionPane.YES_NO_OPTION, JOptionPane.QUESTION_MESSAGE);
            if (r == JOptionPane.YES_OPTION) requestExit();
        });
        mFile.add(miExit);

        bar.add(mFile);

        // ---- 维护菜单（数据库维护类危险操作集中在此） ----
        JMenu mMaintenance = new JMenu(I18n.t("menu.maintenance"));
        mMaintenance.setMnemonic(I18n.t("menu.maintenance.mnemonic").charAt(0));

        JMenuItem miPrune = new JMenuItem(I18n.t("menu.prune"));
        miPrune.addActionListener(e -> onPruneDialog());
        mMaintenance.add(miPrune);

        bar.add(mMaintenance);

        JMenu mHelp = new JMenu(I18n.t("menu.help"));
        mHelp.setMnemonic(I18n.t("menu.help.mnemonic").charAt(0));
        JMenuItem miAbout = new JMenuItem(I18n.t("menu.about"));
        miAbout.addActionListener(e -> JOptionPane.showMessageDialog(this,
                I18n.t("about.body"), I18n.t("menu.about"), JOptionPane.INFORMATION_MESSAGE));
        mHelp.add(miAbout);
        bar.add(mHelp);

        return bar;
    }

    /** Open a directory in the OS file explorer (Windows Explorer / macOS Finder). */
    private void openInDesktop(Path dir) {
        try {
            if (dir == null) return;
            if (!Files.exists(dir)) Files.createDirectories(dir);
            if (Desktop.isDesktopSupported()) {
                Desktop.getDesktop().open(dir.toFile());
            } else {
                // Fallback — Windows-only shell call if Desktop API is blocked
                Runtime.getRuntime().exec("explorer \"" + dir.toAbsolutePath() + "\"");
            }
        } catch (Exception ex) {
            JOptionPane.showMessageDialog(this, I18n.t("dlg.open-dir-fail", ex.getMessage()),
                    I18n.t("dlg.warning"), JOptionPane.WARNING_MESSAGE);
        }
    }

    private void onStartStop() {
        var controller = ServerController.get();
        if (controller.isRunning()) {
            // 已运行 → 重启：二次确认防误触
            int confirm = JOptionPane.showConfirmDialog(this,
                    I18n.t("dlg.restart-confirm"),
                    I18n.t("dlg.warning"),
                    JOptionPane.YES_NO_OPTION,
                    JOptionPane.WARNING_MESSAGE);
            if (confirm != JOptionPane.YES_OPTION) return;

            try {
                controller.restart(config, store);
                appendWelcomeRow("welcome.restarted");
                statusLabel.setText(I18n.t("status.listening",
                        config.syslogPort,
                        config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
            } catch (Exception e) {
                JOptionPane.showMessageDialog(this, I18n.t("dlg.server-fail", e.getMessage()),
                        I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
                // 重启失败则保证 UI 状态真实
                startStopBtn.setText(I18n.t("btn.start"));
                statusLabel.setText(I18n.t("status.stopped"));
                return;
            }
        } else {
            try {
                controller.start(config, store);
                appendWelcomeRow("welcome.started");
                statusLabel.setText(I18n.t("status.listening",
                        config.syslogPort,
                        config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
            } catch (Exception e) {
                JOptionPane.showMessageDialog(this, I18n.t("dlg.server-fail", e.getMessage()),
                        I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
                return;
            }
        }
        startStopBtn.setText(I18n.t("btn.restart"));
    }

    /** Append a synthetic informational row to the table (never written to DB). */
    public void appendWelcomeRow(String i18nKey) {
        AuditRow row = new AuditRow(
                -1L,                              // id = -1 means non-persisted
                java.time.Instant.now(),
                "—",                              // hostname placeholder
                6,                                // severity informational
                "informational",
                I18n.t("app.title"),
                "", "", 0,
                "",
                I18n.t(i18nKey),
                null
        );
        model.addRow(row);
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

    /** 从维护菜单进入：一体化弹窗（输入保留行数 → 预览将删多少 → 二次确认 → 执行）。 */
    private void onPruneDialog() {
        long total;
        try {
            total = store.totalCount();
        } catch (Exception e) {
            JOptionPane.showMessageDialog(this, I18n.t("dlg.prune-fail", e.getMessage()),
                    I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
            return;
        }

        // 构建弹窗：警告说明 + 输入保留行数
        JPanel panel = new JPanel(new BorderLayout(10, 10));
        JTextArea warn = new JTextArea(I18n.t("dlg.prune-dialog-warn", total));
        warn.setEditable(false);
        warn.setLineWrap(true);
        warn.setWrapStyleWord(true);
        warn.setForeground(new Color(180, 50, 50));
        warn.setBackground(panel.getBackground());
        panel.add(warn, BorderLayout.NORTH);

        JPanel inputRow = new JPanel(new FlowLayout(FlowLayout.LEFT, 6, 0));
        inputRow.add(new JLabel(I18n.t("lbl.keep.rows")));
        JSpinner keepSpinner = new JSpinner(
                new SpinnerNumberModel((int) Math.min(total, 100_000), 1000, 10_000_000, 1000));
        inputRow.add(keepSpinner);
        panel.add(inputRow, BorderLayout.CENTER);

        int choice = JOptionPane.showConfirmDialog(this, panel,
                I18n.t("menu.prune"),
                JOptionPane.OK_CANCEL_OPTION,
                JOptionPane.WARNING_MESSAGE);
        if (choice != JOptionPane.OK_OPTION) return;

        int keep = (Integer) keepSpinner.getValue();
        long willDelete = Math.max(0, total - keep);
        if (willDelete == 0) {
            JOptionPane.showMessageDialog(this, I18n.t("dlg.prune-nothing", total),
                    I18n.t("dlg.info"), JOptionPane.INFORMATION_MESSAGE);
            return;
        }

        // 二次确认（明确告知风险 + 具体数字）
        int confirm = JOptionPane.showConfirmDialog(this,
                I18n.t("dlg.prune-confirm", willDelete, keep, total),
                I18n.t("dlg.warning"),
                JOptionPane.YES_NO_OPTION,
                JOptionPane.WARNING_MESSAGE);
        if (confirm != JOptionPane.YES_OPTION) return;

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

    private void refreshStatusBarPort() {
        String bind = config.syslogBindAddress;
        if ("0.0.0.0".equals(bind)) bind = "*";
        portLabel.setText(I18n.t("status.port", config.syslogPort) +
                " " + bind +
                (config.tlsEnabled ? I18n.t("status.tls.enabled") : I18n.t("status.tls.disabled")));
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

        JCheckBox autoServerCheck = new JCheckBox(I18n.t("chk.auto-start-server"), config.autoStartServer);
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

        // ---- 网络设置 ----
        JSpinner portSpinner = new JSpinner(
                new SpinnerNumberModel(config.syslogPort, 1, 65535, 1));
        portSpinner.setPreferredSize(new Dimension(110, 26));
        JTextField bindField = new JTextField(config.syslogBindAddress, 12);

        JPanel box = new JPanel(new GridBagLayout());
        GridBagConstraints gbc = new GridBagConstraints();
        gbc.insets = new Insets(6, 6, 6, 6);
        gbc.anchor = GridBagConstraints.WEST;

        // --- 网络 ---
        gbc.gridx = 0; gbc.gridy = 0; gbc.gridwidth = 2;
        box.add(new JLabel(I18n.t("dlg.settings.network")), gbc);

        gbc.gridx = 0; gbc.gridy++; gbc.gridwidth = 1;
        box.add(new JLabel(I18n.t("lbl.port")), gbc);
        gbc.gridx = 1;
        box.add(portSpinner, gbc);

        gbc.gridx = 0; gbc.gridy++;
        box.add(new JLabel(I18n.t("lbl.bind.address")), gbc);
        gbc.gridx = 1;
        box.add(bindField, gbc);

        // --- 行为 ---
        gbc.gridx = 0; gbc.gridy++; gbc.gridwidth = 2;
        box.add(Box.createVerticalStrut(8), gbc);
        gbc.gridy++;
        box.add(new JLabel(I18n.t("dlg.settings.behavior")), gbc);

        gbc.gridwidth = 1;
        gbc.gridy++;
        box.add(new JLabel(I18n.t("dlg.language")), gbc);
        gbc.gridx = 1;
        box.add(langCombo, gbc);

        gbc.gridx = 0; gbc.gridwidth = 2; gbc.gridy++;
        box.add(autoServerCheck, gbc);
        gbc.gridy++;
        box.add(bootCheck, gbc);
        gbc.gridy++;
        box.add(trayCheck, gbc);

        int res = JOptionPane.showConfirmDialog(this, box,
                I18n.t("dlg.settings.title"),
                JOptionPane.OK_CANCEL_OPTION, JOptionPane.PLAIN_MESSAGE);
        if (res != JOptionPane.OK_OPTION) return;

        boolean prevAutoStart = config.autoStartServer;
        int prevPort = config.syslogPort;
        String prevBind = config.syslogBindAddress;
        config.autoStartServer = autoServerCheck.isSelected();
        config.autostartOnBoot = bootCheck.isSelected();
        config.minimizeToTray  = trayCheck.isSelected();
        config.syslogPort = (Integer) portSpinner.getValue();
        config.syslogBindAddress = bindField.getText().trim().isEmpty() ? "0.0.0.0" : bindField.getText().trim();
        // 语言：取 SUPPORTED[selIdx][0]
        config.locale = I18n.SUPPORTED[langCombo.getSelectedIndex()][0];

        boolean running = ServerController.get().isRunning();

        // ---- 如果端口或绑定地址变了且服务器正在运行，自动重启以应用新配置 ----
        boolean netChanged = config.syslogPort != prevPort || !config.syslogBindAddress.equals(prevBind);
        if (running && netChanged) {
            try {
                ServerController.get().restart(config, store);
                appendWelcomeRow("welcome.restarted");
            } catch (Exception e) {
                JOptionPane.showMessageDialog(this, I18n.t("dlg.server-fail", e.getMessage()),
                        I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
            }
        } else if (!running && config.autoStartServer) {
            // ---- 服务器未运行但用户刚打开 autoStartServer，立即启动 ----
            try { ServerController.get().start(config, store); }
            catch (Exception ignored) {}
        } else if (config.autoStartServer && !prevAutoStart) {
            // ---- 行为设置变更：autoStartServer 从 OFF → ON ----
            if (!running) {
                try { ServerController.get().start(config, store); }
                catch (Exception ignored) {}
            }
        }

        // ---- 状态栏端口 label 立即刷新（无论重启与否） ----
        refreshStatusBarPort();

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

    /** Update the start/stop button label based on current server state. */
    public void refreshStartStopButton() {
        boolean running = ServerController.get().isRunning();
        startStopBtn.setText(running ? I18n.t("btn.restart") : I18n.t("btn.start"));
    }

    /** Refresh all visible UI text when locale changes at runtime. */
    private void applyLocaleToFrame() {
        setTitle(I18n.t("app.title"));
        refreshStartStopButton();
        refreshBtn.setText(I18n.t("btn.refresh"));
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

        // 菜单栏（重建才能吃到新 locale 的 I18n.t()）
        setJMenuBar(buildMenuBar());
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
