# v3 UI の網羅テストと修正の記録(2026-09-14, macOS)

Claude(ポート 47301)と Codex(ポート 47302、別ワークツリー)がそれぞれ自動操作口(`tools/tcb3/tcb3ctl`)から実機を操作・撮影して確かめ、見つけたものはその場で直してコミットした。ユーザーが作業中の Host(47300)には触っていない。

## 確認した範囲

- ヘッダー: プロジェクト名と保存状態、プロジェクトメニュー、Undo/Redo の有効・無効、FPS/DROP 表示
- モニター: 受信 TC の進行、RAW/OFFSET(符号・各桁)、LOCK、NEXT CUE と T− の減少、CUE SYNC(送信先 0 のトースト、送信ログ、OSC 実受信)、LIVE/MUTE(ボタン・L キー)
- キューリスト: 追加ダイアログの全項目と検証(必須、`/` 始まり、引数の形式、FF 上限、TC秒数送信→送信 TC、発火時ミュート→解除の依存)、編集、Shift/Cmd の複数選択、一括編集(各項目、範囲外オフセットのスキップ通知)、複製、連続複製(タイムコード間隔、24 時間超え)、削除→確認→Undo/Redo、時間順、有効チェック、▶ の手動発火(赤フラッシュ、ミュート中も発火)、列幅ドラッグ、判定幅、Enter/Delete/Space/L/P/Esc/Cmd+Z、Tab で行に到達
- 自動発火: 時刻到達で発火し OSC が届く、無効キューはスキップ、発火時ミュート→解除時間で自動解除(行に MUTE と残り時間)、ミュート中は自動発火しない
- 右ペイン: LTC受信/内部生成の切替(戻ると自動再接続)、入力デバイスの選択解除/再選択/再スキャン/再接続/フリーラン、LTC でない入力(信号なし)、内部生成(25fps・29.97df、実出力デバイスで LTC 出力しつつキュー発火→OSC 受信、リセット)、Cue-Sync 設定、送信ログの並びとクリア、ホスト管理(検証エラー、IPv6、疎通確認の成功/失敗、有効、編集、削除、到達不能・無効ホストへの発火)
- OSC ポン出し: P/Esc、グリッド変更、編集モードでの作成/編集/削除、実行モードでの発火→OSC 受信
- プロジェクト: `project.open {path}`(ヘッダー、内容、recentFiles)、`project.new`、存在しないパス、壊れた JSON
- 見た目: 120 文字級の名前・アドレス・ホスト名、1100px 幅相当(style 注入)での overflow、スクロール(300 件)
- 通し: `tools/tcb3/smoke.sh`(新ビルドで PASS)。release-v3.yml と同じ手順で `.app`(Release・self-contained・osx-arm64・ad-hoc 署名)を作り、その実行ファイルに対しても smoke PASS(同梱の web/ に当日の変更が入っていることを確認)
- Codex の第 8・9 ラウンド: 内部生成→LTC 復帰時のレート遷移の計測(100ms 刻み)と修正、不正・境界入力の直送(10,000 文字名、制御文字、型違い、null、count=500×3)、mode/デバイス/生成の 20 往復、1000 キュー・20 ホストの保存/新規/開く(95 / 87 / 159 ms)と 1000 行の描画、壊れた JSON・未知 type・古い revision の patch 注入(resync が動く)。Host は落ちず、expectedRevision の検査漏れ 1 件を修正
- Codex の第 6・7 ラウンド: Windows 経路の静的レビュー(WebView2 / WASAPI / パス / libltc / installer)、可視コントロールのアクセシブル名走査、Host のエラー(validation / notFound / ioError / conflict)の UI 表示、送信ログ 610 回、未使用コードの整理、仕上げの回帰(確認済み範囲と修正項目の全件、回帰なし)
- Codex の第 4・5 ラウンド: 29.97df で 20 キューの通し運用(自動ミュート・無効・オフセット・複数宛先・再生中の追加/編集/削除/OFFSET/L/CUE SYNC)、LTC 受信モードで 15 キュー、判定幅 0/3/10 の境界、疎通確認の連打と ping 中の削除、発火 610 回。いずれも OSC 実受信と Host ログが一致

## 見つけて直したもの(すべて feature/v3 にコミット済み)

| 場所 | 内容 | 対処 |
|---|---|---|
| モニター OFFSET | 符号だけ変えても Host に届かない(TimecodeInput の符号変更が onCommit を呼ばない) | 符号変更でも commit |
| タイムコード入力 | 2 桁目を打つと消える(focus 移動→同期 blur が古い値で上書き)、↑↓ も同様 | 直前に確定した値を ref で参照 |
| トースト | 消えずに残る | 4 秒で自動消去 |
| キュー編集 | OSC アドレスのエラーが追加アドレス欄の下に出る | 入力欄直下へ |
| キュー編集 | 基準フレームレートを変えても FF が丸まらず保存時に Host の形式エラー | 切替時に各 TC を丸める |
| 送信ログ | 空のときの文言が 70px 列で折り返す | 全幅 |
| キューリスト | 自動ミュート中の MUTE 表示と解除カウントダウンが無い(旧 UI にはあった) | 行に MUTE m:ss、MUTE ボタン下に「自動解除まで」 |
| キューリスト | ▶/チェックボックスのダブルクリックで編集が開く | ボタン・入力上では無視 |
| キューリスト | 0.7 秒以内の再発火で先のタイマーが後のフラッシュを消す | 発火ごとのトークンで判定 |
| フッター | detailText が空だと「·」だけ残る | 空なら区切りを出さない |
| 一括編集 | 同一フレーム内の複数更新で前の値が消える(setValues の非関数形)、チェックだけ入れた項目が JSON から落ちて無視される | 関数形に、空値(false / [] / "")を明示送信 |
| ポン出し | 続けて押したボタンの結果表示が消える(同上) | 関数形に |
| Host cue.duplicate | 連続複製が 23:59:59:FF を超えると不正な時刻を作る(Codex) | interval の validation |
| Core TryApplyTriggerOffset | 29.97df で一日を 24*3600*30 と近似し、23:59:59;29 + 1f を範囲内と誤判定(Codex) | そのレートの最終 TC から一日分を求める |
| Host MainWindow | `project.open` が開けているのに内部エラー(最近使ったファイル更新でメニュー全体を作り直し、Avalonia.Native が例外) | サブメニューの項目だけ差し替える |
| Web 全般 | 長文が 1100px 幅で NEXT/フッター/Cue-Sync/ログ/ポン出しボタンからはみ出す(Codex) | min-width:0 と省略記号 |
| キューリスト | Tab で行に到達できない(Codex) | tabIndex と aria-label。キーボードで来たときだけ選択を同期(マウスの Shift 範囲選択は維持) |
| docs | design-plan.md の古いファイル名、README の headless Chrome の案内の矛盾 | 修正 |
| Web patch | 最後のキューを過ぎても NEXT CUE が残る(patch が null を省くため消せない、Codex) | `nextCueCleared:true` を追加 |
| Web 送信ログ | logsAppend を無制限に連結し長時間運用で DOM が増え続ける(Codex) | 最新 500 件に |
| Host project.save | path 無しでは常にネイティブ保存パネル(無人保存不可、Codex 指摘) | `{path}` でダイアログなし保存 |
| キュー行 | onFocus のフラグが残り、同じ行を続けて押した後の Tab 到達で選択が同期されない | pointerdown からの経過時間で判定 |
| Host MainWindow | ネイティブメニューの修飾キーが全 OS で Meta 固定(Windows で Ctrl+N/O/S/Z にならない、Codex) | macOS は Cmd、それ以外は Ctrl |
| native/libltc/build-windows.cmd | 出力先の相対パスが work へ cd した後にずれる(Codex) | 先に絶対化 |
| Web a11y | キューの有効/▶、Undo/Redo、入力デバイス、フリーラン、Cue-Sync、ポン出しの空セルにアクセシブル名が無い(Codex) | aria-label を付与(可視コントロールで欠落 0) |
| Host OSC 引数 | 未知の type や型に合わない value を黙って落として ok を返す(Codex P0) | validation(cue/changes/button.arguments) |
| Host expectedRevision | 編集系 command の expectedRevision を比較せず、古い revision でも変更が通る(Codex) | 食い違えば変更前に conflict(retryable) |
| Host 発火結果 | 無効・存在しないホストだけが送信先でも黙って何も送らず {sent:true}(Codex) | sentCount / skippedHostIds を返し、0 件なら失敗ログとトースト |
| Host 入力検証 | 名前の制御文字、OSC アドレスの空白・制御文字を受理していた | validation で拒否 |
| Host 受信レート | 内部生成(29.97df)から LTC へ戻すと約 2.6 秒、30fps の LTC に生成側のレートが付く(検出器の初期値に generator 設定が流用されていた、Codex) | 最後に受信した LTC のレートと表示に戻してから受信開始 |

## 旧 UI にあって v3 に無かったもの(Codex 第 11 ラウンドの棚卸しから、必要と判断して追加)

- 一括編集の OSC 引数(type:value を 1 行 1 件)
- 送信ログの「失敗のみ」フィルター
- ポン出しの行・列を縮小するとき、範囲外で消えるボタンの数を出して確認する
- ホスト削除の確認に、そのホストを送信先にしているキュー・ポン出しの件数を添える
- 発火後オートミュートのマスタースイッチをキューリスト下部で切り替えられるようにする(以前は表示のみ)
- Windows 流儀の Ctrl+Y でやり直し
- あとから追加したもの: 信号なし / フリーラン中の「最終受信 n 秒前」、TC 入出力の誤り率表示、Web メニューの最近使ったプロジェクト(Windows ではネイティブメニューが出ないため)
- 復活させないと判断したもの: タイムコード中継と RELAY ON(v3 で意図的に廃止)。同一ログの集約は未着手(表示件数と Host の 500 件上限、失敗のみフィルターとの整合を決めてから)

## 次にやるべきこと(Codex 第 7 ラウンドの優先順、未着手。1 番目の FPS 表示と 6 番目の smoke --attach は対応済み)

2. Windows の CI/実機(WebView2、WASAPI、Inno Setup、build-windows.cmd)は macOS では動かせない。release workflow の PR 検証と installer の起動 smoke を追加する
3. 外部から LTC 信号を落とす統合試験(フリーラン→信号なし、その間の NEXT と発火抑止)
4. 無効ホストをスキップしたことをログで観測できるようにする。UDP 送信の「成功」を「送信済み」と表現し、疎通状態を併記する
5. NEXT/current 行の自動スクロール(旧 UI 同等だが運用性の改善余地)
6. 起動済みの Host に対して smoke.sh を使える `--attach` 相当のモード

## 仕様どおり・記録のみ

- キュー名・OSC アドレスに長さや文字種の上限が無い(10,000 文字や制御文字も受理される)。上限を設けるなら仕様値の決定が要る
- 1000 行のキューリストは仮想化していない(1000 件では操作に支障なし)

- Cue-Sync は過去に発火したキューが無いと引数 0.0 を送る(Core の仕様)
- 到達不能ホストへの OSC は UDP なので成功ログになる。疎通確認は失敗ログとトースト
- 無効ホストが送信先に含まれるキューは、そのホストへの送信を省く。送信先が 0 件になったときだけ送信ログに失敗 1 行とトーストが出る(第 10 ラウンドで追加)
- `ltc.stop` は明示停止なのでフリーランに入らない。フリーラン→信号なしの遷移は自己生成 LTC を止める手段が無く UI からは未確認
- 次のキューが画面外にあっても自動スクロールしない(旧 UI にも無し)
- モニターの「-- Vpp」は Host が値を出したことが無かったので、波形と一緒に届く直近区間のピーク(dBFS)の表示に置き換えた(levelVpp は protocol から削除)

## 自動操作の落とし穴(今日踏んだもの)

- `project.save/saveAs` はパス無しだとネイティブ保存パネルが開いて command が戻らない。`project.open/new` も未保存だとネイティブ確認が出る。自動操作では保存済み状態で `project.open {path}` を使う
- ウィンドウが隠れていると Host が patch を送らないので DOM も撮影も古いまま。`document.hidden` を見てから判定する
- `tcb3ctl start` は他の TimecodeBridge3 が動いていても、自動操作口つきなら別ポートで起動できるようにした(TCB3_PORT / TCB3_RUN_DIR / TCB3_EXE)。bin/Debug を上書きしないよう Host は `dotnet build -o` で別ディレクトリに、テストも `dotnet build tests/... -o` → `dotnet test <dll>` で回す
- ワークツリーで動かす Codex は `.git/worktrees` がサンドボックス外で commit できない。未コミットのまま報告させ、`git diff | git apply` で取り込む
