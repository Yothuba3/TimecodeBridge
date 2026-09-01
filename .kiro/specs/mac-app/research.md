# Research & Design Decisions: mac-app

## Summary
- **Feature**: mac-app（TimecodeBridge macOS版移植）
- **Discovery Scope**: Complex Integration（既存Windowsアプリケーションの完全移植）
- **Key Findings**:
  - NAudioはmacOS CoreAudioをネイティブサポートしておらず、CoreAudio P/Invoke実装が必要
  - Avalonia UIは60fps@1080pの実時間更新をサポート可能、CompiledBindingsで最適化必須
  - libltcユニバーサルバイナリ（x64/ARM64）はlipo + CFLAGSで構築可能
  - .NET 8のmacOS公証プロセスは整備されており、JIT必要アプリはcom.apple.security.cs.allow-jit entitlement必須

## Research Log

### NAudio macOS互換性調査

**Context**: 既存WindowsアプリはNAudio 2.2.1でWASAPI（Windows CoreAudio）を使用しているが、macOS移植でも同様のAPIが使用できるか検証が必要。

**Sources Consulted**:
- NAudio GitHub Issue #1077 "Cross Platform Support with Linux & Mac"
- NAudio 2.3.0 NuGet Package仕様
- GitHub - naudio/NAudio公式リポジトリ
- Apple Developer Documentation - Core Audio

**Findings**:
- **NAudio.Core**はクロスプラットフォームパッケージだが、フル機能（録音・再生）はWindows専用
- NAudioのCoreAudio APIは**Windows WASAPI**を指し、macOS CoreAudioとは異なる
- GitHub Issue #184（2023年）によると、Linux/Mac上でNAudioクロスプラットフォーム名前空間を使用するとクラッシュする報告あり
- 2025年時点でも**NAudioのmacOS CoreAudioネイティブサポートは存在しない**

**Implications**:
- **決定**: NAudioを使用せず、macOS CoreAudio P/Invokeによるカスタム実装が必須
- `IAudioCapture`インターフェースによる抽象化が設計の中核となる
- macOS版のオーディオレイヤーは完全新規実装として扱う

---

### Avalonia UI性能検証（リアルタイム更新対応）

**Context**: タイムコード表示は30/60fpsでの高頻度UI更新が必要。Avalonia UIがWPFと同等のパフォーマンスを達成できるか検証。

**Sources Consulted**:
- Avalonia UI Performance Optimization公式ドキュメント
- GitHub Discussion #19239 "Realtime UI Updates (Around 60 FPS)"
- Avalonia UI macOS専用ページ
- Avalonia 11.1リリースノート

**Findings**:
- **60fps@1080p**の実時間更新は実証済み（GitHub Discussion実例あり）
- CompositionCustomVisuals APIを使用することで、コンポジションスレッド上で直接レンダリング可能（ディスパッチャーをバイパス）
- macOS版はSkia + Metal APIによるレンダリングで、Retina対応高フレームレート描画を保証
- **CompiledBindings**によりリフレクションオーバーヘッドを削減、コンパイル時バインディング最適化
- `UseRegionDirtyRectClipping`により変更領域のみ再描画でパフォーマンス向上

**Performance Best Practices**:
- ビジュアルツリーの深さを最小化（レイアウトパスは要素毎に2回実行される）
- `RelativeSource.FindAncestor`バインディングをDataTemplate内で回避（解決遅延とエラーの原因）
- データロードは非同期化してUIスレッドの過負荷を防止
- バインディングエラーを全て解決（エラー毎にパフォーマンス低下）

**Implications**:
- Avalonia UIは要件13（1フレーム以内のレイテンシ）を満たす性能を持つ
- **設計決定**: タイムコード表示にはCompiledBindings必須、ViewModelバインディングを最適化
- 波形表示などリアルタイム描画にはCompositionCustomVisuals API使用を推奨
- WPF → Avalonia移植時のパフォーマンス低下リスクは低い

---

### libltc.dylibビルド戦略

**Context**: 既存のlibltc.dll（Windows）をmacOS用.dylibに置換。x64/ARM64ユニバーサルバイナリ構築方法の確立。

**Sources Consulted**:
- GitHub Issue: libsndfile "How to build dylib on macOS for ARM-based Apple Silicon"
- Apple Developer Documentation "Building a universal macOS binary"
- VCV Community "What is multi-arch x64+arm64 Universal Mac binary?"
- Falko's Blog "Building a fat/universal library for macOS"

**Findings**:
- **Method 1（推奨）**: 単一ビルドで複数アーキテクチャ指定
  ```bash
  CFLAGS="-arch arm64 -arch x86_64" ./configure --prefix=/build/dir
  make
  ```
  検証: `lipo -info libltc.dylib` → "Architectures: x86_64 arm64"

- **Method 2**: 個別ビルド + lipoマージ
  ```bash
  # x64ビルド
  ./configure CFLAGS="-arch x86_64"
  make
  cp src/.libs/libltc.dylib libltc_x86_64.dylib

  # ARM64ビルド
  ./configure CFLAGS="-arch arm64"
  make
  cp src/.libs/libltc.dylib libltc_arm64.dylib

  # マージ
  lipo -create libltc_x86_64.dylib libltc_arm64.dylib -output libltc.dylib
  ```

- **必須環境**: Xcode 12以降（Apple Siliconユニバーサルバイナリサポート）

**Implications**:
- libltc.dylibのビルド手順は確立済み、技術的リスクは低
- **設計決定**: Method 1（CFLAGSによる単一ビルド）を採用、ビルドスクリプト化
- P/Invoke DllImport属性のパス指定: `@rpath/libltc.dylib`またはバンドル内パス
- CI/CDパイプラインにmacOSビルドステップを追加

---

### .NET 8 macOSアプリバンドル・公証プロセス

**Context**: .appバンドル作成、コード署名、Apple公証によるGatekeeper回避の実装方法確認。

**Sources Consulted**:
- Microsoft Learn "Publish .NET apps for macOS"
- Microsoft Learn "Working with macOS Catalina Notarization"
- Ken Muse Blog "Notarizing .NET Console Apps for macOS"
- Apple Developer Documentation "Notarizing macOS software before distribution"

**Findings**:

**必須要件**:
- Apple Developer Account（コード署名・公証用）
- Xcode Command Line Tools（codesign, altoolユーティリティ）
- Developer ID Application証明書

**必須Entitlements**:
- JIT使用アプリ（非Native AOT）: `com.apple.security.cs.allow-jit`
- Native AOTアプリ: entitlement不要

**.NET 8のmacOSパブリッシング**:
- `dotnet publish`がデフォルトでReleaseビルド、署名、パッケージングを実行
- ネイティブapphostが実行エントリポイントとして生成される

**公証プロセス概要**:
1. Developer ID Application証明書作成
2. Entitlementsファイル作成（.entitlements）
3. コード署名: `codesign --deep --force --verify --verbose --sign "Developer ID Application: NAME" --options runtime --entitlements app.entitlements TimecodeBridge.app`
4. ZIP圧縮: `ditto -c -k --keepParent TimecodeBridge.app TimecodeBridge.zip`
5. 公証申請: `xcrun notarytool submit TimecodeBridge.zip --apple-id EMAIL --team-id TEAMID --password APP_PASSWORD --wait`
6. 公証チケットのステープリング: `xcrun stapler staple TimecodeBridge.app`

**Implications**:
- .NET 8のmacOS公証プロセスは十分に整備されており、自動化可能
- **設計決定**: CI/CD統合により公証を自動化、マニュアルステップ排除
- com.apple.security.cs.allow-jitは必須（TimecodeBridgeはJITコンパイル使用）
- Info.plistに`NSMicrophoneUsageDescription`が必須（オーディオ入力権限）

---

### Avalonia UI - WPF XAML移植ガイド

**Context**: 既存WPF UIのAvalonia XAML移植における主要な差異と移植戦略の確認。

**Sources Consulted**:
- Avalonia Docs "Migrating from WPF"
- Avalonia Docs "WPF and UWP Comparison"
- Avalonia Blog "The Expert Guide to Porting WPF Applications to Avalonia"
- Avalonia Docs "WPF to Avalonia cheat sheet"

**Findings**:

**XAML名前空間変更**:
- WPF: `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"`
- Avalonia: `xmlns="https://github.com/avaloniaui"`

**バインディング構文**:
- ElementName: `{Binding #elementName.Property}` （WPF: `ElementName=elementName, Path=Property`）
- RelativeSource: `{Binding $parent[Type]}` （WPF: `RelativeSource AncestorType=Type`）

**リソースURI**:
- Avalonia: `avares://AssemblyName/path`
- WPF: `pack://application:,,,/`

**プロパティシステム**:
- Avalonia: `StyledProperty`, `DirectProperty`
- WPF: `DependencyProperty`

**コントロール差異**:
- DataGridは別NuGetパッケージ（Avalonia.Controls.DataGrid）
- HierarchicalDataTemplate → TreeDataTemplate（概念は同一）
- LayoutTransform → LayoutTransformControl（ラッパー使用）

**コマンド**:
- WPFのRoutedCommandインフラはAvalonia非対応
- **推奨**: `ICommand`実装（CommunityToolkit.Mvvm）

**イベント**:
- Avaloniaはポインターベースイベント名（マウス、タッチ、ペン統合サポート）

**スタイリング**:
- AvaloniaはCSS-likeスタイリングシステム、WPFよりシンプル

**Implications**:
- **移植コスト**: XAMLの手動移植が必要（自動変換ツール不在）
- **設計決定**: CommunityToolkit.Mvvmは既存で使用中のため、コマンドインフラの変更不要
- ViewModelのDispatcher呼び出しをAvalonia.Threading.Dispatcher.UIThreadに変更
- 既存XAMLを`.axaml`（Avalonia XAML）としてリファクタリング

---

### .NET CoreAudio macOS P/Invoke代替ライブラリ調査

**Context**: NAudio非対応のため、macOS CoreAudio統合のための.NETライブラリまたはP/Invokeアプローチを調査。

**Sources Consulted**:
- Bastian Bechtold Blog "Audio APIs, Part 1: Core Audio / macOS"
- GitHub: NetCoreAudio NuGetパッケージ
- Apple Developer Archive "Core Audio Overview - Programming Interfaces"
- DEV Community "From Core Audio to LLMs: Native macOS Audio Capture"

**Findings**:

**CoreAudio特性**:
- macOSネイティブオーディオライブラリ、高性能・低レイテンシ
- **ドキュメントの貧弱さ**で知られる（"horrible documentation"）
- macOS 14.4+でシステムオーディオキャプチャ新API追加（ユーザー権限必要）

**NetCoreAudio**:
- NuGetパッケージ、.NETクロスプラットフォーム対応
- **制限**: オーディオ再生のみサポート、キャプチャ機能なし

**現実的なアプローチ**:
- macOS CoreAudio P/Invokeによるカスタム実装
- 参考実装: AudioCapサンプルコード（GitHub - insidegui/AudioCap）
- Windows版と同様の`IAudioCapture`抽象化インターフェースを実装

**Implications**:
- **設計決定**: CoreAudio P/Invokeによるネイティブ実装が唯一の実用的選択肢
- `IAudioCapture`、`IAudioPlayback`インターフェースを定義し、プラットフォーム毎に実装
- macOS版実装は`CoreAudioCapture`、`CoreAudioPlayback`クラスとして新規作成
- TCC（Transparency, Consent, and Control）権限要求がInfo.plistで必須

---

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| **Layered Architecture with Shared Core** | TimecodeBridge.Coreを共通ライブラリとし、Windows/macOS版が参照 | プラットフォーム非依存ロジックの再利用、明確な責任分離 | 初期セットアップコスト、3プロジェクト管理 | 既存gap-analysis推奨（Hybrid Approach）と整合 |
| **Port and Adapters (Hexagonal)** | CoreドメインをポートとしてI/F定義、各プラットフォームがアダプタ実装 | テスタビリティ、技術スタック独立性 | 小規模アプリには過剰設計 | オーディオI/OとUI層でPort/Adapter境界を採用 |
| **MVVM with Platform Services** | ViewModelsは共有、View + Servicesはプラットフォーム固有 | 既存WPF MVVMパターン踏襲、学習コスト低 | ViewModelsのDispatcher依存が残留 | 現実的な選択肢、部分的にAdapter統合 |

**選定**: **Layered Architecture + MVVM + Adapter Pattern（ハイブリッド）**
- TimecodeBridge.Core: Models、Interfaces、プラットフォーム非依存Services
- TimecodeBridge.macOS: Avalonia Views、macOS固有Services（CoreAudio、FileDialog）
- Adapter境界: IAudioCapture/IAudioPlayback、IFileDialogService

---

## Design Decisions

### Decision: UIフレームワーク選定（Avalonia UI vs MAUI vs AppKit）

**Context**: WPF置換のため、macOSネイティブルック&フィールを持つUIフレームワークが必要。

**Alternatives Considered**:
1. **Avalonia UI** — WPF類似XAML、クロスプラットフォーム
2. **.NET MAUI** — Microsoft公式、モバイルファースト設計
3. **AppKit (Xamarin.Mac)** — macOSネイティブAPI直接利用

**Selected Approach**: **Avalonia UI**

**Rationale**:
- WPF XAMLとの高い類似性により移植コスト削減
- 60fps@1080pの実時間更新性能実証済み（要件13対応）
- クロスプラットフォーム対応（将来Linux版も視野）
- アクティブなコミュニティ、.NET 8.0完全サポート
- CommunityToolkit.Mvvm完全互換

**Trade-offs**:
- ✅ WPF資産の部分的再利用、XAML手動移植必要だが構造類似
- ✅ Metal APIベースの高性能レンダリング
- ❌ macOS専用機能（NSMenuBar統合）はプラットフォーム固有実装必要
- ❌ DataGridは別パッケージ（Avalonia.Controls.DataGrid）

**Follow-up**: Phase 2でAvalonia UIプロトタイプを作成し、タイムコード表示の60fps動作を検証

---

### Decision: オーディオAPI戦略（CoreAudio P/Invoke）

**Context**: NAudioがmacOS非対応のため、オーディオ入出力の実装アプローチを決定。

**Alternatives Considered**:
1. **NAudio macOS版を待つ** — 現実的でない（公式サポート予定なし）
2. **NetCoreAudio使用** — 再生のみ、キャプチャ非対応のため不可
3. **CoreAudio P/Invoke実装** — カスタム実装、完全制御可能

**Selected Approach**: **CoreAudio P/Invoke + IAudioCaptureアダプタ**

**Rationale**:
- NAudio macOS対応の見込みなし（2025年時点）
- CoreAudioはmacOSネイティブAPI、低レイテンシ保証
- `IAudioCapture`/`IAudioPlayback`抽象化により、プラットフォーム差異を局所化
- 既存Windows版の`WasapiAudioCapture`と並列実装可能

**Trade-offs**:
- ✅ 完全な性能制御、ネイティブAPI直接利用
- ✅ Windows版との一貫したインターフェース設計
- ❌ P/Invoke実装コスト（初期開発時間増）
- ❌ CoreAudioドキュメント不足によるトラブルシューティング困難

**Follow-up**: AudioCapサンプルコード（GitHub - insidegui/AudioCap）を参考にP/Invokeラッパー実装

---

### Decision: プロジェクト構成（3プロジェクト分離）

**Context**: 既存Windows版との共存とコード再利用を両立するプロジェクト構成。

**Alternatives Considered**:
1. **単一プロジェクト + 条件コンパイル** — 保守困難、非推奨（gap-analysis結論）
2. **macOS専用プロジェクト、コピー&修正** — コード重複、保守コスト高
3. **共有Coreプロジェクト + プラットフォーム固有プロジェクト** — 責任分離明確

**Selected Approach**: **Option 3（3プロジェクト分離）**

```
TimecodeBridge/
├── src/
│   ├── TimecodeBridge.Core/          # 共有コア
│   │   ├── Models/
│   │   ├── Services/Interfaces/
│   │   ├── Services/                 # プラットフォーム非依存Services
│   │   └── TimecodeBridge.Core.csproj
│   ├── TimecodeBridge/               # Windows版（既存）
│   │   ├── Services/                 # Windows固有（NAudio実装）
│   │   ├── ViewModels/
│   │   ├── Views/                    # WPF XAML
│   │   └── TimecodeBridge.csproj
│   └── TimecodeBridge.macOS/         # macOS版（新規）
│       ├── Services/                 # macOS固有（CoreAudio実装）
│       ├── ViewModels/               # Avalonia対応修正版
│       ├── Views/                    # Avalonia XAML
│       └── TimecodeBridge.macOS.csproj
└── tests/
    ├── TimecodeBridge.Core.Tests/
    └── TimecodeBridge.macOS.Tests/
```

**Rationale**:
- Models、プラットフォーム非依存Servicesの再利用率60-70%（gap-analysis推定）
- Windows版の既存動作を保証（Phase 1でリグレッションテスト）
- プラットフォーム固有実装の分離により、保守性向上
- Git履歴の保持（Coreへのファイル移動はgit mvで追跡）

**Trade-offs**:
- ✅ 長期保守性、テスト容易性
- ✅ Windows/macOS並行開発可能
- ❌ 初期リファクタリングコスト（Phase 1: 5-7日）
- ❌ ソリューション構成の複雑化

**Follow-up**: Phase 1でTimecodeBridge.Core抽出、Windows版でリグレッションテスト実施

---

### Decision: libltc.dylibビルド方法（単一ビルド + CFLAGS）

**Context**: x64/ARM64ユニバーサルバイナリの効率的なビルド手順確立。

**Alternatives Considered**:
1. **Method 1: 単一ビルド + CFLAGS** — シンプル、ビルドスクリプト1ステップ
2. **Method 2: 個別ビルド + lipo** — 柔軟性高、ビルドステップ複雑

**Selected Approach**: **Method 1（単一ビルド + CFLAGS）**

```bash
#!/bin/bash
# build-libltc-universal.sh

# libltcソース取得
git clone https://github.com/x42/libltc.git
cd libltc

# ユニバーサルバイナリビルド
CFLAGS="-arch arm64 -arch x86_64" ./configure --prefix=$(pwd)/build
make
make install

# 検証
lipo -info build/lib/libltc.dylib
```

**Rationale**:
- ビルドステップがシンプル（CI/CD統合容易）
- 公式libltcのconfigureスクリプトがCFLAGS対応
- 検証コマンド（lipo -info）で即座にアーキテクチャ確認可能

**Trade-offs**:
- ✅ 自動化容易、1ステップビルド
- ✅ アーキテクチャ毎のビルド設定差異なし
- ❌ アーキテクチャ個別最適化が困難（実用上は問題なし）

**Follow-up**: CI/CDにmacOSビルドジョブ追加、libltc.dylibをアーティファクトとして保存

---

## Risks & Mitigations

### リスク1: CoreAudio P/Invoke実装の複雑性
- **リスク**: CoreAudioドキュメント不足、P/Invokeデバッグ困難
- **Mitigation（更新 - Design Review対応）**:
  - **Phase 2技術検証の成果物明確化**:
    - 検証基準: AudioCapサンプルコードベースの最小限プロトタイプ（単一デバイス、48kHzモノラル、30秒連続キャプチャ成功）
    - 成功条件: 48kHzモノラルキャプチャ成功、TCC権限エラー処理実装、デバイスリスト取得成功
    - 失敗条件: 2日（16時間）以上の実装停滞、またはAudioUnitSetProperty連続失敗
  - **フォールバック案（Plan B）**:
    - PortAudioライブラリ（libportaudio.dylib）を代替案として事前調査
    - Phase 2開始前に、PortAudio + .NET P/Invoke基本動作確認（0.5日）
    - CoreAudio実装が2日停滞した時点でPortAudioへの切り替え判断（技術検証会議）
  - **段階的実装計画**:
    - Step 1（1日目）: 基本キャプチャ（AudioComponentFindNext、AudioUnitSetProperty、Start/Stop）
    - Step 2（2日目）: デバイス切断検出とエラーハンドリング
    - Step 3（3日目）: TCC権限エラー処理（-50エラー）、ユーザーガイダンス表示
    - 各ステップでのリスク評価ポイント設定、停滞判断

### リスク2: Avalonia XAML移植コスト過小評価
- **リスク**: WPF XAML → Avalonia XAML移植が想定より時間がかかる
- **Mitigation（更新 - Design Review対応）**:
  - **Phase 2を2サブフェーズに分割**:
    - Phase 2a (Week 3): MainWindow + TimecodeDisplayView（最小限UI、タイムコード表示のみ）
    - Phase 2b (Week 4): CueListView + HostManagerView + LogView + 全機能統合
  - **並列作業の明示**:
    - Phase 2a中: ViewModelsのAvalonia Dispatcher対応（TimecodeViewModel、CueListViewModel）を並行実施
    - Phase 2b開始時: View統合作業の前提条件（ViewModel準備完了）を満たす
  - **移植チェックリスト作成**:
    - WPF→Avalonia変換パターン（research.mdから抽出）を事前にドキュメント化
    - 各View移植時の確認項目: ElementName → #elementName、RelativeSource → $parent、CompiledBindings適用
  - **スケジュールバッファ**: 全体をWeek 1-7に延長（Phase 2を3週間確保）を検討、またはCueListView DataGrid仮想化をPhase 3に後回し

### リスク3: .NET 8公証プロセスのCI/CD統合失敗
- **リスク**: 公証自動化スクリプトがCI環境で失敗、リリースブロック
- **Mitigation**:
  - Phase 5（パッケージング）で専用のGitHub Actionsワークフロー作成
  - Apple App-Specific Passwordをシークレット管理
  - 公証失敗時のフォールバック（手動公証手順ドキュメント化）

### リスク4: プロジェクトファイル互換性破損
- **リスク**: Windows版とmacOS版でプロジェクトファイル（JSON）の互換性が失われる
- **Mitigation**:
  - ProjectData.csをTimecodeBridge.Coreに配置、シリアライゼーションロジック共有
  - Phase 1のリファクタリング後、既存プロジェクトファイルの読み書きテスト実施
  - JSONスキーマのバージョニング（将来の拡張に備える）

### リスク5: Windows版リグレッション検出遅延（新規追加 - Design Review対応）
- **リスク**: Phase 1（Core抽出）後のWindows版リグレッションが検出されず、Phase 2以降に波及
- **Mitigation**:
  - **Phase 1成功基準の具体化**:
    - 既存tests/TimecodeBridge.Tests/の全単体テストがパス（ViewModels、Services）
    - 手動E2Eテスト項目リスト作成（8項目: タイムコード生成→表示、LTCキャプチャ→デコード、キュートリガー→OSC送信、プロジェクト保存→読込、オフセット適用、Freerunタイマー、デバイス切断検出、1000件キュー登録）
    - Windows版でのスモークテスト（30分の実動作確認、Process Explorerでメモリリーク監視）
  - **TimecodeBridge.Core.Tests作成**:
    - Phase 1中に、Models単体テスト（TimecodeValue.Add、TimecodeOffset演算、ProjectData JSON Serialize/Deserialize）をCore.Testsに移行
    - Services単体テスト（CueManager.CheckTriggerWindow、ProjectService.LoadProject）をCore.Testsに追加
    - Phase 2以降でmacOS版テストの基盤とする
  - **Git移行戦略の詳細化**:
    - `git mv`によるファイル移動でGit履歴を保持
    - 各ファイルの移動前後でのdiff確認チェックリスト作成（名前空間変更、usingディレクティブ追加のみであることを保証）
    - リファクタリング前後でのバイナリ比較（Windows版実行ファイルのハッシュ値確認）

---

## References

### Official Documentation
- [Avalonia UI - Performance Optimization](https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance)
- [Avalonia UI - Migrating from WPF](https://www.mintlify.com/avaloniaui/avalonia/guides/migration-from-wpf)
- [Apple Developer - Building a universal macOS binary](https://developer.apple.com/documentation/apple-silicon/building-a-universal-macos-binary)
- [Apple Developer - Notarizing macOS software](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
- [Microsoft Learn - Publish .NET apps for macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)

### Community Resources
- [NAudio GitHub Issue #1077 - Cross Platform Support](https://github.com/naudio/NAudio/issues/1077)
- [Avalonia GitHub Discussion #19239 - Realtime UI Updates](https://github.com/AvaloniaUI/Avalonia/discussions/19239)
- [GitHub - insidegui/AudioCap](https://github.com/insidegui/AudioCap) — macOS CoreAudioサンプルコード
- [Ken Muse - Notarizing .NET Console Apps for macOS](https://www.kenmuse.com/blog/notarizing-dotnet-console-apps-for-macos/)

### Technical References
- [Bastian Bechtold - Audio APIs: Core Audio / macOS](https://bastibe.de/2017-06-17-audio-apis-coreaudio.html) — CoreAudio概要
- [Falko's Blog - Building a fat/universal library for macOS](https://www.f-ax.de/dev/2021/01/15/build-fat-macos-library.html) — lipo詳細
