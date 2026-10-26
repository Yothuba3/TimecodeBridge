# v3 の Windows 実機検証と修正の記録(2026-09-27)

`verification-2026-09-14.md` の「次にやるべきこと」2 番(Windows の実機・CI)。macOS で作った v3 を Windows で初めてビルド・起動・通し確認し、見つけたものを直した。

## 環境

- Windows 11 Pro 10.0.26200(x64、日本語)、WebView2 ランタイム 153.0.4234.48
- .NET SDK 9 / 10(net8.0 ランタイム 8.0.25 で起動)、Visual Studio 2022(MSVC 14.44)、Node.js 24、Inno Setup 6
- 音声: VB-Audio Virtual Cable(CABLE In 16ch、16 チャンネルの出力)、Steinberg UR22C。LTC は CABLE In 16ch へ生成し、そのループバック取り込みで受信した

## 見つかった問題と修正

| # | 症状 | 原因 | 修正 |
|---|---|---|---|
| 1 | **v3.0.0 の Windows 版が起動直後に落ちる**(リリースの zip で再現。GUI アプリなので利用者には何も出ずに終わる) | Host に対応 OS を宣言した `app.manifest` が無く、Avalonia が WebView(NativeControlHost)用の子ウィンドウを作れない(`Unable to create child window for native control host`) | `src/TimecodeBridge.Host/app.manifest`(Windows 10/11 の supportedOS)を追加 |
| 2 | Program Files のような書けない場所に入れると起動直後に落ちる(インストーラーで「すべてのユーザー」を選んだ場合) | WebView2 が既定で実行ファイルの隣(`TimecodeBridge3.exe.WebView2`)にユーザーデータを作ろうとして `E_ACCESSDENIED`。書き込みを禁止したフォルダで修正前の版が落ちることを再現した | `NativeWebView.EnvironmentRequested` で `UserDataFolder` を `%LOCALAPPDATA%\TimecodeBridge3\WebView2` に。アンインストール後にフォルダが残る問題も消えた |
| 3 | 入力デバイス一覧に出力デバイスが「(Loopback)」付きと無しの 2 回、同じ id で並ぶ | `HostState.BuildReceive` がキャプチャとレンダーの一覧をつないでいた。Windows ではループバック取り込みがキャプチャ側に入る(IAudioDeviceService の契約) | 入力の候補はキャプチャ側だけにした。**macOS でも出力専用デバイスが入力一覧に出なくなる**(macOS 実機では未確認) |
| 4 | 生成中にフレームレートや開始 TC を変えて「停止→再生」すると、画面は新しい設定なのに古いレートと位置のまま LTC を出す。「リセット」では新しい開始 TC を古いレートで数える(24fps の 10:59:52:00 が 30fps の 08:47:55 になる) | `generator.start` が保留中の設定を捨てて一時停止から再開し、`generator.reset` は開始 TC だけを差し替えていた(OS 共通) | 保留中の変更があれば最初から生成し直し、リセットはレートが変わっていれば生成器を作り直す。テスト 2 件を追加 |
| 5 | Host のテストが Windows で終わらない(`HostPingReportsReachabilityAndLatency`) | テストの `HostHarness.Run` が UI スレッドで非同期 command を同期待ちしており、Windows の `Ping.SendPingAsync` は必ず非同期で完了するためデッドロック。実機の Host は await しているので起きない(実機の疎通確認は成功) | `HostHarness.RunAsync` を足してテストを async に |
| 6 | Host のテスト 4 件が Windows で落ちる | パス区切り(`/` 固定)と `platform=="macos"` 固定の期待値 | パスは JSON にエスケープして渡し、期待値を OS から求める |
| 7 | ルート README の libltc が「LGPL-2.1」「復号・生成に使う」 | 同梱の COPYING は LGPL v3。生成は Core の自前の LtcEncoder | 「復号に使う libltc は LGPL-3.0-or-later」に |
| 8 | LTC受信 ⇄ 内部生成の切り替えに時間がかかる(画面の切り替えまで 内部生成へ約 0.66 秒・LTC受信へ約 2.6 秒、受信開始まで約 3.9 秒) | Windows のデバイス列挙はエンドポイントのプロパティ読みで 1 回 0.35〜0.5 秒かかる(この PC は 16 エンドポイント)。Host は状態を組み立てるたび(transport の入力デバイス名、receive / generator の一覧)とデバイスを探すたびに列挙しており、切り替え 1 回で何度も列挙していた。transport はオートミュートの切り替わりなどでも組み直す | `CachedAudioDeviceService` で一覧を「再スキャン」(audio.refreshDevices)まで使い回す。ID で見つからないときだけ一度列挙し直す(起動後に挿したデバイス)。画面の切り替えは 17〜65ms、受信開始まで約 0.2 秒に。画面の一覧が更新される契機(再スキャン)は従来どおり。再スキャンは裏のスレッドで列挙して終わってから一覧を差し替え(その間も UI スレッドは止まらない。スキャン中の状態取得 2〜6ms)、Web は result が返るまでボタンを「スキャン中…」にして帯を流す(この PC で約 0.65 秒)。最初の列挙は起動時に裏で先に始める(起動直後の状態取得 約 620ms → 59ms) |

## 確認した範囲(修正後、すべて通過)

- ビルド: `build-windows.cmd` で libltc.dll(日本語 Windows では `ltc.h` のコメント内の文字で C4819 が出るが無害)、Web の typecheck / test 51 件 / build(dist の差分は改行コードだけ)、Host のビルド、`dotnet publish -r win-x64 --self-contained`、`iscc` でインストーラー
- テスト: Ltc 13 件、Host 60 件
- 起動: WebView2 で Web UI が表示される(日本語フォント・ダークテーマ・波形)。自動操作口(`/health` `/state` `/clock` `/command` `/eval`)
- LTC: 2 つの Host で 生成 → CABLE In 16ch → ループバック受信。24 / 25 / 29.97df / 30fps のいずれも同じレート(29.97 はドロップ判定)で受信、誤り率は 5 秒受けた時点で 0〜0.4%(受信開始からの累計なので、開始直後に弾かれる数フレームが効く。2.5 秒時点では 1.2% のこともあった)。モノラル 16bit の出力を 16ch デバイスへ、16ch のループバックから先頭チャンネルを取り込む経路も動く
- 生成: フレームレート・開始 TC の変更がリセット、停止→再生で反映される(修正 4 の後)
- キュー: LTC の時刻で自動発火した OSC を受信(引数 int32 と日本語の string)、手動発火、CUE SYNC、送信ログと発火記録、疎通確認(ICMP 0ms)
- 配布: 発行物を起動、インストーラーでユーザー単位にサイレントインストール → 起動して LTC 受信とキュー発火まで確認 → サイレントアンインストール(登録とフォルダが消える)、書き込みを禁止したフォルダに置いた発行物でも起動する(修正 2 の後)
- 通し確認: `tools/tcb3/smoke-windows.ps1 -LoopbackDevice "CABLE In 16ch"` を 3 回続けて SMOKE PASS

## 追加したもの

- `tools/tcb3/smoke-windows.ps1`: smoke.sh の Windows 版(pwsh 7、jq・python 不要)。起動 → Web ready → OSC 発火と受信 → 送信ログと画面 → WebView2 のデータが実行ファイルの隣にできていないこと → 撮影(PrintWindow)。`-LoopbackDevice` で 2 つ目の Host に LTC を生成させ、ループバック受信とキュー発火まで確かめる
- `.github/workflows/ci-v3.yml`: PR と feature/v3 への push で、Windows のビルド・テスト・発行物の smoke・インストーラーでサイレントインストールしたものの smoke(ランナーに音声デバイスが無いので LTC は確かめない)
- `release-v3.yml`: zip にする前に発行物を smoke し、起動しないビルドを出さない

## 未対応・気づいたこと

- **v3.0.0 の Windows 版(zip・インストーラー)は使えない状態のまま公開されている。** 修正を含めた v3.0.1 を出す必要がある
- ci-v3.yml はまだ一度も GitHub で動かしていない。ランナーで WebView2 の画面が作れるか(GUI セッション)、自動操作口の HttpListener が使えるかは初回の実行で確かめる。release-v3.yml の smoke も同じ前提なので、先に ci-v3 を通してからタグを打つ
- 修正 3・4 は macOS にも効く(入力一覧から出力専用デバイスが消える、生成の設定変更の反映)。macOS 実機での確認が要る
- 起動後に出た未処理例外はどこにも記録されない(起動エラー画面と startup-error.log は DI の組み立てでの失敗だけ)。修正 1・2 のような落ち方をしたとき利用者の手元に手がかりが残らないので、未処理例外を DataDirectory に記録する仕組みがあると良い
- `TIMECODEBRIDGE_SELFTEST_OUTPUT`(同一プロセスで生成と受信)は macOS のみ。Windows はループバック取り込みが別プロセスでも乱れないので、2 つの Host で代替した
- 自動操作口の `/screenshot` は Windows では 501 のまま(smoke-windows.ps1 は外から PrintWindow で撮る)。tcb3ctl / smoke.sh も macOS 専用のまま
- `TIMECODEBRIDGE_BRIDGE_TRACE` の標準出力は Windows では日本語が文字化けする(コンソールのコードページ)。開発用のログだけ
- ウィンドウのタイトルが「TimecodeBridge」(3 が付かない)。OS 共通
- 実際のオーディオインターフェース(UR22C)への物理的な LTC 入出力、管理者権限での「すべてのユーザー」インストールは行っていない(後者は書き込み禁止のフォルダで代替)
- v1/v2 のテストが Windows で落ちる(v3 の対象外なので直していない): `TimecodeBridge.Tests` の CoreProjectStructureTests 12 件(実行ディレクトリからの相対パス)、OscTriggerPanelManagerTests 1 件(v3 で `Trigger` が `IOscSender.SendWithResult` に判定を任せたため、既定実装のままのテスト用スパイでは「送信先なし」にならない。OS 共通)、`TimecodeBridge.App.Tests` 2 件(macOS 前提の DI テスト、実際の利用者の設定を読む最近使ったプロジェクトのテスト)
