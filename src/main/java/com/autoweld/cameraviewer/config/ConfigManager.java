package com.autoweld.cameraviewer.config;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 应用配置的 JSON 持久化管理。
 * 配置文件保存在 jar 文件同目录下。
 */
public class ConfigManager {
    private static final Logger LOGGER = Logger.getLogger(ConfigManager.class.getName());
    private static final String CONFIG_FILE_NAME = "camera-viewer-config.json";

    private final Gson gson;
    private final Path configPath;

    public ConfigManager() {
        this.gson = new GsonBuilder()
                .setPrettyPrinting()
                .serializeNulls()
                .create();
        this.configPath = resolveConfigPath();
        LOGGER.info("Config path: " + configPath);
    }

    /**
     * 解析配置文件路径：优先取 jar 所在目录，否则取当前工作目录。
     */
    private static Path resolveConfigPath() {
        Path jarDir = getJarDirectory();
        if (jarDir != null) {
            return jarDir.resolve(CONFIG_FILE_NAME);
        }
        return Paths.get(CONFIG_FILE_NAME).toAbsolutePath();
    }

    private static Path getJarDirectory() {
        try {
            java.net.URI uri = ConfigManager.class.getProtectionDomain()
                    .getCodeSource()
                    .getLocation()
                    .toURI();
            Path path = Paths.get(uri);
            if (Files.isRegularFile(path)) {
                return path.getParent();
            }
            return path;
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to resolve jar directory", e);
            return null;
        }
    }

    /**
     * 加载配置；若文件不存在或解析失败，返回默认配置。
     */
    public AppConfig load() {
        if (!Files.exists(configPath)) {
            LOGGER.info("Config file not found, using default config");
            return new AppConfig();
        }

        try {
            String json = Files.readString(configPath, StandardCharsets.UTF_8);
            AppConfig config = gson.fromJson(json, AppConfig.class);
            if (config == null) {
                LOGGER.warning("Parsed config is null, using default config");
                return new AppConfig();
            }
            ensureGridCount(config);
            return config;
        } catch (IOException e) {
            LOGGER.log(Level.WARNING, "Failed to read config file, using default config", e);
            return new AppConfig();
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to parse config file, using default config", e);
            return new AppConfig();
        }
    }

    /**
     * 保存配置到 JSON 文件，使用临时文件 + 原子替换避免写坏原文件。
     */
    public void save(AppConfig config) throws IOException {
        ensureGridCount(config);
        String json = gson.toJson(config);
        Path temp = configPath.resolveSibling(CONFIG_FILE_NAME + ".tmp");
        Files.writeString(temp, json, StandardCharsets.UTF_8);
        Files.move(temp, configPath, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        LOGGER.info("Config saved to " + configPath);
    }

    /**
     * 保存配置，失败时记录日志而不抛出异常。
     */
    public void saveQuietly(AppConfig config) {
        try {
            save(config);
        } catch (IOException e) {
            LOGGER.log(Level.SEVERE, "Failed to save config", e);
        }
    }

    private static void ensureGridCount(AppConfig config) {
        if (config.getGrids() == null) {
            config.setGrids(new java.util.ArrayList<>());
        }
        while (config.getGrids().size() < AppConfig.GRID_COUNT) {
            config.getGrids().add(new com.autoweld.cameraviewer.model.GridConfig());
        }
        while (config.getGrids().size() > AppConfig.GRID_COUNT) {
            config.getGrids().remove(config.getGrids().size() - 1);
        }
    }

    public Path getConfigPath() {
        return configPath;
    }
}
