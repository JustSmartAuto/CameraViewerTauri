package com.auditlog.ui;

import com.auditlog.camera.CameraClient;
import com.auditlog.camera.CameraConfig;
import com.auditlog.camera.CameraPoller;
import com.auditlog.camera.CameraStore;
import com.auditlog.i18n.I18n;

import javax.swing.*;
import javax.swing.table.AbstractTableModel;
import java.awt.*;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.List;

/**
 * 相机管理对话框：增删改相机、测试连接、立即同步。
 * 变更立即写入 {@code config/cameras.json} 并刷新轮询器。
 */
public class CameraDialog extends JDialog {

    private static final DateTimeFormatter SYNC_FMT =
            DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss").withZone(ZoneId.systemDefault());

    private final CameraStore store;
    private final CameraPoller poller;
    private final List<CameraConfig> cameras;
    private final CameraTableModel model = new CameraTableModel();
    private final JTable table = new JTable(model);

    private CameraDialog(Window owner, CameraStore store, CameraPoller poller, List<CameraConfig> cameras) {
        super(owner, I18n.t("cam.dlg.title"), ModalityType.APPLICATION_MODAL);
        this.store = store;
        this.poller = poller;
        this.cameras = cameras;

        table.setRowHeight(24);
        table.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        table.getColumnModel().getColumn(0).setPreferredWidth(140);
        table.getColumnModel().getColumn(1).setPreferredWidth(130);
        table.getColumnModel().getColumn(6).setPreferredWidth(150);

        JPanel toolbar = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 8));
        JButton addBtn = new JButton(I18n.t("cam.btn.add"));
        JButton editBtn = new JButton(I18n.t("cam.btn.edit"));
        JButton removeBtn = new JButton(I18n.t("cam.btn.remove"));
        JButton testBtn = new JButton(I18n.t("cam.btn.test"));
        JButton syncBtn = new JButton(I18n.t("cam.btn.sync"));
        JButton closeBtn = new JButton(I18n.t("cam.btn.close"));
        addBtn.addActionListener(e -> onAdd());
        editBtn.addActionListener(e -> onEdit());
        removeBtn.addActionListener(e -> onRemove());
        testBtn.addActionListener(e -> onTest());
        syncBtn.addActionListener(e -> onSync());
        closeBtn.addActionListener(e -> dispose());
        toolbar.add(addBtn);
        toolbar.add(editBtn);
        toolbar.add(removeBtn);
        toolbar.add(Box.createHorizontalStrut(16));
        toolbar.add(testBtn);
        toolbar.add(syncBtn);
        toolbar.add(Box.createHorizontalStrut(16));
        toolbar.add(closeBtn);

        setLayout(new BorderLayout());
        add(toolbar, BorderLayout.NORTH);
        add(new JScrollPane(table), BorderLayout.CENTER);
        setSize(900, 420);
        setLocationRelativeTo(owner);
    }

    public static void show(Window owner, CameraStore store, CameraPoller poller, List<CameraConfig> cameras) {
        new CameraDialog(owner, store, poller, cameras).setVisible(true);
    }

    private void onAdd() {
        CameraConfig c = new CameraConfig("", "");
        if (!editForm(c, true)) return;
        cameras.add(c);
        persist();
        if (c.enabled && poller != null) poller.syncNow(c);   // 新相机立即拉一次全量历史
    }

    private void onEdit() {
        CameraConfig c = selected();
        if (c == null) return;
        if (editForm(c, false)) persist();
    }

    private void onRemove() {
        CameraConfig c = selected();
        if (c == null) return;
        int r = JOptionPane.showConfirmDialog(this, I18n.t("cam.msg.confirm-remove", c.label()),
                I18n.t("dlg.confirm"), JOptionPane.YES_NO_OPTION, JOptionPane.WARNING_MESSAGE);
        if (r != JOptionPane.YES_OPTION) return;
        cameras.remove(c);
        persist();
    }

    private void onTest() {
        CameraConfig c = selected();
        if (c == null) return;
        setCursor(Cursor.getPredefinedCursor(Cursor.WAIT_CURSOR));
        new SwingWorker<Integer, Void>() {
            @Override protected Integer doInBackground() throws Exception {
                return CameraClient.testConnection(c);
            }
            @Override protected void done() {
                setCursor(Cursor.getDefaultCursor());
                try {
                    int n = get();
                    JOptionPane.showMessageDialog(CameraDialog.this, I18n.t("cam.msg.test-ok", n),
                            I18n.t("dlg.info"), JOptionPane.INFORMATION_MESSAGE);
                } catch (Exception ex) {
                    Throwable root = ex.getCause() != null ? ex.getCause() : ex;
                    JOptionPane.showMessageDialog(CameraDialog.this,
                            I18n.t("cam.msg.test-fail", root.getMessage()),
                            I18n.t("dlg.error"), JOptionPane.ERROR_MESSAGE);
                }
            }
        }.execute();
    }

    private void onSync() {
        CameraConfig c = selected();
        if (c == null || poller == null) return;
        poller.syncNow(c);
        JOptionPane.showMessageDialog(this, I18n.t("cam.msg.sync-triggered", c.label()),
                I18n.t("dlg.info"), JOptionPane.INFORMATION_MESSAGE);
    }

    private CameraConfig selected() {
        int row = table.getSelectedRow();
        if (row < 0) {
            JOptionPane.showMessageDialog(this, I18n.t("cam.msg.select-first"),
                    I18n.t("dlg.warning"), JOptionPane.WARNING_MESSAGE);
            return null;
        }
        return cameras.get(table.convertRowIndexToModel(row));
    }

    private void persist() {
        store.save(cameras);
        if (poller != null) poller.reload(cameras);
        model.fireTableDataChanged();
    }

    /** 添加/编辑表单；返回 true 表示用户确认并已把值写回 {@code c}。 */
    private boolean editForm(CameraConfig c, boolean isNew) {
        JTextField name = new JTextField(c.name, 18);
        JTextField ip = new JTextField(c.ip, 18);
        JSpinner port = new JSpinner(new SpinnerNumberModel(Math.max(1, c.port), 1, 65535, 1));
        JTextField user = new JTextField(c.username, 14);
        JPasswordField pass = new JPasswordField(c.password == null ? "" : c.password, 14);
        JSpinner poll = new JSpinner(new SpinnerNumberModel(Math.max(5, c.pollSeconds), 5, 3600, 5));
        JCheckBox enabled = new JCheckBox(I18n.t("cam.form.enabled"), c.enabled);

        JPanel form = new JPanel(new GridBagLayout());
        GridBagConstraints g = new GridBagConstraints();
        g.insets = new Insets(5, 6, 5, 6);
        g.anchor = GridBagConstraints.WEST;
        int y = 0;
        addRow(form, g, y++, I18n.t("cam.form.name"), name);
        addRow(form, g, y++, I18n.t("cam.form.ip"), ip);
        addRow(form, g, y++, I18n.t("cam.form.port"), port);
        addRow(form, g, y++, I18n.t("cam.form.user"), user);
        addRow(form, g, y++, I18n.t("cam.form.password"), pass);
        addRow(form, g, y++, I18n.t("cam.form.poll"), poll);
        g.gridx = 1; g.gridy = y; form.add(enabled, g);

        int r = JOptionPane.showConfirmDialog(this, form,
                isNew ? I18n.t("cam.form.title.new") : I18n.t("cam.form.title.edit"),
                JOptionPane.OK_CANCEL_OPTION, JOptionPane.PLAIN_MESSAGE);
        if (r != JOptionPane.OK_OPTION) return false;

        String ipText = ip.getText().trim();
        if (ipText.isEmpty()) {
            JOptionPane.showMessageDialog(this, I18n.t("cam.msg.need-ip"),
                    I18n.t("dlg.warning"), JOptionPane.WARNING_MESSAGE);
            return false;
        }

        c.name = name.getText().trim();
        c.ip = ipText;
        c.port = (Integer) port.getValue();
        c.username = user.getText().trim();
        c.password = new String(pass.getPassword());
        c.pollSeconds = (Integer) poll.getValue();
        c.enabled = enabled.isSelected();
        return true;
    }

    private static void addRow(JPanel p, GridBagConstraints g, int y, String label, JComponent field) {
        g.gridx = 0; g.gridy = y; p.add(new JLabel(label), g);
        g.gridx = 1; g.gridy = y; p.add(field, g);
    }

    private class CameraTableModel extends AbstractTableModel {
        private final String[] cols = {
                I18n.t("cam.col.name"), I18n.t("cam.col.ip"), I18n.t("cam.col.port"),
                I18n.t("cam.col.user"), I18n.t("cam.col.poll"), I18n.t("cam.col.enabled"),
                I18n.t("cam.col.last")
        };

        @Override public int getRowCount() { return cameras.size(); }
        @Override public int getColumnCount() { return cols.length; }
        @Override public String getColumnName(int col) { return cols[col]; }
        @Override public boolean isCellEditable(int r, int c) { return false; }

        @Override public Object getValueAt(int row, int col) {
            CameraConfig c = cameras.get(row);
            return switch (col) {
                case 0 -> c.name;
                case 1 -> c.ip;
                case 2 -> c.port;
                case 3 -> c.username;
                case 4 -> c.pollSeconds;
                case 5 -> c.enabled ? "✓" : "";
                case 6 -> {
                    Instant ts = CameraClient.parseTimestamp(c.lastSync);
                    yield ts == null ? "—" : SYNC_FMT.format(ts);
                }
                default -> "";
            };
        }
    }
}
