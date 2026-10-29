package com.auditlog.server;

import com.auditlog.config.AppConfig;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.net.ssl.*;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.UnknownHostException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyStore;
import java.time.Instant;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;

/**
 * Syslog TCP server (port 5140 by default). Supports optional TLS/SSL
 * with configurable keystore/truststore (per the Cognex Fluentd article).
 * <p>
 * Two framing styles are accepted:
 * <ul>
 *   <li>Octet-counting: {@code "185 <134>1 2026-08-06T09:38:33Z OP90-125 ..."}</li>
 *   <li>Newline-terminated: one RFC 5424 message per line.</li>
 * </ul>
 */
public class SyslogServer {

    private static final Logger log = LoggerFactory.getLogger(SyslogServer.class);

    private final AppConfig config;
    private final Consumer<SyslogMessage> sink;
    private ServerSocket serverSocket;
    private ExecutorService pool;
    private volatile boolean running;
    private SSLContext sslContext;

    public SyslogServer(AppConfig config, Consumer<SyslogMessage> sink) {
        this.config = config;
        this.sink = sink;
    }

    public synchronized void start() throws IOException {
        if (running) return;
        running = true;

        if (config.tlsEnabled) {
            sslContext = buildSslContext();
        }

        if (sslContext != null) {
            serverSocket = sslContext.getServerSocketFactory().createServerSocket();
        } else {
            serverSocket = new ServerSocket();
        }
        serverSocket.setReuseAddress(true);
        InetAddress bindAddr = resolveBindAddress(config.syslogBindAddress);
        serverSocket.bind(new InetSocketAddress(bindAddr, config.syslogPort));

        pool = Executors.newVirtualThreadPerTaskExecutor();

        var acceptThread = new Thread(this::acceptLoop, "syslog-accept");
        acceptThread.setDaemon(true);
        acceptThread.start();

        log.info("Syslog server started on {}:{} (tls={})", bindAddr.getHostAddress(), config.syslogPort, config.tlsEnabled);
    }

    /** Resolve config.syslogBindAddress into an InetAddress.
     *  "0.0.0.0" / blank → wildcard (all interfaces); invalid strings → fallback to 0.0.0.0. */
    private static InetAddress resolveBindAddress(String bind) {
        if (bind == null || bind.isBlank() || "0.0.0.0".equals(bind)) {
            return wildcard();
        }
        try {
            return InetAddress.getByName(bind);
        } catch (UnknownHostException e) {
            log.warn("cannot resolve bind address '{}', falling back to 0.0.0.0: {}", bind, e.getMessage());
            return wildcard();
        }
    }

    /** Always resolve to 0.0.0.0 (wildcard) — guarded so it never propagates. */
    private static InetAddress wildcard() {
        try { return InetAddress.getByName("0.0.0.0"); }
        catch (UnknownHostException e) { return InetAddress.getLoopbackAddress(); }
    }

    public synchronized void stop() {
        running = false;
        try { if (serverSocket != null) serverSocket.close(); } catch (IOException ignored) {}
        try { if (pool != null) pool.shutdownNow(); } catch (Exception ignored) {}
        log.info("Syslog server stopped");
    }

    public boolean isRunning() { return running; }
    public int localPort() { return serverSocket == null ? -1 : serverSocket.getLocalPort(); }
    public boolean tlsActive() { return config.tlsEnabled && sslContext != null; }

    private void acceptLoop() {
        while (running) {
            try {
                Socket client = serverSocket.accept();
                client.setSoTimeout(0);
                pool.submit(() -> handleClient(client));
            } catch (IOException e) {
                if (running) log.warn("accept error: {}", e.getMessage());
            }
        }
    }

    private void handleClient(Socket client) {
        String peer = client.getRemoteSocketAddress().toString();
        try (client) {
            var parser = new SyslogParser();
            if (sslContext != null) {
                // SSL handshake timeout
            }
            try (var reader = new BufferedReader(new InputStreamReader(client.getInputStream(), StandardCharsets.UTF_8))) {
                StringBuilder buf = new StringBuilder();
                int c;
                while ((c = reader.read()) != -1) {
                    char ch = (char) c;
                    if (ch == '\n') {
                        deliver(parser, buf.toString(), peer);
                        buf.setLength(0);
                    } else {
                        buf.append(ch);
                        // handle octet-count framing: first token is message length
                        if (buf.length() > 1 && Character.isDigit(buf.charAt(0))) {
                            int sp = indexOf(buf, ' ');
                            if (sp > 0) {
                                int len = Integer.parseInt(buf.substring(0, sp));
                                if (buf.length() >= sp + 1 + len) {
                                    String framed = buf.substring(sp + 1, sp + 1 + len);
                                    deliver(parser, framed, peer);
                                    buf.delete(0, sp + 1 + len);
                                }
                            }
                        }
                    }
                }
                if (!buf.isEmpty()) deliver(parser, buf.toString(), peer);
            }
        } catch (IOException e) {
            log.debug("client {} disconnected: {}", peer, e.getMessage());
        } catch (Exception e) {
            log.error("client {} error", peer, e);
        }
    }

    private void deliver(SyslogParser parser, String raw, String peer) {
        if (raw.isBlank()) return;
        try {
            SyslogMessage msg = parser.parse(raw, stripBrackets(peer));
            // Fallback timestamp if parser couldn't get one
            if (msg.timestamp() == null) {
                msg = new SyslogMessage(Instant.now(), msg.hostname(), msg.facility(), msg.severity(),
                        msg.appName(), msg.procId(), msg.msgId(), msg.structuredData(),
                        msg.message(), msg.raw(), msg.sourceIp());
            }
            sink.accept(msg);
        } catch (Exception e) {
            log.warn("parse error: {}", e.getMessage());
        }
    }

    private static int indexOf(StringBuilder sb, char c) {
        for (int i = 0; i < sb.length(); i++) if (sb.charAt(i) == c) return i;
        return -1;
    }

    private static String stripBrackets(String s) {
        if (s.startsWith("/")) s = s.substring(1);
        if (s.startsWith("[")) {
            int end = s.indexOf(']');
            return end > 0 ? s.substring(1, end) : s;
        }
        int colon = s.indexOf(':');
        return colon > 0 ? s.substring(0, colon) : s;
    }

    private SSLContext buildSslContext() {
        try {
            SSLContext ctx = SSLContext.getInstance("TLS");

            KeyStore ks = KeyStore.getInstance("PKCS12");
            try (var in = Files.newInputStream(Path.of(config.tlsKeystorePath))) {
                ks.load(in, config.tlsKeystorePassword.toCharArray());
            }
            KeyManagerFactory kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
            kmf.init(ks, config.tlsKeystorePassword.toCharArray());

            TrustManager[] tms = null;
            if (config.tlsTruststorePath != null && !config.tlsTruststorePath.isBlank()) {
                KeyStore ts = KeyStore.getInstance("PKCS12");
                try (var in = Files.newInputStream(Path.of(config.tlsTruststorePath))) {
                    ts.load(in, config.tlsTruststorePassword.toCharArray());
                }
                TrustManagerFactory tmf = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm());
                tmf.init(ts);
                tms = tmf.getTrustManagers();
            }

            ctx.init(kmf.getKeyManagers(), tms, null);
            return ctx;
        } catch (Exception e) {
            log.error("TLS init failed, running plaintext: {}", e.getMessage());
            return null;
        }
    }
}
