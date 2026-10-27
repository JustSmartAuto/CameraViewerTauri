package com.auditlog.server;

import com.google.gson.Gson;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import java.time.Instant;
import java.time.format.DateTimeParseException;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Parsed RFC 5424 syslog message.
 * <p>
 * The {@code structuredData} field is a map of SD-ID -> (param-name -> param-value).
 * Cognex In-Sight audit logs put {@code auditEvent@39975}, {@code configChange@39975},
 * {@code auth@39975} etc. inside RFC 5424 structured data — we also additionally
 * accept raw JSON messages that Fluentd might forward.
 */
public record SyslogMessage(
        Instant timestamp,
        String hostname,
        int facility,
        int severity,
        String appName,
        String procId,
        String msgId,
        Map<String, Map<String, String>> structuredData,
        String message,
        String raw,
        String sourceIp) {

    /** Convenience: severity name from RFC 5424 numerical code. */
    public String severityName() {
        return switch (severity) {
            case 0 -> "emerg";
            case 1 -> "alert";
            case 2 -> "crit";
            case 3 -> "err";
            case 4 -> "warning";
            case 5 -> "notice";
            case 6 -> "info";
            case 7 -> "debug";
            default -> "sev(" + severity + ")";
        };
    }

    /** Extract Cognex-type structuredData JSON blob (configChange, auth, etc.). */
    public JsonElement findCognexBlock(String sdPrefix, Gson gson) {
        for (var entry : structuredData.entrySet()) {
            if (entry.getKey().startsWith(sdPrefix)) {
                // values are url-decoded strings; try to parse as JSON
                for (var pv : entry.getValue().values()) {
                    if (pv == null || pv.isBlank()) continue;
                    try {
                        return gson.fromJson(pv, JsonElement.class);
                    } catch (Exception ignored) {}
                }
            }
        }
        return null;
    }

    /** Render to a compact JSON line suitable for persistence. */
    public JsonObject toJson() {
        var g = new Gson();
        var obj = new JsonObject();
        obj.addProperty("timestamp", timestamp == null ? null : timestamp.toString());
        obj.addProperty("hostname", hostname);
        obj.addProperty("severity", severity);
        obj.addProperty("severityName", severityName());
        obj.addProperty("appName", appName);
        obj.addProperty("procId", procId);
        obj.addProperty("msgId", msgId);
        obj.addProperty("message", message);
        obj.addProperty("sourceIp", sourceIp);
        var sdObj = new JsonObject();
        for (var e : structuredData.entrySet()) {
            var inner = new JsonObject();
            for (var p : e.getValue().entrySet()) {
                inner.addProperty(p.getKey(), p.getValue());
            }
            sdObj.add(e.getKey(), inner);
        }
        obj.add("structuredData", sdObj);
        return obj;
    }
}
