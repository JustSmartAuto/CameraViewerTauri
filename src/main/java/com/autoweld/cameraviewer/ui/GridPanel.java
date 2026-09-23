package com.autoweld.cameraviewer.ui;

import com.autoweld.cameraviewer.model.CameraDataMessage;
import com.autoweld.cameraviewer.model.CameraLogMessage;
import com.autoweld.cameraviewer.model.GridConfig;
import com.autoweld.cameraviewer.script.draw.DrawingCommand;

import javax.swing.*;
import java.awt.*;
import java.awt.image.BufferedImage;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.List;

/**
 * 单个画面格子面板：显示图像、测量数据、日志和连接状态。
 */
public class GridPanel extends JPanel {
    private final int gridIndex;
    private final JLabel titleLabel;
    private final JLabel statusLabel;
    private final ImageCanvas imageCanvas;
    private final JTextArea dataTextArea;
    private final JTextArea logTextArea;
    private final JTabbedPane tabbedPane;
    private final JToggleButton showDrawingsButton;
    private final JLabel infoLabel;
    private MainFrame mainFrame;

    private int imageCount;
    private int dataCount;
    private int logCount;

    public GridPanel(int gridIndex) {
        this.gridIndex = gridIndex;
        setLayout(new BorderLayout(4, 4));
        setBorder(BorderFactory.createEmptyBorder(4, 4, 4, 4));

        // Header
        titleLabel = new JLabel("画面 " + (gridIndex + 1));
        statusLabel = new JLabel("● 未连接");
        statusLabel.setForeground(Color.GRAY);
        infoLabel = new JLabel("等待数据...");

        JPanel header = new JPanel(new BorderLayout(8, 0));
        header.add(titleLabel, BorderLayout.WEST);
        header.add(infoLabel, BorderLayout.CENTER);
        header.add(statusLabel, BorderLayout.EAST);
        add(header, BorderLayout.NORTH);

        // Center panel: toolbar + image canvas
        imageCanvas = new ImageCanvas();

        JPanel toolBar = new JPanel(new FlowLayout(FlowLayout.LEFT, 4, 2));
        JButton fitButton = new JButton("适合");
        JButton actualButton = new JButton("最大化");
        JButton zoomInButton = new JButton("放大");
        JButton zoomOutButton = new JButton("缩小");
        showDrawingsButton = new JToggleButton("隐藏绘制", true);
        fitButton.setToolTipText("适应窗口");
        actualButton.setToolTipText("实际大小");
        zoomInButton.setToolTipText("放大");
        zoomOutButton.setToolTipText("缩小");
        showDrawingsButton.setToolTipText("显示/隐藏脚本绘制内容");
        fitButton.addActionListener(e -> imageCanvas.fit());
        actualButton.addActionListener(e -> imageCanvas.actualSize());
        zoomInButton.addActionListener(e -> imageCanvas.zoomIn());
        zoomOutButton.addActionListener(e -> imageCanvas.zoomOut());
        showDrawingsButton.addActionListener(e -> {
            boolean selected = showDrawingsButton.isSelected();
            imageCanvas.setShowCommands(selected);
            showDrawingsButton.setText(selected ? "隐藏绘制" : "显示绘制");
            if (mainFrame != null) {
                mainFrame.getConfig().getGrid(gridIndex).setShowDrawings(selected);
                mainFrame.getConfigManager().saveQuietly(mainFrame.getConfig());
            }
        });
        toolBar.add(fitButton);
        toolBar.add(actualButton);
        toolBar.add(zoomInButton);
        toolBar.add(zoomOutButton);
        toolBar.add(showDrawingsButton);

        JPanel centerPanel = new JPanel(new BorderLayout());
        centerPanel.add(toolBar, BorderLayout.NORTH);
        centerPanel.add(imageCanvas, BorderLayout.CENTER);
        add(centerPanel, BorderLayout.CENTER);

        // South: data + log tabs
        dataTextArea = createTextArea();
        logTextArea = createTextArea();

        tabbedPane = new JTabbedPane();
        tabbedPane.addTab("数据", new JScrollPane(dataTextArea));
        tabbedPane.addTab("日志", new JScrollPane(logTextArea));
        tabbedPane.setPreferredSize(new Dimension(0, 120));
        add(tabbedPane, BorderLayout.SOUTH);
    }

    private JTextArea createTextArea() {
        JTextArea area = new JTextArea();
        area.setEditable(false);
        area.setFont(new Font(Font.MONOSPACED, Font.PLAIN, 12));
        return area;
    }

    public void setMainFrame(MainFrame mainFrame) {
        this.mainFrame = mainFrame;
    }

    public void updateConfig(GridConfig config) {
        titleLabel.setText(config.getName());
        showDrawingsButton.setSelected(config.isShowDrawings());
        showDrawingsButton.setText(config.isShowDrawings() ? "隐藏绘制" : "显示绘制");
        imageCanvas.setShowCommands(config.isShowDrawings());

        boolean showData = config.isShowData();
        boolean showLog = config.isShowLog();
        tabbedPane.setVisible(showData || showLog);
        tabbedPane.removeAll();
        if (showData) {
            tabbedPane.addTab("数据", new JScrollPane(dataTextArea));
        }
        if (showLog) {
            tabbedPane.addTab("日志", new JScrollPane(logTextArea));
        }
        revalidate();
        repaint();
    }

    public void setConnected(boolean connected, String reason) {
        if (connected) {
            statusLabel.setText("● 已连接");
            statusLabel.setForeground(new Color(0, 170, 0));
        } else {
            statusLabel.setText("● " + (reason != null ? reason : "未连接"));
            statusLabel.setForeground(Color.RED);
        }
    }

    public void setImage(BufferedImage image) {
        imageCanvas.setImage(image);
    }

    public void setCheckerboardImage() {
        imageCount++;
        updateCounters();
        imageCanvas.setCheckerboardImage();
    }

    public void setBase64Image(String base64) {
        imageCount++;
        updateCounters();
        imageCanvas.setBase64Image(base64);
    }

    public void setDrawingCommands(List<DrawingCommand> commands) {
        imageCanvas.setCommands(commands);
    }

    public BufferedImage getRenderedImage() {
        return imageCanvas.getRenderedImage();
    }

    public BufferedImage getCurrentImage() {
        return imageCanvas.getCurrentImage();
    }

    public void clearImage() {
        imageCanvas.clear();
    }

    public void setImageError(String error) {
        imageCanvas.setError(error);
    }

    public void setData(CameraDataMessage data) {
        if (data == null) return;
        dataCount++;
        StringBuilder sb = new StringBuilder();
        sb.append("时间戳: ").append(data.getTimestamp()).append("\n");
        sb.append("配方号: ").append(data.getProductNumber())
                .append("  面号: ").append(data.getSurfaceNumber()).append("\n");
        sb.append("图像尺寸: ").append(data.getImageWidth()).append(" x ").append(data.getImageHeight()).append("\n");
        sb.append("--- 测量值 ---\n");
        sb.append(String.format("产品宽度: %.3f\n", data.getProductWidth()));
        sb.append(String.format("产品高度: %.3f\n", data.getProductHeight()));
        sb.append(String.format("对称度:   %.3f\n", data.getSymmetry()));
        sb.append(String.format("左点: (%.2f, %.2f)\n", data.getLeftPointX(), data.getLeftPointY()));
        sb.append(String.format("右点: (%.2f, %.2f)\n", data.getRightPointX(), data.getRightPointY()));
        sb.append(String.format("顶点: (%.2f, %.2f)\n", data.getTopPointX(), data.getTopPointY()));
        dataTextArea.setText(sb.toString());
        updateCounters();
    }

    public void appendLog(CameraLogMessage log) {
        if (log == null) return;
        logCount++;
        String time = log.getTimestamp();
        if (time == null || time.isEmpty()) {
            time = new SimpleDateFormat("HH:mm:ss").format(new Date());
        }
        logTextArea.append("[" + time + "] " + log.getMessage() + "\n");
        // Auto scroll
        logTextArea.setCaretPosition(logTextArea.getDocument().getLength());
        updateCounters();
    }

    public void clear() {
        imageCanvas.clear();
        dataTextArea.setText("");
        logTextArea.setText("");
        imageCount = 0;
        dataCount = 0;
        logCount = 0;
        updateCounters();
    }

    private void updateCounters() {
        infoLabel.setText(String.format("图像: %d | 数据: %d | 日志: %d", imageCount, dataCount, logCount));
    }
}
