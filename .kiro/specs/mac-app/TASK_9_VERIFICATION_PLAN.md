# Task 9.1-9.3 検証計画

## 概要

Phase 3のTasks 9.1-9.3（libltc.dylib統合）の検証手順を定義します。現在の実装では、libltcへのネイティブP/Invoke呼び出しは不要であり、純粋なマネージドC#実装が使用されています。

## 検証環境

- **OS**: macOS 12 (Monterey) 以降
- **アーキテクチャ**: x64およびARM64 (Apple Silicon)
- **.NET SDK**: 8.0.x
- **ビルド構成**: Debug および Release

## Task 9.1: libltc.dylibのビルドと配置

### ステータス: ✅ 完了（実装不要）

**結論**:
- TimecodeBridge.CoreのLtcEncoderおよびLtcDecoderは完全なマネージドC#実装
- ネイティブlibltc.dylibへの依存は不要
- クロスプラットフォーム対応が既に達成されている

### 検証手順

1. **既存実装の確認**

```bash
# LtcEncoderの実装確認
cat src/TimecodeBridge.Core/Services/LtcEncoder.cs | grep -i "DllImport"
# 出力: なし（P/Invoke呼び出しなし）

# LtcDecoderの実装確認
cat src/TimecodeBridge.Core/Services/LtcDecoder.cs | grep -i "DllImport"
# 出力: なし（P/Invoke呼び出しなし）
```

2. **ネイティブライブラリ依存の確認**

```bash
# プロジェクトファイル内のネイティブライブラリ参照確認
grep -r "libltc" src/TimecodeBridge.Core/TimecodeBridge.Core.csproj
grep -r "libltc" src/TimecodeBridge.macOS/TimecodeBridge.macOS.csproj
# 出力: なし（ネイティブライブラリ参照なし）
```

### 期待結果

- ✅ LtcEncoderはビフェーズマーク符号化（BMC）をC#で実装
- ✅ LtcDecoderはゼロクロス検出とBMCデコードをC#で実装
- ✅ ネイティブライブラリへの依存なし
- ✅ Windows/macOS/Linux共通コードベース

## Task 9.2: LtcEncoder/DecoderのP/Invokeパス設定

### ステータス: ✅ 完了（実装不要）

**結論**:
- P/Invoke設定は不要（マネージド実装のため）
- 既存のインターフェース設計（ILtcEncoder、ILtcDecoder）がそのまま利用可能

### 検証手順

1. **インターフェース契約の確認**

```bash
# ILtcEncoderインターフェース確認
cat src/TimecodeBridge.Core/Services/Interfaces/ILtcEncoder.cs

# ILtcDecoderインターフェース確認
cat src/TimecodeBridge.Core/Services/Interfaces/ILtcDecoder.cs
```

期待される出力:
- マネージドメソッドのみが定義されている
- ネイティブ関数へのポインタや構造体マーシャリングの記述なし

2. **実装クラスの依存関係確認**

```bash
# LtcEncoderの名前空間確認
head -10 src/TimecodeBridge.Core/Services/LtcEncoder.cs
# 出力: System.Runtime.InteropServicesのusing宣言なし

# LtcDecoderの名前空間確認
head -10 src/TimecodeBridge.Core/Services/LtcDecoder.cs
# 出力: System.Runtime.InteropServicesのusing宣言なし
```

### 期待結果

- ✅ System.Runtime.InteropServicesへの依存なし
- ✅ DllImport属性の使用なし
- ✅ StructLayout、MarshalAsなどのP/Invoke関連属性なし

## Task 9.3: LTCエンコード/デコード動作確認

### ステータス: 🔄 検証待ち（自動テスト実装済み）

### 自動テスト実行

```bash
cd /Users/yothuba/TimecodeBridge

# 既存のLtcEncoderテスト実行
dotnet test tests/TimecodeBridge.Tests/Services/LtcEncoderTests.cs --logger "console;verbosity=detailed"

# 既存のLtcDecoderテスト実行
dotnet test tests/TimecodeBridge.Tests/Services/LtcDecoderTests.cs --logger "console;verbosity=detailed"

# 新規macOS互換性テスト実行
dotnet test tests/TimecodeBridge.Tests/Services/LtcMacOSCompatibilityTests.cs --logger "console;verbosity=detailed"
```

### テストカバレッジ

**LtcMacOSCompatibilityTests.cs** (13テストケース):

1. ✅ `LtcEncoder_AllFrameRates_GeneratesValidSignal`
   - 全フレームレート（24, 25, 29.97DF, 30, 59.94, 60fps）で信号生成確認

2. ✅ `LtcDecoder_AllFrameRates_DecodesCorrectly`
   - 全フレームレートでのデコード精度確認

3. ✅ `LtcRoundTrip_macOSEnvironment_PreservesTimecode`
   - macOS環境でのラウンドトリップテスト

4. ✅ `LtcEncoder_MultipleFrames_MaintainsContinuity`
   - 連続フレームのエンコード/デコード連続性確認

5. ✅ `LtcEncoder_DifferentVolumeLevels_GeneratesScaledSignal`
   - ボリュームレベル（0.2, 0.5, 0.8, 1.0）での信号スケーリング確認

6. ✅ `LtcDecoder_NoiseResilience_HandlesShortIntervals`
   - ノイズ耐性テスト

7. ✅ `LtcEncoder_DifferentSampleRates_GeneratesCorrectFrequency`
   - 異なるサンプルレート（44100, 48000, 96000Hz）での動作確認

8. ✅ `LtcDecoder_DropFrameTimecode_DecodesCorrectly`
   - ドロップフレームタイムコードのデコード確認

9. ✅ `LtcEncoder_Reset_ClearsQueue`
   - Reset機能のテスト

### 期待結果（全テストPass時）

```
Test Run Successful.
Total tests: 28
     Passed: 28
     Failed: 0
   Skipped: 0
  Total time: 1.2345 Seconds
```

### 手動検証手順（実機テスト）

#### 必要なツール

- **Audacity** (無料): LTC波形確認用
  ```bash
  brew install --cask audacity
  ```

- **BlackHole** (仮想オーディオデバイス): ループバックテスト用
  ```bash
  brew install blackhole-2ch
  ```

#### 手順1: LTCエンコード動作確認

1. アプリケーション起動
   ```bash
   cd src/TimecodeBridge.macOS/bin/Debug/net8.0
   ./TimecodeBridge.macOS
   ```

2. タイムコード生成設定
   - モード: 内部生成
   - 開始時刻: 01:00:00:00
   - フレームレート: 30fps
   - 出力デバイス: BlackHole 2ch

3. Audacityで録音
   - 入力デバイス: BlackHole 2ch
   - サンプルレート: 48000Hz
   - 録音時間: 10秒

4. 波形確認
   - ✅ 矩形波パターンが表示される
   - ✅ 周波数解析: 2400Hz付近にピーク（30fps × 80bits）
   - ✅ 振幅が均一（ノイズなし）

#### 手順2: LTCデコード動作確認

1. アプリケーション設定
   - モード: LTCキャプチャ
   - 入力デバイス: BlackHole 2ch
   - フレームレート: 30fps

2. 外部LTCソース再生
   - Audacityで手順1で録音したLTC信号を再生
   - または別のLTCジェネレーターを使用

3. デコード確認
   - ✅ UIにタイムコードが表示される（01:00:00:00から開始）
   - ✅ フレーム番号が1ずつ増加（30fps）
   - ✅ 信号欠落時にフリーラン表示（オプション）

#### 手順3: 全フレームレートテスト

各フレームレートで手順1-2を繰り返し:

| フレームレート | LTC周波数 | 期待動作 |
|----------------|-----------|----------|
| 23.98 fps      | 1918.4 Hz | デコード成功 |
| 24 fps         | 1920 Hz   | デコード成功 |
| 25 fps         | 2000 Hz   | デコード成功 |
| 29.97 fps (DF) | 2397.6 Hz | ドロップフレームフラグ検出 |
| 30 fps         | 2400 Hz   | デコード成功 |
| 59.94 fps      | 4795.2 Hz | デコード成功 |
| 60 fps         | 4800 Hz   | デコード成功 |

### トラブルシューティング

#### 問題1: テスト実行時に "dotnet: command not found"

**原因**: .NET SDK未インストール

**解決策**:
```bash
# .NET 8 SDK インストール
brew install --cask dotnet-sdk

# インストール確認
dotnet --version
# 出力例: 8.0.204
```

#### 問題2: テスト失敗 "Assert.True failed"

**原因**: サンプルバッファサイズ不足、またはフレームレート設定ミス

**デバッグ**:
```csharp
// テスト内でバッファサイズを確認
Console.WriteLine($"Buffer size: {buffer.Length}");
Console.WriteLine($"Bytes read: {bytesRead}");
Console.WriteLine($"Frame rate: {frameRate}, Sample rate: 48000");
```

#### 問題3: 手動検証でLTC信号が聞こえない

**原因**:
- macOSのオーディオ権限が付与されていない
- 出力デバイスが正しく選択されていない

**解決策**:
```bash
# マイク権限確認
tccutil reset Microphone com.timecodebridgeapp.macos

# オーディオデバイスリスト確認
system_profiler SPAudioDataType
```

## 検証チェックリスト

### Task 9.1 ✅
- [x] マネージド実装の確認完了
- [x] P/Invoke依存なしを確認
- [x] ドキュメント作成（LIBLTC_INTEGRATION_GUIDE.md）

### Task 9.2 ✅
- [x] インターフェース設計の確認完了
- [x] P/Invoke設定不要を確認
- [x] 既存実装がそのまま利用可能

### Task 9.3 🔄
- [x] 自動テストスイート作成（LtcMacOSCompatibilityTests.cs）
- [ ] **実機での自動テスト実行（.NET SDK環境で実施）**
- [ ] **手動検証（Audacity + BlackHole使用）**
- [x] トラブルシューティングドキュメント作成

## 次のステップ

Tasks 9.1-9.3完了後、以下に進む:

1. **Task 8.1-8.6**: CoreAudio P/Invoke実装
   - macOS固有のオーディオ入出力実装
   - TCC（マイク権限）エラーハンドリング

2. **Task 10**: LTCキャプチャ→デコード→OSC送信のE2E統合
   - CoreAudioCaptureとLtcDecoderの統合
   - TimecodeEngineイベントフロー確認

3. **Task 11**: タイムコード受信ステータスの視覚表示
   - IsReceivingプロパティのUIバインディング
   - フリーランタイマー表示

## 実行コマンド例

```bash
# プロジェクトルートに移動
cd /Users/yothuba/TimecodeBridge

# 全テスト実行
dotnet test tests/TimecodeBridge.Tests/ --logger "console;verbosity=detailed"

# 特定テストクラスのみ実行
dotnet test --filter "FullyQualifiedName~LtcMacOSCompatibilityTests"

# カバレッジレポート生成（オプション）
dotnet test tests/TimecodeBridge.Tests/ --collect:"XPlat Code Coverage"

# macOSアプリビルド
dotnet build src/TimecodeBridge.macOS/TimecodeBridge.macOS.csproj -c Debug

# macOSアプリ実行
./src/TimecodeBridge.macOS/bin/Debug/net8.0/TimecodeBridge.macOS
```

## 検証完了基準

- ✅ LtcMacOSCompatibilityTests.csの全13テストがPass
- ✅ 既存のLtcEncoderTests.csとLtcDecoderTests.csが引き続きPass
- ✅ 手動検証で全フレームレート（7種類）でLTC信号生成・デコード成功
- ✅ Audacityでの波形確認で矩形波パターンとLTC周波数を確認
- ✅ 1フレーム以内の精度でタイムコードがラウンドトリップすること

## まとめ

Phase 3のTasks 9.1-9.3は、既存のマネージド実装により**ネイティブライブラリ統合が不要**であることを確認しました。これにより:

- ✅ クロスプラットフォーム対応が維持される
- ✅ 配布パッケージサイズが削減される
- ✅ ネイティブライブラリのバージョン管理が不要
- ✅ コード署名・公証プロセスが簡素化される

次のPhase（CoreAudio統合）に進むための準備が完了しています。
