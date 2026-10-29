package com.auditlog.util;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.net.Inet4Address;
import java.net.NetworkInterface;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Enumeration;
import java.util.List;

/**
 * 首次启动时在 {@code <config>/tls/} 下生成自签 CA 与服务器证书（全部为无口令保护的 PEM）：
 * <ul>
 *   <li>{@code ca-cert.pem} —— CA 公钥，导入相机「CA 公钥」用</li>
 *   <li>{@code ca-key.pem} —— CA 私钥（仅服务器留存，用于重签）</li>
 *   <li>{@code server-cert.pem} —— 服务器证书链（服务器证书 + CA 证书）</li>
 *   <li>{@code server-key.pem} —— 服务器私钥（PKCS#8 PEM）</li>
 * </ul>
 *
 * <p>用纯 JDK 手写 DER 生成 X.509，不依赖 BouncyCastle，也不依赖部署机上有 keytool/openssl。</p>
 */
public final class TlsCertManager {

    private static final Logger log = LoggerFactory.getLogger(TlsCertManager.class);

    public static final String TLS_DIR = "tls";
    public static final String CA_CERT = "ca-cert.pem";
    public static final String CA_KEY = "ca-key.pem";
    public static final String SERVER_CERT = "server-cert.pem";
    public static final String SERVER_KEY = "server-key.pem";

    /** 生成的证书文件路径集合。 */
    public record Paths(Path dir, Path caCert, Path caKey, Path serverCert, Path serverKey) {}

    private static final byte[] OID_CN = {0x06, 0x03, 0x55, 0x04, 0x03};
    private static final byte[] OID_O  = {0x06, 0x03, 0x55, 0x04, 0x0A};
    private static final byte[] OID_BASIC_CONSTRAINTS = {0x06, 0x03, 0x55, 0x1D, 0x13};
    private static final byte[] OID_KEY_USAGE = {0x06, 0x03, 0x55, 0x1D, 0x0F};
    private static final byte[] OID_EXT_KEY_USAGE = {0x06, 0x03, 0x55, 0x1D, 0x25};
    private static final byte[] OID_SUBJECT_ALT_NAME = {0x06, 0x03, 0x55, 0x1D, 0x11};
    private static final byte[] OID_SERVER_AUTH = {0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x01};
    /** sha256WithRSAEncryption 1.2.840.113549.1.1.11 + NULL */
    private static final byte[] ALG_SHA256_RSA = {
            0x30, 0x0D, 0x06, 0x09, 0x2A, (byte) 0x86, 0x48, (byte) 0x86,
            (byte) 0xF7, 0x0D, 0x01, 0x01, 0x0B, 0x05, 0x00};

    private TlsCertManager() {}

    /**
     * 确保证书存在（缺失时生成）。已存在则直接复用，不会覆盖用户手工放入的证书。
     */
    public static Paths ensure(Path configDir) throws Exception {
        Path dir = configDir.resolve(TLS_DIR);
        Files.createDirectories(dir);
        Paths p = new Paths(dir,
                dir.resolve(CA_CERT), dir.resolve(CA_KEY),
                dir.resolve(SERVER_CERT), dir.resolve(SERVER_KEY));

        if (Files.exists(p.caCert()) && Files.exists(p.caKey())
                && Files.exists(p.serverCert()) && Files.exists(p.serverKey())) {
            return p;
        }

        log.info("生成自签 TLS 证书与私钥: {}", dir);
        KeyPair caKp = generateKeyPair();
        byte[] caName = name("CN=CognexLoggingServer CA, O=CognexLoggingServer");
        X509Certificate caCert = buildCertificate(caName, caName, caKp.getPublic(), caKp.getPrivate(), 10,
                extensions(true, List.of()));
        writePem(p.caCert(), pem("CERTIFICATE", caCert.getEncoded()));
        writePem(p.caKey(), pkcs8Pem(caKp.getPrivate()));

        KeyPair srvKp = generateKeyPair();
        byte[] srvName = name("CN=CognexLoggingServer, O=CognexLoggingServer");
        X509Certificate srvCert = buildCertificate(srvName, caName, srvKp.getPublic(), caKp.getPrivate(), 10,
                extensions(false, localIpv4Addresses()));
        srvCert.verify(caKp.getPublic());   // 自检：链能验通

        writePem(p.serverCert(), pem("CERTIFICATE", srvCert.getEncoded())
                + pem("CERTIFICATE", caCert.getEncoded()));
        writePem(p.serverKey(), pkcs8Pem(srvKp.getPrivate()));
        log.info("TLS 证书已生成: CA={} 服务器证书={}", p.caCert(), p.serverCert());
        return p;
    }

    private static KeyPair generateKeyPair() throws Exception {
        KeyPairGenerator kpg = KeyPairGenerator.getInstance("RSA");
        kpg.initialize(2048);
        return kpg.generateKeyPair();
    }

    /** 本机所有 IPv4 地址，写进服务器证书的 SAN，避免相机按 IP 校验时失败。 */
    private static List<byte[]> localIpv4Addresses() {
        List<byte[]> out = new ArrayList<>();
        out.add(hexToBytes("7F000001")); // 127.0.0.1
        try {
            Enumeration<NetworkInterface> nis = NetworkInterface.getNetworkInterfaces();
            while (nis != null && nis.hasMoreElements()) {
                NetworkInterface ni = nis.nextElement();
                if (!ni.isUp() || ni.isLoopback()) continue;
                Enumeration<java.net.InetAddress> addrs = ni.getInetAddresses();
                while (addrs.hasMoreElements()) {
                    var a = addrs.nextElement();
                    if (a instanceof Inet4Address && !a.isLoopbackAddress()) {
                        out.add(a.getAddress());
                    }
                }
            }
        } catch (Exception ignored) {}
        return out;
    }

    private static byte[] hexToBytes(String hex) {
        byte[] b = new byte[hex.length() / 2];
        for (int i = 0; i < b.length; i++) {
            b[i] = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
        }
        return b;
    }

    /** 构造 v3 证书并返回解析后的对象（自签时 issuer == subject，signerKey 为该证书私钥）。 */
    private static X509Certificate buildCertificate(byte[] subject, byte[] issuer, PublicKey pub,
                                                    PrivateKey signerKey, int years, byte[] extensions) throws Exception {
        byte[] tbs = der(0x30, concat(
                der(0xA0, der(0x02, new byte[]{0x02})),        // version v3
                der(0x02, randomSerial()),
                ALG_SHA256_RSA,
                issuer,
                validity(years),
                subject,
                pub.getEncoded(),                              // SubjectPublicKeyInfo
                der(0xA3, extensions)                          // [3] EXPLICIT Extensions
        ));
        byte[] sig = sign(tbs, signerKey);
        byte[] cert = der(0x30, concat(tbs, ALG_SHA256_RSA, der(0x03, concat(new byte[]{0}, sig))));
        try (var in = new java.io.ByteArrayInputStream(cert)) {
            return (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(in);
        }
    }

    private static byte[] sign(byte[] data, PrivateKey key) throws Exception {
        Signature s = Signature.getInstance("SHA256withRSA");
        s.initSign(key);
        s.update(data);
        return s.sign();
    }

    private static byte[] randomSerial() {
        byte[] s = new byte[8];
        new SecureRandom().nextBytes(s);
        s[0] = 0; // 保证正整数
        return s;
    }

    private static byte[] validity(int years) {
        DateTimeFormatter f = DateTimeFormatter.ofPattern("yyMMddHHmmss'Z'").withZone(ZoneOffset.UTC);
        Instant now = Instant.now();
        String from = f.format(now.minusSeconds(86400L));
        String to = f.format(now.plusSeconds(86400L * 365L * years));
        return der(0x30, concat(utcTime(from), utcTime(to)));
    }

    private static byte[] utcTime(String s) {
        return der(0x17, s.getBytes(StandardCharsets.US_ASCII));
    }

    /** RDNSequence：CN + O。 */
    private static byte[] name(String dn) {
        List<byte[]> rdns = new ArrayList<>();
        for (String part : dn.split(",")) {
            String[] kv = part.trim().split("=", 2);
            if (kv.length != 2) continue;
            byte[] oid = "CN".equals(kv[0]) ? OID_CN : OID_O;
            byte[] atv = der(0x30, concat(oid, der(0x0C, kv[1].getBytes(StandardCharsets.UTF_8))));
            rdns.add(der(0x31, atv));
        }
        return der(0x30, concat(rdns.toArray(new byte[0][])));
    }

    /** 扩展序列：CA 用 basicConstraints+keyUsage；服务器证书再加 SAN 与 serverAuth。 */
    private static byte[] extensions(boolean isCa, List<byte[]> sanIps) {
        List<byte[]> exts = new ArrayList<>();

        byte[] bcValue = isCa
                ? der(0x30, der(0x01, new byte[]{(byte) 0xFF}))    // SEQUENCE { TRUE }
                : der(0x30, new byte[0]);                          // SEQUENCE {} (cA=FALSE)
        exts.add(extension(OID_BASIC_CONSTRAINTS, true, bcValue));

        byte[] kuBits = isCa ? new byte[]{0x06} : new byte[]{(byte) 0xA0};
        exts.add(extension(OID_KEY_USAGE, true, der(0x03, concat(new byte[]{0}, kuBits))));

        if (!isCa) {
            exts.add(extension(OID_EXT_KEY_USAGE, false, der(0x30, OID_SERVER_AUTH)));

            List<byte[]> names = new ArrayList<>();
            names.add(der(0x82, "localhost".getBytes(StandardCharsets.US_ASCII)));
            for (byte[] ip : sanIps) names.add(der(0x87, ip));
            exts.add(extension(OID_SUBJECT_ALT_NAME, false, der(0x30, concat(names.toArray(new byte[0][])))));
        }
        return der(0x30, concat(exts.toArray(new byte[0][])));
    }

    private static byte[] extension(byte[] oid, boolean critical, byte[] value) {
        byte[] body = critical
                ? concat(oid, der(0x01, new byte[]{(byte) 0xFF}), der(0x04, value))
                : concat(oid, der(0x04, value));
        return der(0x30, body);
    }

    // ---------- DER 基础工具 ----------

    private static byte[] der(int tag, byte[] content) {
        return concat(new byte[]{(byte) tag}, derLength(content.length), content);
    }

    private static byte[] derLength(int n) {
        if (n < 0x80) return new byte[]{(byte) n};
        if (n <= 0xFF) return new byte[]{(byte) 0x81, (byte) n};
        if (n <= 0xFFFF) return new byte[]{(byte) 0x82, (byte) (n >> 8), (byte) n};
        return new byte[]{(byte) 0x83, (byte) (n >> 16), (byte) (n >> 8), (byte) n};
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

    // ---------- PEM 输出 ----------

    private static String pem(String type, byte[] der) {
        return "-----BEGIN " + type + "-----\n"
                + Base64.getMimeEncoder(64, "\n".getBytes(StandardCharsets.US_ASCII)).encodeToString(der)
                + "\n-----END " + type + "-----\n";
    }

    private static String pkcs8Pem(PrivateKey key) {
        return pem("PRIVATE KEY", key.getEncoded());
    }

    private static void writePem(Path file, String content) throws Exception {
        Files.writeString(file, content, StandardCharsets.US_ASCII);
    }
}
