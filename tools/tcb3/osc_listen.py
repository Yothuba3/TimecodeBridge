#!/usr/bin/env python3
"""OSC(UDP)を受けて 1 行 1 メッセージの JSON で出す。Host が本当に送ったかを実機確認で確かめる用。

例: osc_listen.py --port 9100 --count 1 --timeout 20 --out /tmp/osc.jsonl
  --count 件受けたら終了コード 0、--timeout 秒で足りなければ 1。--count 0 は Ctrl-C まで受け続ける。
"""
import argparse
import json
import socket
import struct
import sys
import time
from datetime import datetime, timezone


def _read_string(buf, i):
    end = buf.index(b"\x00", i)
    text = buf[i:end].decode("utf-8", "replace")
    i = (end + 1 + 3) & ~3
    return text, i


def parse_message(buf):
    address, i = _read_string(buf, 0)
    args = []
    if i >= len(buf) or buf[i:i + 1] != b",":
        return address, args
    tags, i = _read_string(buf, i)
    for tag in tags[1:]:
        if tag == "i":
            args.append(struct.unpack(">i", buf[i:i + 4])[0]); i += 4
        elif tag == "f":
            args.append(struct.unpack(">f", buf[i:i + 4])[0]); i += 4
        elif tag == "s":
            value, i = _read_string(buf, i); args.append(value)
        elif tag == "b":
            size = struct.unpack(">i", buf[i:i + 4])[0]; i += 4
            args.append({"blob": buf[i:i + size].hex()}); i += (size + 3) & ~3
        elif tag == "T":
            args.append(True)
        elif tag == "F":
            args.append(False)
        elif tag == "N":
            args.append(None)
        else:
            args.append({"unsupported": tag})
            break
    return address, args


def parse_packet(buf, out):
    if buf.startswith(b"#bundle\x00"):
        i = 16
        while i + 4 <= len(buf):
            size = struct.unpack(">i", buf[i:i + 4])[0]; i += 4
            parse_packet(buf[i:i + size], out); i += size
    else:
        out.append(parse_message(buf))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=9000)
    ap.add_argument("--bind", default="0.0.0.0")
    ap.add_argument("--count", type=int, default=0, help="この件数を受けたら終了(0 = 無制限)")
    ap.add_argument("--timeout", type=float, default=0, help="秒。0 = 無制限")
    ap.add_argument("--out", help="JSON Lines の書き出し先(標準出力にも出す)")
    args = ap.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((args.bind, args.port))
    out_file = open(args.out, "a", encoding="utf-8") if args.out else None
    deadline = time.monotonic() + args.timeout if args.timeout > 0 else None
    received = 0
    try:
        while args.count <= 0 or received < args.count:
            if deadline is not None:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    return 1
                sock.settimeout(remaining)
            try:
                data, (host, port) = sock.recvfrom(65536)
            except socket.timeout:
                return 1
            messages = []
            try:
                parse_packet(data, messages)
            except Exception as ex:  # 壊れたパケットも記録して続ける
                messages.append(("<parse error: %s>" % ex, [data.hex()]))
            for address, values in messages:
                line = json.dumps({
                    "t": datetime.now(timezone.utc).isoformat(timespec="milliseconds"),
                    "from": "%s:%d" % (host, port),
                    "address": address,
                    "args": values,
                }, ensure_ascii=False)
                print(line, flush=True)
                if out_file:
                    out_file.write(line + "\n"); out_file.flush()
                received += 1
        return 0
    except KeyboardInterrupt:
        return 0
    finally:
        if out_file:
            out_file.close()
        sock.close()


if __name__ == "__main__":
    sys.exit(main())
