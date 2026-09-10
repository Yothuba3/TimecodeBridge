# TimecodeBridge3.Web 構成案 v0.2

## 結論

**Preact + TypeScript + esbuild を採用する。** モックのHTML/CSSによる外観を保ち、画面の低頻度な状態更新とフォーム／モーダルはPreactで宣言的に構成する。時計（最大30Hz）と波形はPreactの再レンダー経路から分離し、専用DOM text node／Canvasへ最新値だけを命令的に反映する。キュー一覧は不変IDをkeyに行DOMを再利用し、1000件で実測して必要なら表示範囲をvirtualizeする。

採用理由は、M2以降の編集draft、複数選択、モーダル、snapshot受信中のfocus／IME保持を手書きDOM同期だけで堅牢に保つより、コンポーネント境界とkey付き差分更新を利用する方が保守しやすいためである。PreactはReact互換のTSXモデルを小さい実行時依存1個で提供する。30Hzデータを全体stateへ入れない限り、現場オペレート要件とも両立する。Viteや状態管理libraryは入れず、buildはesbuild、testはNode標準test runnerとし、依存とビルド経路を抑える。

## 構成

```text
src/TimecodeBridge.Web/
  package.json
  package-lock.json
  tsconfig.json
  esbuild.mjs
  src/
    index.html
    styles/{tokens,layout,components}.css
    main.tsx
    protocol.ts          # wire DTO・type guard・version
    bridge.ts            # ready/command/resync/viewport・pending request
    store.ts             # Host state + local UI state・revision適用
    render.ts            # clock/wave等の高頻度dirty domain更新scheduler
    commands.ts
    shortcuts.ts
    validation.ts
    components/
      app.tsx
      monitor.tsx
      cue-list.tsx
      receive-panel.tsx
      generator-panel.tsx
      cue-sync.tsx
      host-panel.tsx
      log-panel.tsx
      trigger-drawer.tsx
      dialogs.tsx
    interactions/{column-resize,drawer-resize,selection,focus}.ts
    testing/fake-host.ts
  tests/{protocol,store,shortcuts,validation}.test.ts
  dist/
```

protocolをC#から自動生成するのは初期には過剰。C#出力fixtureをTSでparseし、TS fixtureをC#でdeserializeするcontract testを置く。必要になればJSON Schemaを単一sourceにする。

## build

- `npm run build`: esbuildで `main.tsx` を `dist/assets/app.js` へbundle/minifyしCSS/HTMLをcopy。
- asset名は固定。hashはlocal同梱アプリでは利点が小さい。
- `npm run typecheck`: `tsc --noEmit`; `strict`, `noUncheckedIndexedAccess` を有効化。
- source mapはDebugのみ。
- CSPは `default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'` を目標にしinline scriptをなくす。InvokeScriptとの相性はM0確認。
- Webからnetworkへ直接接続しない。OSC/file/audioは全てHost経由。

ViteはHMRの効果が明確で、Host側のdev URL / packaged URL切替が安全に通った場合だけ採用する。本番は常に静的assetとする。

## 状態モデル

```ts
interface RootStore {
 host:{sessionId:string|null;revision:number|null;state:AppState|null;clockSeq:number;waveSeq:number;connected:boolean;resyncing:boolean};
 ui:{selectedCueIds:Set<string>;anchorCueId:string|null;focusedCueId:string|null;cueScrollTop:number;cueColumnWidths:number[];drawerOpen:boolean;drawerHeight:number;activeDialog:DialogState|null;drafts:Map<string,unknown>;pendingCommands:Map<string,PendingCommand>;toastQueue:Toast[]};
}
```

Host stateとUI stateを混ぜない。snapshot/patchで選択・scroll・編集中入力を消さず、削除されたIDだけ選択から除く。入力はblur/Enter/保存でcommit。sliderはlocal表示し100ms程度のdebounceまたはpointerupで送る。

受信直後に全DOMを書き換えずdirty domainを `requestAnimationFrame` で最大1回/paintへまとめる。clockは専用text nodeだけ更新。waveはCanvas 2D。Cue一覧は `data-cue-id` でDOMを再利用し、1000件で不足した場合だけvirtualizeする。

## モックからの移行

1. モックをHTMLとCSSへ移し、外観を維持。inline scriptとsample生成を除く。
2. fake-hostからsnapshot/clock/waveを流し、静的dataなしで再現。
3. 最重要のCue一覧を先に実データ化。選択、複数選択、有効化、手動発火、追加・編集・削除、次Cue強調。
4. TC、LIVE/MUTE、CueSyncを配線し、頻用操作を常時見える位置に維持。
5. 受信設定は右paneのcompact領域で即accessを維持。generate時だけ同領域を切替。
6. Host管理とlogを実データ化。
7. OSCポン出しを下drawer化。開閉中もCue一覧stateを保持。
8. native Hostへ接続しfake-hostをtest専用化。

実データをモックの `innerHTML` templateへ入れない。`textContent`, `createElement`, attribute APIで構築し、Cue名・OSC・log由来のHTML注入を防ぐ。

## keyboard・focus・IME

WebView内shortcutはdocument capture listenerで処理し、HostのPreviewKeyDownと二重登録しない。native menuの標準accelerator（Cmd/Ctrl+O/S/Z等）はHost担当、結果はpatchで返す。

次の場合はshortcutを処理しない。

- `event.isComposing` またはcomposition開始から終了まで
- targetがinput/textarea/select/contenteditable
- modal表示中（Escape等、modal自身の許可キーを除く）
- 未定義のCmd/Ctrl/Alt組合せ
- `event.repeat` かつCueSync・手動発火・OSCポン出し等の単発送信

初期案: Space=CueSync、L=LIVE/MUTE、P=drawer、Escape=modal→drawer、上下=cue移動（Shift範囲）、Enter=編集、Delete/Backspace=削除確認。Enterは発火に割り当てない。

WebViewクリック後もnative menu acceleratorが効くか、Tabが内部を巡回しnative chromeへ戻れるかを両OS確認する。推測: WebViewは別native surfaceなのでAvalonia側KeyDownだけで内部keyを捕捉する設計は不安定である。

日本語IMEはcompositionstart/end, beforeinput, inputを使い、keydownだけで確定しない。TC/OSC検証はcompositionend/blur時。snapshot受信時もfocus中draftを上書きせず、基底revision変更は保存時に競合表示する。

## 列幅・drawer drag

Pointer Events + `setPointerCapture` は通常利用できるが、WKWebView/WebView2実機でM0確認する。

- pointerdownでpreventDefault/captureし、move/up/cancel/lostpointercaptureを処理。
- window外・blurでもdrag stateを解除。`touch-action:none`, drag中 `user-select:none`。
- 列幅は隣接列との境界移動方式にする。現行モックの対象列だけ加算は総幅超過を起こす。
- Cue名/OSC列を優先伸縮し、check/時刻/実行列にmin/max。
- 列幅・drawer高はTimecodeBridge3固有keyでlocalStorage保存可能。
- drawer高変更時もCue一覧の最低高を保証し、恒常的に圧迫しない。
- 列幅reset・drawer既定高復帰を用意する。

## Hostへの同梱

第一候補はHost assemblyへの埋め込み。build時に `npm ci && npm run build`、distを埋め、起動時にversion付き専用cacheへ展開しindex.htmlを開く。古いcacheは次回起動時に安全に掃除する。

`file://` のmodule/CSP/subresourceとNativeWebView navigationをM0確認し、問題時は次の順に選ぶ。

1. NativeWebViewのvirtual host / local resource mapping（実package APIで確認）。
2. Host内loopback HTTP server（127.0.0.1のみ、random portとtoken）。
3. NavigateToStringは相対asset/CSP/sizeが扱いづらく単一file spike以外では避ける。

隣接directoryはDebug向きだがReleaseでは欠落・改変・installer差異が出やすい。Releaseは埋め込み＋展開を推奨。libltcは別のnative動的libraryとして配置し、About/同梱文書からlicenseとsource入手案内へ到達可能にする。

Nodeなしの `dotnet build` 方針を明文化する。推奨は `BuildWebAssets=true` 時だけnpm実行し、通常Host buildは既生成distを消費。distをgit管理しCI再build後に差分ゼロを検査する。dist非管理ならNodeを正式な必須SDKにする。

## test

- unit: revision欠落、古いclock破棄、command timeout、IME guard、TC/OSC/IP validation。
- DOM: focus/selection/scroll保持、1000件更新で全行再生成しない。
- contract: C#/TS相互fixture、未知field許容、必須field欠落拒否。
- integration: ready→snapshot→command→result→patch、resync、Host再起動。
- E2E両OS: Tab、IME、native menu shortcut、pointer capture、最小化/復帰、reload、close。
