package com.auditlog.camera;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.reflect.TypeToken;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/** {@code config/cameras.json} 的读写（数组格式，无 BOM，UTF-8）。 */
public final class CameraStore {

    private final Gson gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
    private final Path file;

    public CameraStore(Path file) {
        this.file = file;
    }

    public Path file() {
        return file;
    }

    public List<CameraConfig> load() {
        List<CameraConfig> out = new ArrayList<>();
        if (!Files.exists(file)) return out;
        try {
            String json = Files.readString(file, StandardCharsets.UTF_8);
            List<CameraConfig> list = gson.fromJson(json, new TypeToken<List<CameraConfig>>() {}.getType());
            if (list != null) out.addAll(list);
        } catch (Exception ignored) {
            // 文件损坏时按空列表处理，避免影响启动
        }
        return out;
    }

    public synchronized void save(List<CameraConfig> cameras) {
        try {
            Path parent = file.getParent();
            if (parent != null) Files.createDirectories(parent);
            Files.writeString(file, gson.toJson(cameras), StandardCharsets.UTF_8);
        } catch (IOException ignored) {
            // 配置写失败不阻断运行
        }
    }
}
