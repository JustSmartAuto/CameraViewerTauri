package com.autoweld.cameraviewer.script;

import cn.net.zhijian.quickjs.*;
import com.autoweld.cameraviewer.script.draw.DrawingCommand;
import com.google.gson.Gson;

import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.geom.AffineTransform;
import java.awt.image.BufferedImage;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Base64;
import java.util.List;
import java.util.function.Supplier;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 每个画面格子独立的脚本执行上下文。
 */
public class ScriptContext implements AutoCloseable {
    private static final Logger LOGGER = Logger.getLogger(ScriptContext.class.getName());
    private static final Gson GSON = new Gson();

    private final QuickJSContext context;
    private final DrawingApi drawingApi;
    private final ScriptConsole console;
    private final int gridIndex;

    private JSFunction processFunction;
    private String lastError;
    private Supplier<BufferedImage> renderedImageSupplier;

    public ScriptContext(int gridIndex, ScriptConsole console) {
        this.gridIndex = gridIndex;
        this.context = QuickJSContext.create();
        this.console = console != null ? console : new ScriptConsole();
        this.drawingApi = new DrawingApi();
        initialize();
    }

    private void initialize() {
        context.setConsole(console);
        JSObject global = context.getGlobalObject();
        global.setJavaObject("Draw", drawingApi);

        // 将绘图函数暴露为全局函数，脚本可直接调用 PlotString(...)
        global.setProperty("PlotString", (JSCallFunction) args -> {
            drawingApi.PlotString(intArg(args, 0), intArg(args, 1),
                    doubleArg(args, 2), doubleArg(args, 3),
                    stringArg(args, 4), intArg(args, 5), args[6]);
            return null;
        });
        global.setProperty("PlotLine", (JSCallFunction) args -> {
            drawingApi.PlotLine(intArg(args, 0), intArg(args, 1),
                    doubleArg(args, 2), doubleArg(args, 3),
                    doubleArg(args, 4), doubleArg(args, 5),
                    doubleArg(args, 6), args[7]);
            return null;
        });
        global.setProperty("PlotCircle", (JSCallFunction) args -> {
            drawingApi.PlotCircle(intArg(args, 0), intArg(args, 1),
                    doubleArg(args, 2), doubleArg(args, 3),
                    doubleArg(args, 4), doubleArg(args, 5), args[6]);
            return null;
        });
        global.setProperty("PlotPolygon", (JSCallFunction) args -> {
            drawingApi.PlotPolygon(intArg(args, 0), intArg(args, 1),
                    args[2], doubleArg(args, 3), args[4]);
            return null;
        });
        global.setProperty("PlotPoint", (JSCallFunction) args -> {
            drawingApi.PlotPoint(intArg(args, 0), intArg(args, 1),
                    doubleArg(args, 2), doubleArg(args, 3),
                    doubleArg(args, 4), args[5]);
            return null;
        });
        global.setProperty("ShowImage", (JSCallFunction) args -> {
            drawingApi.ShowImage(stringArg(args, 0));
            return null;
        });
        global.setProperty("SaveImage", (JSCallFunction) args -> {
            drawingApi.SaveImage(stringArg(args, 0), stringArg(args, 1));
            return null;
        });
        global.setProperty("Clear", (JSCallFunction) args -> {
            drawingApi.Clear();
            return null;
        });
        global.setProperty("SaveRenderedImage", (JSCallFunction) args -> {
            saveRenderedImage(stringArg(args, 0));
            return null;
        });
        global.setProperty("GetTimeStamp", (JSCallFunction) args -> {
            return getTimeStamp(stringArg(args, 0));
        });
    }

    private static int intArg(Object[] args, int index) {
        if (args == null || index >= args.length || args[index] == null) return 0;
        return ((Number) args[index]).intValue();
    }

    private static double doubleArg(Object[] args, int index) {
        if (args == null || index >= args.length || args[index] == null) return 0.0;
        return ((Number) args[index]).doubleValue();
    }

    private static String stringArg(Object[] args, int index) {
        if (args == null || index >= args.length || args[index] == null) return "";
        return args[index].toString();
    }

    /**
     * 加载并编译用户脚本。
     */
    public void loadScript(String script) {
        lastError = null;
        processFunction = null;
        if (script == null || script.trim().isEmpty()) {
            return;
        }
        try {
            context.evaluate(script, "grid_" + gridIndex + ".js");
            Object fn = context.getProperty(context.getGlobalObject(), "process");
            if (fn instanceof JSFunction) {
                processFunction = (JSFunction) fn;
            } else {
                lastError = "脚本中未找到 process(message) 函数";
            }
        } catch (QuickJSException e) {
            lastError = e.getMessage();
            LOGGER.log(Level.WARNING, "Grid " + gridIndex + " script error", e);
        }
    }

    /**
     * 处理消息：将 Java 对象序列化为 JSON 后解析为 JS 对象，再调用 process 函数。
     */
    public void processMessage(Object message) {
        lastError = null;
        drawingApi.clear();
        if (processFunction == null) {
            return;
        }
        try {
            String json = GSON.toJson(message);
            Object jsObject = context.parse(json);
            processFunction.call(jsObject);
        } catch (QuickJSException e) {
            lastError = e.getMessage();
            LOGGER.log(Level.WARNING, "Grid " + gridIndex + " process error", e);
        }
    }

    public List<DrawingCommand> getCommands() {
        return drawingApi.getCommands();
    }

    public String getCurrentImageBase64() {
        return drawingApi.getCurrentImageBase64();
    }

    public boolean isClearImageRequested() {
        return drawingApi.isClearImageRequested();
    }

    public void resetClearImageRequested() {
        drawingApi.resetClearImageRequested();
    }

    public void setRenderedImageSupplier(Supplier<BufferedImage> renderedImageSupplier) {
        this.renderedImageSupplier = renderedImageSupplier;
    }

    private void saveRenderedImage(String filePath) {
        if (filePath == null || filePath.isEmpty()) {
            return;
        }
        // 优先使用脚本通过 ShowImage 设置的图像，否则回退到 UI 当前显示的原图
        BufferedImage baseImage = decodeBase64Image(drawingApi.getCurrentImageBase64());
        if (baseImage == null && renderedImageSupplier != null) {
            baseImage = renderedImageSupplier.get();
        }
        if (baseImage == null) {
            return;
        }
        try {
            Path path = Path.of(filePath);
            Path parent = path.getParent();
            if (parent != null) {
                Files.createDirectories(parent);
            }
            // 在原图上叠加当前脚本已绘制的命令
            BufferedImage rendered = new BufferedImage(baseImage.getWidth(), baseImage.getHeight(), BufferedImage.TYPE_INT_RGB);
            Graphics2D g2d = rendered.createGraphics();
            g2d.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            g2d.drawImage(baseImage, 0, 0, null);
            AffineTransform identity = new AffineTransform();
            for (DrawingCommand cmd : drawingApi.getCommands()) {
                try {
                    cmd.draw(g2d, identity);
                } catch (Exception e) {
                    LOGGER.log(Level.WARNING, "Drawing command failed in saveRenderedImage", e);
                }
            }
            g2d.dispose();

            javax.imageio.ImageWriter writer = javax.imageio.ImageIO.getImageWritersByFormatName("jpg").next();
            javax.imageio.ImageWriteParam param = writer.getDefaultWriteParam();
            param.setCompressionMode(javax.imageio.ImageWriteParam.MODE_EXPLICIT);
            param.setCompressionQuality(0.95f);
            try (javax.imageio.stream.ImageOutputStream ios = javax.imageio.ImageIO.createImageOutputStream(path.toFile())) {
                writer.setOutput(ios);
                writer.write(null, new javax.imageio.IIOImage(rendered, null, null), param);
            } finally {
                writer.dispose();
            }
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "SaveRenderedImage failed: " + filePath, e);
        }
    }

    private BufferedImage decodeBase64Image(String base64) {
        if (base64 == null || base64.isEmpty()) {
            return null;
        }
        try {
            String data = base64;
            int commaIndex = base64.indexOf(',');
            if (commaIndex >= 0) {
                data = base64.substring(commaIndex + 1);
            }
            data = data.replaceAll("\\s+", "");
            byte[] bytes = Base64.getDecoder().decode(data);
            return javax.imageio.ImageIO.read(new java.io.ByteArrayInputStream(bytes));
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to decode base64 image in saveRenderedImage", e);
            return null;
        }
    }

    private String getTimeStamp(String format) {
        if (format == null || format.isEmpty()) {
            format = "yyyyMMdd_HHmmss";
        }
        try {
            DateTimeFormatter formatter = DateTimeFormatter.ofPattern(format);
            return LocalDateTime.now().format(formatter);
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "GetTimeStamp failed: " + format, e);
            return "";
        }
    }

    public String getLastError() {
        return lastError;
    }

    public ScriptConsole getConsole() {
        return console;
    }

    @Override
    public void close() {
        if (context != null && !context.closed()) {
            context.close();
        }
    }
}
