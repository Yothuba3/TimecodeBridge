# v3 実機確認の記録(2026-09-13, macOS)

`tools/tcb3/tcb3ctl` と `TIMECODEBRIDGE_AUTOMATION_PORT`(README「人手なしの実機確認」)で、人手を介さずに Host を起動・操作・撮影して確かめた結果。LTC は同一プロセスの自己テスト生成(Pro Tools Audio Bridge 2-A)、OSC は `tools/tcb3/osc_listen.py`(udp/9100)で実受信して判定した。

## 合格した項目

- **M1**: 受信中・誤り率 0%・時計が進む。IME 変換中(`isComposing`)の Space / L / P は無視され、入力欄にフォーカスがあるときの L も無視される。publish 成果物(`dotnet publish -c Release --self-contained -r osx-arm64` を release-v3.yml と同じ手順で `.app` 化、ad-hoc 署名)は、直接実行でも `open -a` でも起動し、同梱の libltc.dylib と web/ を読み込んで受信できる。
- **M2a**: キュー行の ▶ で OSC が出る。Cue-Sync のホスト選択 → CUE SYNC ボタン / Space で `/cuesync` が出る。MUTE ボタンでミュート中はキュー時刻を過ぎても OSC が出ず runtime も未発火のまま、L で LIVE に戻る。`autoMuteOnFire` のキューは発火後に自動ミュート(`autoMutedCueId` 一致)し、`autoUnmuteAfter` の 2 秒後に解除される。NEXT CUE は追加直後・発火後とも正しく切り替わり、次のキューの行が current でハイライトされる(Host 側の修正後、下記)。
- **M2b**: 行を選んで Enter で「キューを編集」ダイアログ、Esc で閉じる。削除 → 確認 → ↶ Undo → ↷ Redo。時間順ソート。
- **M3**: 内部生成へ切替 → 生成パネル → ▶ 再生で `running` / 出力中、時計が 10:00:00:00 から進む → ■ 停止 → LTC受信へ戻すと選択済み入力で自動再接続。ホスト管理の追加ダイアログ(名前 / IP / ポート)→ 疎通確認(`reachable`、トースト「疎通 OK · 0 ms」)→ 編集 → 削除。送信ログの表示とクリア。OSC ポン出しは P で開き、編集モードで空セルからボタンを作成(ラベル / アドレス / 引数 `int32:7` / 送信先)、実行モードで押すと `/r/go [7]` が届く、Esc で閉じる。タイムコード中継は単発送信で `/timecode` が届き、連続送信 ON で 2 秒に 62 件、OFF で止まる(タイムコード中継は 2026-09-14 に機能ごと削除)。
- **負荷(1000 cues × 10 分)**: 下記「負荷測定」。

## 見つけた不具合と対処

| 場所 | 内容 | 状態 |
|---|---|---|
| Host `HostState.Flush` | `d.HasFlag(Domain.NextCue \| Domain.Cues)` は両方のビットが立つときしか真にならず、さらに `Domain.NextCue` をどこも dirty にしていなかったため、`state.nextCue` が patch に一切乗らず Web の NEXT CUE が snapshot 時点のまま止まっていた | 修正済み。`BuildClock` で次のキューの id が変わったら `Domain.NextCue` を dirty にし、判定を `(d & (NextCue \| Cues)) != 0` に変更。テスト 2 件追加 |
| Web `app.tsx` | NEXT CUE の「T− …f」が `state.nextCue.framesUntil`(patch 時点の値)を表示していて進まない。`clock.framesUntilNextCue` は毎フレーム届いている | 修正済み(Codex, 2026-09-14)。clock の値を表示。回帰テスト追加。実機で T− が毎秒減ることを確認 |
| Web `host-panel.tsx` / m3.css | ホスト行の「有効 / 疎通確認 / 編集 / 削除」が 1 文字ずつ縦に折り返される(右ペイン幅 約 470px)。`.host-row` の grid 列が `auto auto auto auto` で、ボタンの `white-space` 指定が無い | 修正済み(Codex, 2026-09-14)。名前と IP:ポートを 1 行目、操作群を 2 行目(折り返しなし)に。実機でボタン高さ 27px・横並びを確認(ウィンドウ幅 1100 でも崩れない) |
| Web `generator-panel.tsx` / m3.css | 内部生成の「開始 TC」のフレーム欄と隣の「FPS」ラベルが重なる(`.generator-settings` の 2 列 grid に `timecode-input` が収まらない) | 修正済み(Codex, 2026-09-14)。開始 TC を全幅に。実機で矩形が重ならないことを確認 |
| Web `host-panel.tsx` | ホスト追加ダイアログの入力が `setD({...d, [key]: v})` で、同一フレーム内に複数フィールドが更新されると前の値が失われる(人が打つ分には問題ないが、貼り付けや自動入力で欠ける) | 修正済み(Codex, 2026-09-14)。関数形の state 更新に変更 |
| Host `HostState.BuildTransport` | `LevelVpp` を常に null で返している(モニターの「-- Vpp」が埋まらない) | 未修正(未実装) |

Codex の修正後に `npm run typecheck && npm test && npm run build`(26 件合格、dist は再ビルドしても同じ差分)、Host 再ビルド、`tools/tcb3/smoke.sh`(PASS)を実行した。

## 確認時の注意(道具側)

- ウィンドウが他のウィンドウに隠れると WebKit が描画と requestAnimationFrame を止め、DOM も撮影も古いままになる。`tcb3ctl start` は ready 後にウィンドウを前面に出す(`POST /activate`)。
- command 直後の DOM は patch 到達(約 35ms)と再描画の前なので古い。DOM を読む前に 0.3〜0.5 秒待つ。
- `cue.duplicate` の `count` は 500 まで。1000 件は末尾のキューを基準に 2 回に分ける。

## 負荷測定(1000 cues × 10 分)

自己テスト LTC を受信しながら 1 秒間隔のキュー 1000 件(`cue.duplicate` 500 件 × 2 回、17ms)を載せ、5 秒ごとに 120 回記録した。2 回目は Web の `window.tcb.receive` をフックして「Web が受け取った最新の clock」と rAF 回数も記録した(scratchpad の load10.py)。

| 項目 | 結果 |
|---|---|
| 受信 | 10 分間 receiving のまま、誤り率 0% |
| 発火 | 588 / 588(時刻が過ぎたキューはすべて発火)、OSC 実受信 589、送信失敗 0、resync 0、patch 1188 件 |
| snapshot | 398KB → 496KB(送信ログ 500 件分)、取得 p50 29ms / p95 32ms / 最大 35ms |
| Web への clock 到達 | 118 / 120 サンプルで Host との差 0〜2 フレーム。1 回だけ 5.4 秒(162 フレーム)止まり、その直前が 11 フレーム |
| JS 応答(eval 往復) | p50 5ms / p95 13ms / 最大 32ms |
| Host プロセス | 常駐 208 → 197MB(最大 225MB)、CPU 最大 26% |
| DOM 1000 行の描画 | 可視のときは Host との差 0〜2 フレーム |

判定: Host とブリッジは M0 の目安(dispatcher queue の継続増加なし、p95 が約 33ms を大きく越えない)を満たす。**追跡が要るのは Web への clock 到達が 5 秒止まった 1 回**(01:01:56 → 01:02:02、ウィンドウが隠れている間に発生、原因未特定)。可視状態での再測定と、`InvokeScript` 1 回ごとの所要時間の記録が次の手。

測定中 120 サンプル中 110 でウィンドウが他のウィンドウに隠れていた(この Mac で別の作業が行われていたため)。隠れている間は WebKit が描画と requestAnimationFrame を止めるので画面の時計は止まって見えるが、可視に戻ると即座に追いつく(受け取った clock は最新)。

## 2026-09-14 追記

- **入力デバイスを未選択に戻せる**(ユーザー要望): Host に `receive.selectDevice {deviceId}` を追加(null/空で受信停止と選択解除、それ以外は `ltc.reconnect` と同じ)。Web の「入力デバイス」select は Codex が `receive.selectDevice` へ配線(herdr 経由で依頼、Codex 自身が tcb3ctl で実機確認)。実機で「選択してください」を選ぶと `selectedDeviceId` が null、transport が stopped、接続欄が「入力デバイスを選んでください」、再接続ボタンが無効になり、選び直すと受信に戻ることを確認。未選択のまま内部生成 → LTC 受信へ戻しても自動再接続しない(テスト追加)。
- **波形の表示幅を UI 改修前と同じに**(ユーザー要望): v3 は 100ms(4800 サンプル)だったのを、v2 の `AudioWaveformView` と同じ約 0.1 フレーム分(`0.1/30` 秒 ≒ 3.3ms、48kHz で 160 サンプル)に変更(`WaveReducer.WindowSeconds`)。実際のサンプルレートに追従する。180 点の要求に対し 1 点 1 サンプルになるため min と max が同じ値になるが、Web の polygon は stroke を持つので線として描ける。実機で LTC の矩形波が数個見える表示になった。
