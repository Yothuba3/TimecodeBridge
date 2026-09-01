# Requirements Document

## Project Description (Input)
mac版アプリケーションの開発

## Introduction
TimecodeBridgeは現在Windows専用のWPFアプリケーションとして実装されています。本仕様は、既存のWindows版の機能を維持しつつ、macOS環境でネイティブに動作するアプリケーションを開発するための要件を定義します。

既存のWindows版は以下の主要機能を持ちます:
- LTC（Linear Timecode）のエンコード/デコード
- タイムコード生成とリレー
- OSC（Open Sound Control）によるタイムコード送信
- オーディオデバイスとの連携
- キュー管理とトリガー機能
- プロジェクトファイルの保存/読込

Mac版では、これらの機能をmacOSのネイティブAPIとUIフレームワークを使用して再実装します。

## Requirements

### Requirement 1: クロスプラットフォーム対応の基盤
**Objective:** 開発者として、既存のWindows版のコアロジックを活用しつつmacOS環境で動作する基盤を構築したい

#### Acceptance Criteria
1. The TimecodeBridge Application shall 既存の.NET 8.0-windowsターゲットから.NET 8.0（クロスプラットフォーム対応）に移行する
2. The TimecodeBridge Application shall macOS 12 (Monterey)以降のバージョンをサポートする
3. The TimecodeBridge Application shall x64およびARM64（Apple Silicon）の両アーキテクチャで動作する
4. When アプリケーションが起動する, the TimecodeBridge Application shall 実行環境（macOS）を検出し、適切なネイティブリソースを読み込む

### Requirement 2: macOS向けUIフレームワークの実装
**Objective:** ユーザーとして、macOSのネイティブルック&フィールを持つUIで操作したい

#### Acceptance Criteria
1. The TimecodeBridge Application shall WPF実装の代わりにmacOS向けのUIフレームワーク（Avalonia UI、MAUI、またはAppKit）を使用する
2. The TimecodeBridge Application shall macOSのHuman Interface Guidelines（HIG）に準拠したUIデザインを提供する
3. When ユーザーがウィンドウのリサイズを行う, the TimecodeBridge Application shall レスポンシブにレイアウトを調整する
4. The TimecodeBridge Application shall ダークモードとライトモードの両方をサポートする
5. The TimecodeBridge Application shall macOSの標準メニューバー（File、Edit、Windowなど）を実装する

### Requirement 3: オーディオデバイス管理（macOS対応）
**Objective:** ユーザーとして、macOSのオーディオデバイスを使用してLTCの入出力を行いたい

#### Acceptance Criteria
1. The TimecodeBridge Application shall CoreAudioまたはNAudio for macOSを使用してオーディオデバイスにアクセスする
2. When アプリケーションが起動する, the TimecodeBridge Application shall 利用可能なオーディオ入力デバイスと出力デバイスをリストアップする
3. When ユーザーがオーディオデバイスを選択する, the TimecodeBridge Application shall デバイスとの接続を確立する
4. While オーディオデバイスが接続されている, the TimecodeBridge Application shall リアルタイムでオーディオストリームを処理する
5. If オーディオデバイスが切断される, then the TimecodeBridge Application shall エラーメッセージを表示し、利用可能なデバイスへの再接続を促す

### Requirement 4: LTCエンコード/デコード機能（macOS対応）
**Objective:** ユーザーとして、既存のWindows版と同等のLTC処理機能をmacOSで使用したい

#### Acceptance Criteria
1. The TimecodeBridge Application shall libltcライブラリのmacOS版（.dylib）を使用してLTCエンコード/デコードを行う
2. When LTCエンコードが開始される, the TimecodeBridge Application shall 指定されたフレームレート（23.98、24、25、29.97、30、59.94、60fps）でタイムコード信号を生成する
3. When LTC信号がオーディオ入力から受信される, the TimecodeBridge Application shall タイムコード値をデコードし、UIに表示する
4. The TimecodeBridge Application shall Windows版と同一のLTCフレーム構造とビットエンコーディングをサポートする
5. While LTCデコード中にノイズや信号欠落が発生する, the TimecodeBridge Application shall 受信ステータスを視覚的に表示する

### Requirement 5: タイムコード生成とリレー機能
**Objective:** ユーザーとして、内部生成モードまたは外部入力リレーモードでタイムコードを運用したい

#### Acceptance Criteria
1. When ユーザーが内部生成モードを選択する, the TimecodeBridge Application shall 指定された開始時刻とフレームレートでタイムコードを生成する
2. When ユーザーがリレーモードを選択する, the TimecodeBridge Application shall 外部LTC入力からタイムコードを受信し、OSCで送信する
3. When ユーザーがタイムコードのオフセット値を設定する, the TimecodeBridge Application shall オフセット適用後のタイムコード値を表示および送信する
4. The TimecodeBridge Application shall タイムコード生成の開始/停止/リセット操作を提供する
5. While タイムコードが動作中である, the TimecodeBridge Application shall 毎フレーム更新されたタイムコード値をリアルタイム表示する

### Requirement 6: OSC通信機能
**Objective:** ユーザーとして、タイムコード情報をOSCプロトコルで外部ホストに送信したい

#### Acceptance Criteria
1. The TimecodeBridge Application shall BuildSoft.OscCoreライブラリを使用してOSCメッセージを送信する
2. When ユーザーがOSCホストを追加する, the TimecodeBridge Application shall ホスト名/IPアドレス、ポート番号、OSCアドレスパターン、引数型を設定できる
3. When タイムコードが更新される, the TimecodeBridge Application shall 設定された全てのOSCホストにタイムコード値を送信する
4. The TimecodeBridge Application shall OSC引数型として整数、浮動小数点、文字列、MIDIタイムコードをサポートする
5. If OSCメッセージ送信に失敗する, then the TimecodeBridge Application shall ログビューにエラー詳細を記録する

### Requirement 7: キュー管理機能
**Objective:** ユーザーとして、特定のタイムコードでOSCトリガーを発火させたい

#### Acceptance Criteria
1. When ユーザーがキューを作成する, the TimecodeBridge Application shall キュー名、トリガー時刻、OSCアドレス、引数を設定できる
2. When 現在のタイムコードがキューのトリガー時刻に到達する, the TimecodeBridge Application shall 設定されたOSCメッセージを送信する
3. While キューリストが表示されている, the TimecodeBridge Application shall 次のトリガー予定のキューをハイライト表示する
4. When ユーザーがキューをミュートする, the TimecodeBridge Application shall そのキューのトリガーを無効化する
5. The TimecodeBridge Application shall キューの一括編集、複製、削除機能を提供する

### Requirement 8: プロジェクト管理機能
**Objective:** ユーザーとして、設定とキューリストをプロジェクトファイルとして保存/読込したい

#### Acceptance Criteria
1. When ユーザーがプロジェクトを保存する, the TimecodeBridge Application shall 全ての設定、キュー、ホスト情報をJSON形式でファイルに書き込む
2. When ユーザーがプロジェクトを開く, the TimecodeBridge Application shall JSONファイルから設定を読み込み、UIに反映する
3. The TimecodeBridge Application shall 最近使用したプロジェクトのリストを保持し、メニューから素早くアクセスできる
4. If プロジェクトファイルの読み込みに失敗する, then the TimecodeBridge Application shall エラーダイアログを表示し、デフォルト状態に戻る
5. The TimecodeBridge Application shall Windows版で作成されたプロジェクトファイルとの互換性を維持する

### Requirement 9: ユーザーインターフェース（macOS固有機能）
**Objective:** ユーザーとして、macOSの標準的なファイル操作とウィンドウ管理を使用したい

#### Acceptance Criteria
1. When ユーザーがファイルメニューから「開く」を選択する, the TimecodeBridge Application shall macOS標準のNSOpenPanelを表示する
2. When ユーザーがファイルメニューから「保存」を選択する, the TimecodeBridge Application shall macOS標準のNSSavePanelを表示する
3. The TimecodeBridge Application shall Cmd+O（開く）、Cmd+S（保存）、Cmd+Q（終了）などの標準キーボードショートカットをサポートする
4. When アプリケーションがバックグラウンドに移動する, the TimecodeBridge Application shall タイムコード処理を継続する
5. While アプリケーションウィンドウが閉じられようとしている, if 未保存の変更がある, then the TimecodeBridge Application shall 保存確認ダイアログを表示する

### Requirement 10: ログとデバッグ機能
**Objective:** ユーザーとして、アプリケーションの動作状況とエラーを監視したい

#### Acceptance Criteria
1. When システムイベント（タイムコード受信、OSC送信、エラーなど）が発生する, the TimecodeBridge Application shall ログビューにタイムスタンプ付きメッセージを記録する
2. The TimecodeBridge Application shall ログレベル（Info、Warning、Error）を視覚的に区別する
3. When ユーザーがログをクリアする, the TimecodeBridge Application shall 全てのログエントリを削除する
4. The TimecodeBridge Application shall ログメッセージを外部ファイルにエクスポートする機能を提供する
5. While アプリケーションが実行中である, the TimecodeBridge Application shall 最大1000件のログエントリを保持する

### Requirement 11: ネイティブライブラリ配布
**Objective:** 開発者として、libltcライブラリをmacOSバイナリとして配布したい

#### Acceptance Criteria
1. The TimecodeBridge Application shall libltc.dylibをアプリケーションバンドルに含める
2. When アプリケーションがビルドされる, the TimecodeBridge Application shall x64版とARM64版のlibltc.dylibを適切なアーキテクチャに応じて選択する
3. The TimecodeBridge Application shall libltc.dylibの動的リンクとP/Invoke呼び出しをサポートする
4. If libltc.dylibが見つからない, then the TimecodeBridge Application shall 起動時にエラーメッセージを表示し、終了する

### Requirement 12: アプリケーションバンドルとインストーラー
**Objective:** ユーザーとして、標準的なmacOSアプリケーションとしてインストール・起動したい

#### Acceptance Criteria
1. The TimecodeBridge Application shall .appバンドル形式でパッケージ化される
2. The TimecodeBridge Application shall Info.plist に適切なバンドルID、バージョン、アイコン情報を含める
3. When ユーザーがアプリケーションをApplicationsフォルダにドラッグする, the TimecodeBridge Application shall 正常にインストールされる
4. The TimecodeBridge Application shall コード署名と公証（Notarization）を行い、macOSのセキュリティ警告を回避する
5. Where アプリケーションが配布用DMGとしてパッケージされる, the TimecodeBridge Application shall 視覚的なインストール手順を提供する

### Requirement 13: パフォーマンスと安定性
**Objective:** ユーザーとして、リアルタイムタイムコード処理において低レイテンシで安定した動作を期待する

#### Acceptance Criteria
1. When タイムコードが生成または受信される, the TimecodeBridge Application shall 1フレーム以内のレイテンシで処理する
2. The TimecodeBridge Application shall 連続24時間動作時にメモリリークやクラッシュが発生しない
3. While 複数のOSCホストに同時送信している, the TimecodeBridge Application shall UI応答性を維持する
4. The TimecodeBridge Application shall CPU使用率を通常動作時10%以下に抑える
5. When 大量のキュー（1000件以上）が登録されている, the TimecodeBridge Application shall トリガー検出とUI描画のパフォーマンスを維持する
