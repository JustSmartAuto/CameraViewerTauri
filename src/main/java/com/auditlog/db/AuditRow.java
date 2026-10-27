package com.auditlog.db;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.Instant;

/** One audit event row for UI consumption. */
public record AuditRow(
        long id,
        Instant ts,
        String hostname,
        int severity,
        String severityName,
        String appName,
        String procId,
        String msgId,
        int facility,
        String sourceIp,
        String message,
        String structuredDataJson
) {
    public static AuditRow from(ResultSet rs) throws SQLException {
        var ts = rs.getObject("ts", Instant.class);
        if (ts == null) {
            // H2 may return LocalDateTime; fall back
            var ldt = rs.getObject("ts", java.time.LocalDateTime.class);
            if (ldt != null) ts = ldt.toInstant(java.time.ZoneOffset.UTC);
            else ts = Instant.EPOCH;
        }
        return new AuditRow(
                rs.getLong("id"),
                ts,
                rs.getString("hostname"),
                rs.getInt("severity"),
                rs.getString("severity_name"),
                rs.getString("app_name"),
                rs.getString("proc_id"),
                rs.getString("msg_id"),
                rs.getInt("facility"),
                rs.getString("source_ip"),
                rs.getString("message"),
                rs.getString("structured_data")
        );
    }
}
