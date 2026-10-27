package com.auditlog.ui;

import com.auditlog.config.AppConfig;
import com.auditlog.db.AuditRow;
import com.auditlog.db.H2Store;

import javax.swing.*;
import javax.swing.table.AbstractTableModel;
import javax.swing.table.DefaultTableCellRenderer;
import java.awt.*;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;

/**
 * Main Swing frame. Uses FlatLaf theming.
 */
public class MainFrame extends JFrame {

    private static final DateTimeFormatter FMT = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")
            .withZone(ZoneId.systemDefault());

    private final AppConfig config;
    private final H2Store store;

    private final LogTableModel model = new LogTableModel();
    private final JTable table = new JTable(model);
    private final JLabel statusLabel = new JLabel("● stopped");
    private final JLabel countLabel = new JLabel("events: 0");
    private final JComboBox<String> hostCombo = new JComboBox<>(new String[]{"(all)"});
    private final JSpinner sevMin = new JSpinner(new SpinnerNumberModel(0, 0, 7, 1));
    private final JSpinner sevMax = new JSpinner(new SpinnerNumberModel(7, 0, 7, 1));
    private final JCheckBox autoScroll = new JCheckBox("auto-scroll to newest", true);
    private final JButton refreshBtn = new JButton("Refresh");
    private final JButton startStopBtn = new JButton("Start Server");
    private final JButton exportBtn = new JButton("Export JSON");
    private final JButton pruneBtn = new JButton("Prune");
    private final JSpinner pruneKeep = new JSpinner(new SpinnerNumberModel(100_000, 1000, 10_000_000, 1000));

    public MainFrame(AppConfig config, H2Store store) {
        super("Audit Log Server");
        this.config = config;
        this.store = store;

        setDefaultCloseOperation(EXIT_ON_CLOSE);
        setSize(1280, 780);
        setLocationRelativeTo(null);

        buildUi();
        refreshHosts();
        refreshData();
    }

    private void buildUi() {
        // ---- toolbar ----
        JPanel top = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 6));
        top.setBorder(BorderFactory.createMatteBorder(0, 0, 1, 0, UIManager.getColor("Separator.foreground")));

        startStopBtn.addActionListener(e -> onStartStop());
        refreshBtn.addActionListener(e -> refreshData());
        exportBtn.addActionListener(e -> onExportJson());
        pruneBtn.addActionListener(e -> onPrune());

        top.add(startStopBtn);
        top.add(Box.createHorizontalStrut(12));
        top.add(new JLabel("Host:"));
        hostCombo.setEditable(true);
        hostCombo.setPreferredSize(new Dimension(180, 26));
        hostCombo.addActionListener(e -> refreshData());
        top.add(hostCombo);
        top.add(Box.createHorizontalStrut(6));
        top.add(new JLabel("Severity min:"));
        top.add(sevMin);
        top.add(new JLabel("max:"));
        top.add(sevMax);
        top.add(refreshBtn);
        top.add(Box.createHorizontalStrut(12));
        top.add(autoScroll);
        top.add(Box.createHorizontalGlue());
        top.add(countLabel);
        top.add(Box.createHorizontalStrut(12));
        top.add(new JLabel("Keep rows:"));
        top.add(pruneKeep);
        top.add(pruneBtn);
        top.add(exportBtn);

        // ---- table ----
        table.setAutoCreateRowSorter(true);
        table.setRowHeight(22);
        table.setShowGrid(true);
        table.setGridColor(UIManager.getColor("Table.gridColor"));
        table.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        table.getColumnModel().getColumn(0).setPreferredWidth(80);   // ts
        table.getColumnModel().getColumn(1).setPreferredWidth(120);  // host
        table.getColumnModel().getColumn(2).setPreferredWidth(70);   // sev
        table.getColumnModel().getColumn(3).setPreferredWidth(110);   // app
        table.getColumnModel().getColumn(4).setPreferredWidth(90);   // msgid
        table.getColumnModel().getColumn(5).setPreferredWidth(90);   // src
        table.getColumnModel().getColumn(6).setPreferredWidth(400);  // msg
        table.getColumnModel().getColumn(7).setPreferredWidth(600);  // sd
        table.setDefaultRenderer(Object.class, new SevRenderer());

        JScrollPane scroll = new JScrollPane(table, JScrollPane.VERTICAL_SCROLLBAR_AS_NEEDED,
                JScrollPane.HORIZONTAL_SCROLLBAR_AS_NEEDED);

        // ---- bottom status ----
        JPanel bottom = new JPanel(new BorderLayout(8, 0));
        bottom.setBorder(BorderFactory.createMatteBorder(1, 0, 0, 0, UIManager.getColor("Separator.foreground")));
        statusLabel.setBorder(BorderFactory.createEmptyBorder(4, 8, 4, 0));
        bottom.add(statusLabel, BorderLayout.WEST);
        JLabel portLabel = new JLabel("port " + config.syslogPort + (config.tlsEnabled ? "  TLS enabled" : ""));
        portLabel.setBorder(BorderFactory.createEmptyBorder(4, 0, 4, 8));
        bottom.add(portLabel, BorderLayout.EAST);

        // ---- split pane ----
        JSplitPane split = new JSplitPane(JSplitPane.VERTICAL_SPLIT, scroll, bottom);
        split.setDividerLocation(680);
        split.setResizeWeight(1.0);

        add(top, BorderLayout.NORTH);
        add(split, BorderLayout.CENTER);
    }

    // ---- actions ----

    private void onStartStop() {
        var controller = ServerController.get();
        if (controller.isRunning()) {
            controller.stop();
            startStopBtn.setText("Start Server");
            statusLabel.setText("● stopped");
        } else {
            try {
                controller.start(config, store);
                startStopBtn.setText("Stop Server");
                statusLabel.setText("● listening on " + config.syslogPort +
                        (config.tlsEnabled ? "  (TLS)" : "  (plaintext)"));
            } catch (Exception e) {
                JOptionPane.showMessageDialog(this, "Failed to start server: " + e.getMessage(),
                        "Error", JOptionPane.ERROR_MESSAGE);
            }
        }
    }

    private void refreshHosts() {
        SwingUtilities.invokeLater(() -> {
            try {
                String current = (String) hostCombo.getSelectedItem();
                hostCombo.removeAllItems();
                hostCombo.addItem("(all)");
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
                if (hostFilter != null && hostFilter.isBlank()) hostFilter = null;
                else if ("(all)".equals(hostFilter)) hostFilter = null;
                else if (hostFilter != null && !hostFilter.contains("%")) hostFilter = hostFilter + "%";

                int sevMinV = (Integer) sevMin.getValue();
                int sevMaxV = (Integer) sevMax.getValue();

                long total = store.totalCount();
                var rows = store.query(hostFilter, sevMinV, sevMaxV, 0, config.maxTableRows);
                model.setRows(rows);
                countLabel.setText("events: " + total + "  shown: " + rows.size());

                if (autoScroll.isSelected() && !rows.isEmpty()) {
                    table.scrollRectToVisible(table.getCellRect(0, 0, true));
                }
            } catch (Exception e) {
                countLabel.setText("error: " + e.getMessage());
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
        JOptionPane.showMessageDialog(this, "Exported to " + fc.getSelectedFile().getAbsolutePath());
    }

    private void onPrune() {
        int keep = (Integer) pruneKeep.getValue();
        try {
            int removed = store.prune(keep);
            refreshData();
            JOptionPane.showMessageDialog(this, "Pruned " + removed + " rows. Current total: " + store.totalCount());
        } catch (Exception e) {
            JOptionPane.showMessageDialog(this, "Prune failed: " + e.getMessage());
        }
    }

    // ---- inbound event push from server thread ----
    public void appendRow(AuditRow row) {
        model.addRow(row);
    }

    // ---- table model ----
    static class LogTableModel extends AbstractTableModel {
        private final String[] cols = {"Time", "Host", "Sev", "App", "MsgId", "Source IP", "Message", "Structured Data"};
        private List<AuditRow> rows = new ArrayList<>();

        void setRows(List<AuditRow> r) { rows = r; fireTableDataChanged(); }
        void addRow(AuditRow r) {
            rows.addFirst(r);
            if (rows.size() > 5000) rows.removeLast(); // safety cap for auto-inbound
            fireTableRowsInserted(0, 0);
        }

        @Override public int getRowCount() { return rows.size(); }
        @Override public int getColumnCount() { return cols.length; }
        @Override public String getColumnName(int c) { return cols[c]; }
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
