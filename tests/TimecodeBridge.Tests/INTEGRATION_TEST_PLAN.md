# 全機能統合テスト計画（Task 7）

## テスト概要

Phase 2b完了時の包括的統合テストを実施し、以下の観点から品質を検証する：

1. **E2E機能テスト**: キュー作成 → タイムコード到達 → OSC送信
2. **プロジェクト管理テスト**: 保存 → アプリ再起動 → 読込
3. **互換性テスト**: Windows版プロジェクトファイルとの相互読込
4. **パフォーマンステスト**: 1000件キュー登録時のレイテンシ検証

## 自動テスト vs 手動テスト

### 自動テスト可能項目（xUnit）

✅ **実装済み**: `IntegrationTests.cs`

#### 1. キュー管理機能
- ✅ `CueManager_ShouldTriggerCue_WhenTimecodeReachesTriggerTime`
  - タイムコード到達時のキュートリガー検証
  - High Water Mark動作の確認
- ✅ `CueManager_ShouldNotTrigger_WhenCueIsMuted`
  - ミュート機能の動作確認
- ✅ `CueManager_ShouldResetHighWaterMark_OnManualReset`
  - リセット後の再トリガー可能性検証

#### 2. プロジェクト保存/読込
- ✅ `ProjectService_ShouldSaveAndLoadProject_WithFullData`
  - 完全なプロジェクトデータの永続化と復元
  - Cue、Host、Offset、RelaySettings、SourceSettingsの検証
- ✅ `ProjectService_ShouldLoadProject_WithDifferentOscArgumentTypes`
  - Int32、Float32、String引数型の正しいシリアライズ/デシリアライズ

#### 3. Windows版互換性
- ✅ `ProjectData_ShouldBeCompatible_WithWindowsJsonFormat`
  - Windows版が出力したJSON形式の読込確認
  - camelCaseプロパティ名の対応

#### 4. パフォーマンス
- ✅ `CueManager_ShouldHandleLargeCueList_WithAcceptablePerformance`
  - 1000件キュー登録時のトリガー検出レイテンシ < 1ms（要件13.5）
- ✅ `ProjectService_ShouldSaveAndLoadLargeProject_WithAcceptablePerformance`
  - 1000件キュープロジェクトの保存/読込が各1秒以内

### 手動テスト必須項目

以下は実機環境またはUIインタラクションが必要なため手動テストで実施：

#### E2E-1: macOS版 アプリ起動 → プロジェクト作成 → 保存
**手順**:
1. TimecodeBridge.macOS.appを起動
2. メニューバー → File → New Project
3. MainWindowが表示され、初期状態が空であることを確認
4. File → Save As で新規プロジェクトを保存
5. 指定パスに.jsonファイルが生成されることを確認

**期待結果**:
- ファイルダイアログがmacOS標準（NSSavePanel）で表示される
- JSONファイルが正しいフォーマットで保存される

---

#### E2E-2: キュー作成 → タイムコード内部生成 → トリガー検出
**手順**:
1. macOS版を起動
2. TimecodeDisplayView で "Internal Generate" モードを選択
3. Start Time: 00:00:00:00, Frame Rate: 30fps に設定
4. CueListView で "Add Cue" ボタンをクリック
5. 新規キュー作成:
   - Name: "Test Trigger"
   - Trigger Timecode: 00:00:05:00
   - OSC Address: /test/trigger
   - Arguments: (String) "GO"
6. TimecodeDisplayViewで "Start" ボタンをクリック
7. タイムコードが 00:00:05:00 に到達した瞬間を観察
8. LogViewにトリガーメッセージが記録されることを確認

**期待結果**:
- タイムコードが60fps（内部生成モード）で更新される
- 00:00:05:00到達時にキューがハイライト表示される
- OSC送信ログが表示される（実際の送信はホスト設定による）

---

#### E2E-3: プロジェクト保存 → アプリ再起動 → 読込
**手順**:
1. E2E-2で作成したプロジェクトを保存（File → Save）
2. アプリを終了（Cmd+Q）
3. macOS版を再起動
4. File → Open で保存したプロジェクトを開く
5. CueListView、HostManagerView、TimecodeDisplayViewの設定が復元されることを確認

**期待結果**:
- 全てのキュー、ホスト、タイムコード設定が正確に復元される
- UIの状態が保存時と一致する

---

#### E2E-4: Windows版プロジェクト → macOS版で読込
**手順**:
1. Windows環境でTimecodeBridge（WPF版）を起動
2. サンプルプロジェクトを作成:
   - Cue: 3件（異なるOSC引数型: Int32, Float32, String）
   - Host: 2件（QLab、Resolume）
   - Offset: 01:30:45:10
3. プロジェクトを `windows_test.json` として保存
4. macOS環境に `windows_test.json` をコピー
5. macOS版で File → Open → `windows_test.json` を選択
6. 全データが正しく読み込まれることを確認

**期待結果**:
- Windows版で保存したJSONがmacOS版で完全に再現される
- データ型変換エラーが発生しない

---

#### E2E-5: macOS版プロジェクト → Windows版で読込
**手順**:
1. macOS版でプロジェクト作成（E2E-2と同様）
2. プロジェクトを `macos_test.json` として保存
3. Windows環境に `macos_test.json` をコピー
4. Windows版で File → Open → `macos_test.json` を選択
5. 全データが正しく読み込まれることを確認

**期待結果**:
- macOS版で保存したJSONがWindows版で完全に再現される
- プラットフォーム固有設定（AudioDeviceIdなど）がエラーにならない

---

#### PERF-1: 1000件キュー登録時のUI応答性
**手順**:
1. macOS版を起動
2. TestData/sample_project_1000_cues.json を生成（後述スクリプト使用）
3. File → Open で1000件キュープロジェクトを開く
4. CueListViewにスクロールバーが表示され、全キューがリスト表示されることを確認
5. タイムコード内部生成モードで開始
6. タイムコードが更新される間、CueListViewのスクロールやウィンドウリサイズを実行
7. UI応答性（60fpsフレームレート維持）を体感評価

**期待結果**:
- CueListViewの仮想化（VirtualizingStackPanel）が機能
- タイムコード更新中もUIが遅延なく応答
- CPU使用率が10%以下（Activity Monitor確認）

---

#### PERF-2: タイムコードトリガー検出レイテンシ計測
**手順**:
1. 1000件キュープロジェクトを読込
2. タイムコード内部生成で開始
3. LogViewでトリガーログのタイムスタンプを確認
4. 実際のタイムコード到達時刻とログ記録時刻の差分を計測

**期待結果**:
- トリガー検出からログ記録まで < 1ms（要件13.5）
- 自動テスト `CueManager_ShouldHandleLargeCueList_WithAcceptablePerformance` でも検証済み

---

## テストデータ生成

### 1000件キュープロジェクトファイル生成スクリプト

以下のC#スクリプトを実行してテストデータを生成：

```csharp
// GenerateLargeProjectTestData.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TimecodeBridge.Core.Models;

var largeProject = new ProjectData
{
    Cues = new List<Cue>(),
    Hosts = new List<OscHost>
    {
        new OscHost
        {
            Id = Guid.NewGuid(),
            Name = "Performance Test Host",
            IpAddress = "127.0.0.1",
            Port = 9000,
            IsEnabled = true
        }
    },
    Offset = new TimecodeOffset(0, 0, 0, 0),
    RelaySettings = new RelaySettings
    {
        InputDeviceId = "default-input",
        OutputDeviceId = "default-output"
    },
    SourceSettings = new TimecodeSourceSettings
    {
        FrameRate = FrameRate.Fps30,
        StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30)
    }
};

// 1000件のキュー生成
for (int i = 0; i < 1000; i++)
{
    int hours = i / 3600;
    int minutes = (i % 3600) / 60;
    int seconds = i % 60;
    int frames = (i * 7) % 30; // バリエーション追加

    largeProject.Cues.Add(new Cue
    {
        Id = Guid.NewGuid(),
        Name = $"Performance Cue {i:D4}",
        TriggerTimecode = new TimecodeValue(hours, minutes, seconds, frames, FrameRate.Fps30),
        OscAddress = $"/perf/cue/{i}",
        OscArguments = new List<OscArgument>
        {
            new OscInt32Argument(i),
            new OscStringArgument($"Value_{i}")
        },
        IsMuted = i % 50 == 0 // 50件に1件ミュート
    });
}

// JSON出力
var jsonOptions = ProjectData.CreateJsonOptions();
var json = JsonSerializer.Serialize(largeProject, jsonOptions);

File.WriteAllText(
    "tests/TimecodeBridge.Tests/TestData/sample_project_1000_cues.json",
    json
);

Console.WriteLine("✅ 1000件キュープロジェクトファイルを生成しました");
```

---

## 実行コマンド

### 自動テスト実行
```bash
# 全統合テスト実行
cd /Users/yothuba/TimecodeBridge
dotnet test tests/TimecodeBridge.Tests/TimecodeBridge.Tests.csproj --filter "FullyQualifiedName~IntegrationTests"

# 特定テストクラスのみ実行
dotnet test --filter "FullyQualifiedName~IntegrationTests" --logger "console;verbosity=detailed"
```

### テストデータ生成
```bash
# C#スクリプト実行（.NET 8 Script必要）
dotnet script GenerateLargeProjectTestData.csx
```

---

## 検証基準

### 合格基準
- ✅ 全自動テストがPASS（IntegrationTests.cs内の全テストメソッド）
- ✅ 手動E2Eテスト（E2E-1～E2E-5）が全て期待結果を満たす
- ✅ パフォーマンステスト（PERF-1, PERF-2）が要件13.5を満たす
- ✅ Windows版プロジェクトファイルとの双方向互換性が確認される

### 不合格基準
- ❌ 自動テストで1件でも失敗
- ❌ Windows版で保存したJSONがmacOS版で読み込めない（または逆）
- ❌ 1000件キュー時のトリガー検出が1ms以上
- ❌ UI応答性が著しく低下（フレームレート30fps未満）

---

## 実施者向けメモ

### macOS実機環境要件
- macOS 12 (Monterey) 以降
- .NET 8.0 SDK インストール済み
- Avalonia UIビルド環境構築済み

### Windows実機環境要件（互換性テスト用）
- Windows 10/11
- .NET 8.0 Runtime
- 既存TimecodeBridge WPF版ビルド済み

### テスト実施タイミング
- Phase 2b完了後（Task 7実施時）
- Phase 3（CoreAudio実装）前
- 最終リリース前の統合確認

---

## 関連要件

- **要件8.5**: Windows版プロジェクトファイルとの互換性維持
- **要件13.5**: 1000件キュー登録時のパフォーマンス維持（トリガー検出レイテンシ < 1ms）

---

## テスト結果記録

実施日: ___________

| テストID | 結果 | 備考 |
|---------|------|------|
| IntegrationTests (自動) | ⬜ PASS / ⬜ FAIL |  |
| E2E-1 | ⬜ PASS / ⬜ FAIL |  |
| E2E-2 | ⬜ PASS / ⬜ FAIL |  |
| E2E-3 | ⬜ PASS / ⬜ FAIL |  |
| E2E-4 | ⬜ PASS / ⬜ FAIL |  |
| E2E-5 | ⬜ PASS / ⬜ FAIL |  |
| PERF-1 | ⬜ PASS / ⬜ FAIL | CPU使用率: ____% |
| PERF-2 | ⬜ PASS / ⬜ FAIL | レイテンシ: ____ms |

実施者: ___________
