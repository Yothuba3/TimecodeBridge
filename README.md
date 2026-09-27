# TimecodeBridge 3

LTC(リニアタイムコード)を受けてキューの時刻に OSC を送る、舞台・配信向けのタイムコードブリッジです。macOS(Apple Silicon)と Windows(x64)で同じ画面が動きます。

- 音声入力の LTC を libltc で復号し、キューリストの時刻に到達した OSC メッセージを登録したホスト(QLab、Resolume、照明卓など)へ UDP で送ります
- 内部生成モードでは LTC を自分で作って出力し、同じキューを走らせられます(24 / 25 / 29.97df / 30 fps)
- CUE SYNC(現在位置の一斉送信)、LIVE / MUTE(キュー送出の一時停止)、発火時オートミュート、OSC ポン出しボタン、送信ログ、キーボード操作(Space / L / P / 矢印 / Enter / Delete)

v1(Windows / WPF)と v2(macOS / Avalonia)の後継です。プロジェクトファイルとホスト設定の形式は引き継いでいます。

## ダウンロードとインストール

[Releases](https://github.com/Yothuba3/TimecodeBridge/releases) から OS に合うものを取ります。

| OS | ファイル | 手順 |
|---|---|---|
| macOS 13 以降(Apple Silicon) | `TimecodeBridge3-v3.x.y-osx-arm64.zip` | 展開して `TimecodeBridge3.app` をアプリケーションフォルダへ。署名は ad-hoc なので初回は右クリック →「開く」で起動する(ターミナルなら `xattr -d com.apple.quarantine TimecodeBridge3.app`)。マイク(音声入力)の許可を求められたら許可する |
| Windows 10 / 11(x64) | `TimecodeBridge3-Setup-3.x.y.exe` | インストーラーを実行する。Microsoft Edge WebView2 ランタイムが無い PC には自動で入れる |
| Windows(zip 版) | `TimecodeBridge3-v3.x.y-win-x64.zip` | 展開して `TimecodeBridge3.exe` を実行する。WebView2 ランタイムは別途必要(Windows 11 と最近の Windows 10 には入っている) |

## 使い方の要点

1. **動作モード**を選ぶ。「LTC受信」は入力デバイスを選ぶと受信が始まり、「内部生成」は開始 TC・フレームレート・出力デバイスを決めて ▶ で走らせる
2. **ホスト管理**タブで送信先(名前・IP・ポート)を登録し、「疎通確認」で届くか見る
3. **キューリスト**の「＋ 追加」で時刻・OSC アドレス・引数・送信先を入れる。行の ▶ で手動発火、Shift / Cmd クリックで複数選択して一括編集・連続複製
4. 本番中は **LIVE / MUTE**(L キー)で送出を止め、**CUE SYNC**(Space)で現在位置を全ホストへ送る
5. **OSC ポン出し**(P キー)はタイムコードと無関係に押して送るボタン盤

キューの時刻には基準フレームレートがあり、FF の上限はその値に従います。モニターの OFFSET は入力後に「確定」で反映されます。「ジャンプ判定 ±n f」は n フレーム以内の前進・後退を通常再生や揺れとして扱い、それを超えて飛んだときは頭出しとして途中のキューを発火させません(既定 3)。

プロジェクト(キュー・ホスト・入出力設定・オフセット)は JSON ファイルに保存します。未保存のまま閉じようとすると確認が出ます。

## 開発者向け

ビルド、自動操作口(`TIMECODEBRIDGE_AUTOMATION_PORT`)、実機での無人確認(`tools/tcb3/tcb3ctl`)、Host ⇄ Web のプロトコルは [docs/v3/README.md](docs/v3/README.md) と [docs/v3/bridge-protocol.md](docs/v3/bridge-protocol.md) にあります。構成は Avalonia 12 の薄いホスト + WebView(Preact / TypeScript)+ libltc です。

リリースは `v3.x.y` のタグを push すると GitHub Actions が macOS と Windows の成果物を作ります。

## ライセンス

本体は [LICENSE](LICENSE) のとおり。LTC の復号に使う libltc は LGPL-3.0-or-later で、動的リンクのまま同梱しています。同梱物の一覧は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
