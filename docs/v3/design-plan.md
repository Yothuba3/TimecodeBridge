# TimecodeBridge v3（派生版）設計案 v0.3

作成: Claude / レビュー: Codex(v0.1 レビュー済み、所見を反映) / 決定: yothuba
ユーザー決定(2026-09-11): 名前は TimecodeBridge のまま、バージョン系列を 3 にする(v2 の慣例に合わせ Product/バンドル名は TimecodeBridge3、バンドルID com.yothuba.timecodebridge3、設定は ~/Library/Application Support/TimecodeBridge3、v2 設定は初回に取り込み)。mac は Apple Silicon(arm64)専用。Web のフレームワーク採否は Codex 判断(素の JS が辛ければ使ってよい)。
関連文書: tcb3-protocol.md(Host⇄Web プロトコル草案, Codex) / tcb3-web-structure.md(Web 構成案, Codex)

## 目的
既存 TimecodeBridge2(Avalonia UI) とは**完全に別アプリ**として派生版を作る。
- LTC復号を **libltc 1.3.2**(LGPL-3.0, 枯れた実装)に置き換え、「TCが止まる」不安を断つ。
- UIを **HTML/CSS/TypeScript** で実装し、Codex設計の全画面モック(codex-redesign2.html)をそのまま表示面に使う。
- Avaloniaは **NativeWebView を載せる薄いホスト**(ウィンドウ・メニュー・ファイルダイアログ)に縮小する。
- Core(OSC/Cue/Models/Project) と 音声層(WASAPI, CoreAudio P/Invoke) は再利用する。

## 事実確認済み(M0 スパイク, 2026-09-11, このMac)
- libltc: ソース(autotools)からビルド。Apple Silicon 専用に決まったので arm64 のみ(ユニバーサルも可能だったが不要)。build script は scratchpad/native-libltc/build-macos.sh(取得・sha256 検証・@rpath 化・ad-hoc 署名・検証まで一括、実行済み)。`install_name_tool -id @rpath/libltc.dylib` + ad-hoc codesign 後、実行ファイル隣のパスから NativeLibrary で読込・復号OK。Windows は vcpkg ポートなし、ソースは src/{ltc,decoder,encoder,timecode}.c の4ファイル → CI で MSVC/clang 直コンパイル可(未検証)。
- libltc P/Invoke: LTCFrameExt=368B, SMPTETimecode=13B で C と一致。既存 LtcEncoder 出力 12f のうち 11f 復号(末尾 1f は次同期語待ち=仕様)。
- Avalonia: 現行 11.3.0。WebView は Avalonia.Controls.WebView 12.x のため Avalonia 12 系へ。`Avalonia.Controls.NativeWebView`: Navigate(Uri)/NavigateToString, JS→C# `invokeCSharpAction(body)`→`WebMessageReceived`, C#→JS `InvokeScript(js)`。Win=WebView2, mac=WKWebView。
- ブリッジ負荷: InvokeScript 30Hz×90回=2990ms、縮約波形240点×60Hz 300回=4922ms(理想4800ms)。いずれも遅延蓄積なし(mac のみ。Windows/WebView2 は未計測)。
- 既存コードの Avalonia 依存は AXAML 約1500行 + ViewModel の Dispatcher 5箇所のみ。Core 3263行 / Windows音声 674行 / CoreAudio 等 2673行 は無依存。

## 構成（新規ブランチ。名前は仮、後で変更可）
```
src/TimecodeBridge.Host/         Avalonia 12 シェル(AssemblyName TimecodeBridge3)。MainWindow に NativeWebView 1枚。DI composition root(OS 別音声実装の差し替え)、メニュー、ファイルダイアログ、設定保存先
src/TimecodeBridge.Web/          HTML/CSS/TS(esbuild)。フレームワーク採否は Codex 判断。構成は tcb3-web-structure.md。dist は git 管理し CI で再ビルド差分ゼロを検査
src/TimecodeBridge.Ltc/          libltc P/Invoke ラッパ(ILtcDecoder 実装)。decoder の所有権と破棄、audio thread 境界を明示。callback から UI/CueManager を直接呼ばない
src/TimecodeBridge.Mac/          既存 App/Services/CoreAudio を **抽出**した共通プロジェクト(TimecodeBridge.Windows と対称)。既存 App も参照を切り替える(移設して重複させない)
src/TimecodeBridge.Windows/      既存(WASAPI)。そのまま参照
src/TimecodeBridge.Core/         そのまま参照(LtcDecoder は使わない。ILtcDecoder の実装を libltc 版に差し替え)
native/libltc/                   取得元 version・sha256・build script(mac universal / win dll)・LGPL 全文・ソース再取得手順。バイナリは CI 生成
```

## Host ⇄ Web ブリッジ
詳細は tcb3-protocol.md。要点:
- Host を唯一の正とする。C#→JS は `window.tcb.receive(json)` の一入口、4系統: `snapshot`(初期・再同期の全量) / `patch`(revision 付き、状態変更時のみ、ドメイン単位置換) / `clock`(最大30Hz、最新1件のみ) / `wave`(縮約済み、初期30Hz、実測後 60Hz)。
- JS→C# は全て `requestId` 付き command、Host は必ず `result` を返す。編集系は `expectedRevision` 必須。`cue.fire`/`cueSync.send` 等の単発送信は requestId で重複排除。
- 編集 draft・選択・scroll・列幅・drawer は Web ローカル状態。snapshot で潰さない。
- キュー発火→OSC は C# 内で完結(Audio → libltc → CueManager → UDP)。WebView はレイテンシ経路に入らない。
- キー操作: WebView 内は Web 側(IME 変換中・入力欄フォーカス中・key repeat は抑止)、Cmd/Ctrl+O/S/Z 等のメニューアクセラレータは Host 側。
- 契約テスト: C# 出力 fixture を TS で parse、TS fixture を C# で deserialize(共同成果物)。

## libltc 同梱の課題（mac）と対処
1. **アーキテクチャ**: Apple Silicon 専用(ユーザー決定)。CI(macos-latest, arm64)でソースから arm64 ビルド(検証済み)。osx-x64 の publish は v3 では行わない。
2. **install_name**: 既定は絶対パス → `install_name_tool -id @rpath/libltc.dylib`、Host 側は `NativeLibrary.SetDllImportResolver` で `Contents/MacOS/` から解決(検証済み)。
3. **署名/公証**: 同梱 dylib も codesign 対象(現行 .app は未署名運用。当面 ad-hoc、将来 Developer ID で署名する際に dylib も含める)。
4. **LGPL-3.0**: 動的リンク(dylib/dll 同梱)。アプリ内に libltc の著作権表示・ライセンス全文・ソース入手先を掲載。libltc 自体を改変しない。CI で静的リンクへ偶発的に変わっていないことを検査(otool -L / dumpbin)。
5. **Windows**: CI(windows-latest)で `cl /LD` or clang で libltc.dll を作り exe 隣に配置(未検証)。

## 分担
- **Claude**: libltc P/Invoke ラッパ + 復号E2E(既存 MacLoopbackE2E を流用)、native/libltc(CI 同梱)、TimecodeBridge.Mac 抽出と Host への配線、Host シェル(Avalonia 12 + WebView)と Protocol.cs、ビルド/テスト運用。
- **Codex**: Web UI(モックの TS 化・実データバインド・キーボード/IME・列幅と drawer のドラッグ)、protocol.ts と UI 状態モデル。デザインは Codex 主導を継続。
- 共同: 契約テスト(fixture 相互検証)。相互レビュー: 私は Web、Codex は Host/Ltc。

## マイルストーン
- **M0 スパイク**: 完了(上記)。Windows/WebView2 側の負荷計測と publish 成果物からの起動確認は M1 の完了条件に繰り越す。
- **M1**: LTC受信 → TC表示 → 状態表示(Host+Ltc+Audio 配線、Web は表示のみ)。**Host/Web とも実装済み(2026-09-11)。同一プロセス生成の実機確認で受信安定・逆行0・誤り0%。**完了条件に 10分連続負荷・1000 cues の snapshot/描画時間・IME 中の Space/L/P 抑止・publish 成果物起動を含める。
- **M2a**: キュー一覧・手動▶・キュー発火→OSC・CUE SYNC・LIVE/MUTE。**Host 側 command 実装済み、Web 側実装済み(Codex)。実機での目視確認待ち。**
- **M2b**: キュー追加/編集/一括編集/複製/削除、プロジェクト保存/読込、未保存確認、Undo/Redo。**Host 側実装済み(2026-09-11)。Web 側は Codex 作業中。**
- **M3**: 内部生成(再生/停止トグル・リセット・出力・音量)、ホスト管理、ログ、OSCポン出しドロワー。
- **M4**: パッケージング(mac .app(arm64) + libltc.dylib / win zip + libltc.dll)、LGPL 表記、CI。**release-v3.yml と THIRD_PARTY_NOTICES.md を作成済み(未実行)。Windows の WASAPI 実装は実機未確認。インストーラー(Inno Setup, WebView2 ランタイム同梱)は未着手。**

## 未決事項
- なし(名前・対象アーキ・フレームワーク方針は 2026-09-11 に決定)。
