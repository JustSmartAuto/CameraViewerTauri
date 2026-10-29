package com.auditlog.db;

import com.auditlog.server.SyslogMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.*;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

/**
 * H2 embedded (file mode) persistence layer.
 * <p>
 * H2 URL uses {@code MODE=PostgreSQL} so that TIMESTAMP WITH TIME ZONE
 * and JSON-like CLOB behave predictably.
 */
public class H2Store implements AutoCloseable {

    private static final Logger log = LoggerFactory.getLogger(H2Store.class);

    private final String dbPath;
    private final String url;
    private Connection conn;

    public H2Store(String dbPath) {
        // dbPath like "./auditlog/auditlog" → H2 file "./auditlog/auditlog.mv.db"
        // AUTO_SERVER=FALSE (default) — 单 JVM 内嵌，不绑定额外 TCP 端口
        this.dbPath = dbPath;
        this.url = "jdbc:h2:file:" + dbPath + ";DB_CLOSE_DELAY=-1;DATABASE_TO_LOWER=TRUE";
    }

    public synchronized void open() throws SQLException {
        if (conn != null && !conn.isClosed()) return;
        try {
            // Guard 1: ensure parent directory exists recursively — if H2 opens against
            // a path whose ancestor does not exist, DriverManager.getConnection() crashes
            // with a cryptic "A file path that is not found" instead of creating it.
            Path p = Path.of(dbPath).toAbsolutePath();
            Files.createDirectories(p.getParent());

            Class.forName("org.h2.Driver");
        } catch (ClassNotFoundException e) {
            throw new SQLException("H2 driver not found", e);
        } catch (IOException e) {
            throw new SQLException("Cannot create H2 parent directory: " + e.getMessage(), e);
        }
        conn = DriverManager.getConnection(url, "sa", "");
        conn.setAutoCommit(true);
        createSchema();
        log.info("H2 store opened: {}", url);
    }

    private void createSchema() throws SQLException {
        try (var st = conn.createStatement()) {
            // 单行 SQL — 避免 H2 2.5.x 对 text block 里尾随 \n 的怪异解析
            st.execute("CREATE TABLE IF NOT EXISTS audit_event (" +
                    "id BIGINT AUTO_INCREMENT PRIMARY KEY, " +
                    "ts TIMESTAMP NOT NULL, " +
                    "hostname VARCHAR(255), " +
                    "severity INT, " +
                    "severity_name VARCHAR(32), " +
                    "app_name VARCHAR(255), " +
                    "proc_id VARCHAR(128), " +
                    "msg_id VARCHAR(255), " +
                    "facility INT, " +
                    "source_ip VARCHAR(64), " +
                    "message CLOB, " +
                    "structured_data CLOB, " +
                    "raw_line CLOB)");
            st.execute("CREATE INDEX IF NOT EXISTS idx_audit_ts   ON audit_event (ts)");
            st.execute("CREATE INDEX IF NOT EXISTS idx_audit_host ON audit_event (hostname)");
            st.execute("CREATE INDEX IF NOT EXISTS idx_audit_sev   ON audit_event (severity)");
        }
    }

    /** Insert one event; returns the generated id. */
    public synchronized long insert(SyslogMessage msg, String structuredDataJson) throws SQLException {
        try (var ps = conn.prepareStatement(
                "INSERT INTO audit_event (ts, hostname, severity, severity_name, app_name, proc_id, msg_id, " +
                "facility, source_ip, message, structured_data, raw_line) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", Statement.RETURN_GENERATED_KEYS)) {
            Instant ts = msg.timestamp() == null ? Instant.now() : msg.timestamp();
            ps.setTimestamp(1, Timestamp.from(ts));
            ps.setString(2, msg.hostname());
            ps.setInt(3, msg.severity());
            ps.setString(4, msg.severityName());
            ps.setString(5, msg.appName());
            ps.setString(6, msg.procId());
            ps.setString(7, msg.msgId());
            ps.setInt(8, msg.facility());
            ps.setString(9, msg.sourceIp());
            ps.setString(10, msg.message());
            ps.setString(11, structuredDataJson);
            ps.setString(12, msg.raw());
            ps.executeUpdate();
            try (var rs = ps.getGeneratedKeys()) {
                return rs.next() ? rs.getLong(1) : -1;
            }
        }
    }

    /** Count events matching optional filters. */
    public long count(String hostnameLike, Integer severityMin, Integer severityMax) throws SQLException {
        StringBuilder sql = new StringBuilder("SELECT COUNT(*) FROM audit_event WHERE 1=1");
        List<Object> params = new ArrayList<>();
        if (hostnameLike != null && !hostnameLike.isBlank()) {
            sql.append(" AND hostname LIKE ?");
            params.add(hostnameLike);
        }
        if (severityMin != null) { sql.append(" AND severity >= ?"); params.add(severityMin); }
        if (severityMax != null) { sql.append(" AND severity <= ?"); params.add(severityMax); }
        try (var ps = conn.prepareStatement(sql.toString())) {
            for (int i = 0; i < params.size(); i++) ps.setObject(i + 1, params.get(i));
            try (var rs = ps.executeQuery()) {
                rs.next();
                return rs.getLong(1);
            }
        }
    }

    /** Page through events — newest first. */
    public synchronized List<AuditRow> query(String hostnameLike, Integer severityMin, Integer severityMax,
                                              int offset, int limit) throws SQLException {
        StringBuilder sql = new StringBuilder(
                "SELECT id, ts, hostname, severity, severity_name, app_name, proc_id, msg_id, " +
                "facility, source_ip, message, structured_data FROM audit_event WHERE 1=1");
        List<Object> params = new ArrayList<>();
        if (hostnameLike != null && !hostnameLike.isBlank()) {
            sql.append(" AND hostname LIKE ?");
            params.add(hostnameLike);
        }
        if (severityMin != null) { sql.append(" AND severity >= ?"); params.add(severityMin); }
        if (severityMax != null) { sql.append(" AND severity <= ?"); params.add(severityMax); }
        sql.append(" ORDER BY id DESC LIMIT ? OFFSET ?");
        try (var ps = conn.prepareStatement(sql.toString())) {
            for (int i = 0; i < params.size(); i++) ps.setObject(i + 1, params.get(i));
            ps.setInt(params.size() + 1, limit);
            ps.setInt(params.size() + 2, offset);
            List<AuditRow> out = new ArrayList<>(limit);
            try (var rs = ps.executeQuery()) {
                while (rs.next()) out.add(AuditRow.from(rs));
            }
            return out;
        }
    }

    public synchronized List<String> distinctHostnames(int limit) throws SQLException {
        try (var ps = conn.prepareStatement("SELECT DISTINCT hostname FROM audit_event " +
                "WHERE hostname IS NOT NULL ORDER BY hostname LIMIT ?")) {
            ps.setInt(1, limit);
            List<String> out = new ArrayList<>();
            try (var rs = ps.executeQuery()) {
                while (rs.next()) out.add(rs.getString(1));
            }
            return out;
        }
    }

    /** Apply a row cap (delete oldest rows beyond {@code keep}). */
    public synchronized int prune(int keep) throws SQLException {
        try (var ps = conn.prepareStatement(
                "DELETE FROM audit_event WHERE id NOT IN (SELECT id FROM audit_event ORDER BY id DESC LIMIT ?)")) {
            ps.setInt(1, keep);
            return ps.executeUpdate();
        }
    }

    public synchronized long totalCount() throws SQLException {
        try (var rs = conn.createStatement().executeQuery("SELECT COUNT(*) FROM audit_event")) {
            rs.next();
            return rs.getLong(1);
        }
    }

    @Override
    public synchronized void close() throws SQLException {
        if (conn != null && !conn.isClosed()) {
            try { conn.close(); } finally { conn = null; }
        }
    }
}
