package com.autoweld.cameraviewer.ui;

import com.autoweld.cameraviewer.config.AppConfig;
import com.autoweld.cameraviewer.model.GridConfig;

import javax.swing.*;
import java.awt.*;

/**
 * 设置对话框，配置 4 个格子的 MQTT 参数与显示选项。
 */
public class SettingsDialog extends JDialog {

    private final MainFrame mainFrame;
    private final AppConfig workingConfig;
    private boolean applied = false;

    public SettingsDialog(MainFrame owner) {
        super(owner, "设置", true);
        this.mainFrame = owner;
        this.workingConfig = copyConfig(owner.getConfig());
        initialize();
    }

    private void initialize() {
        setDefaultCloseOperation(DISPOSE_ON_CLOSE);
        setSize(600, 520);
        setLocationRelativeTo(mainFrame);
        setLayout(new BorderLayout(8, 8));

        JTabbedPane tabs = new JTabbedPane();
        tabs.addTab("全局", createGlobalPanel());
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            tabs.addTab(workingConfig.getGrid(i).getName(), createGridPanel(i));
        }
        add(tabs, BorderLayout.CENTER);

        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.RIGHT, 8, 8));
        JButton apply = new JButton("应用");
        JButton cancel = new JButton("取消");
        apply.addActionListener(e -> apply());
        cancel.addActionListener(e -> dispose());
        buttons.add(apply);
        buttons.add(cancel);
        add(buttons, BorderLayout.SOUTH);
    }

    private JPanel createGlobalPanel() {
        JPanel panel = new JPanel(new GridBagLayout());
        GridBagConstraints gbc = new GridBagConstraints();
        gbc.insets = new Insets(8, 8, 8, 8);
        gbc.fill = GridBagConstraints.HORIZONTAL;
        gbc.gridx = 0;
        gbc.gridy = 0;
        gbc.weightx = 1.0;

        JCheckBox maximizeBox = new JCheckBox("启动时最大化", workingConfig.isMaximizeOnStartup());
        maximizeBox.addActionListener(e -> workingConfig.setMaximizeOnStartup(maximizeBox.isSelected()));
        panel.add(maximizeBox, gbc);

        gbc.gridy = 1;
        JCheckBox checkerboardBox = new JCheckBox("无图像时显示 5120×5120 棋盘格", workingConfig.isShowCheckerboardWhenNoImage());
        checkerboardBox.setToolTipText("当图像消息没有 ImageData 时，显示 512×512 的棋盘格占位图");
        checkerboardBox.addActionListener(e -> workingConfig.setShowCheckerboardWhenNoImage(checkerboardBox.isSelected()));
        panel.add(checkerboardBox, gbc);

        gbc.gridy = 2;
        gbc.weighty = 1.0;
        panel.add(Box.createVerticalGlue(), gbc);

        return panel;
    }

    private JPanel createGridPanel(int index) {
        GridConfig grid = workingConfig.getGrid(index);
        JPanel panel = new JPanel(new GridBagLayout());
        GridBagConstraints gbc = new GridBagConstraints();
        gbc.insets = new Insets(4, 8, 4, 8);
        gbc.fill = GridBagConstraints.HORIZONTAL;

        JTextField nameField = new JTextField(grid.getName(), 20);
        JCheckBox enabledBox = new JCheckBox("启用", grid.isEnabled());
        JTextField hostField = new JTextField(grid.getBrokerHost(), 20);
        JSpinner portSpinner = new JSpinner(new SpinnerNumberModel(grid.getBrokerPort(), 1, 65535, 1));
        JTextField clientIdField = new JTextField(grid.getClientId(), 20);
        JTextField imageTopicField = new JTextField(grid.getImageTopic(), 25);
        JTextField dataTopicField = new JTextField(grid.getDataTopic(), 25);
        JTextField logTopicField = new JTextField(grid.getLogTopic(), 25);
        JCheckBox showImageBox = new JCheckBox("显示图像", grid.isShowImage());
        JCheckBox showDataBox = new JCheckBox("显示数据", grid.isShowData());
        JCheckBox showLogBox = new JCheckBox("显示日志", grid.isShowLog());

        int row = 0;
        addRow(panel, gbc, row++, "名称:", nameField);
        addRow(panel, gbc, row++, "启用:", enabledBox);
        addRow(panel, gbc, row++, "Broker 地址:", hostField);
        addRow(panel, gbc, row++, "Broker 端口:", portSpinner);
        addRow(panel, gbc, row++, "Client ID:", clientIdField);
        addRow(panel, gbc, row++, "图像话题:", imageTopicField);
        addRow(panel, gbc, row++, "数据话题:", dataTopicField);
        addRow(panel, gbc, row++, "日志话题:", logTopicField);

        JPanel displayPanel = new JPanel(new FlowLayout(FlowLayout.LEFT));
        displayPanel.add(showImageBox);
        displayPanel.add(showDataBox);
        displayPanel.add(showLogBox);
        addRow(panel, gbc, row++, "显示选项:", displayPanel);

        gbc.gridx = 0;
        gbc.gridy = row;
        gbc.gridwidth = 2;
        gbc.weighty = 1.0;
        panel.add(Box.createVerticalGlue(), gbc);

        // Attach update logic
        nameField.addActionListener(e -> grid.setName(nameField.getText()));
        nameField.addFocusListener(new java.awt.event.FocusAdapter() {
            @Override
            public void focusLost(java.awt.event.FocusEvent e) {
                grid.setName(nameField.getText());
            }
        });
        enabledBox.addActionListener(e -> grid.setEnabled(enabledBox.isSelected()));
        hostField.addActionListener(e -> grid.setBrokerHost(hostField.getText()));
        hostField.addFocusListener(new java.awt.event.FocusAdapter() {
            @Override
            public void focusLost(java.awt.event.FocusEvent e) {
                grid.setBrokerHost(hostField.getText());
            }
        });
        portSpinner.addChangeListener(e -> grid.setBrokerPort((Integer) portSpinner.getValue()));
        clientIdField.addActionListener(e -> grid.setClientId(clientIdField.getText()));
        clientIdField.addFocusListener(new java.awt.event.FocusAdapter() {
            @Override
            public void focusLost(java.awt.event.FocusEvent e) {
                grid.setClientId(clientIdField.getText());
            }
        });
        imageTopicField.addActionListener(e -> grid.setImageTopic(imageTopicField.getText()));
        imageTopicField.addFocusListener(new java.awt.event.FocusAdapter() {
            @Override
            public void focusLost(java.awt.event.FocusEvent e) {
                grid.setImageTopic(imageTopicField.getText());
            }
        });
        dataTopicField.addActionListener(e -> grid.setDataTopic(dataTopicField.getText()));
        dataTopicField.addFocusListener(new java.awt.event.FocusAdapter() {
            @Override
            public void focusLost(java.awt.event.FocusEvent e) {
                grid.setDataTopic(dataTopicField.getText());
            }
        });
        logTopicField.addActionListener(e -> grid.setLogTopic(logTopicField.getText()));
        logTopicField.addFocusListener(new java.awt.event.FocusAdapter() {
            @Override
            public void focusLost(java.awt.event.FocusEvent e) {
                grid.setLogTopic(logTopicField.getText());
            }
        });
        showImageBox.addActionListener(e -> grid.setShowImage(showImageBox.isSelected()));
        showDataBox.addActionListener(e -> grid.setShowData(showDataBox.isSelected()));
        showLogBox.addActionListener(e -> grid.setShowLog(showLogBox.isSelected()));

        return panel;
    }

    private static void addRow(JPanel panel, GridBagConstraints gbc, int row, String label, JComponent field) {
        gbc.gridx = 0;
        gbc.gridy = row;
        gbc.gridwidth = 1;
        gbc.weightx = 0.0;
        panel.add(new JLabel(label), gbc);

        gbc.gridx = 1;
        gbc.weightx = 1.0;
        panel.add(field, gbc);
    }

    private void apply() {
        mainFrame.applyConfig(workingConfig);
        applied = true;
        dispose();
    }

    public boolean isApplied() {
        return applied;
    }

    private static AppConfig copyConfig(AppConfig source) {
        com.google.gson.Gson gson = new com.google.gson.Gson();
        String json = gson.toJson(source);
        AppConfig copy = gson.fromJson(json, AppConfig.class);
        if (copy == null) {
            return new AppConfig();
        }
        return copy;
    }
}
