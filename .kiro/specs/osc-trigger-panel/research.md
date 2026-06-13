# Research & Design Decisions

## Summary
- **Feature**: `osc-trigger-panel`
- **Discovery Scope**: Extension（既存 WPF アプリへの機能追加）
- **Key Findings**:
  - 既存 UI はタブ構成ではなく `MainWindow.xaml` の `Grid`（Row0:メニュー / Row1 / Row2）に各 View を直接配置している。タブ化は Row1+Row2 を 1 つの TabItem に内包し、TabControl を新設する形が最小改変。
  - OSC 送出・ホスト管理・引数モデル・永続化はすべて既存資産で完結する（`IOscSender.Send`、`HostRegistry`/`OscHost`、`OscArgument` 派生レコード、`ProjectData` + `OscArgumentJsonConverter`）。新規の送出/シリアライズ実装は不要。
  - 状態保持は「Singleton サービスが真実の状態を持ち、ViewModel が参照、`MainViewModel` が Save/Open/New 時に集約・復元」という確立パターンがある（`CueManager`/`HostRegistry`/`TimecodeRelay`）。新機能も同型で追加するのが一貫的。

## Research Log

### 既存タブ/レイアウト構造
- **Context**: 「タブを追加」をどう実現するか。
- **Sources Consulted**: `src/TimecodeBridge/MainWindow.xaml`, `MainWindow.xaml.cs`
- **Findings**:
  - `MainWindow.xaml` は `Grid`（3 行）。Row0=Menu、Row1=CueList/HostManager/RelayControl、Row2=Timecode/Waveform/Log。
  - 子 View は `OnDataContextChanged`（`MainWindow.xaml.cs:52-70`）で DI から ViewModel を取得して注入している。
- **Implications**: メニュー（Row0）はタブ外に残し、Row1+Row2 を「タイムコード」タブへ移設。新タブ用 View も同じく `OnDataContextChanged` で DI 注入する。

### OSC 送出と引数モデル
- **Context**: ボタン押下時の送出と引数編集をどう既存に乗せるか。
- **Sources Consulted**: `Services/Interfaces/IOscSender.cs`, `Services/OscSender.cs`, `Models/OscArgument.cs`, `Views/CueEditDialog.xaml.cs`
- **Findings**:
  - 送出口は `IOscSender.Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)`。有効ホスト解決・`SendCompleted` 通知は `OscSender` 内で実施。
  - 引数は `OscInt32Argument`/`OscFloat32Argument`/`OscStringArgument`（record）。`CueEditDialog` は `i:1 f:2.5 s:foo` 形式の 1 行テキストで複数引数を編集（`ParseArguments`/`FormatArguments`）。
- **Implications**: ボタン送出は `IOscSender.Send` をそのまま利用しログも既存機構に乗る。引数編集 UI は CueEditDialog のテキスト記法を踏襲し、パース/整形ロジックは共通ヘルパへ抽出して重複を避ける。

### ホスト選択 UI パターン
- **Context**: 送信先ホストの複数選択 UI。
- **Sources Consulted**: `ViewModels/RelayViewModel.cs`, `Views/CueEditDialog.xaml.cs`
- **Findings**: `HostSelection { Id, Name, IsSelected }` を `ObservableCollection` でチェックボックス表示。`HostRegistry.HostChanged` 購読で再構築（`RefreshHostSelections`）。
- **Implications**: ボタン編集ダイアログでも `HostSelection` を再利用する。

### 永続化統合
- **Context**: グリッド/ボタン設定をプロジェクトへ保存・復元。
- **Sources Consulted**: `Models/ProjectData.cs`, `ViewModels/MainViewModel.cs`, `Services/ProjectService.cs`
- **Findings**:
  - `ProjectData`（camelCase + `OscArgumentJsonConverter`）に Cues/Hosts/RelaySettings/Offset/SourceSettings を保持。
  - `MainViewModel.SaveToPath`/`OpenProject`/`NewProject`/`ClearAllData` が各サービスから設定を集約/復元/初期化。
- **Implications**: `ProjectData` に `OscTriggerPanel` セクションを追加。`MainViewModel` の Save/Open/New/Clear 4 箇所へ統合。旧ファイルは `OscTriggerPanel` 欠落時に既定値で読み込み継続（後方互換）。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| Manager(Singleton) + ViewModel + DialogService（採用） | 既存 CueManager/CueDialogService と同型 | 一貫性・テスト容易・永続化統合が既存パターンに乗る | クラス数が増える | プロジェクト標準に合致 |
| ViewModel が IOscSender を直接呼ぶ（状態も VM 保持） | クラス削減 | 軽量 | 状態の真実が VM に偏り、Save/Load 統合・テストが煩雑 | 既存パターンから逸脱 |

## Design Decisions

### Decision: タブ化の方式
- **Context**: 既存はタブ無し。要件 R1 でタブ化が必要。
- **Alternatives Considered**:
  1. `MainWindow` に `TabControl` を新設し、既存レイアウト全体を「タイムコード」TabItem へ内包。
  2. 既存レイアウトを大幅に作り替えてタブ前提に再設計。
- **Selected Approach**: 1。Row0 メニューは据え置き、Row1+Row2 の `Grid` を「タイムコード」TabItem に移し、「OSCポン出し」TabItem を追加。
- **Rationale**: 既存機能の操作性・レイアウトを温存しつつ最小改変。`OnDataContextChanged` の DI 配線も流用可能。
- **Trade-offs**: タブ切替時も全 View は構築済みのまま（`IsVisible` 切替）で、バックグラウンド処理は継続（R1.3 を満たす）。
- **Follow-up**: タブ切替で View が破棄されないこと（`TabControl` 既定は再生成されないが、`DataContext` 配線タイミングに留意）。

### Decision: ボタン位置の保持方法
- **Context**: 固定グリッド上のボタン配置を永続化する。
- **Selected Approach**: 各ボタンに `Row`/`Column` を保持。グリッドは `Rows`×`Columns`。
- **Rationale**: 行列数を変更してもボタンの論理位置を維持できる。範囲外（縮小時）判定も `Row >= Rows || Column >= Columns` で単純。
- **Trade-offs**: セル index 方式より若干冗長だが、リサイズ耐性が高い。

### Decision: セル操作の UX（送出 vs 編集）
- **Context**: R3.1（編集）と R4.1（送出）、R4.6（未設定セル押下）。
- **Selected Approach**: 設定済みセル＝主クリックで送出、補助操作（右クリック or 編集アイコン）で編集ダイアログ。未設定セル＝クリックで編集ダイアログ。
- **Rationale**: 運用中の誤編集を避けつつ、空セルからの登録動線を確保。
- **Follow-up**: 視覚フィードバック（R4.5）は設定済みボタンの一時ハイライトで表現。

## Risks & Mitigations
- グリッド縮小で範囲外ボタンが発生 — 縮小確定前に確認ダイアログを出し、範囲外ボタンは削除（R2.5）。
- 送信先が未設定/全無効のボタン押下 — 送出せずログ/通知（R4.4）。
- 旧プロジェクトファイルに新セクションが無い — デシリアライズ既定値（空 + 既定行列）で継続（R5.5）。
- 引数パースの記法不一致 — CueEditDialog と同一記法を共通ヘルパで統一し挙動を一致させる。

## References
- 既存実装（一次情報）: `IOscSender`, `OscSender`, `HostRegistry`, `RelayViewModel`, `CueEditDialog`, `CueDialogService`, `ProjectData`, `MainViewModel`
- BuildSoft.OscCore 1.2.1.1（既存依存、`OscTransport` 経由で利用）
