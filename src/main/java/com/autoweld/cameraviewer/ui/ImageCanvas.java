package com.autoweld.cameraviewer.ui;

import com.autoweld.cameraviewer.script.draw.DrawingCommand;

import javax.swing.*;
import java.awt.*;
import java.awt.event.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Point2D;
import java.awt.image.BufferedImage;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Collections;
import java.util.List;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 支持缩放、平移的图像与脚本叠加图形渲染画布。
 */
public class ImageCanvas extends JPanel {
    private static final Logger LOGGER = Logger.getLogger(ImageCanvas.class.getName());
    private static final double MIN_SCALE = 0.05;
    private static final double MAX_SCALE = 10.0;
    private static final double ZOOM_STEP = 1.2;

    // 5120x5120 对应的棋盘格占位图尺寸
    private static final int CHECKERBOARD_SCREEN_SIZE = 5120;
    private static final int CHECKERBOARD_SQUARES = 16;
    private static final int CHECKERBOARD_SQUARE_SIZE = CHECKERBOARD_SCREEN_SIZE / CHECKERBOARD_SQUARES;
    private static BufferedImage checkerboardImage;

    public static BufferedImage getCheckerboardImage() {
        if (checkerboardImage != null) {
            return checkerboardImage;
        }
        checkerboardImage = new BufferedImage(CHECKERBOARD_SCREEN_SIZE, CHECKERBOARD_SCREEN_SIZE, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = checkerboardImage.createGraphics();
        for (int y = 0; y < CHECKERBOARD_SQUARES; y++) {
            for (int x = 0; x < CHECKERBOARD_SQUARES; x++) {
                g.setColor((x + y) % 2 == 0 ? Color.DARK_GRAY : Color.LIGHT_GRAY);
                g.fillRect(x * CHECKERBOARD_SQUARE_SIZE, y * CHECKERBOARD_SQUARE_SIZE,
                        CHECKERBOARD_SQUARE_SIZE, CHECKERBOARD_SQUARE_SIZE);
            }
        }
        g.dispose();
        return checkerboardImage;
    }

    private static BufferedImage calibboardImage;

    public static BufferedImage getCalibboardImage() {
        if (calibboardImage != null) {
            return calibboardImage;
        }
        try (java.io.InputStream is = ImageCanvas.class.getClassLoader().getResourceAsStream("imgs/calibboard.jpg")) {
            if (is != null) {
                calibboardImage = javax.imageio.ImageIO.read(is);
                if (calibboardImage != null) {
                    return calibboardImage;
                }
            }
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to load imgs/calibboard.jpg, fallback to checkerboard", e);
        }
        return getCheckerboardImage();
    }

    private BufferedImage currentImage;
    private int imageWidth;
    private int imageHeight;
    private List<DrawingCommand> commands = Collections.emptyList();
    private String errorMessage;

    private double scale = 1.0;
    private double offsetX = 0.0;
    private double offsetY = 0.0;
    private boolean dragging = false;
    private int lastDragX;
    private int lastDragY;
    private boolean fitNeeded = false;
    private double deferredScale = -1;
    private boolean showCommands = true;

    public ImageCanvas() {
        setBackground(Color.BLACK);
        setOpaque(true);
        setupMouseListeners();
    }

    private void setupMouseListeners() {
        MouseAdapter adapter = new MouseAdapter() {
            @Override
            public void mouseWheelMoved(MouseWheelEvent e) {
                int rotation = e.getWheelRotation();
                if (rotation < 0) {
                    zoomInAt(e.getX(), e.getY());
                } else if (rotation > 0) {
                    zoomOutAt(e.getX(), e.getY());
                }
            }

            @Override
            public void mousePressed(MouseEvent e) {
                if (SwingUtilities.isLeftMouseButton(e)) {
                    dragging = true;
                    lastDragX = e.getX();
                    lastDragY = e.getY();
                    setCursor(Cursor.getPredefinedCursor(Cursor.MOVE_CURSOR));
                }
            }

            @Override
            public void mouseReleased(MouseEvent e) {
                dragging = false;
                setCursor(Cursor.getDefaultCursor());
            }

            @Override
            public void mouseDragged(MouseEvent e) {
                if (dragging) {
                    int dx = e.getX() - lastDragX;
                    int dy = e.getY() - lastDragY;
                    offsetX += dx;
                    offsetY += dy;
                    lastDragX = e.getX();
                    lastDragY = e.getY();
                    repaint();
                }
            }
        };
        addMouseListener(adapter);
        addMouseMotionListener(adapter);
        addMouseWheelListener(adapter);
    }

    public void setImage(BufferedImage image) {
        this.currentImage = image;
        if (image != null) {
            this.imageWidth = image.getWidth();
            this.imageHeight = image.getHeight();
        } else {
            this.imageWidth = 0;
            this.imageHeight = 0;
        }
        fitNeeded = true;
        fit();
    }

    public void setCheckerboardImage() {
        BufferedImage placeholder = getCalibboardImage();
        this.currentImage = placeholder;
        // 标定板/棋盘格按 5120x5120 的图像坐标系处理
        this.imageWidth = 5120;
        this.imageHeight = 5120;
        // 默认缩放 15% 并居中显示
        fitNeeded = false;
        scale = 0.15;
        centerWithScale(scale);
    }

    private void centerWithScale(double newScale) {
        if (imageWidth <= 0 || imageHeight <= 0) {
            scale = 1.0;
            offsetX = 0.0;
            offsetY = 0.0;
            repaint();
            return;
        }
        int cw = getWidth();
        int ch = getHeight();
        if (cw <= 0 || ch <= 0) {
            // 画布尚未布局，延迟到 paintComponent 中居中
            fitNeeded = true;
            deferredScale = newScale;
            return;
        }
        scale = newScale;
        offsetX = (cw - imageWidth * scale) / 2.0;
        offsetY = (ch - imageHeight * scale) / 2.0;
        deferredScale = -1;
        repaint();
    }

    public void setBase64Image(String base64) {
        if (base64 == null || base64.trim().isEmpty()) {
            setImage(null);
            return;
        }
        try {
            String data = base64;
            if (data.contains(",")) {
                data = data.substring(data.indexOf(',') + 1);
            }
            byte[] bytes = Base64.getDecoder().decode(data);
            BufferedImage img = javax.imageio.ImageIO.read(new java.io.ByteArrayInputStream(bytes));
            setImage(img);
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to decode base64 image", e);
            setError("图像解码失败: " + e.getMessage());
        }
    }

    public void setCommands(List<DrawingCommand> commands) {
        this.commands = commands != null ? new ArrayList<>(commands) : Collections.emptyList();
        repaint();
    }

    public void setError(String errorMessage) {
        this.errorMessage = errorMessage;
        repaint();
    }

    public void clear() {
        currentImage = null;
        imageWidth = 0;
        imageHeight = 0;
        commands = Collections.emptyList();
        errorMessage = null;
        scale = 1.0;
        offsetX = 0.0;
        offsetY = 0.0;
        repaint();
    }

    /**
     * 适应窗口：图像完整显示在画布内并居中。
     */
    public void fit() {
        if (imageWidth <= 0 || imageHeight <= 0) {
            scale = 1.0;
            offsetX = 0.0;
            offsetY = 0.0;
            fitNeeded = false;
            repaint();
            return;
        }

        int cw = getWidth();
        int ch = getHeight();
        if (cw <= 0 || ch <= 0) {
            // 画布尚未布局，延迟到 paintComponent 中再适应
            fitNeeded = true;
            return;
        }

        double scaleX = (double) cw / imageWidth;
        double scaleY = (double) ch / imageHeight;
        scale = Math.min(scaleX, scaleY);
        offsetX = (cw - imageWidth * scale) / 2.0;
        offsetY = (ch - imageHeight * scale) / 2.0;
        fitNeeded = false;
        repaint();
    }

    /**
     * 实际大小：1 个图像像素对应 1 个屏幕像素。
     */
    public void actualSize() {
        if (imageWidth <= 0 || imageHeight <= 0) {
            return;
        }
        scale = 1.0;
        offsetX = (getWidth() - imageWidth) / 2.0;
        offsetY = (getHeight() - imageHeight) / 2.0;
        repaint();
    }

    public void zoomIn() {
        zoomAt(getWidth() / 2.0, getHeight() / 2.0, scale * ZOOM_STEP);
    }

    public void zoomOut() {
        zoomAt(getWidth() / 2.0, getHeight() / 2.0, scale / ZOOM_STEP);
    }

    private void zoomInAt(double screenX, double screenY) {
        zoomAt(screenX, screenY, scale * ZOOM_STEP);
    }

    private void zoomOutAt(double screenX, double screenY) {
        zoomAt(screenX, screenY, scale / ZOOM_STEP);
    }

    private void zoomAt(double screenX, double screenY, double newScale) {
        if (imageWidth <= 0 || imageHeight <= 0) {
            return;
        }
        newScale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, newScale));
        if (Math.abs(newScale - scale) < 1e-9) {
            return;
        }

        // 保持鼠标指向的图像点位置不变
        double imageX = (screenX - offsetX) / scale;
        double imageY = (screenY - offsetY) / scale;
        offsetX = screenX - imageX * newScale;
        offsetY = screenY - imageY * newScale;
        scale = newScale;

        repaint();
    }

    public double getScale() {
        return scale;
    }

    public AffineTransform getImageToScreenTransform() {
        AffineTransform transform = new AffineTransform();
        transform.translate(offsetX, offsetY);
        transform.scale(scale, scale);
        return transform;
    }

    public void setShowCommands(boolean showCommands) {
        this.showCommands = showCommands;
        repaint();
    }

    public boolean isShowCommands() {
        return showCommands;
    }

    public BufferedImage getCurrentImage() {
        return currentImage;
    }

    /**
     * 获取当前图像与脚本叠加图形渲染后的图像（原始分辨率）。
     */
    public BufferedImage getRenderedImage() {
        if (currentImage == null) {
            return null;
        }
        BufferedImage rendered = new BufferedImage(imageWidth, imageHeight, BufferedImage.TYPE_INT_RGB);
        Graphics2D g2d = rendered.createGraphics();
        g2d.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g2d.drawImage(currentImage, 0, 0, null);
        if (showCommands) {
            AffineTransform identity = new AffineTransform();
            for (DrawingCommand cmd : commands) {
                try {
                    cmd.draw(g2d, identity);
                } catch (Exception e) {
                    LOGGER.log(Level.WARNING, "Drawing command failed in getRenderedImage", e);
                }
            }
        }
        g2d.dispose();
        return rendered;
    }

    @Override
    protected void paintComponent(Graphics g) {
        super.paintComponent(g);

        // 若之前因画布未布局导致 fit/居中 失败，在首次绘制时重新应用
        if (getWidth() > 0 && getHeight() > 0) {
            if (deferredScale > 0) {
                centerWithScale(deferredScale);
            } else if (fitNeeded) {
                fit();
            }
        }

        Graphics2D g2d = (Graphics2D) g;
        g2d.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g2d.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);

        if (errorMessage != null) {
            g2d.setColor(Color.RED);
            g2d.drawString(errorMessage, 10, 20);
            return;
        }

        AffineTransform transform = getImageToScreenTransform();

        if (currentImage != null) {
            g2d.drawImage(currentImage, transform, this);
        }

        if (showCommands) {
            for (DrawingCommand cmd : commands) {
                try {
                    cmd.draw(g2d, transform);
                } catch (Exception e) {
                    LOGGER.log(Level.WARNING, "Drawing command failed", e);
                }
            }
        }

        // 绘制缩放比例提示
        g2d.setColor(Color.YELLOW);
        g2d.drawString(String.format("%.0f%%", scale * 100), 10, 20);
    }
}
