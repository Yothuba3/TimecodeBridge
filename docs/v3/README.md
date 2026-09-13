# TimecodeBridge v3 開発メモ

v3 は Avalonia 12 の薄いホスト(`src/TimecodeBridge.Host`)が全面 WebView に Web UI(`src/TimecodeBridge.Web`, Preact + TypeScript)を表示し、LTC の復号に libltc(`src/TimecodeBridge.Ltc`)を使う構成です。設計は `design-plan.md`、ホストと Web の通信は `bridge-protocol.md`、Web の構成は `web-structure.md` を参照してください。

## 初回セットアップ(macOS)

```sh
./native/libltc/build-macos.sh native/libltc/out   # libltc.dylib(arm64)をビルド
cd src/TimecodeBridge.Web && npm ci && npm run build && cd ../..
dotnet build src/TimecodeBridge.Host/TimecodeBridge.Host.csproj
```

`dotnet build` は `native/libltc/out/libltc.dylib` と `src/TimecodeBridge.Web/dist` を実行ファイルの隣(`libltc.dylib`, `web/`)へコピーします。どちらかが無いとビルド時に警告が出て、起動時に見つからない旨のエラーになります。

## 起動

```sh
./src/TimecodeBridge.Host/bin/Debug/net8.0/TimecodeBridge3
```

確認用の環境変数(いずれも省略可):

| 変数 | 内容 |
|---|---|
| `TIMECODEBRIDGE_SELFTEST_OUTPUT=<出力デバイス名の一部>` | 同一プロセス内で LTC(30fps, 01:00:00:00 から)を生成してその出力へ流す |
| `TIMECODEBRIDGE_AUTOSTART_INPUT=<入力デバイス名の一部>` | 接続直後にそのデバイスで LTC 受信を始める |
| `TIMECODEBRIDGE_BRIDGE_TRACE=1` / `2` | Web との送受信・受信状態を標準出力へ(2 は毎フレームの時計も) |
| `TIMECODEBRIDGE_WEB_DIST=<dist ディレクトリ>` | 同梱の web/ ではなくそのディレクトリの index.html を開く(Web 開発中に便利) |
| `TIMECODEBRIDGE_LIBLTC=<dylib のフルパス>` | 同梱の libltc ではなくそのファイルを読み込む |
| `TIMECODEBRIDGE_AUTOMATION_PORT=<port>` | 127.0.0.1:<port> で自動操作用の HTTP を待ち受ける(下記「人手なしの実機確認」) |

Pro Tools Audio Bridge のような仮想ループバックは、再生と取り込みが別プロセスだとタイムコードが乱れます。ハードウェアなしで確かめるときは `TIMECODEBRIDGE_SELFTEST_OUTPUT` を使ってください。

## テスト

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/TimecodeBridge.Ltc.Tests    # libltc ラッパー
DOTNET_ROLL_FORWARD=Major dotnet test tests/TimecodeBridge.Host.Tests   # ブリッジ・command・プロジェクト
cd src/TimecodeBridge.Web && npm run typecheck && npm test               # Web
```

実機での通し確認は `tools/MacLoopbackE2E`(`run "2-A" 15`、`TCB_DECODER=libltc` で v3 のデコーダに切替)を使います。

## 人手なしの実機確認(macOS)

Host は環境変数 `TIMECODEBRIDGE_AUTOMATION_PORT` があるときだけ、127.0.0.1 の HTTP で状態の取得・command の実行・WebView 内の JS 実行・自ウィンドウの撮影を受け付けます(`src/TimecodeBridge.Host/Services/DevAutomation.cs`)。撮影は自プロセスのウィンドウを `CGWindowListCreateImage` で取るので、画面収録の許可は要りません。`tools/tcb3/tcb3ctl` がその薄いラッパーです。

```sh
tools/tcb3/tcb3ctl start --build --selftest "Pro Tools Audio Bridge 2-A"   # 起動し、同一プロセスで LTC を生成して受信も始める
tools/tcb3/tcb3ctl state '.state.transport'                                # 状態(jq フィルタ可)
tools/tcb3/tcb3ctl cmd cue.add '{"cue":{"name":"x","triggerTime":"01:00:10:00","oscAddress":"/x"}}'
tools/tcb3/tcb3ctl click-text 'ホスト管理'                                  # WebView 内の要素を文字で探して click
tools/tcb3/tcb3ctl eval 'document.title'                                    # 任意の JS
tools/tcb3/tcb3ctl shot /tmp/tcb3.png                                       # ウィンドウを PNG に
tools/tcb3/tcb3ctl wait '.state.transport.status=="receiving"' 20           # 条件を満たすまで待つ
tools/tcb3/tcb3ctl stop
```

| エンドポイント | 内容 |
|---|---|
| `GET /health` | `{ok, pid, webReady, uptimeSeconds}` |
| `GET /state` / `GET /clock` | Web へ送るものと同じ snapshot / clock |
| `POST /command` | `{command, args?, expectedRevision?}` を Web からの command と同じ経路で実行し、result を返す |
| `POST /eval` | 本文の JS を WebView で評価し `{result}` を返す |
| `POST /activate` | ウィンドウを前面に出す。他のウィンドウに隠れていると WebKit が描画と requestAnimationFrame を止め、DOM も撮影も古いままになる(`tcb3ctl start` が自動で呼ぶ) |
| `POST /reload` | Web UI を読み込み直す(Host の状態は保つ)。`TIMECODEBRIDGE_WEB_DIST` を指していれば dist の変更がそのまま反映される |
| `GET /screenshot` | ウィンドウの PNG(撮れない環境は 501) |

`tools/tcb3/smoke.sh` は「起動 → LTC 受信(誤り 0%・時計が進む)→ ホストとキューを登録 → 発火した OSC を `tools/tcb3/osc_listen.py` で受ける → 送信ログとキューの発火記録を確認 → 撮影」を一続きで行い、`SMOKE PASS` / `SMOKE FAIL` で終わります。

### Web を直しながら実機で見る(Codex でも同じ)

```sh
tools/tcb3/tcb3ctl start --dev-web --selftest "Pro Tools Audio Bridge 2-A"   # 同梱の web/ でなくリポジトリの dist を直接読む
cd src/TimecodeBridge.Web && npm run build && cd ../..                        # Web を変更したら
tools/tcb3/tcb3ctl reload && tools/tcb3/tcb3ctl shot /tmp/tcb3ctl/after.png  # Host を再ビルドせずに反映して撮る
```

Codex を herdr のペインで動かす場合(`herdr agent start <name> --kind codex --pane <id> -- -s workspace-write -a never -c sandbox_workspace_write.network_access=true`)も、この `tcb3ctl reload` / `shot` をそのまま使えます。`network_access=true` が無いとサンドボックスから 127.0.0.1 の自動操作口に届きません。Codex は撮った PNG を自分の画像表示ツールで開いて確認できます(実機で確認済み)。Codex のサンドボックスでは headless Chrome が起動できない(プロファイル先やヘルパープロセスの制約で SIGABRT)ので、見た目の確認は実機一本にしてください。ループバックのデバイス名は `TCB3_SELFTEST_DEVICE`、作業ファイルの置き場は `TCB3_RUN_DIR`(既定 `/tmp/tcb3ctl`)で変えられます。`osc_listen.py` は単体でも使え、届いた OSC を 1 行 1 メッセージの JSON で出します。

## Web UI の変更

Web は Codex が担当しています。`dist/` は git 管理で、CI が再ビルドして差分が無いことを検査するため、変更後は必ず `npm run build` の結果をコミットしてください。見た目の確認は headless Chrome で描画できます(WebKit だけの見え方、たとえば number input のスピナーは Chrome では出ません)。

```sh
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --disable-gpu --hide-scrollbars \
  --window-size=1600,900 --force-device-scale-factor=2 --virtual-time-budget=4000 \
  --screenshot=/tmp/tcb3.png "file://$PWD/src/TimecodeBridge.Web/dist/index.html"
```

## リリース

`v3.*` のタグで `.github/workflows/release-v3.yml` が動き、macOS(Apple Silicon)の `TimecodeBridge3.app`(zip)と Windows の zip・インストーラー(WebView2 ランタイムを未導入なら導入)を作ります。libltc は LGPL-3.0 のため動的リンクで同梱し、`THIRD_PARTY_NOTICES.md` と `libltc-COPYING.txt` を含めます。
