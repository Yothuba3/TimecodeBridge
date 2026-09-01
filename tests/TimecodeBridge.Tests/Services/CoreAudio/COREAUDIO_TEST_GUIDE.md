# CoreAudio実装テストガイド

## 概要

このドキュメントでは、TimecodeBridge macOS版のCoreAudio実装に対するテスト戦略と、自動テストと手動テストの区分について説明します。

## テスト構成

### 自動テスト（CI/CD環境で実行可能）

以下のテストはmacOSハードウェアがなくても実行でき、CI/CD環境で自動実行できます。

#### 1. P/Invoke署名テスト (`CoreAudioInteropTests.cs`)

**目的**: CoreAudio P/Invoke署名とネイティブ構造体の正確性を検証

**テストケース**:
- `AudioComponentDescription_ShouldHaveCorrectStructureSize`: 構造体サイズと定数値の検証
- `AudioStreamBasicDescription_ShouldHaveCorrectStructureForLTC`: LTC標準フォーマット（48kHz Mono 16bit）の検証
- `AudioTimeStamp_ShouldHaveCorrectStructure`: タイムスタンプ構造体の検証
- `AudioBufferList_ShouldHaveCorrectStructure`: バッファリスト構造体の検証
- `CoreAudioConstants_ShouldHaveValidValues`: 定数値の検証

**実行方法**:
```bash
dotnet test --filter "FullyQualifiedName~CoreAudioInteropTests"
```

#### 2. CoreAudioCaptureユニットテスト (`CoreAudioCaptureTests.cs`)

**目的**: CoreAudioCaptureクラスのAPI契約と基本動作を検証

**テストケース**:
- `Constructor_ShouldInitializeSuccessfully`: コンストラクタの正常動作
- `Start_WithNullDevice_ShouldThrowArgumentNullException`: null引数検証
- `Stop_WithoutStart_ShouldNotThrow`: 開始前の停止の安全性
- `Dispose_ShouldCleanupResources`: Disposeパターンの実装
- `AudioSamplesAvailable_ShouldBeSubscribable`: イベント購読の検証
- `ErrorOccurred_ShouldBeSubscribable`: エラーイベント購読の検証
- `Start_AfterDispose_ShouldThrowObjectDisposedException`: Dispose後の操作禁止
- `Stop_AfterDispose_ShouldThrowObjectDisposedException`: Dispose後の操作禁止

**実行方法**:
```bash
dotnet test --filter "FullyQualifiedName~CoreAudioCaptureTests"
```

#### 3. CoreAudioPlaybackユニットテスト (`CoreAudioPlaybackTests.cs`)

**目的**: CoreAudioPlaybackクラスのAPI契約と基本動作を検証

**テストケース**:
- `Constructor_ShouldInitializeSuccessfully`: コンストラクタの正常動作
- `Start_WithNullDevice_ShouldThrowArgumentNullException`: null引数検証
- `WriteSamples_WithNullBuffer_ShouldThrowArgumentNullException`: null引数検証
- `WriteSamples_WithInvalidOffset_ShouldThrowArgumentOutOfRangeException`: 範囲外引数検証
- `WriteSamples_WithInvalidCount_ShouldThrowArgumentOutOfRangeException`: 範囲外引数検証
- Dispose後の操作禁止検証

**実行方法**:
```bash
dotnet test --filter "FullyQualifiedName~CoreAudioPlaybackTests"
```

#### 4. CoreAudioDeviceServiceテスト (`CoreAudioDeviceServiceTests.cs`)

**目的**: デバイスサービスの基本動作を検証

**テストケース**:
- `Constructor_ShouldInitializeSuccessfully`: コンストラクタの正常動作
- `GetCaptureDevices_ShouldReturnList`: デバイスリスト取得の安全性
- `GetRenderDevices_ShouldReturnList`: デバイスリスト取得の安全性
- `GetCaptureDevices_MultipleCallsShouldSucceed`: 複数回呼び出しの安全性

**実行方法**:
```bash
dotnet test --filter "FullyQualifiedName~CoreAudioDeviceServiceTests"
```

#### 5. 統合テスト（基本動作） (`CoreAudioIntegrationTests.cs`)

**目的**: コンポーネント間の統合とライフサイクル管理を検証

**テストケース**:
- `DeviceService_GetCaptureDevices_ShouldReturnValidList`: デバイス列挙の検証
- `DeviceService_GetRenderDevices_ShouldReturnValidList`: デバイス列挙の検証
- `Capture_ShouldImplementIAudioCapture`: インターフェース実装の検証
- `Playback_ShouldImplementIAudioPlayback`: インターフェース実装の検証
- `Capture_LifecycleTest_ShouldNotThrow`: ライフサイクル管理の検証
- `Playback_LifecycleTest_ShouldNotThrow`: ライフサイクル管理の検証
- `Capture_MultipleStartStop_ShouldNotThrow`: 複数回Start/Stopの安全性
- `Playback_MultipleStartStop_ShouldNotThrow`: 複数回Start/Stopの安全性

**実行方法**:
```bash
dotnet test --filter "FullyQualifiedName~CoreAudioIntegrationTests"
```

**全自動テストの実行**:
```bash
dotnet test --filter "FullyQualifiedName~CoreAudio" --filter "Skip!=Requires"
```

---

### 手動テスト（macOS実機が必要）

以下のテストはmacOSハードウェアとCoreAudio APIへのアクセスが必要です。これらは手動で実施します。

#### 1. TCC権限エラーハンドリングテスト

**目的**: マイク権限未付与時の適切なエラー処理を検証

**前提条件**:
- macOS 12+ が動作する実機
- TimecodeBridgeアプリのマイク権限が拒否されている状態

**手順**:
1. システム設定 > プライバシーとセキュリティ > マイクでTimecodeBridgeの権限を拒否
2. TimecodeBridgeアプリを起動
3. オーディオキャプチャを開始

**期待される結果**:
- `UnauthorizedAccessException` がスローされる
- エラーメッセージに「マイク権限が必要です」と表示される
- エラーダイアログにシステム設定へのガイダンスが表示される

**対応テストケース**: `Capture_WithoutTCCPermission_ShouldThrowUnauthorizedAccessException` (skip)

#### 2. 実デバイスでのキャプチャテスト

**目的**: 実際のオーディオデバイスからのサンプル取得を検証

**前提条件**:
- macOS 12+ が動作する実機
- マイク権限が付与されている
- 動作するオーディオ入力デバイス

**手順**:
1. オーディオ入力デバイスを接続（またはビルトインマイクを使用）
2. TimecodeBridgeアプリを起動
3. デバイスリストから入力デバイスを選択
4. キャプチャを開始
5. マイクに向かって音を出す

**期待される結果**:
- `AudioSamplesAvailable` イベントが定期的に発火する
- サンプルデータが48kHz、モノラル、16bitフォーマットで取得される
- サンプル値が正常な範囲（-1.0 ~ +1.0）に収まる

**対応テストケース**: `Capture_Start_WithValidDevice_ShouldReceiveSamples` (skip)

#### 3. 実デバイスでのプレイバックテスト

**目的**: 実際のオーディオデバイスへのサンプル出力を検証

**前提条件**:
- macOS 12+ が動作する実機
- 動作するオーディオ出力デバイス

**手順**:
1. オーディオ出力デバイスを接続（またはビルトインスピーカーを使用）
2. TimecodeBridgeアプリを起動
3. デバイスリストから出力デバイスを選択
4. プレイバックを開始
5. テストトーン（1kHz正弦波）を生成して出力

**期待される結果**:
- スピーカーから1kHzトーンが聞こえる
- 音飛びやノイズがない
- 停止後に音が停止する

**対応テストケース**: `Playback_Start_WithValidDevice_ShouldOutputSamples` (skip)

#### 4. 30秒連続キャプチャテスト

**目的**: Phase 2技術検証成果物基準の達成確認

**前提条件**:
- macOS 12+ が動作する実機
- マイク権限が付与されている
- 動作するオーディオ入力デバイス

**手順**:
1. オーディオ入力デバイスを接続
2. TimecodeBridgeアプリを起動
3. キャプチャを開始
4. 30秒間動作させる
5. キャプチャを停止

**期待される結果**:
- 30秒間エラーなく動作する
- メモリリークが発生しない
- CPU使用率が過度に上昇しない（<10%目標）
- サンプル取得が連続して成功する

**対応テストケース**: `Integration_CaptureToPlayback_30SecondsContinuous` (skip)

#### 5. デバイス列挙テスト

**目的**: システムに存在する全オーディオデバイスの正確な列挙を検証

**前提条件**:
- macOS 12+ が動作する実機
- 複数のオーディオデバイス（推奨）

**手順**:
1. TimecodeBridgeアプリを起動
2. デバイスリストを表示
3. システム設定 > サウンドのデバイスリストと比較

**期待される結果**:
- 入力デバイスがすべて列挙される
- 出力デバイスがすべて列挙される
- デバイス名が正確に表示される
- ループバックデバイスが適切にマークされる（存在する場合）

**対応テストケース**:
- `GetCaptureDevices_OnMacOS_ShouldReturnActualDevices` (skip)
- `GetRenderDevices_OnMacOS_ShouldReturnActualDevices` (skip)

#### 6. デバイス切断検出テスト

**目的**: オーディオデバイスが切断された際の適切なエラーハンドリングを検証

**前提条件**:
- macOS 12+ が動作する実機
- 取り外し可能なUSBオーディオデバイス

**手順**:
1. USBオーディオデバイスを接続
2. TimecodeBridgeアプリを起動
3. USBデバイスを選択してキャプチャを開始
4. キャプチャ中にUSBデバイスを物理的に取り外す

**期待される結果**:
- `ErrorOccurred` イベントが発火する
- エラーメッセージに「デバイスが切断されました」と表示される
- アプリケーションがクラッシュしない
- 再接続後にデバイスリストが更新される

---

## テストカバレッジ目標

| カテゴリ | 自動テスト | 手動テスト | 合計 |
|---------|-----------|-----------|------|
| P/Invoke署名 | 100% | - | 100% |
| API契約 | 100% | - | 100% |
| 基本動作 | 80% | 20% | 100% |
| エラーハンドリング | 60% | 40% | 100% |
| 統合シナリオ | 40% | 60% | 100% |
| **全体** | **70%** | **30%** | **100%** |

---

## 自動テストと手動テストの区分基準

### 自動テスト対象

- API契約の検証（null引数、範囲外引数、Dispose後の操作など）
- 構造体とP/Invoke署名の検証
- インターフェース実装の検証
- ライフサイクル管理の検証
- 例外スローの検証
- エラーハンドリングロジックの検証（モック使用）

### 手動テスト対象

- 実際のCoreAudio APIとの通信
- 実際のオーディオデバイスとの連携
- TCC権限ダイアログの表示
- 実際のオーディオサンプル入出力
- デバイス切断/再接続の動作
- 長時間動作の安定性
- パフォーマンス（CPU使用率、メモリ使用量）

---

## CI/CD環境での実行

### GitHub Actions ワークフロー例

```yaml
name: CoreAudio Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest  # macOS環境でなくても実行可能
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Run CoreAudio Automated Tests
        run: dotnet test --no-build --filter "FullyQualifiedName~CoreAudio" --filter "Skip!=Requires"
```

---

## 手動テスト実施チェックリスト

Phase 3完了前に以下の手動テストをすべて実施し、結果を記録してください。

- [ ] TCC権限エラーハンドリングテスト
- [ ] 実デバイスでのキャプチャテスト
- [ ] 実デバイスでのプレイバックテスト
- [ ] 30秒連続キャプチャテスト（Phase 2技術検証基準）
- [ ] デバイス列挙テスト
- [ ] デバイス切断検出テスト

---

## トラブルシューティング

### よくある問題

#### 1. `DllNotFoundException: /System/Library/Frameworks/CoreAudio.framework/CoreAudio`

**原因**: macOS環境でない、またはCoreAudio Frameworkが見つからない

**解決策**: macOS実機でテストを実行する

#### 2. `UnauthorizedAccessException: Audio permission denied (TCC)`

**原因**: マイク権限が付与されていない

**解決策**: システム設定 > プライバシーとセキュリティ > マイクでTimecodeBridgeを許可

#### 3. `InvalidOperationException: Failed to find HAL Output audio component`

**原因**: CoreAudio APIの初期化に失敗

**解決策**:
- macOS 12以降であることを確認
- システムオーディオ設定を確認
- アプリケーションを再起動

---

## まとめ

CoreAudio実装は以下の比率でテストされています:

- **自動テスト**: 70%（CI/CD環境で実行可能）
- **手動テスト**: 30%（macOS実機が必要）

自動テストでAPI契約と基本動作を保証し、手動テストで実際のハードウェアとの統合を検証します。Phase 3完了前に手動テストチェックリストをすべて実施してください。
