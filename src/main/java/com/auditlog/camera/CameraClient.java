package com.auditlog.camera;

import com.auditlog.server.SyslogMessage;
import com.google.gson.Gson;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Cognex Platform API 客户端：{@code GET http://<ip>/api/audit-log}（Basic 认证，
 * 默认账号 {@code admin} / 空密码），并把返回的事件转换成项目内部的 {@link SyslogMessage}，
 * 复用既有的入库／界面刷新链路。
 */
public final class CameraClient {

    private static final Gson GSON = new Gson();
    private static final HttpClient HTTP = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(5))
            .build();

    /** Cognex 审计事件属于 RFC 5424 facility 13（log audit）。 */
    private static final int FACILITY_AUDIT = 13;

    private CameraClient() {}

    /**
     * 拉取审计日志。{@code afterIso} 为空表示拉全量历史（首次添加相机时）。
     */
    public static List<JsonObject> fetchAuditLog(CameraConfig cam, String afterIso)
            throws IOException, InterruptedException {
        StringBuilder url = new StringBuilder("http://")
                .append(cam.ip).append(':').append(cam.port).append("/api/audit-log");
        if (afterIso != null && !afterIso.isBlank()) {
            url.append("?after=").append(URLEncoder.encode(afterIso, StandardCharsets.UTF_8));
        }

        HttpRequest req = HttpRequest.newBuilder(URI.create(url.toString()))
                .timeout(Duration.ofSeconds(20))
                .header("Authorization", basicAuth(cam))
                .header("Accept", "application/json")
                .GET()
                .build();

        HttpResponse<String> resp = HTTP.send(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        if (resp.statusCode() != 200) {
            throw new IOException("HTTP " + resp.statusCode()
                    + (resp.statusCode() == 401 ? "（用户名或密码错误）" : ""));
        }

        String body = resp.body() == null ? "" : resp.body().trim();
        if (body.isEmpty()) return List.of();
        JsonElement root = JsonParser.parseString(body);
        if (!root.isJsonArray()) throw new IOException("返回内容不是 JSON 数组");
        List<JsonObject> out = new ArrayList<>();
        for (JsonElement el : root.getAsJsonArray()) {
            if (el.isJsonObject()) out.add(el.getAsJsonObject());
        }
        return out;
    }

    /** 连接测试：只取最近 1 小时，避免拉全量。返回条数。 */
    public static int testConnection(CameraConfig cam) throws IOException, InterruptedException {
        String after = Instant.now().minusSeconds(3600).toString();
        return fetchAuditLog(cam, after).size();
    }

    /** 相机事件 → 内部 SyslogMessage（facility 13，msgId = event，appName = type）。 */
    public static SyslogMessage toMessage(JsonObject e, String cameraIp) {
        Instant ts = parseTimestamp(str(e, "timestamp"));
        String hostname = str(e, "hostname");
        String type = str(e, "type");
        String event = str(e, "event");
        String result = str(e, "result");
        JsonObject sd = e.has("structuredData") && e.get("structuredData").isJsonObject()
                ? e.getAsJsonObject("structuredData") : new JsonObject();

        return new SyslogMessage(
                ts,
                hostname,
                FACILITY_AUDIT,
                severityCode(str(e, "severity")),
                type,
                "",
                event,
                flatten(sd),
                composeMessage(event, result, sd),
                GSON.toJson(e),
                cameraIp);
    }

    public static Instant parseTimestamp(String iso) {
        if (iso == null || iso.isBlank()) return null;
        try {
            return OffsetDateTime.parse(iso).toInstant();
        } catch (Exception ignored) {}
        try {
            return Instant.parse(iso);
        } catch (Exception ignored) {}
        return null;
    }

    private static String basicAuth(CameraConfig cam) {
        String raw = (cam.username == null ? "" : cam.username) + ":" + (cam.password == null ? "" : cam.password);
        return "Basic " + Base64.getEncoder().encodeToString(raw.getBytes(StandardCharsets.UTF_8));
    }

    private static String str(JsonObject o, String key) {
        JsonElement el = o.get(key);
        if (el == null || el.isJsonNull()) return "";
        return el.isJsonPrimitive() ? el.getAsString() : el.toString();
    }

    /** structuredData 里既有字符串也有嵌套对象（configChange.new/old），统一压成 Map<String,String>。 */
    private static Map<String, Map<String, String>> flatten(JsonObject sd) {
        Map<String, Map<String, String>> out = new LinkedHashMap<>();
        for (var block : sd.entrySet()) {
            if (!block.getValue().isJsonObject()) continue;
            Map<String, String> inner = new LinkedHashMap<>();
            for (var p : block.getValue().getAsJsonObject().entrySet()) {
                JsonElement v = p.getValue();
                if (v == null || v.isJsonNull()) inner.put(p.getKey(), "");
                else if (v.isJsonPrimitive()) inner.put(p.getKey(), v.getAsString());
                else inner.put(p.getKey(), GSON.toJson(v));
            }
            out.put(block.getKey(), inner);
        }
        return out;
    }

    /** 消息列：事件名 + 关键字段（如 user=admin、property=Wired connection）。 */
    private static String composeMessage(String event, String result, JsonObject sd) {
        StringBuilder sb = new StringBuilder(event);
        List<String> details = new ArrayList<>();
        for (var block : sd.entrySet()) {
            String key = block.getKey();
            if (key.startsWith("auditEvent") || key.startsWith("timeQuality")) continue;
            if (!block.getValue().isJsonObject()) continue;
            for (var p : block.getValue().getAsJsonObject().entrySet()) {
                if (!p.getValue().isJsonPrimitive()) continue;
                details.add(p.getKey() + "=" + p.getValue().getAsString());
                if (details.size() >= 3) break;
            }
            if (details.size() >= 3) break;
        }
        if (!details.isEmpty()) sb.append(' ').append(String.join(" ", details));
        if (result != null && !result.isBlank()) sb.append(" result=").append(result);
        return sb.toString().trim();
    }

    /** RFC 5424 名称 → 数字严重级别。 */
    private static int severityCode(String name) {
        if (name == null) return 5;
        String n = name.trim().toLowerCase(java.util.Locale.ROOT);
        return switch (n) {
            case "emerg", "emergency", "panic" -> 0;
            case "alert" -> 1;
            case "crit", "critical" -> 2;
            case "err", "error" -> 3;
            case "warn", "warning" -> 4;
            case "notice" -> 5;
            case "info", "informational" -> 6;
            case "debug" -> 7;
            default -> { try { yield Integer.parseInt(n); } catch (NumberFormatException e) { yield 5; } }
        };
    }
}
