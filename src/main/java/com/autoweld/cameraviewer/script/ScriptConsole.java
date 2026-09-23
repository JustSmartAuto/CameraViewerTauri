package com.autoweld.cameraviewer.script;

import cn.net.zhijian.quickjs.QuickJSContext;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;

/**
 * 捕获 QuickJS 的 console 输出。
 */
public class ScriptConsole implements QuickJSContext.Console {
    private final List<String> lines = new ArrayList<>();
    private final Consumer<String> onLine;

    public ScriptConsole() {
        this(null);
    }

    public ScriptConsole(Consumer<String> onLine) {
        this.onLine = onLine;
    }

    @Override
    public void debug(String message) {
        append("[debug] " + message);
    }

    @Override
    public void info(String message) {
        append("[info] " + message);
    }

    @Override
    public void warn(String message) {
        append("[warn] " + message);
    }

    @Override
    public void error(String message) {
        append("[error] " + message);
    }

    private void append(String line) {
        lines.add(line);
        if (onLine != null) {
            onLine.accept(line);
        }
    }

    public List<String> getLines() {
        return new ArrayList<>(lines);
    }

    public String getText() {
        return String.join("\n", lines);
    }

    public void clear() {
        lines.clear();
    }
}
