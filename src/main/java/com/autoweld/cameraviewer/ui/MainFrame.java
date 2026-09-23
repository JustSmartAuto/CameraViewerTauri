package com.autoweld.cameraviewer.ui;

import com.autoweld.cameraviewer.config.AppConfig;
import com.autoweld.cameraviewer.config.ConfigManager;
import com.autoweld.cameraviewer.model.*;
import com.autoweld.cameraviewer.mqtt.MqttClientManager;
import com.autoweld.cameraviewer.mqtt.MqttMessageListener;
import com.autoweld.cameraviewer.script.ScriptEngine;
import com.autoweld.cameraviewer.script.draw.DrawingCommand;
import com.autoweld.cameraviewer.xml.XmlParser;

import javax.swing.*;
import java.awt.*;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.logging.Level;
import java.util.logging.Logger;

public class MainFrame extends JFrame implements MqttMessageListener {
    private static final Logger LOGGER = Logger.getLogger(MainFrame.class.getName());
    private static final String TITLE = "电磁系统相机显示软件";

    private final AppConfig config;
    private final ConfigManager configManager;
    private final MqttClientManager mqttClientManager;
    private final ScriptEngine scriptEngine;
    private final XmlParser xmlParser;
    private final List<GridPanel> gridPanels;

    public MainFrame(AppConfig config, ConfigManager configManager) {
        super(TITLE);
        this.config = config;
        this.configManager = configManager;
        this.mqttClientManager = new MqttClientManager(this);
        this.scriptEngine = new ScriptEngine();
        this.xmlParser = new XmlParser();
        this.gridPanels = new ArrayList<>(AppConfig.GRID_COUNT);
        initialize();
    }

    private void initialize() {
        setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        setSize(1600, 1200);
        setLocationRelativeTo(null);
        if (config.isMaximizeOnStartup()) {
            setExtendedState(getExtendedState() | JFrame.MAXIMIZED_BOTH);
        }

        setupMenuBar();

        setLayout(new GridLayout(2, 2, 4, 4));
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            GridPanel panel = new GridPanel(i);
            panel.setMainFrame(this);
            panel.updateConfig(config.getGrid(i));
            if (config.isShowCheckerboardWhenNoImage()) {
                panel.setCheckerboardImage();
            }
            gridPanels.add(panel);
            add(panel);
        }

        // 为每个脚本上下文注入当前格子的原图获取回调，SaveRenderedImage 在此基础上叠加脚本绘制内容
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            final int gridIndex = i;
            scriptEngine.getContext(gridIndex).setRenderedImageSupplier(() -> gridPanels.get(gridIndex).getCurrentImage());
        }

        loadIcon();

        // 加载脚本
        scriptEngine.applyConfig(config);

        // 启动 MQTT 连接
        SwingUtilities.invokeLater(() -> mqttClientManager.applyConfig(config));

        // 关闭时断开 MQTT 和脚本引擎
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            mqttClientManager.disconnectAll();
            scriptEngine.close();
        }));
    }

    public void applyConfig(AppConfig newConfig) {
        this.config.setGrids(newConfig.getGrids());
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            GridConfig gridConfig = config.getGrid(i);
            gridPanels.get(i).updateConfig(gridConfig);
        }
        scriptEngine.applyConfig(config);
        mqttClientManager.applyConfig(config);
        configManager.saveQuietly(config);
    }

    public AppConfig getConfig() {
        return config;
    }

    public ConfigManager getConfigManager() {
        return configManager;
    }

    public ScriptEngine getScriptEngine() {
        return scriptEngine;
    }

    public List<GridPanel> getGridPanels() {
        return gridPanels;
    }

    private void setupMenuBar() {
        JMenuBar menuBar = new JMenuBar();

        JMenu fileMenu = new JMenu("文件");
        JMenuItem settingsItem = new JMenuItem("设置");
        settingsItem.addActionListener(e -> new SettingsDialog(this).setVisible(true));
        fileMenu.add(settingsItem);
        fileMenu.addSeparator();
        JMenuItem exitItem = new JMenuItem("退出");
        exitItem.addActionListener(e -> System.exit(0));
        fileMenu.add(exitItem);

        JMenu scriptMenu = new JMenu("脚本");
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            final int gridIndex = i;
            JMenuItem item = new JMenuItem("编辑格子 " + (i + 1) + " 脚本");
            item.addActionListener(e -> new ScriptEditorDialog(this, gridIndex).setVisible(true));
            scriptMenu.add(item);
        }

        JMenu viewMenu = new JMenu("视图");
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            final int gridIndex = i;
            JMenu gridViewMenu = new JMenu("格子 " + (i + 1));

            GridConfig gridConfig = config.getGrid(i);
            JCheckBoxMenuItem dataItem = new JCheckBoxMenuItem("数据显示", gridConfig.isShowData());
            JCheckBoxMenuItem logItem = new JCheckBoxMenuItem("日志显示", gridConfig.isShowLog());

            dataItem.addActionListener(e -> {
                config.getGrid(gridIndex).setShowData(dataItem.isSelected());
                gridPanels.get(gridIndex).updateConfig(config.getGrid(gridIndex));
                configManager.saveQuietly(config);
            });
            logItem.addActionListener(e -> {
                config.getGrid(gridIndex).setShowLog(logItem.isSelected());
                gridPanels.get(gridIndex).updateConfig(config.getGrid(gridIndex));
                configManager.saveQuietly(config);
            });

            gridViewMenu.add(dataItem);
            gridViewMenu.add(logItem);
            viewMenu.add(gridViewMenu);
        }

        JMenu helpMenu = new JMenu("帮助");
        JMenuItem aboutItem = new JMenuItem("关于");
        aboutItem.addActionListener(e -> JOptionPane.showMessageDialog(this,
                "电磁系统相机显示软件 v1.0.0\n基于 Java + FlatLaf + MQTT + QuickJS",
                "关于", JOptionPane.INFORMATION_MESSAGE));
        helpMenu.add(aboutItem);

        menuBar.add(fileMenu);
        menuBar.add(scriptMenu);
        menuBar.add(viewMenu);
        menuBar.add(helpMenu);
        setJMenuBar(menuBar);
    }

    @Override
    public void onMessage(int gridIndex, MessageType type, String payload) {
        SwingUtilities.invokeLater(() -> handleMessage(gridIndex, type, payload));
    }

    private void handleMessage(int gridIndex, MessageType type, String payload) {
        GridPanel panel = gridPanels.get(gridIndex);

        switch (type) {
            case IMAGE -> {
                CameraImageMessage msg = xmlParser.parseImage(payload);
                if (msg == null) {
                    panel.setImageError("图像 XML 解析失败");
                    return;
                }
                // 按面号分发：格子 1 显示面 1，格子 2 显示面 2，以此类推
                if (msg.getSurfaceNumber() != gridIndex + 1) {
                    return;
                }
                scriptEngine.processMessage(gridIndex, msg);
                String base64 = scriptEngine.getContext(gridIndex).getCurrentImageBase64();
                if (base64 == null || base64.isEmpty()) {
                    base64 = msg.getImageData();
                }
                if (base64 != null && !base64.isEmpty()) {
                    panel.setBase64Image(base64);
                } else if (config.isShowCheckerboardWhenNoImage()) {
                    panel.setCheckerboardImage();
                }
                List<DrawingCommand> commands = scriptEngine.getContext(gridIndex).getCommands();
                panel.setDrawingCommands(commands);
            }
            case DATA -> {
                CameraDataMessage msg = xmlParser.parseData(payload);
                if (msg == null) {
                    return;
                }
                // 按面号分发：格子 1 显示面 1，以此类推
                if (msg.getSurfaceNumber() != gridIndex + 1) {
                    return;
                }
                scriptEngine.processMessage(gridIndex, msg);
                panel.setData(msg);
                List<DrawingCommand> commands = scriptEngine.getContext(gridIndex).getCommands();
                panel.setDrawingCommands(commands);
            }
            case LOG -> {
                CameraLogMessage msg = xmlParser.parseLog(payload);
                if (msg == null) {
                    return;
                }
                // 按面号分发：格子 1 显示面 1，以此类推
                if (msg.getSurfaceNumber() != gridIndex + 1) {
                    return;
                }
                scriptEngine.processMessage(gridIndex, msg);
                panel.appendLog(msg);
            }
        }

        // 处理脚本 Clear() 请求
        if (scriptEngine.getContext(gridIndex).isClearImageRequested()) {
            panel.clearImage();
            scriptEngine.getContext(gridIndex).resetClearImageRequested();
        }

        String error = scriptEngine.getContext(gridIndex).getLastError();
        if (error != null) {
            panel.setImageError("脚本错误: " + error);
        }
    }

    @Override
    public void onConnectionChanged(int gridIndex, boolean connected, String reason) {
        SwingUtilities.invokeLater(() -> gridPanels.get(gridIndex).setConnected(connected, reason));
    }

    private void loadIcon() {
        try (InputStream is = MainFrame.class.getClassLoader().getResourceAsStream("green_windows.jpg")) {
            if (is != null) {
                ImageIcon icon = new ImageIcon(is.readAllBytes());
                setIconImage(icon.getImage());
            } else {
                LOGGER.warning("Icon resource green_windows.jpg not found");
            }
        } catch (IOException e) {
            LOGGER.log(Level.WARNING, "Failed to load icon", e);
        }
    }
}
