package com.autoweld.cameraviewer.script;

import com.autoweld.cameraviewer.config.AppConfig;
import com.autoweld.cameraviewer.model.GridConfig;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 管理 4 个格子的脚本上下文。
 */
public class ScriptEngine {
    private static final Logger LOGGER = Logger.getLogger(ScriptEngine.class.getName());

    private final List<ScriptContext> contexts;
    private final List<Consumer<String>> consoleListeners;

    public ScriptEngine() {
        this.contexts = new ArrayList<>(AppConfig.GRID_COUNT);
        this.consoleListeners = new ArrayList<>();
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            final int gridIndex = i;
            ScriptConsole console = new ScriptConsole(line -> {
                for (Consumer<String> listener : consoleListeners) {
                    listener.accept("[" + gridIndex + "] " + line);
                }
            });
            contexts.add(new ScriptContext(i, console));
        }
    }

    public void addConsoleListener(Consumer<String> listener) {
        consoleListeners.add(listener);
    }

    public void removeConsoleListener(Consumer<String> listener) {
        consoleListeners.remove(listener);
    }

    /**
     * 根据配置重新加载每个格子的脚本。
     */
    public void applyConfig(AppConfig config) {
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            GridConfig gridConfig = config.getGrid(i);
            contexts.get(i).loadScript(gridConfig.getScript());
        }
    }

    /**
     * 重新加载指定格子的脚本。
     */
    public void reloadScript(int gridIndex, String script) {
        contexts.get(gridIndex).loadScript(script);
    }

    /**
     * 处理指定格子的消息。
     */
    public void processMessage(int gridIndex, Object message) {
        contexts.get(gridIndex).processMessage(message);
    }

    public ScriptContext getContext(int gridIndex) {
        return contexts.get(gridIndex);
    }

    public void close() {
        for (ScriptContext context : contexts) {
            try {
                context.close();
            } catch (Exception e) {
                LOGGER.log(Level.WARNING, "Error closing script context", e);
            }
        }
    }
}
