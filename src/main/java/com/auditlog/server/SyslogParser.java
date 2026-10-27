package com.auditlog.server;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeParseException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * RFC 5424 syslog parser. Accepts both "Framed" (octet-counting) and
 * "Transparent-Framing" (NL terminated) variants that Fluentd/Cognex use.
 */
public class SyslogParser {

    // 1.6.6 - RFC 5424: <PRI>VERSION SP TIMESTAMP SP HOSTNAME SP APP-NAME SP PROCID SP MSGID SP STRUCTURED-DATA [SP MSG]
    private static final DateTimeFormatter RFC3339 = DateTimeFormatter.ISO_OFFSET_DATE_TIME;

    /** Parse one raw TCP syslog line. */
    public SyslogMessage parse(String raw, String sourceIp) {
        String line = raw.strip();
        if (line.isBlank()) return malformed(line, sourceIp);
        if (line.startsWith("<")) {
            return parseRfc5424(line, sourceIp);
        }
        // JSON (Fluentd forward / Cognex 111.json-style)
        if (line.startsWith("{")) {
            return parseJsonForward(line, sourceIp);
        }
        return parseJsonRaw(line, sourceIp);
    }

    /**
     * Parse a JSON object that Fluentd sends upstream. Two shapes observed:
     * <ul>
     *   <li>Cognex In-Sight audit log export: top-level fields timestamp, hostname, severity,
     *       type, event, result, structuredData (map with auditEvent@39975 / configChange@39975 / ...)</li>
     *   <li>Fluentd generic forward: has {@code message} + {@code fluent_time} or similar</li>
     * </ul>
     * We also accept a JSON array of one element (batch forward).
     */
    private SyslogMessage parseJsonForward(String line, String sourceIp) {
        try {
            JsonElement root = JsonParser.parseString(line);
            JsonObject obj;
            if (root.isJsonArray() && root.getAsJsonArray().size() > 0) {
                JsonElement first = root.getAsJsonArray().get(0);
                if (first.isJsonPrimitive()) {
                    // Fluentd "<tag> <time> <message>" concatenated form — treat the first primitive as message
                    return new SyslogMessage(Instant.now(), null, 0, 0, null, null, null, Map.of(), first.getAsString(), line, sourceIp);
                }
                obj = first.getAsJsonObject();
            } else {
                obj = root.getAsJsonObject();
            }

            Instant ts = null;
            if (obj.has("timestamp")) {
                try { ts = Instant.parse(obj.get("timestamp").getAsString()); }
                catch (Exception ignored) {}
            }
            if (ts == null && obj.has("fluent_time")) {
                try { ts = Instant.ofEpochSecond(obj.get("fluent_time").getAsLong()); }
                catch (Exception ignored) {}
            }

            String hostname = stringOrNull(obj, "hostname");
            String message = stringOrNull(obj, "message");
            if (message == null) message = stringOrNull(obj, "result");
            if (message == null) message = stringOrNull(obj, "_msg");

            int severity = 0;
            String sev = stringOrNull(obj, "severity");
            if (sev != null) {
                severity = switch (sev.toLowerCase()) {
                    case "emerg", "panic" -> 0;
                    case "alert" -> 1;
                    case "crit", "critical" -> 2;
                    case "err", "error" -> 3;
                    case "warn", "warning" -> 4;
                    case "notice" -> 5;
                    case "info", "information" -> 6;
                    case "debug", "trace" -> 7;
                    default -> 6;
                };
            }

            String type = stringOrNull(obj, "type");
            String event = stringOrNull(obj, "event");
            String msgId = (type != null && event != null) ? type + "/" + event : null;

            // structuredData map — preserve all K/V pairs, also stash raw JSON blob
            Map<String, Map<String, String>> sd = new LinkedHashMap<>();
            if (obj.has("structuredData") && obj.get("structuredData").isJsonObject()) {
                for (var entry : obj.getAsJsonObject("structuredData").entrySet()) {
                    Map<String, String> inner = new LinkedHashMap<>();
                    JsonElement val = entry.getValue();
                    if (val.isJsonObject()) {
                        for (var e2 : val.getAsJsonObject().entrySet()) {
                            inner.put(e2.getKey(), e2.getValue().toString());
                        }
                    } else if (val.isJsonPrimitive()) {
                        inner.put("value", val.getAsString());
                    }
                    sd.put(entry.getKey(), inner);
                }
            }
            // also surface type/event as pseudo SD entries so UI filter logic can see them
            if (type != null) sd.put("_type", Map.of("value", type));
            if (event != null) sd.put("_event", Map.of("value", event));

            return new SyslogMessage(ts, hostname, 0, severity, type, null, msgId, sd, message, line, sourceIp);
        } catch (Exception e) {
            return new SyslogMessage(Instant.now(), null, 0, 0, null, null, null, Map.of(), line, line, sourceIp);
        }
    }

    private static String stringOrNull(JsonObject o, String key) {
        if (!o.has(key) || !o.get(key).isJsonPrimitive()) return null;
        return o.get(key).getAsString();
    }

    private SyslogMessage parseRfc5424(String line, String sourceIp) {
        // <PRI>VERSION ...
        int end = line.indexOf('>');
        if (end < 0) return malformed(line, sourceIp);
        int pri;
        try { pri = Integer.parseInt(line.substring(1, end)); }
        catch (NumberFormatException e) { return malformed(line, sourceIp); }

        String rest = line.substring(end + 1);
        int severity = pri & 0x07;
        int facility = (pri >> 3) & 0x1F;

        int versionEnd = rest.indexOf(' ');
        if (versionEnd < 0) return malformed(line, sourceIp);
        // version = Integer.parseInt(rest.substring(0, versionEnd)) - usually 1

        String fields = rest.substring(versionEnd + 1);
        Instant ts = null;
        String hostname = "-";
        String app = "-";
        String procId = "-";
        String msgId = "-";
        Map<String, Map<String, String>> sd = new LinkedHashMap<>();

        // parse 5 space-separated mandatory fields, then structured data, then msg
        List<String> tokens = splitFields(fields);
        if (tokens.size() >= 1) ts = parseTimestamp(tokens.get(0));
        if (tokens.size() >= 2) hostname = nvl(tokens.get(1));
        if (tokens.size() >= 3) app = nvl(tokens.get(2));
        if (tokens.size() >= 4) procId = nvl(tokens.get(3));
        if (tokens.size() >= 5) msgId = nvl(tokens.get(4));
        if (tokens.size() >= 6) sd = parseStructuredData(tokens.get(5));

        String message = null;
        if (tokens.size() > 6) {
            message = tokens.get(tokens.size() - 1);
        }

        return new SyslogMessage(ts, hostname, facility, severity, app, procId, msgId, sd, message, line, sourceIp);
    }

    /** Split on spaces, preserving empty-field marker '-', but keep SD intact. */
    private List<String> splitFields(String s) {
        List<String> out = new ArrayList<>();
        int i = 0;
        int n = s.length();
        // ts hostname app procid msgid sd [msg]
        for (int f = 0; f < 5 && i < n; f++) {
            int sp = s.indexOf(' ', i);
            if (sp < 0) { out.add(s.substring(i)); i = n; break; }
            out.add(s.substring(i, sp));
            i = sp + 1;
        }
        // structured-data — starts with '[' or '-'
        if (i < n) {
            if (s.charAt(i) == '-') {
                out.add("-");
                // optionally followed by MSG after a space
                i += 1;
                if (i < n && s.charAt(i) == ' ') i++;
                if (i < n) out.add(s.substring(i));
            } else if (s.charAt(i) == '[') {
                int sdEnd = findStructuredDataEnd(s.substring(i));
                if (sdEnd >= 0) {
                    out.add(s.substring(i, i + sdEnd + 1));
                    i += sdEnd + 1;
                    if (i < n && s.charAt(i) == ' ') i++;
                    if (i < n) out.add(s.substring(i));
                } else {
                    out.add(s.substring(i));
                }
            } else {
                out.add(s.substring(i));
            }
        }
        return out;
    }

    private int indexOfTokenStart(String fields, int tokenIndex) {
        int i = 0;
        for (int k = 0; k < tokenIndex; k++) {
            int sp = fields.indexOf(' ', i);
            if (sp < 0) return -1;
            i = sp + 1;
        }
        return i;
    }

    /** Scan from first '[' matching its closing ']' (supports nested brackets). */
    private int findStructuredDataEnd(String s) {
        if (!s.startsWith("[")) return -1;
        int depth = 0;
        boolean inBracket = false;
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '[' && (i == 0 || s.charAt(i - 1) != '\\')) { depth++; inBracket = true; }
            else if (c == ']' && (i == 0 || s.charAt(i - 1) != '\\')) { depth--; if (depth == 0 && inBracket) return i; }
        }
        return -1;
    }

    private Map<String, Map<String, String>> parseStructuredData(String raw) {
        Map<String, Map<String, String>> result = new LinkedHashMap<>();
        if (raw == null || raw.equals("-") || raw.isBlank()) return result;
        int i = 0;
        while (i < raw.length()) {
            int open = raw.indexOf('[', i);
            if (open < 0) break;
            int close = findStructuredDataEnd(raw.substring(open)) + open;
            if (close < open) break;
            String one = raw.substring(open + 1, close);
            // one = "SDID key=\"val\" key2=\"val2\""
            int sp = one.indexOf(' ');
            String sdId = sp < 0 ? one : one.substring(0, sp);
            Map<String, String> params = new LinkedHashMap<>();
            if (sp >= 0) {
                String tail = one.substring(sp + 1);
                parseSdParams(tail, params);
            }
            result.put(sdId, params);
            i = close + 1;
        }
        return result;
    }

    private void parseSdParams(String tail, Map<String, String> out) {
        int i = 0;
        while (i < tail.length()) {
            while (i < tail.length() && Character.isWhitespace(tail.charAt(i))) i++;
            if (i >= tail.length()) break;
            int eq = tail.indexOf('=', i);
            if (eq < 0) break;
            String key = tail.substring(i, eq);
            int quote = tail.indexOf('"', eq + 1);
            if (quote < 0) break;
            int endQuote = quote + 1;
            StringBuilder val = new StringBuilder();
            while (endQuote < tail.length()) {
                char c = tail.charAt(endQuote);
                if (c == '\\' && endQuote + 1 < tail.length()) {
                    val.append(tail.charAt(endQuote + 1));
                    endQuote += 2;
                } else if (c == '"') {
                    endQuote++;
                    break;
                } else {
                    val.append(c);
                    endQuote++;
                }
            }
            // URL-decode simple %xx (RFC 5424 SD param values are free-form; Fluentd often passes raw JSON)
            out.put(key, val.toString());
            i = endQuote;
        }
    }

    private Instant parseTimestamp(String s) {
        if (s == null || s.equals("-")) return null;
        try { return Instant.parse(s); }
        catch (DateTimeParseException ignored) {}
        try {
            ZonedDateTime zdt = ZonedDateTime.parse(s, RFC3339);
            return zdt.toInstant();
        } catch (DateTimeParseException ignored) {}
        return null;
    }

    private SyslogMessage parseJsonRaw(String line, String sourceIp) {
        // Not RFC 5424 — probably a raw JSON forward. Timestamp and hostname will be nulled.
        return new SyslogMessage(null, null, 0, 0, null, null, null, Map.of(), line, line, sourceIp);
    }

    private SyslogMessage malformed(String raw, String sourceIp) {
        return new SyslogMessage(null, null, 0, 0, null, null, null, Map.of(), raw, raw, sourceIp);
    }

    private static String nvl(String s) { return "-".equals(s) ? null : s; }
}
