#!/usr/bin/env bash
# smoke.sh — 実機(このMac)で LTC ループバック受信 → キュー発火 → OSC 受信 → 撮影 まで人手なしで通す。
# 前提: tcb3ctl start --build 済みか Debug ビルドがあること、Pro Tools Audio Bridge 等の同名入出力デバイスがあること。
# 使い方: tools/tcb3/smoke.sh [--keep]   (--keep で終了時に Host を止めない)
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CTL="$HERE/tcb3ctl"
DEVICE="${TCB3_SELFTEST_DEVICE:-Pro Tools Audio Bridge 2-A}"
OSC_PORT="${TCB3_OSC_PORT:-9100}"
export TCB3_RUN_DIR="${TCB3_RUN_DIR:-/tmp/tcb3ctl}"
KEEP=0; [ "${1:-}" = "--keep" ] && KEEP=1
FAIL=0

step() { echo "== $*"; }
ok() { echo "   ok: $*"; }
ng() { echo "   NG: $*"; FAIL=1; }
add_seconds() { python3 -c 'import sys; h,m,s,_=map(int,sys.argv[1].replace(";",":").split(":")); t=(h*3600+m*60+s+int(sys.argv[2]))%86400; print("%02d:%02d:%02d:00"%(t//3600,t%3600//60,t%60))' "$1" "$2"; }

mkdir -p "$TCB3_RUN_DIR"
OSC_OUT="$TCB3_RUN_DIR/osc-$(date +%H%M%S).jsonl"

"$CTL" stop >/dev/null 2>&1 || true
step "起動(自己テスト LTC → $DEVICE)"
"$CTL" start --selftest "$DEVICE" || { echo "SMOKE FAIL: 起動できません"; exit 1; }

step "LTC 受信"
if "$CTL" wait '.state.transport.status=="receiving"' 20; then ok "transport.status=receiving"; else ng "receiving にならない"; fi
"$CTL" state '.state.transport | {statusText, detailText, signalErrorRatePercent, locked}'
c1=$("$CTL" clock | jq -r .raw); sleep 1; c2=$("$CTL" clock | jq -r .raw)
if [ "$c1" != "$c2" ]; then ok "時計が進む ($c1 → $c2)"; else ng "時計が進まない ($c1)"; fi
err=$("$CTL" state '.state.transport.signalErrorRatePercent // 0')
if awk -v e="$err" 'BEGIN{exit !(e < 1)}'; then ok "誤り率 $err%"; else ng "誤り率 $err%"; fi

step "OSC 受信側を起動 (udp/$OSC_PORT)"
python3 "$HERE/osc_listen.py" --port "$OSC_PORT" --count 1 --timeout 30 --out "$OSC_OUT" >/dev/null &
LPID=$!
sleep 0.3

step "ホストとキューを登録"
hid=$("$CTL" cmd host.add "{\"host\":{\"name\":\"smoke\",\"ipAddress\":\"127.0.0.1\",\"port\":$OSC_PORT}}" | jq -r '.data.id // empty')
if [ -n "$hid" ]; then ok "host $hid"; else ng "host.add 失敗"; fi
now=$("$CTL" clock | jq -r .raw)
trig=$(add_seconds "$now" 4)
cid=$("$CTL" cmd cue.add "{\"cue\":{\"name\":\"smoke\",\"triggerTime\":\"$trig\",\"oscAddress\":\"/smoke/fire\",\"targetHostIds\":[\"$hid\"],\"arguments\":[{\"type\":\"int32\",\"value\":1}]}}" | jq -r '.data.id // empty')
if [ -n "$cid" ]; then ok "cue $cid @ $trig (now $now)"; else ng "cue.add 失敗"; fi

step "発火を待つ(最大 30 秒)"
if wait "$LPID"; then ok "OSC 受信: $(cat "$OSC_OUT")"; else ng "OSC が届かない"; fi
if "$CTL" wait ".state.cues[]|select(.id==\"$cid\")|.runtime.lastTriggeredAtUtc!=null" 5; then ok "cue.runtime.lastTriggeredAtUtc あり"; else ng "lastTriggeredAtUtc が無い"; fi
if [ "$("$CTL" state '[.state.logs[]|select(.success and (.message|test("/smoke/fire")))]|length>0')" = true ]; then ok "送信ログに成功"; else ng "送信ログに /smoke/fire の成功が無い"; fi

step "撮影"
shot=$("$CTL" shot) && ok "$shot" || ng "撮影失敗"

if [ $KEEP = 0 ]; then "$CTL" stop; fi
if [ $FAIL = 0 ]; then echo "SMOKE PASS"; else echo "SMOKE FAIL"; exit 1; fi
