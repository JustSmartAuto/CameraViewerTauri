# -*- coding: utf-8 -*-
"""
FTP 服务器端到端测试脚本（黑盒，纯标准库实现）
================================================

被测对象：CameraViewerTauri 内置 FTP 服务（src-tauri/src/lib.rs，libunftp）。

覆盖的被测行为：
  1. TCP 21（或自定义端口）监听，连接返回 220 欢迎信息；
  2. 认证：未知用户 / 错误密码返回 530；空密码用户任意密码可登录（默认用户 A1）；
  3. PWD / TYPE I / NOOP / SYST 等基础命令；
  4. PASV 被动模式上传（STOR）二进制文件，下载（RETR）内容 SHA256 一致；
  5. 【核心特性】向不存在的多层嵌套目录 STOR 文件，服务端自动递归创建目录；
  6. MKD / NLST / DELE / RMD；
  7. 若通过 --root 给出该用户配置的本地根目录，额外校验文件确实落盘且字节一致；
  8. 全部测试文件放在唯一临时目录 e2e_<时间戳>/ 下，结束自动清理。

前置条件：
  - 软件正在运行，并在「FTP 服务器」页启用了服务；
  - 端口、用户名/密码与软件中的 FTP 配置一致（默认 A1 / 空密码）。

用法：
  python ftp-test-end2end.py                                          # 默认 127.0.0.1:21, A1/空密码
  python ftp-test-end2end.py --host 192.168.1.10 --port 2121 --user cam --password 123
  python ftp-test-end2end.py --root "D:/CCD图片"                      # 同时做落盘校验
  python ftp-test-end2end.py --tls                                    # 服务端启用了 FTPS
  python ftp-test-end2end.py --secured-user test --secured-password test123  # 额外验证错误密码被拒
  python ftp-test-end2end.py --selftest                               # 内置模拟 FTP 服务自测，不依赖软件

说明：
  - --selftest 的模拟服务不支持 TLS；FTPS 链路请对真实软件加 --tls 测试。
  - 退出码：0 = 全部通过；1 = 存在失败用例；2 = 参数/环境错误。
"""

import argparse
import ftplib
import hashlib
import io
import os
import shutil
import socket
import socketserver
import sys
import threading
import time

# ---------------- 结果统计 ----------------

_passed = 0
_failed = 0
_skipped = 0


def ok(msg):
    global _passed
    _passed += 1
    print(f"  [PASS] {msg}")


def fail(msg):
    global _failed
    _failed += 1
    print(f"  [FAIL] {msg}")


def skip(msg):
    global _skipped
    _skipped += 1
    print(f"  [SKIP] {msg}")


def check(condition, pass_msg, fail_msg):
    if condition:
        ok(pass_msg)
    else:
        fail(fail_msg)
    return condition


def section(title):
    print(f"\n=== {title} ===")


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


# ---------------- 对真实/模拟服务通用的测试流程 ----------------

def run_ftp_tests(host, port, user, password, use_tls, local_root,
                  secured_user=None, secured_password=None):
    test_id = f"e2e_{int(time.time())}_{os.getpid()}"
    nested_rel = f"{test_id}/nested/deep"
    file_top = f"{test_id}/photo_top.bin"
    file_nested = f"{nested_rel}/photo_nested.bin"
    payload_top = os.urandom(8 * 1024)
    payload_nested = os.urandom(16 * 1024) + b"\r\nFTP-E2E-NESTED-MARKER\r\n"

    section("连接与欢迎信息")
    try:
        if use_tls:
            ctx = ssl_unverified_context()
            ftp = ftplib.FTP_TLS(context=ctx, timeout=8)
        else:
            ftp = ftplib.FTP(timeout=8)
        banner = ftp.connect(host, port)
        check(banner.startswith("220"), f"收到 220 欢迎信息（{banner[:60]}）",
              f"欢迎信息应以 220 开头，实际：{banner!r}")
    except Exception as e:
        fail(f"无法连接 FTP 服务 {host}:{port}：{e}（请确认软件中已启动 FTP 服务）")
        return

    try:
        # ---- 认证异常用例 ----
        section("认证（异常用例）")
        try:
            ftp.login("e2e_nobody_user", "whatever")
            fail("未知用户应被拒绝（530），但登录成功")
        except ftplib.error_perm as e:
            check(str(e).startswith("530"), f"未知用户被拒绝（{e}）",
                  f"未知用户应返回 530，实际：{e}")

        if secured_user:
            try:
                ftp.login(secured_user, secured_password + "_wrong_suffix")
                fail(f"用户 {secured_user} 错误密码应被拒绝，但登录成功")
            except ftplib.error_perm as e:
                check(str(e).startswith("530"), f"用户 {secured_user} 错误密码被拒绝（{e}）",
                      f"错误密码应返回 530，实际：{e}")
        else:
            skip("未提供 --secured-user/--secured-password，跳过「错误密码拒绝」用例"
                 "（默认用户 A1 为空密码，任意密码均可登录）")

        section("认证（正常登录）")
        try:
            login_resp = ftp.login(user, password if password else "")
            ok(f"用户 '{user}' 登录成功（{login_resp.strip()}）")
        except ftplib.error_perm as e:
            fail(f"用户 '{user}' 登录失败：{e}")
            return
        if use_tls:
            try:
                ftp.prot_p()
                ok("PROT P 数据通道加密协商成功")
            except Exception as e:
                fail(f"PROT P 协商失败：{e}")

        ftp.set_pasv(True)
        pwd = ftp.pwd()
        check(pwd == "/" or pwd.lower() in ("/", "."), f"登录后根目录为 {pwd!r}",
              f"登录后 PWD 应为 '/'，实际 {pwd!r}")
        resp = ftp.voidcmd("NOOP")
        check(resp.startswith("200"), f"NOOP -> {resp.strip()}", f"NOOP 异常：{resp}")

        # ---- 显式 MKD/RMD ----
        section("目录管理（MKD / RMD）")
        mkdir_name = f"{test_id}/plain_mkdir"
        try:
            # 注意：ftplib.mkd 成功时返回解析出的目录名（不是 257 原始响应）
            created = ftp.mkd(mkdir_name)
            check(created.replace("\\", "/").rstrip("/").endswith("plain_mkdir"),
                  f"MKD {mkdir_name} 成功（返回 {created!r}）",
                  f"MKD 返回值异常：{created!r}")
            ftp.rmd(mkdir_name)
            ok(f"RMD {mkdir_name} 成功")
        except ftplib.error_perm as e:
            fail(f"MKD/RMD 用例失败：{e}")

        # ---- 上传（含嵌套自动建目录） ----
        section("文件上传（STOR，含嵌套目录自动创建）")
        try:
            ftp.storbinary(f"STOR {file_top}", io.BytesIO(payload_top))
            ok(f"上传单层文件 {file_top}（{len(payload_top)} bytes）")
        except Exception as e:
            fail(f"上传 {file_top} 失败：{e}")

        try:
            ftp.storbinary(f"STOR {file_nested}", io.BytesIO(payload_nested))
            ok(f"上传嵌套文件 {file_nested}（不存在的目录应被自动递归创建）")
        except Exception as e:
            fail(f"上传嵌套文件失败（自动递归建目录特性异常）：{e}")

        # ---- 列表 ----
        section("目录列表（NLST）")
        try:
            raw_names = ftp.nlst(test_id)
            # 容忍不同服务端 "./name"、"/name" 等前缀形式
            names = {n.replace("\\", "/").rstrip("/").split("/")[-1] for n in raw_names}
            check("nested" in names and "plain_mkdir" not in names and "photo_top.bin" in names,
                  f"NLST {test_id} -> {sorted(names)}", f"NLST 内容不符：{raw_names}")
        except Exception as e:
            fail(f"NLST 失败：{e}")

        # ---- 下载校验 ----
        section("文件下载（RETR）与完整性")

        def retr_and_check(name, expected, label):
            buf = io.BytesIO()
            try:
                ftp.retrbinary(f"RETR {name}", buf.write)
                got = buf.getvalue()
                check(sha256_bytes(got) == sha256_bytes(expected),
                      f"{label} 下载内容 SHA256 一致（{len(got)} bytes）",
                      f"{label} 下载内容不一致（{len(got)} vs {len(expected)} bytes）")
            except Exception as e:
                fail(f"RETR {name} 失败：{e}")

        retr_and_check(file_top, payload_top, "单层文件")
        retr_and_check(file_nested, payload_nested, "嵌套文件")

        # ---- 落盘校验 ----
        section("本地落盘校验")
        if local_root:
            for rel, expected, label in [
                (file_top, payload_top, "单层文件"),
                (file_nested, payload_nested, "嵌套文件"),
            ]:
                disk_path = os.path.join(local_root, rel.replace("/", os.sep))
                if os.path.isfile(disk_path):
                    with open(disk_path, "rb") as f:
                        disk_bytes = f.read()
                    check(sha256_bytes(disk_bytes) == sha256_bytes(expected),
                          f"{label} 已落盘且内容一致：{disk_path}",
                          f"{label} 落盘内容不一致：{disk_path}")
                else:
                    fail(f"{label} 在本地根目录未找到：{disk_path}（检查 --root 是否为该用户配置的目录）")
        else:
            skip("未提供 --root，跳过本地落盘校验（协议级上传/下载已校验）")

        # ---- 删除清理（通过 FTP） ----
        section("清理（DELE / RMD）")
        for f in (file_top, file_nested):
            try:
                ftp.delete(f)
                ok(f"DELE {f}")
            except Exception as e:
                fail(f"DELE {f} 失败：{e}")
        for d in (f"{test_id}/nested/deep", f"{test_id}/nested", test_id):
            try:
                ftp.rmd(d)
                ok(f"RMD {d}")
            except Exception as e:
                fail(f"RMD {d} 失败：{e}")

        try:
            ftp.quit()
            ok("QUIT 正常退出")
        except Exception:
            ftp.close()
    finally:
        try:
            ftp.close()
        except Exception:
            pass


def ssl_unverified_context():
    import ssl
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    return ctx


# ---------------- 内置模拟 FTP 服务（--selftest，行为对齐 libunftp + 自动建目录包装） ----------------

class _MockFtpHandler(socketserver.StreamRequestHandler):
    server_version = "CameraViewerTauri-MockFTP/1.0"
    authed_user = None
    pasv_sock = None

    def _send(self, msg):
        self.wfile.write((msg + "\r\n").encode("latin-1"))
        self.wfile.flush()

    def _open_pasv_data(self):
        conn, _ = self.pasv_sock.accept()
        self.pasv_sock.close()
        self.pasv_sock = None
        return conn

    def handle(self):
        self.authed_user = None
        self.pasv_sock = None
        self._send(f"220 {self.server_version} ready")
        auth_pending = None
        cwd = "/"
        while True:
            line = self.rfile.readline()
            if not line:
                break
            try:
                text = line.decode("latin-1").strip()
            except Exception:
                self._send("501 bad encoding")
                continue
            if not text:
                continue
            parts = text.split(" ", 1)
            cmd = parts[0].upper()
            arg = parts[1].strip() if len(parts) > 1 else ""

            if cmd == "USER":
                if arg in self.server.users:
                    auth_pending = arg
                    self._send("331 need password")
                else:
                    auth_pending = None
                    self._send("331 need password")  # 不暴露用户是否存在
            elif cmd == "PASS":
                if auth_pending is None:
                    self._send("530 login incorrect")
                    continue
                expected = self.server.users[auth_pending]
                if expected == "" or arg == expected:
                    self.authed_user = auth_pending
                    cwd = "/"
                    self._send("230 logged in")
                else:
                    self._send("530 login incorrect")
            elif cmd == "QUIT":
                self._send("221 bye")
                break
            elif not self.authed_user:
                self._send("530 not logged in")
            elif cmd == "SYST":
                self._send("215 UNIX Type: L8")
            elif cmd == "FEAT":
                self._send("211-Features:\r\n PASV\r\n UTF8\r\n211 End")
            elif cmd == "NOOP":
                self._send("200 noop ok")
            elif cmd == "TYPE":
                self._send("200 type set")
            elif cmd == "PWD" or cmd == "XPWD":
                self._send(f'257 "{cwd}" is current directory')
            elif cmd == "CWD":
                target = self._resolve(arg or "/", cwd)
                if os.path.isdir(target):
                    cwd = self._to_virtual(target)
                    self._send(f'250 "{cwd}" is current directory')
                else:
                    self._send("550 no such directory")
            elif cmd == "PASV":
                if self.pasv_sock:
                    self.pasv_sock.close()
                s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                s.bind(("127.0.0.1", 0))
                s.listen(1)
                port = s.getsockname()[1]
                self.pasv_sock = s
                self._send(f"227 Entering Passive Mode (127,0,0,1,{port >> 8},{port & 0xFF})")
            elif cmd == "MKD" or cmd == "XMKD":
                target = self._resolve(arg, cwd)
                try:
                    os.makedirs(target, exist_ok=True)
                    self._send(f'257 "{arg}" created')
                except OSError as e:
                    self._send(f"550 mkdir failed: {e}")
            elif cmd == "RMD" or cmd == "XRMD":
                target = self._resolve(arg, cwd)
                try:
                    os.rmdir(target)
                    self._send("250 rmd ok")
                except OSError as e:
                    self._send(f"550 rmd failed: {e}")
            elif cmd == "DELE":
                target = self._resolve(arg, cwd)
                try:
                    os.remove(target)
                    self._send("250 dele ok")
                except OSError as e:
                    self._send(f"550 dele failed: {e}")
            elif cmd == "STOR":
                target = self._resolve(arg, cwd)
                os.makedirs(os.path.dirname(target), exist_ok=True)  # 核心特性：自动递归建目录
                self._send("150 opening data connection")
                try:
                    conn = self._open_pasv_data()
                    with open(target, "wb") as f:
                        while True:
                            chunk = conn.recv(65536)
                            if not chunk:
                                break
                            f.write(chunk)
                    conn.close()
                    self._send("226 transfer complete")
                except OSError as e:
                    self._send(f"550 stor failed: {e}")
            elif cmd == "RETR":
                target = self._resolve(arg, cwd)
                if not os.path.isfile(target):
                    self._send("550 no such file")
                    continue
                self._send("150 opening data connection")
                conn = self._open_pasv_data()
                with open(target, "rb") as f:
                    shutil.copyfileobj(f, conn.makefile("wb"))
                conn.close()
                self._send("226 transfer complete")
            elif cmd in ("NLST", "LIST"):
                target = self._resolve(arg, cwd) if arg else self._resolve(cwd, cwd)
                if not os.path.isdir(target):
                    self._send("550 no such directory")
                    continue
                names = os.listdir(target)
                if cmd == "LIST":
                    lines = []
                    for n in names:
                        full = os.path.join(target, n)
                        kind = "d" if os.path.isdir(full) else "-"
                        size = 0 if os.path.isdir(full) else os.path.getsize(full)
                        lines.append(f"{kind}rw-r--r-- 1 ftp ftp {size:>10} Jan 01 00:00 {n}")
                    payload = ("\r\n".join(lines) + "\r\n").encode("latin-1", "replace")
                else:
                    payload = ("\r\n".join(names) + ("\r\n" if names else "")).encode("latin-1", "replace")
                self._send("150 opening data connection")
                conn = self._open_pasv_data()
                conn.sendall(payload)
                conn.close()
                self._send("226 transfer complete")
            elif cmd in ("EPSV", "AUTH", "PBSZ"):
                self._send("502 not supported in mock")
            else:
                self._send(f"502 command {cmd} not implemented in mock")

    def _resolve(self, virtual_path, cwd):
        root = self.server.storage_root
        if not virtual_path.startswith("/"):
            virtual_path = cwd.rstrip("/") + "/" + virtual_path
        parts = [p for p in virtual_path.replace("\\", "/").split("/") if p not in ("", ".")]
        real = os.path.join(root, *parts) if parts else root
        return real

    def _to_virtual(self, real_path):
        rel = os.path.relpath(real_path, self.server.storage_root)
        if rel == ".":
            return "/"
        return "/" + rel.replace(os.sep, "/")

    def finish(self):
        if self.pasv_sock:
            self.pasv_sock.close()
        super().finish()


class _ThreadingFtpServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self, addr, storage_root, users):
        self.storage_root = storage_root
        self.users = users
        super().__init__(addr, _MockFtpHandler)


def run_selftest():
    storage = os.path.join(os.environ.get("TEMP", "."), f"cvt_ftp_selftest_{int(time.time())}")
    os.makedirs(storage, exist_ok=True)
    users = {"A1": "", "test": "test123"}
    server = _ThreadingFtpServer(("127.0.0.1", 0), storage, users)
    port = server.server_address[1]
    threading.Thread(target=server.serve_forever, daemon=True).start()
    print(f"[INFO] selftest：内置模拟 FTP 服务监听 127.0.0.1:{port}，存储根目录 {storage}")
    time.sleep(0.2)
    try:
        run_ftp_tests(
            host="127.0.0.1", port=port, user="A1", password="",
            use_tls=False, local_root=storage,
            secured_user="test", secured_password="test123",
        )
    finally:
        server.shutdown()
        shutil.rmtree(storage, ignore_errors=True)
    return _failed


# ---------------- 入口 ----------------

def main():
    parser = argparse.ArgumentParser(description="CameraViewerTauri 内置 FTP 服务端到端测试")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=21)
    parser.add_argument("--user", default="A1", help="FTP 用户名（默认 A1）")
    parser.add_argument("--password", default="", help="FTP 密码（默认空）")
    parser.add_argument("--tls", action="store_true", help="服务端启用了 FTPS（AUTH TLS + PROT P）")
    parser.add_argument("--root", default="", help="该用户在软件中配置的本地根目录，用于落盘校验")
    parser.add_argument("--secured-user", default="", help="配置了非空密码的用户名，用于验证错误密码被拒")
    parser.add_argument("--secured-password", default="", help="上述用户的正确密码")
    parser.add_argument("--selftest", action="store_true",
                        help="启动内置模拟 FTP 服务对测试逻辑进行自测，不依赖被测软件")
    args = parser.parse_args()

    if args.selftest:
        failed = run_selftest()
    else:
        root = args.root.strip()
        if root and not os.path.isdir(root):
            print(f"[WARN] --root 目录不存在：{root}，落盘校验将失败")
        print(f"[INFO] 目标: {args.host}:{args.port} 用户={args.user!r} TLS={args.tls} "
              f"落盘校验={'有(' + root + ')' if root else '无'}")
        run_ftp_tests(
            host=args.host, port=args.port, user=args.user, password=args.password,
            use_tls=args.tls, local_root=root or None,
            secured_user=args.secured_user or None,
            secured_password=args.secured_password or None,
        )
        failed = _failed

    print(f"\n========== 结果: {_passed} 通过 / {failed} 失败 / {_skipped} 跳过 ==========")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
