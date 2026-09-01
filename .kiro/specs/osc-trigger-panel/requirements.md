# Requirements Document

## Project Description (Input)
タイムコードに依存せず、手動操作でOSCメッセージを送出（ポン出し）する新機能を追加する。

【UI構成】
- 現状のMainWindowはタブUIがなく1画面に全パネルが並ぶ構成。これをTabControl化し、既存の全機能（CueList/HostManager/RelayControl/TimecodeDisplay/AudioWaveform/Log）を「タイムコード」タブに集約する。新たに「OSCポン出し」タブを追加する。
- 「OSCポン出し」タブには、固定グリッド（行数×列数をユーザーが指定）上にトリガーボタンを配置できる。グリッドの各セルにボタンを割り当てる。

【ボタン機能】
- 各ボタンを押すと、設定されたOSCアドレスと引数を、設定された送信先ホストへ即座に送出する（タイムコード進行とは無関係なワンショット送信）。
- ボタンごとに以下を設定可能：表示ラベル、OSCアドレス、OSC引数（既存のOscArgumentモデルを流用：int32/float32/string を複数指定可能）、送信先ホスト（既存HostRegistryのホストから選択）。

【既存資産の再利用】
- OSC送信は既存のIOscSender.Send(oscAddress, arguments, targetHostIds)を利用。
- 送信先ホストは既存のHostRegistry/OscHostを利用。
- 設定の永続化は既存のProjectData（JSON、camelCase、OscArgumentJsonConverter）に新セクションを追加する形で行う。

【技術スタック】
WPF (.NET 8, net8.0-windows), MVVM (CommunityToolkit.Mvvm), DI (Microsoft.Extensions.DependencyInjection)。既存のView+ViewModel+Model+Serviceの構成パターンに従う。

## Introduction
本仕様は、TimecodeBridge にタイムコード非依存の手動 OSC 送出（ポン出し）機能を追加するものである。メインウィンドウを TabControl 構成に再編し、既存の全機能を「タイムコード」タブに集約したうえで、新たに「OSCポン出し」タブを設ける。新タブには行数×列数で構成される固定グリッドを配置し、各セルにトリガーボタンを割り当てられる。各ボタンには表示ラベル・OSCアドレス・OSC引数・送信先ホストを設定でき、押下時にタイムコードの進行とは無関係に即座に OSC メッセージを送出する。OSC 送出（`IOscSender`）・ホスト管理（`HostRegistry`/`OscHost`）・設定永続化（`ProjectData`）は既存資産を再利用し、既存の MVVM 構成・ダークテーマ UI との一貫性を保つ。

## Requirements

### Requirement 1: メイン画面のタブ化
**Objective:** オペレーターとして、機能をタブで切り替えられるようにしたい。そうすることで画面を整理し、タイムコード操作とポン出し操作を目的に応じて素早く使い分けられる。

#### Acceptance Criteria
1. When アプリケーションを起動した時, the TimecodeBridge shall メインウィンドウ上部に「タイムコード」タブと「OSCポン出し」タブを持つタブ UI を表示する。
2. While 「タイムコード」タブを選択している時, the TimecodeBridge shall 既存の全機能（CueList・HostManager・RelayControl・TimecodeDisplay・AudioWaveform・Log）を従来と同等のレイアウト・操作性で表示する。
3. When ユーザーがタブを切り替えた時, the TimecodeBridge shall タイムコードの受信・リレー・キュー判定などのバックグラウンド処理を中断せず継続する。
4. The TimecodeBridge shall アプリ起動時に「タイムコード」タブを初期選択状態として表示する。

### Requirement 2: OSCポン出しグリッドの構成
**Objective:** オペレーターとして、ボタンを並べるグリッドの行数・列数を指定したい。そうすることで用途に応じたボタンレイアウトを作成できる。

#### Acceptance Criteria
1. The OSCポン出しパネル shall 行数と列数を指定して固定グリッドを構成できる。
2. When ユーザーが行数または列数を変更した時, the OSCポン出しパネル shall グリッドの表示を新しい行数×列数に更新する。
3. The OSCポン出しパネル shall グリッドの各セルに最大 1 つのトリガーボタンを割り当てる。
4. If 行数または列数に 1 未満の値が指定された場合, the OSCポン出しパネル shall その変更を拒否し、有効な最小値（1）を維持する。
5. When グリッドの行数または列数を縮小して既存ボタンが範囲外になる場合, the OSCポン出しパネル shall 範囲外になるボタン設定がある旨をユーザーに通知し、確認のうえで処理する。

### Requirement 3: ボタンの設定
**Objective:** オペレーターとして、各ボタンに送出内容を設定したい。そうすることで目的の OSC メッセージをワンタッチで送出できる。

#### Acceptance Criteria
1. When ユーザーがグリッド上のセル（またはボタン）の編集を要求した時, the OSCポン出しパネル shall 表示ラベル・OSCアドレス・OSC引数・送信先ホストを編集できる設定ダイアログを表示する。
2. The OSCポン出しパネル shall ボタンごとに任意の表示ラベルを設定できる。
3. The OSCポン出しパネル shall ボタンごとに OSC アドレスを設定できる。
4. The OSCポン出しパネル shall ボタンごとに int32 / float32 / string 型の OSC 引数を 0 個以上、順序を保って複数設定できる。
5. The OSCポン出しパネル shall ボタンごとに送信先ホストを既存ホスト一覧から複数選択できる。
6. If OSC アドレスが未入力または OSC アドレス書式として不正な場合, the OSCポン出しパネル shall 設定の保存を拒否し、エラー内容を表示する。
7. When ユーザーが既存ボタンの設定を変更して保存した時, the OSCポン出しパネル shall 変更内容を該当ボタンに反映する。

### Requirement 4: ボタン押下によるOSC送出
**Objective:** オペレーターとして、ボタンを押すと即座に OSC を送出したい。そうすることでタイムコードに依存せず任意のタイミングで手動トリガーできる。

#### Acceptance Criteria
1. When ユーザーが設定済みボタンを押下した時, the OSCポン出しパネル shall 設定された OSC アドレスと引数を、設定された送信先ホストへ即座に送出する。
2. While タイムコードが停止中・再生中のいずれの状態であっても, the OSCポン出しパネル shall ボタン押下による送出を実行する。
3. When ボタンによる OSC 送出を実行した時, the TimecodeBridge shall 送出結果（成功／失敗・宛先・OSCアドレス）をログに記録する。
4. If ボタンに送信先ホストが未設定、または選択された送信先がすべて無効な場合, the OSCポン出しパネル shall 送出を行わず、その旨をユーザーに通知する。
5. When ユーザーがボタンを押下した時, the OSCポン出しパネル shall 送出が行われたことが分かる視覚的フィードバックを表示する。
6. If 未設定（OSCアドレス未割り当て）のセルが押下された場合, the OSCポン出しパネル shall 送出を行わず、設定ダイアログを表示するか何もしない。

### Requirement 5: 設定の永続化
**Objective:** オペレーターとして、設定したグリッドとボタンをプロジェクトに保存したい。そうすることで次回起動時も同じ構成で利用できる。

#### Acceptance Criteria
1. When ユーザーがプロジェクトを保存した時, the TimecodeBridge shall グリッド構成（行数・列数）と全ボタン設定（ラベル・OSCアドレス・引数・送信先ホスト）をプロジェクトファイルに保存する。
2. When ユーザーがプロジェクトを読み込んだ時, the TimecodeBridge shall 保存されたグリッド構成とボタン設定を復元する。
3. The TimecodeBridge shall OSCポン出し設定を既存のプロジェクト JSON 形式（camelCase、`OscArgumentJsonConverter`）に統合して保存する。
4. When ユーザーが OSCポン出し設定（グリッドまたはボタン）を変更した時, the TimecodeBridge shall 未保存変更フラグを立てる。
5. If プロジェクトファイルに OSCポン出し設定が存在しない（旧バージョンのファイル）場合, the TimecodeBridge shall 既定値（空グリッドまたは既定行列数）で読み込みを継続する。

### Requirement 6: 既存資産との整合性と一貫性
**Objective:** 開発者・オペレーターとして、新機能が既存の仕組みと統一されていてほしい。そうすることで保守性と一貫した操作性を確保できる。

#### Acceptance Criteria
1. The OSCポン出しパネル shall OSC 送出に既存の `IOscSender.Send(oscAddress, arguments, targetHostIds)` を使用する。
2. The OSCポン出しパネル shall 送信先ホストの参照に既存の `HostRegistry` / `OscHost` を使用する。
3. The OSCポン出しパネル shall 既存のダークテーマ（`DarkTheme`）と UI スタイル（カード・アクセントカラー等）に準拠する。
4. The TimecodeBridge shall 新機能を既存の MVVM 構成（View + ViewModel、`CommunityToolkit.Mvvm`、DI 登録）に従って実装する。

### Requirement 7: 実行モードと編集モード
**Objective:** オペレーターとして、運用中の誤操作を防ぎたい。そうすることで本番中の誤編集・誤送出を避けられる。

#### Acceptance Criteria
1. The OSCポン出しパネル shall 「実行モード」と「編集モード」を切り替える手段を提供する。
2. While 実行モードのとき, the OSCポン出しパネル shall 設定済みボタンのクリックで OSC を送出する。
3. While 実行モードのとき, the OSCポン出しパネル shall グリッドの行数・列数の変更を受け付けない。
4. While 編集モードのとき, the OSCポン出しパネル shall セルのダブルクリックで編集ダイアログを開く。
5. When ユーザーが編集ダイアログで削除を実行した時, the OSCポン出しパネル shall 該当ボタンの設定を削除する。
