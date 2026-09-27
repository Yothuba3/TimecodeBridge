---
name: run-v3
description: TimecodeBridge v3(Host + WebView)をこの Mac で起動し、人手なしで操作・撮影・通し確認する。「アプリを起動して」「実機で確認して」「スクショを撮って」のとき使う。
---

# TimecodeBridge v3 を起動して動かす

Host(`src/TimecodeBridge.Host`, 実行ファイル名 TimecodeBridge3)は環境変数 `TIMECODEBRIDGE_AUTOMATION_PORT` があると 127.0.0.1 の HTTP で自動操作を受け付ける。`tools/tcb3/tcb3ctl` がそのラッパー。詳細は `docs/v3/README.md` の「人手なしの実機確認」。

## 起動と停止

```sh
tools/tcb3/tcb3ctl start --build                                  # Web UI は dist/ を同梱済み。Web を変えたら先に src/TimecodeBridge.Web で npm run build
tools/tcb3/tcb3ctl start --build --selftest "Pro Tools Audio Bridge 2-A"   # 同一プロセスで LTC を生成し、同じデバイスから受信も始める
tools/tcb3/tcb3ctl start --dev-web --selftest "Pro Tools Audio Bridge 2-A" # リポジトリの dist を直接読む。Web 変更は npm run build → tcb3ctl reload で反映(Host 再ビルド不要)
tools/tcb3/tcb3ctl stop
```

- 自動操作口の無い TimecodeBridge3 が動いていると start は拒否する。`pkill -x TimecodeBridge3` してから。
- ログは `$TCB3_RUN_DIR/host.log`(既定 `/tmp/tcb3ctl`)。セッションの scratchpad を `TCB3_RUN_DIR` に指定すると散らからない。
- Web が ready になるまで start が待つ(最大 30 秒)。その後ウィンドウを前面に出す。**隠れたウィンドウは WebKit が描画も requestAnimationFrame も止める**ので、DOM 読みや撮影が古いままになる。怪しければ `tcb3ctl activate`。ユーザーがこの Mac で他のウィンドウを使っていると、その都度隠れる(shot が警告を出す)。描画に依らない値が要るときは `window.tcb.receive` をフックして取る(scratchpad の load10.py 参照)。
- command 直後に DOM を読むと、patch 到達(約 35ms)と再描画の前で古い値を返す。DOM を読む前に 0.3〜0.5 秒待つ。

## 見る・動かす

```sh
tools/tcb3/tcb3ctl state '.state.transport'          # 状態(jq フィルタ)
tools/tcb3/tcb3ctl clock
tools/tcb3/tcb3ctl cmd receive.setTriggerWindow '{"frames":2}'   # command は docs/v3/bridge-protocol.md の表
tools/tcb3/tcb3ctl click-text '内部生成'             # button の文字で click(第2引数で CSS を変えられる)
tools/tcb3/tcb3ctl eval 'document.querySelector(".cue-list").textContent'
tools/tcb3/tcb3ctl wait '.state.transport.status=="receiving"' 20
tools/tcb3/tcb3ctl shot out.png                      # 撮ったら必ず Read で画像を見る。真っ黒なら失敗
```

Web の DOM に data-testid は無い。要素は文字(`click-text`)か CSS class で探す。WebKit だけの見え方(number input のスピナー等)は headless Chrome では出ないので、見た目の確認は shot で行う。

## 通し確認

```sh
TCB3_RUN_DIR=<scratchpad>/tcb3 tools/tcb3/smoke.sh          # 起動→LTC受信→キュー発火→OSC受信→撮影。SMOKE PASS/FAIL
tools/tcb3/smoke.sh --keep                                  # 終了後も Host を残す
python3 tools/tcb3/osc_listen.py --port 9100 --count 1 --timeout 20   # OSC 受信だけ単体で
```

## Codex に Web を直させるとき

Codex は herdr の隣ペインで起動する(`herdr pane split --current --direction right --cwd "$PWD" --no-focus` → `herdr agent start codex-web --kind codex --pane <id> -- -s workspace-write -a never -c sandbox_workspace_write.network_access=true`)。起動直後の「Hooks … needs review」ダイアログは `herdr agent send-keys codex-web esc` で閉じる(信頼はユーザーの判断)。依頼は `herdr agent prompt codex-web "<文>" --wait --timeout 420000`。Codex は代替画面で動くので、結果は「/tmp/tcb3ctl/<name>.md に書いて、最後にそのパスだけ返答」させて読む。Host を `--dev-web` で起動しておけば、Codex は `npm run build → tcb3ctl reload → tcb3ctl shot` で自分の変更を自分で見られる。Codex のサンドボックスでは headless Chrome は起動しない。

前提: Pro Tools Audio Bridge のような同名の入出力デバイス(既定 "Pro Tools Audio Bridge 2-A"、`TCB3_SELFTEST_DEVICE` で変更)。実ハードウェアの LTC を使うときは `--selftest` を付けず、`cmd ltc.reconnect '{"deviceId":"..."}'`(deviceId は `state '.state.receive.devices'`)。
