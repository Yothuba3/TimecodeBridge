# TimecodeBridge3 Host ⇄ Web プロトコル草案 v0.1

## 結論

30Hzで全状態を送らない。Hostを唯一の正とし、通信を `snapshot`（初期・再同期全量）、`patch`（revision付き低頻度変更）、`clock`（最大30Hz・最新値優先）、`wave`（縮約済み、初期30Hz）の4系統に分ける。JS→C#は全て `requestId` 付きcommand、C#は `result` を必ず返す。キュー発火、OSC、ミュート、TC処理はHost内で完結させる。

## エンベロープ

```ts
type HostMessage =
 | {protocolVersion:1; type:"snapshot"; revision:number; state:AppState}
 | {protocolVersion:1; type:"patch"; revision:number; baseRevision:number; changes:StateChanges}
 | {protocolVersion:1; type:"clock"; seq:number; clock:ClockState}
 | {protocolVersion:1; type:"wave"; seq:number; wave:WaveState}
 | {protocolVersion:1; type:"result"; requestId:string; ok:true; data?:unknown; revision?:number}
 | {protocolVersion:1; type:"result"; requestId:string; ok:false; error:ProtocolError};

type WebMessage =
 | {protocolVersion:1; type:"ready"; clientVersion:string; capabilities:string[]}
 | {protocolVersion:1; type:"command"; requestId:string; command:CommandName; args:unknown; expectedRevision?:number}
 | {protocolVersion:1; type:"resync"; currentRevision:number|null; reason:string}
 | {protocolVersion:1; type:"viewport"; waveformWidth:number; devicePixelRatio:number; visible:boolean};
```

C#→JSは `window.tcb.receive(<JSON>)` の一入口にする。JSONはSystem.Text.Jsonで直列化し、スクリプトへ埋める場合は `<`, `>`, `&`, U+2028, U+2029を安全にescapeする。専用post-message APIがあれば優先し、InvokeScriptでも戻り値を待たず、UIスレッドへ未処理呼出しを積まない。

## AppState

enumは言語間で安定する文字列、IDは不変文字列とする。

```ts
type FrameRate = "24"|"25"|"29.97df"|"30";
interface AppState {
 sessionId:string;
 project:{displayName:string;filePath:string|null;dirty:boolean;canUndo:boolean;canRedo:boolean;recentFiles:string[]};
 mode:"ltc"|"generate";
 transport:{status:"stopped"|"receiving"|"freerun"|"signalLost"|"error";statusText:string;detailText:string;signalErrorRatePercent:number|null;locked:boolean;levelVpp:number|null;triggerMuted:boolean;autoMuteEnabled:boolean;autoMutedCueId:string|null;autoUnmuteAtUtc:string|null};
 receive:{selectedDeviceId:string|null;devices:AudioDevice[];offset:string;triggerWindowFrames:number;freerunDurationSeconds:number};
 generator:{running:boolean;startTime:string;frameRate:FrameRate;selectedOutputDeviceId:string|null;outputDevices:AudioDevice[];volume:number;ltcOutputActive:boolean;settingsPendingReset:boolean};
 currentClock:ClockState; nextCue:NextCueState|null; cues:CueDto[]; hosts:HostDto[];
 cueSync:{oscAddress:string;targetHostIds:string[]};
 relay:{oscAddressPattern:string;interval:{mode:"everyFrame"|"custom";intervalMs:number};targetHostIds:string[];continuousEnabled:boolean};
 triggerPanel:{rows:number;columns:number;buttons:TriggerButtonDto[]};
 logs:LogDto[];
 uiCapabilities:{supportsNativeOpenDialog:boolean;supportsNativeSaveDialog:boolean;platform:"windows"|"macos"};
}
interface ClockState {raw:string;display:string;totalFrames:number;frameRate:FrameRate;dropFrame:boolean;receivedAtMonotonicMs:number;nextCueId?:string|null;framesUntilNextCue?:number|null}
interface NextCueState {id:string;name:string;triggerTime:string;effectiveTriggerTime:string;framesUntil:number}
interface AudioDevice {id:string;name:string;loopback:boolean}
interface CueDto {id:string;name:string;memo:string;triggerTime:string;effectiveTriggerTime:string;frameRate:FrameRate;oscAddress:string;additionalOscAddresses:string[];arguments:OscArgumentDto[];targetHostIds:string[];enabled:boolean;sendTriggerTimeAsSeconds:boolean;sendTimecode:string|null;triggerOffset:string|null;autoMuteOnFire:boolean;autoUnmuteAfter:string|null;runtime:{lastTriggeredAtUtc:string|null;flashToken:number;muteCountdownText:string}}
type OscArgumentDto={type:"int32";value:number}|{type:"float32";value:number}|{type:"string";value:string};
interface HostDto {id:string;name:string;ipAddress:string;port:number;enabled:boolean;reachability:"unknown"|"checking"|"reachable"|"unreachable"}
interface TriggerButtonDto {id:string;row:number;column:number;label:string;oscAddress:string;arguments:OscArgumentDto[];targetHostIds:string[]}
interface LogDto {id:string;timestampUtc:string;message:string;success:boolean}
```

`currentClock` は初期描画用。以後の毎フレーム更新はclockだけで行う。

## patchと一貫性

JSON Patchや配列index差分は並べ替えに弱いため使わず、v1はドメイン単位で置換する。

```ts
interface StateChanges {project?:AppState["project"];mode?:AppState["mode"];transport?:AppState["transport"];receive?:AppState["receive"];generator?:AppState["generator"];nextCue?:NextCueState|null;cues?:CueDto[];hosts?:HostDto[];cueSync?:AppState["cueSync"];relay?:AppState["relay"];triggerPanel?:AppState["triggerPanel"];logsAppend?:LogDto[];logsReset?:LogDto[]}
```

- Hostの低頻度状態変更ごとにrevisionを増やす。Webは `baseRevision === currentRevision` のpatchだけ適用し、不一致ならresyncしてsnapshotまで破壊的編集を止める。
- clock/waveは独立seq。古い値は破棄し、欠番は許容する。
- 編集・削除・並べ替え・Undo/RedoはexpectedRevision必須。送信やmuteは省略可。
- Hostはcommandを直列処理し、状態変更→revision更新→patch作成を一dispatcher処理にする。
- draft、選択、scroll、列幅、drawerはWebローカル状態。snapshotで潰さない。
- `cue.fire`, `cueSync.send`, `triggerPanel.fire`, `relay.sendOnce` はHostでrequestIdを短時間記憶し重複排除する。

## 頻度とバックプレッシャー

|種別|上限|方針|
|---|---:|---|
|clock|30Hz|最新1件のみ。前回処理中なら中間値を捨てる|
|wave|初期30Hz|viewport幅へmin/max縮約。非表示時停止。計測後のみ60Hz|
|patch|event driven|同一domainを約16msでまとめてもよい|
|snapshot|必要時|ready/resync/project読込のみ。毎フレーム禁止|
|result|command毎|ダイアログを伴っても最終結果を返す|

M0合否目安（推測）: clock+wave、1000 cues表示を10分継続し、入力遅延やdispatcher queueの継続増加がなく、p95 bridge所要が約33msを大きく越えない。悪ければlatest-only、同期待ち廃止、wave低頻度化の順に対処する。

## JS→C# command

|command|args|成功data / 主なエラー|
|---|---|---|
|`app.undo`, `app.redo`|`{}`|`notAvailable`|
|`project.new/open/save/saveAs`|`{path?,suggestedName?}`|`{cancelled,path?}` / `invalidProject`,`ioError`|
|`mode.set`|`{mode}`|`busy`,`unsupported`|
|`ltc.start/stop/reconnect`|`{deviceId?}`|`deviceNotFound`,`audioError`,`nativeError`|
|`audio.refreshDevices`|`{direction}`|patch / `audioError`|
|`receive.setOffset`|`{value}`|`{normalized}` / `validation`|
|`receive.setTriggerWindow`|`{frames}`|`{frames}` / `validation`|
|`receive.setFreerunDuration`|`{seconds}`|`validation`|
|`generator.configure`|`{startTime,frameRate,outputDeviceId,volume}`|`validation`,`deviceNotFound`|
|`generator.start/stop/reset`|`{}`|`audioError`,`invalidState`|
|`mute.set`, `autoMute.setEnabled`|`{muted? ,enabled?}`|`invalidState`|
|`cue.add/update`|`{id?,cue:CueDraft}`|`{id?}` / `notFound`,`validation`,`conflict`|
|`cue.remove`|`{ids}`|`notFound`,`conflict`|
|`cue.duplicate`|`{id,count?,intervalFrames?}`|`{ids}` / `validation`|
|`cue.batchUpdate`|`{ids,changes:CueBatchDraft}`|`{updated,offsetSkipped}` / `validation`|
|`cue.sortByTime/setEnabled/fire`|`{id?,enabled?}`|`{sent?,failed?}` / `notFound`,`oscError`|
|`cueSync.configure/send`|`{oscAddress?,targetHostIds?}`|`{sent?,failed?}` / `validation`,`oscError`|
|`host.add/update/remove/setEnabled/ping`|`{id?,host?,enabled?}`|`{id?,reachable?,latencyMs?}` / `validation`,`inUse`,`networkError`|
|`relay.configure/setContinuous/sendOnce`|設定または`{enabled}`|`{sent?,failed?}` / `validation`,`oscError`|
|`triggerPanel.configureGrid/upsertButton/removeButton/fire`|grid/button/id|`{id?,sent?,failed?}` / `validation`,`occupiedCell`,`oscError`|
|`logs.clear`|`{}`|なし|
|`app.requestClose`|`{}`|`{cancelled}`|

詳細なcommand名は `domain.verb` とし、表示文字列でなく対象IDを渡す。M1/M2で編集をnative dialogに残すなら `cue.openEditor` 等を暫定提供できるが、最終的にはWeb modalのDraftをadd/updateへ渡す。

## エラー表現

```ts
interface ProtocolError {code:"badMessage"|"unsupportedVersion"|"unknownCommand"|"validation"|"notFound"|"conflict"|"invalidState"|"deviceNotFound"|"audioError"|"nativeError"|"networkError"|"oscError"|"ioError"|"internal";message:string;fieldErrors?:Record<string,string>;retryable:boolean;details?:Record<string,unknown>}
```

予期しない例外はHostログへ詳細を残し、Webにはinternalと相関errorIdだけ返す。JSは信頼境界としてC#でも型・範囲・ID存在を検証する。

## 接続の確立(実機で確定, 2026-09-11)

NativeWebView は `invokeCSharpAction` をページのスクリプト評価より**後**に注入する。そのため Web が起動直後に送る `ready` は届かない。
Host はページ読込完了後、`window.tcb.hostAttached()` を 250ms 間隔で `ready` を受けるまで(最大 10 秒)呼ぶ。Web は `hostAttached()` で実 Host 接続とみなして `ready` を送る(冪等)。fake-host は `hostAttached()` が一定時間呼ばれず `invokeCSharpAction` も無い場合にだけ入る。

## WaveState(確定)

```ts
interface WaveState { sampleRate:number; windowMs:number; min:number[]; max:number[]; levelDbfs:number|null }
```
`min[i]`/`max[i]` は表示 i 点目の区間の最小/最大(−1..1)。点数は Web の `viewport.waveformWidth/2`(16..2048)。`levelDbfs` は直近区間のピーク(dBFS)。

## M2b で確定した command の引数と結果(Host 実装済み, 2026-09-11)

```ts
// cue.add {cue: CueDraft} → {id} / cue.update {id, cue: CueDraft} → {id}
interface CueDraft {
  name: string;                       // 必須(空白のみは validation)
  memo?: string;
  triggerTime: string;                // "HH:MM:SS:FF"(DF は最後が ';' でも可)。FF は frameRate 未満
  frameRate?: FrameRate;              // 省略時は現在の受信フレームレート
  oscAddress: string;                 // '/' 始まり
  additionalOscAddresses?: string[];  // 各要素 '/' 始まり
  arguments?: OscArgumentDto[];
  targetHostIds?: string[];
  enabled?: boolean;                  // 既定 true
  sendTriggerTimeAsSeconds?: boolean;
  sendTimecode?: string | null;       // "HH:MM:SS:FF"。null/空 = トリガー時間をそのまま送る
  triggerOffset?: string | null;      // "±HH:MM:SS:FF"。全ゼロ/空 = なし。適用後が 0〜24 時を超えると validation
  autoMuteOnFire?: boolean;
  autoUnmuteAfter?: string | null;    // "HH:MM:SS:FF"。null = 手動解除まで
}
// validation エラーの fieldErrors のキーは "cue.<field>"

// cue.duplicate {id, count?=1, intervalFrames?=0} → {ids: string[]}
//   count=1 かつ intervalFrames=0 なら同時刻に「<name> (コピー)」を 1 件。それ以外は基準時刻 + interval×i (i=1..count)
// cue.batchUpdate {ids: string[], changes: CueBatchDraft} → {updated, offsetSkipped}
//   changes に存在するフィールドだけ適用(値 null は「クリア」)。triggerOffset 適用後が範囲外のキューはオフセットだけ見送り offsetSkipped に数える
interface CueBatchDraft {
  oscAddress?: string; additionalOscAddresses?: string[]; arguments?: OscArgumentDto[]; targetHostIds?: string[];
  memo?: string; enabled?: boolean; sendTriggerTimeAsSeconds?: boolean; autoMuteOnFire?: boolean;
  sendTimecode?: string | null; triggerOffset?: string | null; autoUnmuteAfter?: string | null;
}
// fieldErrors のキーは "changes.<field>"

// project.new {} → {cancelled}            未保存があれば Host がネイティブ確認ダイアログを出す(Web 側の確認は不要)
// project.open {path?} → {cancelled, path}  path 省略時は Host のファイルダイアログ。存在しない path は notFound、読めなければ ioError
// project.save {} / project.saveAs {suggestedName?} → {cancelled, path}  未保存パスなら save も saveAs と同じくダイアログ
// app.undo / app.redo {} → 成功 or invalidState(履歴なし)。可否は state.project.canUndo/canRedo
//   履歴は ProjectData のスナップショット(最大 50、500ms 以内の連続変更は集約)。ソース設定(デバイス・生成器)は Undo 対象外
// macOS のメニュー(Cmd+N/O/S/Shift+S, Cmd+Z/Shift+Z, 最近使ったプロジェクト)は Host が同じ command を内部で実行し、結果は patch として届く
// state.project.recentFiles は最近使ったプロジェクト(新しい順, 最大 10)。Web から開くときは project.open {path} を送る
```

## 起動順序・M0

Webがhandler/storeを準備してready→Hostがsnapshot→Webがviewport→Hostがclock/wave開始。reload/navigation中は停止し、新readyを待つ。sessionId変更時は旧pending commandを失敗扱いにする。

M0で両OS実機にて、10分連続負荷、1000 cuesのJSON/parse/DOM時間、最小化・reload・close中の例外、IME変換中のSpace/L/P抑止、単発OSCの二重クリック・再送重複排除を検証する。
