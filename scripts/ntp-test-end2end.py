# -*- coding: utf-8 -*-
"""
NTP 时间服务器端到端测试脚本（黑盒，纯标准库实现）
====================================================

被测对象：CameraViewerTauri 内置 NTP 服务（src-tauri/src/lib.rs 中的 run_ntp_server）。

被测服务行为（与 Rust 实现严格对齐，修改实现后请同步修订本脚本）：
  1. UDP 协议，绑定 0.0.0.0:<port>，默认端口 123；
  2. 仅响应长度 >= 48 且 LI/VN/Mode 字节中 mode == 3（客户端）的请求；
  3. 响应包 48 字节：
       byte 0      LI=0 VN=3 Mode=4  -> 0x1C
       byte 1      stratum = 1
       byte 3      precision = 0xFA
       bytes 12-15 reference id = b"LOCL"
       bytes 24-31 origin 时间戳 = 请求包 bytes 40-47（回显客户端 transmit）
       bytes 40-47 transmit 时间戳
  4. 【重要】返回的时间是 UTC+8（北京时间），不是标准 NTP 规定的 UTC。
     这是本软件面向不会做时区换算的国产相机的有意设计，默认按 +8 小时校验；
     若被测对象改为标准 UTC，请加 --expect-offset-hours 0。

前置条件：
  - 软件正在运行，并在「NTP 校时」页启用了 NTP 服务（端口与 --port 一致）。

用法：
  python ntp-test-end2end.py                              # 默认测 127.0.0.1:123，预期 UTC+8
  python ntp-test-end2end.py --host 192.168.1.10 --port 8123
  python ntp-test-end2end.py --expect-offset-hours 0      # 标准 UTC 服务器
  python ntp-test-end2end.py --selftest                   # 不依赖软件，用内置模拟服务自测

退出码：0 = 全部通过；1 = 存在失败用例；2 = 参数/环境错误。
"""

import argparse
import socket
import struct
import sys
import threading
import time

NTP_EPOCH_DELTA = 2208988800  # 1900-01-01 与 1970-01-01 之间的秒数

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


# ---------------- NTP 协议工具 ----------------

def now_ntp_pair(offset_hours):
    """返回当前时间在指定时区偏移下的 NTP (秒, 小数部分)。"""
    t = time.time() + offset_hours * 3600
    seconds = int(t) + NTP_EPOCH_DELTA
    fraction = int((t - int(t)) * (1 << 32))
    return seconds, fraction


def make_client_packet(mode=3, length=48, fill_origin_echo=True):
    """构造客户端请求包。LI=0, VN=3，mode 可指定用于异常用例。"""
    pkt = bytearray(length)
    pkt[0] = (0 << 6) | (3 << 3) | (mode & 0x07)
    if fill_origin_echo and length >= 48:
        secs, frac = now_ntp_pair(0)
        struct.pack_into(">II", pkt, 40, secs, frac)
    return bytes(pkt)


def read_timestamp(pkt, offset):
    secs, frac = struct.unpack_from(">II", pkt, offset)
    return secs, frac, secs + frac / (1 << 32)


def ntp_seconds_to_unix(secs):
    return secs - NTP_EPOCH_DELTA


# ---------------- 核心测试 ----------------

def query_ntp(sock, host, port, packet, timeout=2.0, wait_reply=True):
    """发送一个 NTP 请求，wait_reply=True 时返回响应 bytes，无响应返回 None。"""
    sock.settimeout(timeout)
    sock.sendto(packet, (host, port))
    if not wait_reply:
        return None
    try:
        data, _ = sock.recvfrom(2048)
        return data
    except socket.timeout:
        return None


def test_valid_request(host, port, offset_hours, tolerance_secs, retries):
    """正常 mode=3 请求：校验响应结构与时间正确性。"""
    section("正常请求（mode=3，48 字节）")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        packet = make_client_packet(mode=3)
        req_origin = packet[40:48]  # 期望被服务端回显

        data = None
        for i in range(retries):
            data = query_ntp(sock, host, port, packet)
            if data is not None:
                break
            print(f"  [INFO] 第 {i + 1}/{retries} 次请求无响应，重试...")

        if not check(data is not None, "收到 NTP 响应",
                     f"UDP/{port} 连续 {retries} 次无响应，请确认软件中已启动 NTP 服务且端口正确"):
            return False

        if not check(len(data) == 48, f"响应长度为 48 字节（实际 {len(data)}）",
                     f"响应长度应为 48，实际 {len(data)}"):
            return False

        li = (data[0] >> 6) & 0x03
        vn = (data[0] >> 3) & 0x07
        mode = data[0] & 0x07
        check(li == 0, "LI（闰秒标志）= 0", f"LI 应为 0，实际 {li}")
        check(vn == 3, f"VN（版本号）= 3（实际 {vn}）", f"VN 应为 3，实际 {vn}")
        check(mode == 4, f"Mode = 4（服务器，实际 {mode}）", f"Mode 应为 4，实际 {mode}")
        check(data[0] == 0x1C, f"首字节 = 0x1C（实际 0x{data[0]:02X}）",
              f"首字节应为 0x1C，实际 0x{data[0]:02X}")

        check(data[1] == 1, f"Stratum = 1（实际 {data[1]}）",
              f"Stratum 应为 1，实际 {data[1]}")
        check(data[3] == 0xFA, f"Precision = 0xFA（实际 0x{data[3]:02X}）",
              f"Precision 应为 0xFA，实际 0x{data[3]:02X}")
        check(bytes(data[12:16]) == b"LOCL", 'Reference ID = "LOCL"',
              f'Reference ID 应为 "LOCL"，实际 {bytes(data[12:16])!r}')

        # origin 时间戳必须原样回显请求包的 transmit
        check(bytes(data[24:32]) == req_origin, "Origin 时间戳正确回显客户端 Transmit",
              "Origin 时间戳未回显请求包 bytes[40:48]")

        _, _, ref_t = read_timestamp(data, 16)
        _, _, rcv_t = read_timestamp(data, 32)
        _, _, tx_t = read_timestamp(data, 40)
        check(ref_t <= rcv_t <= tx_t + 1e-6,
              f"时间戳顺序 Reference <= Receive <= Transmit 合理",
              f"时间戳顺序异常：ref={ref_t}, recv={rcv_t}, tx={tx_t}")

        # transmit 时间与本机预期时间对比
        expected_secs, expected_frac = now_ntp_pair(offset_hours)
        expected_ntp = expected_secs + expected_frac / (1 << 32)
        delta = abs(tx_t - expected_ntp)
        check(delta <= tolerance_secs,
              f"Transmit 时间与预期（UTC{'+' if offset_hours >= 0 else ''}{offset_hours}）"
              f"偏差 {delta:.3f}s <= {tolerance_secs}s",
              f"Transmit 时间偏差 {delta:.3f}s 超过 {tolerance_secs}s（检查时区偏移参数）")

        unix_time = ntp_seconds_to_unix(struct.unpack_from(">I", data, 40)[0])
        print(f"  [INFO] 服务器返回时间(UTC{offset_hours:+d}): "
              f"{time.strftime('%Y-%m-%d %H:%M:%S', time.gmtime(unix_time))}")
        return True
    finally:
        sock.close()


def test_ignored_packets(host, port):
    """异常请求：非客户端模式、过短包都不应得到响应。"""
    section("异常请求（应被服务端静默丢弃）")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        # mode=0 保留模式
        data = query_ntp(sock, host, port, make_client_packet(mode=0), timeout=1.5)
        check(data is None, "mode=0（保留）请求无响应", "服务端不应响应 mode=0 请求")

        # mode=4 服务器模式
        data = query_ntp(sock, host, port, make_client_packet(mode=4), timeout=1.5)
        check(data is None, "mode=4（服务器）请求无响应", "服务端不应响应 mode=4 请求")

        # 长度不足 48
        short = make_client_packet(mode=3, length=10)
        data = query_ntp(sock, host, port, short, timeout=1.5)
        check(data is None, "10 字节短包无响应", "服务端不应响应长度 < 48 的请求")
    finally:
        sock.close()


# ---------------- 内置模拟 NTP 服务（--selftest 用，行为与 Rust 实现一致） ----------------

class MockNtpServer(threading.Thread):
    def __init__(self, offset_hours=8):
        super().__init__(daemon=True)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self.port = self.sock.getsockname()[1]
        self.offset_hours = offset_hours
        self._stop = threading.Event()

    def run(self):
        self.sock.settimeout(0.2)
        while not self._stop.is_set():
            try:
                buf, src = self.sock.recvfrom(1024)
            except socket.timeout:
                continue
            except OSError:
                break
            if len(buf) < 48:
                continue
            if (buf[0] & 0x07) != 3:
                continue
            secs, frac = now_ntp_pair(self.offset_hours)
            resp = bytearray(48)
            resp[0] = 0x1C
            resp[1] = 1
            resp[3] = 0xFA
            resp[12:16] = b"LOCL"
            struct.pack_into(">II", resp, 16, secs, frac)
            resp[24:32] = buf[40:48]
            struct.pack_into(">II", resp, 32, secs, frac)
            struct.pack_into(">II", resp, 40, secs, frac)
            self.sock.sendto(bytes(resp), src)

    def stop(self):
        self._stop.set()
        self.sock.close()


# ---------------- 入口 ----------------

def main():
    parser = argparse.ArgumentParser(description="CameraViewerTauri 内置 NTP 服务端到端测试")
    parser.add_argument("--host", default="127.0.0.1", help="NTP 服务器地址（默认 127.0.0.1）")
    parser.add_argument("--port", type=int, default=123, help="NTP 端口（默认 123）")
    parser.add_argument("--expect-offset-hours", type=int, default=8, metavar="H",
                        help="预期服务器相对 UTC 的时区偏移小时数，软件默认 8（北京时间），标准 NTP 传 0")
    parser.add_argument("--tolerance", type=float, default=5.0,
                        help="允许的时间偏差秒数（默认 5.0）")
    parser.add_argument("--retries", type=int, default=3, help="无响应时重试次数（默认 3）")
    parser.add_argument("--selftest", action="store_true",
                        help="启动内置模拟 NTP 服务对测试逻辑进行自测，不依赖被测软件")
    args = parser.parse_args()

    host, port, offset = args.host, args.port, args.expect_offset_hours
    if args.selftest:
        print("[INFO] selftest：启动内置模拟 NTP 服务（UTC+8 行为）...")
        server = MockNtpServer(offset_hours=8)
        server.start()
        host, port, offset = "127.0.0.1", server.port, 8
        time.sleep(0.2)
    else:
        server = None

    print(f"[INFO] 目标: {host}:{port}（UDP），预期时区偏移 UTC{offset:+d} 小时")

    try:
        test_valid_request(host, port, offset, args.tolerance, args.retries)
        test_ignored_packets(host, port)
    finally:
        if server is not None:
            server.stop()

    print(f"\n========== 结果: {_passed} 通过 / {_failed} 失败 / {_skipped} 跳过 ==========")
    return 1 if _failed else 0


if __name__ == "__main__":
    sys.exit(main())
