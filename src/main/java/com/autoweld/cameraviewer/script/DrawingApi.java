package com.autoweld.cameraviewer.script;

import cn.net.zhijian.quickjs.JavascriptMethod;
import com.autoweld.cameraviewer.script.draw.*;

import java.awt.*;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 暴露给 JavaScript 的绘图 API。
 * 每个 ScriptContext 拥有独立的 DrawingApi 实例。
 */
public class DrawingApi {
    private static final Logger LOGGER = Logger.getLogger(DrawingApi.class.getName());

    private final List<DrawingCommand> commands = new ArrayList<>();
    private String currentImageBase64;
    private boolean clearImageRequested;

    @JavascriptMethod
    public void PlotString(int imageWidth, int imageHeight,
                           double x, double y,
                           String text, int fontSize, Object color) {
        commands.add(new DrawStringCommand(x, y, text, fontSize, CoordinateMapper.parseColor(color)));
    }

    @JavascriptMethod
    public void PlotLine(int imageWidth, int imageHeight,
                         double x1, double y1, double x2, double y2,
                         double thickness, Object color) {
        commands.add(new DrawLineCommand(x1, y1, x2, y2,
                (float) thickness, CoordinateMapper.parseColor(color)));
    }

    @JavascriptMethod
    public void PlotCircle(int imageWidth, int imageHeight,
                           double centerX, double centerY, double diameter,
                           double thickness, Object color) {
        commands.add(new DrawCircleCommand(centerX, centerY, diameter,
                (float) thickness, CoordinateMapper.parseColor(color)));
    }

    @JavascriptMethod
    public void PlotPolygon(int imageWidth, int imageHeight,
                            Object points, double thickness, Object color) {
        double[] arr = toDoubleArray(points);
        commands.add(new DrawPolygonCommand(arr,
                (float) thickness, CoordinateMapper.parseColor(color)));
    }

    @JavascriptMethod
    public void PlotPoint(int imageWidth, int imageHeight,
                          double x, double y, double thickness, Object color) {
        commands.add(new DrawPointCommand(x, y,
                (float) thickness, CoordinateMapper.parseColor(color)));
    }

    @JavascriptMethod
    public void ShowImage(String base64) {
        this.currentImageBase64 = base64;
        this.clearImageRequested = false;
    }

    @JavascriptMethod
    public void Clear() {
        this.commands.clear();
        this.currentImageBase64 = null;
        this.clearImageRequested = true;
    }

    public boolean isClearImageRequested() {
        return clearImageRequested;
    }

    public void resetClearImageRequested() {
        this.clearImageRequested = false;
    }

    @JavascriptMethod
    public void SaveImage(String base64, String filePath) {
        if (base64 == null || base64.isEmpty() || filePath == null || filePath.isEmpty()) {
            return;
        }
        try {
            String data = base64;
            int commaIndex = base64.indexOf(',');
            if (commaIndex >= 0) {
                data = base64.substring(commaIndex + 1);
            }
            data = data.replaceAll("\\s+", "");
            byte[] bytes = Base64.getDecoder().decode(data);
            Path path = Path.of(filePath);
            Path parent = path.getParent();
            if (parent != null) {
                Files.createDirectories(parent);
            }
            Files.write(path, bytes);
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "SaveImage failed: " + filePath, e);
        }
    }

    public List<DrawingCommand> getCommands() {
        return new ArrayList<>(commands);
    }

    public String getCurrentImageBase64() {
        return currentImageBase64;
    }

    public void clear() {
        commands.clear();
        currentImageBase64 = null;
    }

    private static double[] toDoubleArray(Object points) {
        if (points instanceof java.util.List) {
            java.util.List<?> list = (java.util.List<?>) points;
            double[] arr = new double[list.size()];
            for (int i = 0; i < list.size(); i++) {
                Object v = list.get(i);
                arr[i] = v instanceof Number ? ((Number) v).doubleValue() : 0.0;
            }
            return arr;
        }
        if (points instanceof cn.net.zhijian.quickjs.JSArray) {
            cn.net.zhijian.quickjs.JSArray array = (cn.net.zhijian.quickjs.JSArray) points;
            int len = array.length();
            double[] arr = new double[len];
            for (int i = 0; i < len; i++) {
                Object v = array.get(i);
                arr[i] = v instanceof Number ? ((Number) v).doubleValue() : 0.0;
            }
            return arr;
        }
        return new double[0];
    }
}
