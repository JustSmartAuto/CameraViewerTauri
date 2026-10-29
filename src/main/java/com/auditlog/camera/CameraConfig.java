package com.auditlog.camera;

import java.util.UUID;

/**
 * 一台通过 Cognex Platform API（{@code GET /api/audit-log}）拉取审计日志的相机。
 * 直接由 Gson 序列化到 {@code config/cameras.json}，字段名即 JSON 键名。
 */
public class CameraConfig {

    public String id = UUID.randomUUID().toString();
    /** 显示名称，例如 OP30-122。 */
    public String name = "";
    public String ip = "";
    public int port = 80;
    /** Platform API 默认凭据 admin / 空密码。 */
    public String username = "admin";
    public String password = "";
    public boolean enabled = true;
    /** 定时拉取间隔（秒）。 */
    public int pollSeconds = 30;
    /** 上次成功拉取到的最新事件时间（ISO-8601，UTC），用于增量拉取。 */
    public String lastSync = "";

    public CameraConfig() {}

    public CameraConfig(String name, String ip) {
        this.name = name;
        this.ip = ip;
    }

    /** 表格/日志里用的简短标识。 */
    public String label() {
        return (name == null || name.isBlank()) ? ip : name + " (" + ip + ")";
    }
}
