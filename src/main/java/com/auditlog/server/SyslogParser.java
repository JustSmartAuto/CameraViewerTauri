package com.auditlog.server;

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
        if (line.startsWith("<")) {
            return parseRfc5424(line, sourceIp);
        }
        return parseJsonRaw(line, sourceIp);
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
