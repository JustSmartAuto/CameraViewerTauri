package com.auditlog.server;

import com.auditlog.config.AppConfig;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.net.ssl.*;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
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
import java.security.KeyFactory;
import java.security.KeyStore;
import java.security.PrivateKey;
import java.security.cert.Certificate;
import java.security.cert.CertificateFactory;
import java.security.spec.PKCS8EncodedKeySpec;
import java.time.Instant;
import java.util.Base64;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
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
        ExecutorService p = pool;
        if (p != null) {
            p.shutdownNow();
            try {
                // 等客户端处理线程收尾，避免退出时还有线程在写库
                if (!p.awaitTermination(3, TimeUnit.SECONDS)) {
                    log.warn("syslog client handlers did not terminate in time");
                }
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
        }
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
            var in = client.getInputStream();
            // 明文监听口收到 TLS 握手 → 说明发送端开了加密，这里不可能解出任何文本。
            // 提前断开并给出明确提示，避免把握手二进制当成日志写进数据库。
            var pin = new java.io.PushbackInputStream(in, 8);
            byte[] head = pin.readNBytes(5);
            if (isTlsClientHello(head)) {
                log.error("client {} started a TLS handshake on the plaintext listener {}:{} — " +
                        "enable syslog.tls.enabled (keystore) or disable encryption on the sender",
                        peer, config.syslogBindAddress, config.syslogPort);
                return;
            }
            pin.unread(head);
            try (var reader = new BufferedReader(new InputStreamReader(pin, StandardCharsets.UTF_8))) {
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

    /** TLS 握手首字节特征：ContentType=handshake(0x16) + 版本 0x03xx（0x0301~0x0304）。 */
    private static boolean isTlsClientHello(byte[] head) {
        return head.length >= 3
                && head[0] == 0x16
                && head[1] == 0x03
                && head[2] >= 0x01 && head[2] <= 0x04;
    }

    /** 判断一段帧是否是正常文本：含解码替换符或非法控制字符即视为二进制垃圾。 */
    private static boolean isPlausibleText(String s) {
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '\uFFFD') return false;
            if (c < 0x20 && c != '\t') return false;
        }
        return true;
    }

    private void deliver(SyslogParser parser, String raw, String peer) {
        if (raw.isBlank()) return;
        if (!isPlausibleText(raw)) {
            // 二进制/解码残留（TLS 报文、乱码）——不入库，只记一条告警
            log.warn("dropped non-text frame from {} ({} chars)", peer, raw.length());
            return;
        }
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

            KeyStore ks;
            String keyPassword;
            if (notBlank(config.tlsCertPath) && notBlank(config.tlsKeyPath)) {
                // Cognex 附件的 Certification / Key 是 PEM，直接组一个内存密钥库
                ks = buildKeystoreFromPem();
                keyPassword = PEM_STORE_PASSWORD;
            } else {
                ks = KeyStore.getInstance("PKCS12");
                try (var in = Files.newInputStream(Path.of(config.tlsKeystorePath))) {
                    ks.load(in, config.tlsKeystorePassword.toCharArray());
                }
                keyPassword = config.tlsKeystorePassword;
            }
            KeyManagerFactory kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
            kmf.init(ks, keyPassword.toCharArray());

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
            log.info("TLS enabled: {}", ks.getType());
            return ctx;
        } catch (Exception e) {
            log.error("TLS init failed, running plaintext: {}", e.getMessage());
            return null;
        }
    }

    private static final String PEM_STORE_PASSWORD = "changeit";

    private static boolean notBlank(String s) { return s != null && !s.isBlank(); }

    /** 用 PEM 证书（可含链）+ PEM 私钥构建内存 PKCS#12 密钥库。 */
    private KeyStore buildKeystoreFromPem() throws Exception {
        Certificate[] chain = readPemCertificates(Path.of(config.tlsCertPath));
        PrivateKey key = readPemPrivateKey(Path.of(config.tlsKeyPath));
        KeyStore ks = KeyStore.getInstance("PKCS12");
        ks.load(null, null);
        ks.setKeyEntry("cognex-log", key, PEM_STORE_PASSWORD.toCharArray(), chain);
        log.info("TLS keystore built from PEM ({} cert(s))", chain.length);
        return ks;
    }

    private static Certificate[] readPemCertificates(Path p) throws Exception {
        try (InputStream in = Files.newInputStream(p)) {
            CertificateFactory cf = CertificateFactory.getInstance("X.509");
            var certs = cf.generateCertificates(in);
            if (certs.isEmpty()) throw new IllegalStateException("no certificate in " + p);
            return certs.toArray(new Certificate[0]);
        }
    }

    private static PrivateKey readPemPrivateKey(Path p) throws Exception {
        String pem = Files.readString(p, StandardCharsets.US_ASCII);
        if (pem.contains("BEGIN ENCRYPTED PRIVATE KEY")) {
            throw new IllegalStateException("encrypted PEM private keys are not supported: " + p);
        }
        String body = pem.replaceAll("-----BEGIN [^-]+-----", "")
                         .replaceAll("-----END [^-]+-----", "")
                         .replaceAll("[^A-Za-z0-9+/=]", "");
        byte[] der = Base64.getDecoder().decode(body);

        if (pem.contains("BEGIN PRIVATE KEY")) {
            // 标准 PKCS#8：头里不带算法，按常见算法逐个尝试
            for (String alg : new String[]{"RSA", "EC", "Ed25519"}) {
                try {
                    return KeyFactory.getInstance(alg).generatePrivate(new PKCS8EncodedKeySpec(der));
                } catch (Exception ignored) {}
            }
            throw new IllegalStateException("unsupported PKCS#8 private key: " + p);
        }
        if (pem.contains("BEGIN RSA PRIVATE KEY")) {
            // PKCS#1 → 包一层 PKCS#8
            byte[] pkcs8 = wrapRsaPkcs1(der);
            return KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(pkcs8));
        }
        throw new IllegalStateException("unsupported private key format (need PKCS#8 or PKCS#1 RSA): " + p);
    }

    private static byte[] wrapRsaPkcs1(byte[] pkcs1) {
        byte[] version = {0x02, 0x01, 0x00};
        byte[] algId = {0x30, 0x0d, 0x06, 0x09, 0x2a, (byte) 0x86, 0x48, (byte) 0x86,
                (byte) 0xf7, 0x0d, 0x01, 0x01, 0x01, 0x05, 0x00};
        byte[] octet = concat(new byte[]{0x04}, derLength(pkcs1.length), pkcs1);
        byte[] body = concat(version, algId, octet);
        return concat(new byte[]{0x30}, derLength(body.length), body);
    }

    private static byte[] derLength(int n) {
        if (n < 0x80) return new byte[]{(byte) n};
        if (n <= 0xFF) return new byte[]{(byte) 0x81, (byte) n};
        return new byte[]{(byte) 0x82, (byte) (n >> 8), (byte) n};
    }

    private static byte[] concat(byte[]... parts) {
        int len = 0;
        for (byte[] p : parts) len += p.length;
        byte[] out = new byte[len];
        int off = 0;
        for (byte[] p : parts) {
            System.arraycopy(p, 0, out, off, p.length);
            off += p.length;
        }
        return out;
    }
}
