package com.autoweld.cameraviewer.ui;

import cn.net.zhijian.quickjs.QuickJSException;
import com.autoweld.cameraviewer.config.AppConfig;
import com.autoweld.cameraviewer.model.CameraDataMessage;
import com.autoweld.cameraviewer.model.CameraImageMessage;
import com.autoweld.cameraviewer.model.CameraLogMessage;
import com.autoweld.cameraviewer.model.GridConfig;
import com.autoweld.cameraviewer.mqtt.MqttMessageListener;
import com.autoweld.cameraviewer.script.ScriptContext;
import com.autoweld.cameraviewer.script.ScriptConsole;
import com.autoweld.cameraviewer.script.draw.DrawingCommand;
import com.autoweld.cameraviewer.xml.XmlParser;
import org.fife.ui.rsyntaxtextarea.RSyntaxTextArea;
import org.fife.ui.rsyntaxtextarea.SyntaxConstants;
import org.fife.ui.rtextarea.RTextScrollPane;

import javax.swing.*;
import java.awt.*;
import java.awt.image.BufferedImage;
import java.util.List;

/**
 * 脚本编辑与调试对话框。
 */
public class ScriptEditorDialog extends JDialog {

    private final MainFrame mainFrame;
    private final RSyntaxTextArea scriptEditor;
    private final JTextArea testDataEditor;
    private final JTextArea consoleArea;
    private final JComboBox<Integer> gridSelector;
    private final JComboBox<MessageTypeItem> typeSelector;
    private final ImageCanvas previewCanvas;
    private final XmlParser xmlParser = new XmlParser();

    public ScriptEditorDialog(MainFrame owner, int initialGridIndex) {
        super(owner, "脚本编辑器", true);
        this.mainFrame = owner;

        scriptEditor = new RSyntaxTextArea(25, 60);
        scriptEditor.setSyntaxEditingStyle(SyntaxConstants.SYNTAX_STYLE_JAVASCRIPT);
        scriptEditor.setCodeFoldingEnabled(true);
        scriptEditor.setAntiAliasingEnabled(true);

        testDataEditor = new JTextArea(15, 40);
        consoleArea = new JTextArea(6, 40);
        consoleArea.setEditable(false);
        consoleArea.setFont(new Font(Font.MONOSPACED, Font.PLAIN, 12));

        previewCanvas = new ImageCanvas();
        previewCanvas.setPreferredSize(new Dimension(320, 240));

        gridSelector = new JComboBox<>();
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            gridSelector.addItem(i);
        }
        gridSelector.setSelectedIndex(initialGridIndex);
        gridSelector.setRenderer(new DefaultListCellRenderer() {
            @Override
            public Component getListCellRendererComponent(JList<?> list, Object value, int index,
                                                          boolean isSelected, boolean cellHasFocus) {
                super.getListCellRendererComponent(list, value, index, isSelected, cellHasFocus);
                if (value instanceof Integer) {
                    int idx = (Integer) value;
                    setText((idx + 1) + " - " + owner.getConfig().getGrid(idx).getName());
                }
                return this;
            }
        });

        typeSelector = new JComboBox<>();
        typeSelector.addItem(new MessageTypeItem("图像", MqttMessageListener.MessageType.IMAGE));
        typeSelector.addItem(new MessageTypeItem("数据", MqttMessageListener.MessageType.DATA));
        typeSelector.addItem(new MessageTypeItem("日志", MqttMessageListener.MessageType.LOG));

        initialize();
        loadCurrentGridScript();

        gridSelector.addActionListener(e -> loadCurrentGridScript());
    }

    private void initialize() {
        setDefaultCloseOperation(DISPOSE_ON_CLOSE);
        setSize(1200, 900);
        setLocationRelativeTo(mainFrame);
        setLayout(new BorderLayout(8, 8));

        // Top toolbar
        JPanel toolbar = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 8));
        toolbar.add(new JLabel("格子:"));
        toolbar.add(gridSelector);
        toolbar.add(new JLabel("消息类型:"));
        toolbar.add(typeSelector);
        JButton loadTemplateButton = new JButton("加载测试模板");
        loadTemplateButton.addActionListener(e -> loadTestTemplate());
        toolbar.add(loadTemplateButton);
        add(toolbar, BorderLayout.NORTH);

        // Center split: editor | test data + preview
        JSplitPane centerSplit = new JSplitPane(JSplitPane.HORIZONTAL_SPLIT);
        centerSplit.setLeftComponent(new RTextScrollPane(scriptEditor));

        JPanel rightPanel = new JPanel(new BorderLayout(4, 4));
        rightPanel.setBorder(BorderFactory.createTitledBorder("测试数据 (XML)"));
        rightPanel.add(new JScrollPane(testDataEditor), BorderLayout.CENTER);

        JPanel previewPanel = new JPanel(new BorderLayout());
        previewPanel.setBorder(BorderFactory.createTitledBorder("预览"));
        previewPanel.add(previewCanvas, BorderLayout.CENTER);
        previewPanel.setPreferredSize(new Dimension(340, 260));
        rightPanel.add(previewPanel, BorderLayout.SOUTH);

        centerSplit.setRightComponent(rightPanel);
        centerSplit.setResizeWeight(0.6);
        add(centerSplit, BorderLayout.CENTER);

        // Bottom: console + buttons
        JPanel bottomPanel = new JPanel(new BorderLayout(4, 4));
        bottomPanel.setBorder(BorderFactory.createTitledBorder("控制台"));
        bottomPanel.add(new JScrollPane(consoleArea), BorderLayout.CENTER);

        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.RIGHT, 8, 8));
        JButton runButton = new JButton("运行测试");
        JButton saveButton = new JButton("保存到配置");
        JButton applyButton = new JButton("应用并关闭");
        JButton closeButton = new JButton("关闭");

        runButton.addActionListener(e -> runTest());
        saveButton.addActionListener(e -> saveScript(false));
        applyButton.addActionListener(e -> {
            saveScript(true);
            dispose();
        });
        closeButton.addActionListener(e -> dispose());

        buttons.add(runButton);
        buttons.add(saveButton);
        buttons.add(applyButton);
        buttons.add(closeButton);
        bottomPanel.add(buttons, BorderLayout.SOUTH);

        add(bottomPanel, BorderLayout.SOUTH);
    }

    private void loadCurrentGridScript() {
        int gridIndex = (Integer) gridSelector.getSelectedItem();
        String script = mainFrame.getConfig().getGrid(gridIndex).getScript();
        scriptEditor.setText(script != null ? script : "");
        scriptEditor.setCaretPosition(0);
    }

    private void loadTestTemplate() {
        MessageTypeItem item = (MessageTypeItem) typeSelector.getSelectedItem();
        if (item == null) return;
        testDataEditor.setText(switch (item.type) {
            case IMAGE -> IMAGE_TEMPLATE;
            case DATA -> DATA_TEMPLATE;
            case LOG -> LOG_TEMPLATE;
        });
    }

    private void runTest() {
        int gridIndex = (Integer) gridSelector.getSelectedItem();
        MessageTypeItem item = (MessageTypeItem) typeSelector.getSelectedItem();
        if (item == null) return;

        consoleArea.setText("");
        previewCanvas.clear();

        ScriptConsole console = new ScriptConsole(line -> {
            consoleArea.append(line + "\n");
            consoleArea.setCaretPosition(consoleArea.getDocument().getLength());
        });

        try (ScriptContext context = new ScriptContext(gridIndex, console)) {
            context.loadScript(scriptEditor.getText());
            if (context.getLastError() != null) {
                consoleArea.append("脚本加载错误: " + context.getLastError() + "\n");
                return;
            }

            String xml = testDataEditor.getText();
            Object message = switch (item.type) {
                case IMAGE -> xmlParser.parseImage(xml);
                case DATA -> xmlParser.parseData(xml);
                case LOG -> xmlParser.parseLog(xml);
            };

            if (message == null) {
                consoleArea.append("测试数据 XML 解析失败\n");
                return;
            }

            context.processMessage(message);

            String error = context.getLastError();
            if (error != null) {
                consoleArea.append("脚本执行错误: " + error + "\n");
                return;
            }

            List<DrawingCommand> commands = context.getCommands();
            consoleArea.append("生成 " + commands.size() + " 条绘图指令\n");

            String base64 = context.getCurrentImageBase64();
            if (base64 != null && !base64.isEmpty()) {
                previewCanvas.setBase64Image(base64);
            } else if (message instanceof CameraImageMessage) {
                previewCanvas.setBase64Image(((CameraImageMessage) message).getImageData());
            } else {
                previewCanvas.setImage(createPlaceholderImage(item.type));
            }
            previewCanvas.setCommands(commands);

        } catch (QuickJSException e) {
            consoleArea.append("QuickJS 错误: " + e.getMessage() + "\n");
        }
    }

    private void saveScript(boolean apply) {
        int gridIndex = (Integer) gridSelector.getSelectedItem();
        mainFrame.getConfig().getGrid(gridIndex).setScript(scriptEditor.getText());
        mainFrame.getScriptEngine().reloadScript(gridIndex, scriptEditor.getText());
        mainFrame.getConfigManager().saveQuietly(mainFrame.getConfig());
        consoleArea.append("脚本已保存到格子 " + (gridIndex + 1) + "\n");
        if (apply) {
            mainFrame.applyConfig(mainFrame.getConfig());
        }
    }

    private static BufferedImage createPlaceholderImage(MqttMessageListener.MessageType type) {
        BufferedImage img = new BufferedImage(512, 512, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = img.createGraphics();
        g.setColor(Color.DARK_GRAY);
        g.fillRect(0, 0, 512, 512);
        g.setColor(Color.WHITE);
        g.drawString("类型: " + type.name(), 20, 30);
        g.dispose();
        return img;
    }

    private static final String IMAGE_TEMPLATE = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ImageMessage>
            <TimeStamp>202608271857890</TimeStamp>
            <ImageHeight>9344</ImageHeight>
            <ImageWidth>7000</ImageWidth>
            <ProductNumber>1</ProductNumber>
            <SurfaceNumber>1</SurfaceNumber>
            <ImageData></ImageData>
            </ImageMessage>
            """;

    private static final String DATA_TEMPLATE = """
            <?xml version="1.0" encoding="UTF-8"?>
            <DataMessage>
            <TimeStamp>202608271857890</TimeStamp>
            <ImageHeight>7000</ImageHeight>
            <ImageWidth>9344</ImageWidth>
            <ProductNumber>1</ProductNumber>
            <SurfaceNumber>1</SurfaceNumber>
            <BaseLineLeftPointX>10.0</BaseLineLeftPointX>
            <BaseLineLeftPointY>500.0</BaseLineLeftPointY>
            <BaseLineRightPointX>502.0</BaseLineRightPointX>
            <BaseLineRightPointY>500.0</BaseLineRightPointY>
            <LeftPointX>50.0</LeftPointX>
            <LeftPointY>250.0</LeftPointY>
            <RightPointX>462.0</RightPointX>
            <RightPointY>250.0</RightPointY>
            <ProductWidth>412.0</ProductWidth>
            <TopPointX>256.0</TopPointX>
            <TopPointY>50.0</TopPointY>
            <ProductHeight>450.0</ProductHeight>
            <Symmetry>0.12</Symmetry>
            </DataMessage>
            """;

    private static final String LOG_TEMPLATE = """
            <?xml version="1.0" encoding="UTF-8"?>
            <LogMessage>
            <TimeStamp>202608271857890</TimeStamp>
            <ImageHeight>7000</ImageHeight>
            <ImageWidth>9344</ImageWidth>
            <ProductNumber>1</ProductNumber>
            <SurfaceNumber>1</SurfaceNumber>
            <Message>检测完成</Message>
            </LogMessage>
            """;

    private record MessageTypeItem(String label, MqttMessageListener.MessageType type) {
        @Override
        public String toString() {
            return label;
        }
    }
}
