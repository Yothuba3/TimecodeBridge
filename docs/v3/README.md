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

Pro Tools Audio Bridge のような仮想ループバックは、再生と取り込みが別プロセスだとタイムコードが乱れます。ハードウェアなしで確かめるときは `TIMECODEBRIDGE_SELFTEST_OUTPUT` を使ってください。

## テスト

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/TimecodeBridge.Ltc.Tests    # libltc ラッパー
DOTNET_ROLL_FORWARD=Major dotnet test tests/TimecodeBridge.Host.Tests   # ブリッジ・command・プロジェクト
cd src/TimecodeBridge.Web && npm run typecheck && npm test               # Web
```

実機での通し確認は `tools/MacLoopbackE2E`(`run "2-A" 15`、`TCB_DECODER=libltc` で v3 のデコーダに切替)を使います。

## Web UI の変更

Web は Codex が担当しています。`dist/` は git 管理で、CI が再ビルドして差分が無いことを検査するため、変更後は必ず `npm run build` の結果をコミットしてください。見た目の確認は headless Chrome で描画できます(WebKit だけの見え方、たとえば number input のスピナーは Chrome では出ません)。

```sh
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --disable-gpu --hide-scrollbars \
  --window-size=1600,900 --force-device-scale-factor=2 --virtual-time-budget=4000 \
  --screenshot=/tmp/tcb3.png "file://$PWD/src/TimecodeBridge.Web/dist/index.html"
```

## リリース

`v3.*` のタグで `.github/workflows/release-v3.yml` が動き、macOS(Apple Silicon)の `TimecodeBridge3.app`(zip)と Windows の zip・インストーラー(WebView2 ランタイムを未導入なら導入)を作ります。libltc は LGPL-3.0 のため動的リンクで同梱し、`THIRD_PARTY_NOTICES.md` と `libltc-COPYING.txt` を含めます。
