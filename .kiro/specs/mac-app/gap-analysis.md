# Implementation Gap Analysis: mac-app

## 分析サマリー

### スコープ
既存のWindows/WPFベースのTimecodeBridgeアプリケーションをmacOSに移植する。コアロジック（タイムコード処理、OSC通信、キュー管理、プロジェクト管理）は再利用可能だが、UI層とプラットフォーム固有APIは全面的な再実装が必要。

### 主要課題
- **UIフレームワーク完全移行**: WPF → Avalonia/MAUI/AppKitの選択と実装（Large影響範囲）
- **オーディオAPIポーティング**: NAudio (Windows) → CoreAudioまたはNAudio macOS対応の検証が必要
- **ネイティブライブラリビルド**: libltc.dll → libltc.dylibのビルド・配布戦略
- **アプリバンドリング**: .appバンドル、コード署名、公証プロセスの実装

### 推奨戦略
**Hybrid Approach（ハイブリッドアプローチ）**を推奨:
- **Phase 1**: コアロジック（Models、Services）を抽出し、プラットフォーム非依存ライブラリ化
- **Phase 2**: UI層をAvaloniaで完全再実装（既存XAMLからの部分移植）
- **Phase 3**: macOS固有機能（CoreAudio、.appバンドル、公証）を段階的に追加

---

## 1. Current State Investigation

### 1.1 アーキテクチャ概要

既存のWindowsアプリケーションは以下の構造:

```
TimecodeBridge/
├── Models/               # データモデル（プラットフォーム非依存）
│   ├── ProjectData.cs   # JSON永続化対応
│   ├── Cue.cs, OscHost.cs, TimecodeValue.cs, etc.
├── Services/            # ビジネスロジック（大部分プラットフォーム非依存）
│   ├── TimecodeEngine.cs      # タイムコード処理の中核
│   ├── LtcEncoder/Decoder.cs  # libltc P/Invoke
│   ├── AudioDeviceService.cs  # NAudio依存（要移植）
│   ├── OscSender.cs, CueManager.cs, ProjectService.cs
│   └── Interfaces/           # 16個のインターフェース
├── ViewModels/          # MVVM ViewModels（CommunityToolkit.Mvvm使用）
│   ├── MainViewModel.cs
│   ├── TimecodeViewModel.cs, CueListViewModel.cs, etc.
├── Views/               # WPF XAML（完全再実装必要）
│   ├── MainWindow.xaml
│   ├── CueListView.xaml, TimecodeDisplayView.xaml, etc.
├── Native/
│   └── libltc.dll       # Windows DLL（macOSでは.dylibが必要）
└── App.xaml.cs          # WPFエントリーポイント（移植必要）
```

### 1.2 依存関係
- **BuildSoft.OscCore** (v1.2.1.1): クロスプラットフォーム、再利用可能
- **CommunityToolkit.Mvvm** (v8.4.0): クロスプラットフォーム、再利用可能
- **Microsoft.Extensions.DependencyInjection** (v10.0.4): 再利用可能
- **NAudio** (v2.2.1): **Windows専用** - macOS対応が最大の懸念事項
- **WPF**: Windows専用、完全置換必要

### 1.3 既存パターンと規約
- **MVVM**: 明確な責任分離（Models、Services、ViewModels、Views）
- **DI Container**: Microsoft.Extensions.DependencyInjectionによるシングルトン管理
- **Interfaceベース設計**: 全サービスがインターフェースを持ち、テスト容易性が高い
- **非同期処理**: Channel<T>ベースのタイムコード処理、async/awaitパターン
- **イベント駆動**: TimecodeEngine.TimecodeUpdatedイベントを中心とした疎結合設計

### 1.4 再利用可能なコンポーネント（推定）

#### 高再利用性（90-100%）
- `Models/*`: 全モデルクラス（ProjectData、Cue、OscHost、TimecodeValue、etc.）
- `Services/ProjectService.cs`: JSON永続化ロジック
- `Services/OscSender.cs`, `Services/OscTransport.cs`: OSC通信
- `Services/CueManager.cs`: キュートリガーロジック
- `Services/TimecodeGenerator.cs`: タイムコード生成ロジック
- `Services/LtcEncoder.cs`, `Services/LtcDecoder.cs`: P/Invokeラッパー（.dylibパス変更のみ）
- `Native/LtcFrameHelper.cs`: メモリレイアウト解析ロジック

#### 中再利用性（50-80%）
- `ViewModels/*`: ロジックは再利用、WPF固有のDispatcher呼び出しをAvaloniaに変更
- `Services/TimecodeEngine.cs`: NAudio依存部分（WasapiCapture、WasapiOut）を抽象化必要
- `Services/AudioDeviceService.cs`: NAudio.CoreAudioApi依存、macOS版への置換必要

#### 低再利用性（0-20%）
- `Views/*.xaml`: WPF XAML、Avalonia XAMLへの手動移植必要
- `App.xaml.cs`: エントリーポイント、Application.OnStartupロジックは移植必要
- `MainWindow.xaml.cs`: ファイルダイアログ（OpenFileDialog/SaveFileDialog）→macOS対応必要

---

## 2. Requirements Feasibility Analysis

### 2.1 技術要件マッピング

| 要件 | 必要技術 | 既存資産 | ギャップ |
|------|---------|---------|---------|
| **Req 1: クロスプラットフォーム基盤** | .NET 8.0、RID選択 | .csprojファイル | `<TargetFramework>net8.0</TargetFramework>`に変更、RID追加 |
| **Req 2: macOS向けUI** | Avalonia/MAUI/AppKit | WPF XAML | **Missing**: UI層完全再実装 |
| **Req 3: オーディオデバイス管理** | CoreAudio/NAudio for macOS | NAudio (Windows) | **Unknown**: NAudio macOS対応調査必要 |
| **Req 4: LTCエンコード/デコード** | libltc.dylib、P/Invoke | libltc.dll、既存ラッパー | **Missing**: .dylibビルド、パス変更 |
| **Req 5: タイムコード生成/リレー** | TimecodeEngine | 既存実装あり | **Constraint**: オーディオAPI依存部分の抽象化 |
| **Req 6: OSC通信** | BuildSoft.OscCore | 既存実装あり | ギャップなし（再利用可能） |
| **Req 7: キュー管理** | CueManager | 既存実装あり | ギャップなし（再利用可能） |
| **Req 8: プロジェクト管理** | ProjectService、JSON | 既存実装あり | ギャップなし（再利用可能） |
| **Req 9: macOS固有UI** | NSOpenPanel/NSSavePanel | WPF FileDialog | **Missing**: Avaloniaのファイルダイアログ使用 |
| **Req 10: ログ/デバッグ** | LogViewModel | 既存実装あり | UI層のみ再実装 |
| **Req 11: ネイティブライブラリ配布** | .dylibバンドリング | .dll配布ロジック | **Missing**: .appバンドル、Info.plist設定 |
| **Req 12: アプリバンドル/インストーラー** | .app、コード署名、公証 | なし | **Missing**: macOSパッケージング全体 |
| **Req 13: パフォーマンス** | 既存アーキテクチャ | Channel<T>、非同期処理 | **Constraint**: CoreAudioレイテンシ検証必要 |

### 2.2 ギャップと制約

#### Missing（欠落）
1. **macOS UIフレームワーク実装**: Avalonia UI/MAUI/AppKitのいずれかの選択と完全実装
2. **libltc.dylib**: macOS用バイナリのビルドまたは調達
3. **オーディオAPIアダプタ**: NAudio macOS対応の検証、または代替実装
4. **アプリバンドリング**: .app、Info.plist、アイコン、コード署名、公証プロセス
5. **ファイルダイアログ**: macOS標準ダイアログへの置換

#### Unknown（調査必要）
1. **NAudio macOS互換性**: NAudio 2.2.1がmacOS CoreAudioをサポートしているか、またはNAudio.Coreのみで動作するか
2. **libltc.dylibビルド**: Homebrewまたはソースからのビルド手順、ユニバーサルバイナリ（x64/ARM64）作成方法
3. **Avaloniaパフォーマンス**: リアルタイムタイムコード更新（30/60fps）でのUI応答性
4. **コード署名要件**: 個人開発者ID証明書の取得プロセス、公証の自動化

#### Constraint（制約）
1. **既存ViewModelの再利用**: Dispatcher呼び出しをAvaloniaのDispatcher.UIThreadに変更必要
2. **TimecodeEngineのオーディオ依存**: WasapiCapture/WasapiOutの抽象化が必要
3. **プロジェクトファイル互換性**: Windows版との完全互換性維持のため、JSONスキーマ変更不可

### 2.3 複雑性シグナル

- **CRUD**: プロジェクト管理（シンプル）
- **アルゴリズムロジック**: LTCデコード、タイムコードオフセット計算（既存実装再利用）
- **ワークフロー**: キュートリガー、Freerunタイマー（既存実装再利用）
- **外部統合**: OSC送信、オーディオデバイスI/O、ネイティブライブラリP/Invoke（**高複雑度**）

---

## 3. Implementation Approach Options

### Option A: Extend Existing Components（既存拡張）

#### 概要
既存のWindows版コードベースに条件分岐を追加し、macOS対応を段階的に実装。

#### 対象ファイル
- `AudioDeviceService.cs`: `#if WINDOWS` / `#if MACOS`でNAudio実装を分岐
- `TimecodeEngine.cs`: オーディオキャプチャ部分を`IAudioCapture`インターフェースに抽象化
- `App.xaml.cs`: WPFとAvaloniaの条件コンパイル
- `MainWindow.xaml.cs`: ファイルダイアログを`IFileDialogService`に抽象化

#### 互換性評価
- ✅ プロジェクトファイル互換性を自然に維持
- ✅ 既存のテストコードを再利用可能
- ❌ WPFとAvaloniaのXAMLは互換性が低く、実質的に2つのUI実装が必要

#### 複雑性と保守性
- ❌ 条件分岐の増加により認知負荷が高まる
- ❌ プラットフォーム固有バグのデバッグが困難
- ❌ 単一責任原則の違反（1ファイルが複数プラットフォーム担当）

#### Trade-offs
- ✅ 初期開発が速い（既存コードを直接編集）
- ✅ Git履歴が連続
- ❌ **長期的な保守コストが高い**
- ❌ UI層が実質的に重複（WPF/Avalonia両対応が現実的でない）

**推奨度**: ❌ **非推奨** - UI層の完全分岐が避けられないため、コード重複が発生

---

### Option B: Create New Components（新規作成）

#### 概要
macOS専用の新規プロジェクトを作成し、既存コードを選択的にコピー/参照。

#### 新規作成コンポーネント
```
TimecodeBridge.macOS/
├── TimecodeBridge.macOS.csproj     # 新規プロジェクト
├── Program.cs, AppDelegate.cs      # Avaloniaエントリーポイント
├── Views/                          # Avalonia XAML
│   ├── MainWindow.axaml
│   ├── CueListView.axaml, etc.
├── ViewModels/                     # 既存からコピー後、Avalonia対応修正
├── Services/
│   ├── AudioDeviceService.macOS.cs # macOS専用実装
│   └── FileDialogService.macOS.cs
└── Native/
    └── libltc.dylib                # macOS用バイナリ
```

#### 統合ポイント
- **共有ライブラリ**: `TimecodeBridge.Core.csproj`を作成し、Models、Services（プラットフォーム非依存部分）を共有
- **参照関係**:
  - `TimecodeBridge.macOS` → `TimecodeBridge.Core` (Project Reference)
  - `TimecodeBridge (Windows)` → `TimecodeBridge.Core` (Project Reference)

#### 責任境界
- **TimecodeBridge.Core**: Models、Interfaces、プラットフォーム非依存Services
- **TimecodeBridge.macOS**: UI (Avalonia)、macOS固有Services、エントリーポイント
- **TimecodeBridge (Windows)**: UI (WPF)、Windows固有Services、エントリーポイント

#### Trade-offs
- ✅ **明確な責任分離**（プラットフォーム毎に独立）
- ✅ テストが容易（プラットフォーム固有部分を分離テスト）
- ✅ 保守性が高い（変更の影響範囲が明確）
- ❌ 初期セットアップコストが高い（プロジェクト構成の再設計）
- ❌ ファイル数が増加（3プロジェクト構成）

**推奨度**: ⚠️ **条件付き推奨** - 長期保守を重視する場合は最適だが、初期コストが高い

---

### Option C: Hybrid Approach（ハイブリッド）

#### 概要
既存コードベース内でコア抽出を段階的に実施し、macOS対応を追加。

#### 戦略

**Phase 1: コア抽出（リファクタリング）**
1. 既存プロジェクトを以下に分離:
   ```
   src/
   ├── TimecodeBridge.Core/         # 新規作成
   │   ├── Models/                  # 既存から移動
   │   ├── Services/                # プラットフォーム非依存部分を移動
   │   │   ├── ProjectService.cs
   │   │   ├── OscSender.cs
   │   │   ├── CueManager.cs
   │   │   ├── TimecodeGenerator.cs
   │   │   └── Interfaces/
   │   └── TimecodeBridge.Core.csproj
   ├── TimecodeBridge/              # 既存（Windows）
   │   ├── Services/                # Windows固有サービス残留
   │   │   ├── AudioDeviceService.cs  # NAudio Windows実装
   │   │   └── FileDialogService.cs
   │   ├── ViewModels/
   │   ├── Views/ (WPF)
   │   └── TimecodeBridge.csproj    # .Core参照追加
   └── TimecodeBridge.macOS/        # 新規作成（Phase 2以降）
   ```

2. `TimecodeEngine.cs`のオーディオ依存を抽象化:
   ```csharp
   // 新規インターフェース
   public interface IAudioCapture {
       event EventHandler<AudioSamplesEventArgs> AudioSamplesAvailable;
       void Start();
       void Stop();
   }

   // Windows実装
   public class WasapiAudioCapture : IAudioCapture { /* NAudio.WasapiCapture */ }

   // macOS実装（Phase 2で実装）
   public class CoreAudioCapture : IAudioCapture { /* CoreAudio/NAudio macOS */ }
   ```

**Phase 2: macOS UI実装（Avalonia選択を想定）**
1. Avalonia UIテンプレートからmacOSプロジェクト作成
2. ViewModelsを`.Core`から参照、Dispatcher呼び出しをAvalonia対応に修正
3. 既存WPF XAMLをベースにAvalonia XAML作成（手動移植）
4. `AudioDeviceService.macOS.cs`、`FileDialogService.macOS.cs`を実装

**Phase 3: macOS固有機能**
1. libltc.dylibのビルド・配布
2. .appバンドル設定（Info.plist、アイコン）
3. コード署名・公証スクリプト作成

#### 段階的実装
- **Phase 1 (1週間)**: コア抽出、Windows版でリグレッションテスト
- **Phase 2 (2-3週間)**: macOS UI実装、基本機能動作確認
- **Phase 3 (1週間)**: ネイティブライブラリ、パッケージング、配布

#### リスク軽減
- Git feature branchで作業、Phase毎にマージ
- Phase 1完了後、Windows版で全機能動作確認（リグレッション防止）
- Phase 2では最小限のUI（タイムコード表示のみ）で動作確認

#### Trade-offs
- ✅ **段階的リスク管理**（Phase毎に検証）
- ✅ 既存Windowsアプリの動作保証
- ✅ コードベース全体の品質向上（抽象化による）
- ⚠️ **Phase 1のリファクタリングがクリティカルパス**
- ❌ 計画の複雑性が高い（3フェーズ管理必要）

**推奨度**: ✅ **強く推奨** - リスクとコストのバランスが最適

---

## 4. Research Needed（設計フェーズで調査すべき項目）

### R1: NAudio macOS互換性調査
- **目的**: NAudio 2.2.1がmacOS CoreAudioをサポートしているか確認
- **調査方法**:
  - NAudioドキュメント・GitHubリポジトリ確認
  - macOS環境でNAudio.CoreAudioApiの動作検証
  - 代替案としてNAudio.Core + CoreAudio P/Invoke wrapper調査
- **成果物**: 技術検証レポート、サンプルコード

### R2: libltc.dylibビルド手順
- **目的**: macOS用libltcバイナリの調達・ビルド方法確立
- **調査方法**:
  - Homebrewパッケージ確認（`brew install libltc`）
  - ソースからのビルド（x64/ARM64ユニバーサルバイナリ）
  - P/Invoke DllImportパス設定（`@rpath/libltc.dylib`）
- **成果物**: ビルドスクリプト、配布用.dylibファイル

### R3: Avalonia UIフレームワーク評価
- **目的**: Avalonia UIがリアルタイムタイムコード表示（30/60fps更新）に対応できるか検証
- **調査方法**:
  - Avaloniaプロトタイプ作成（Timer + TextBlock更新）
  - パフォーマンスプロファイリング（CPU使用率、フレームドロップ）
  - Avalonia vs MAUI vs AppKit比較
- **成果物**: パフォーマンステストレポート、UIフレームワーク選定理由書

### R4: macOSアプリバンドリング・公証プロセス
- **目的**: 配布可能な.appバンドルの作成とセキュリティ警告回避
- **調査方法**:
  - dotnet publish設定（`-r osx-x64 -r osx-arm64`）
  - Info.plist設定項目（CFBundleIdentifier、NSMicrophoneUsageDescription）
  - コード署名コマンド（`codesign`）、公証API（`xcrun notarytool`）
- **成果物**: ビルド・配布自動化スクリプト、開発者証明書取得ガイド

### R5: CoreAudioレイテンシ検証
- **目的**: Requirement 13（1フレーム以内のレイテンシ）を満たせるか確認
- **調査方法**:
  - CoreAudioバッファサイズ設定（AudioDeviceSetProperty）
  - レイテンシ計測（入力→LTCデコード→OSC送信）
  - NAudio vs 生CoreAudio P/Invokeパフォーマンス比較
- **成果物**: レイテンシ計測データ、最適化推奨事項

---

## 5. Implementation Complexity & Risk

### 全体評価

| フェーズ | 工数 | リスク | 根拠 |
|---------|------|--------|------|
| **Phase 1: コア抽出** | M (5-7日) | Medium | 既存コードのリファクタリング、抽象化インターフェース設計。テスト済みパターンを踏襲するため中リスク。 |
| **Phase 2: macOS UI実装** | L (2-3週間) | High | Avalonia新規学習、WPF→Avalonia XAML移植（自動化不可）。NAudio macOS対応が不明なため高リスク。 |
| **Phase 3: パッケージング** | M (5-7日) | Medium | .appバンドル、コード署名は既知技術だが、公証プロセスの自動化にトライアルエラーの可能性。 |
| **統合・テスト** | M (5-7日) | Low | 既存テストコードを再利用、macOS固有テストを追加。明確なスコープのため低リスク。 |

### 詳細評価

#### Requirement 1: クロスプラットフォーム基盤
- **工数**: S (1-2日)
- **リスク**: Low
- **根拠**: .csprojファイル変更のみ、.NET 8.0は成熟したクロスプラットフォームランタイム

#### Requirement 2-9: macOS UI実装
- **工数**: L (2-3週間)
- **リスク**: High
- **根拠**:
  - Avalonia学習曲線（Medium）
  - XAML移植の手作業（Large）
  - **Unknown**: NAudio macOS互換性が不明（High Risk）

#### Requirement 4, 11: LTCライブラリ
- **工数**: M (3-5日)
- **リスク**: Medium
- **根拠**:
  - libltc.dylibビルドは標準的なUnixビルドプロセス
  - P/Invokeラッパーは既存コード流用可能
  - ユニバーサルバイナリ作成にトライアルエラーの可能性

#### Requirement 12: アプリバンドル・公証
- **工数**: M (5-7日)
- **リスク**: Medium
- **根拠**:
  - 技術的には既知のプロセス
  - 公証の自動化スクリプトに試行錯誤が必要
  - Apple Developer証明書取得に時間がかかる可能性

#### Requirement 13: パフォーマンス
- **工数**: S (2-3日、検証のみ）
- **リスク**: Medium
- **根拠**:
  - 既存アーキテクチャ（Channel<T>、非同期処理）は高性能
  - CoreAudioレイテンシは検証必要だが、調整可能な範囲

---

## 6. Recommendations for Design Phase

### 6.1 推奨アプローチ

**Option C: Hybrid Approach（ハイブリッド）** を採用し、以下のマイルストーンで実装:

1. **M1: コア抽出** (Week 1)
   - `TimecodeBridge.Core`プロジェクト作成
   - Models、プラットフォーム非依存Servicesを移動
   - `IAudioCapture`インターフェース導入
   - Windows版でリグレッションテスト実施

2. **M2: 技術検証** (Week 2)
   - R1: NAudio macOS互換性調査
   - R2: libltc.dylibビルド
   - R3: Avalonia UIプロトタイプ（タイムコード表示のみ）
   - Go/No-Go判断: NAudio非対応の場合、CoreAudio P/Invoke実装に切替

3. **M3: macOS UI実装** (Week 3-4)
   - `TimecodeBridge.macOS`プロジェクト作成（Avaloniaテンプレート）
   - MainWindow、TimecodeDisplayView、CueListView実装
   - ViewModelsをAvalonia対応修正
   - 基本機能動作確認（タイムコード生成→表示→OSC送信）

4. **M4: macOS固有機能** (Week 5)
   - `AudioDeviceService.macOS.cs`実装（CoreAudio/NAudio）
   - `FileDialogService.macOS.cs`実装
   - libltc.dylibバンドル設定
   - .appバンドル、Info.plist設定

5. **M5: パッケージング・配布** (Week 6)
   - コード署名スクリプト作成
   - 公証プロセス自動化
   - DMG作成（インストール手順付き）
   - ユーザードキュメント作成

### 6.2 主要設計決定事項

#### D1: UIフレームワーク選定
- **選択肢**: Avalonia UI vs .NET MAUI vs AppKit (Xamarin.Mac)
- **推奨**: **Avalonia UI**
- **理由**:
  - WPF類似のXAML、既存UI資産の移植が容易
  - クロスプラットフォーム（将来的にLinux対応も可能）
  - アクティブなコミュニティ、.NET 8.0サポート
  - MAUIはモバイルファーストで複雑、AppKitはC#バインディングが限定的

#### D2: オーディオAPIアダプタ
- **選択肢**: NAudio macOS vs CoreAudio P/Invoke
- **推奨**: **NAudio優先、非対応時にCoreAudio実装**
- **理由**:
  - NAudio対応なら既存コードの変更最小
  - 非対応でも`IAudioCapture`抽象化により影響局所化
  - CoreAudio P/Invokeは実績あり（他OSSプロジェクト参考可能）

#### D3: プロジェクト構成
```
TimecodeBridge/
├── src/
│   ├── TimecodeBridge.Core/          # 共有コア
│   ├── TimecodeBridge/               # Windows版
│   └── TimecodeBridge.macOS/         # macOS版
├── tests/
│   ├── TimecodeBridge.Core.Tests/
│   └── TimecodeBridge.macOS.Tests/
└── TimecodeBridge.sln                # ソリューション全体
```

### 6.3 設計フェーズで実施すべきResearch

| 優先度 | Research項目 | 期限 | 担当 | ブロッカー |
|-------|-------------|------|------|----------|
| **P0** | R1: NAudio macOS互換性 | Week 2 | 開発者 | M3 (UI実装) |
| **P0** | R2: libltc.dylibビルド | Week 2 | 開発者 | M4 (macOS固有機能) |
| **P1** | R3: Avalonia UIプロトタイプ | Week 2 | 開発者 | M3 (UI実装) |
| **P1** | R4: アプリバンドリング | Week 4 | 開発者 | M5 (パッケージング) |
| **P2** | R5: CoreAudioレイテンシ | Week 3 | 開発者 | Req 13検証 |

### 6.4 成功基準

- ✅ Windows版の全機能がmacOSで動作（プロジェクトファイル互換性維持）
- ✅ パフォーマンス要件達成（Req 13: 1フレーム以内レイテンシ、CPU 10%以下）
- ✅ macOS 12+、x64/ARM64の両アーキテクチャで動作
- ✅ コード署名・公証完了、セキュリティ警告なし
- ✅ 自動ビルド・配布パイプライン構築（CI/CD）

---

## 7. Conclusion

既存のWindows版TimecodeBridgeは優れたアーキテクチャ設計（MVVM、DI、インターフェースベース）により、**コアロジックの60-70%が再利用可能**です。最大の課題はUI層の完全再実装とオーディオAPI移植ですが、**Hybrid Approach**により段階的にリスクを管理しつつ、高品質なmacOS版を開発可能です。

設計フェーズでは、**NAudio macOS対応とlibltc.dylibビルド**の2つの技術検証を最優先で実施し、早期にリスクを解消することを推奨します。
