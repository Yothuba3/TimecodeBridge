# パフォーマンスベンチマーク（Task 7）

## 概要

要件13.5に基づき、1000件キュー登録時のパフォーマンス基準を定義し、検証方法を明記する。

---

## パフォーマンス要件

### 要件13.5: 大量キュー登録時のパフォーマンス維持

> When 大量のキュー（1000件以上）が登録されている, the TimecodeBridge Application shall トリガー検出とUI描画のパフォーマンスを維持する

**具体的基準**:
- トリガー検出レイテンシ: **< 1ms**
- UI更新フレームレート: **60fps維持**（タイムコード更新時）
- CPU使用率: **< 10%**（通常動作時）
- メモリ使用量: **< 150MB**（1000件キュー登録時）

---

## 自動テストによるベンチマーク

### 1. トリガー検出レイテンシ測定

**テストメソッド**: `CueManager_ShouldHandleLargeCueList_WithAcceptablePerformance`

**測定方法**:
```csharp
// 1000件のキューを登録
for (int i = 0; i < 1000; i++) {
    cueManager.Cues.Add(new Cue { /* ... */ });
}

// タイムコード到達をシミュレート
var testTimecode = new TimecodeValue(0, 8, 30, 0, FrameRate.Fps30);
var stopwatch = System.Diagnostics.Stopwatch.StartNew();

cueManager.CheckTriggers(testTimecode); // トリガー検出実行

stopwatch.Stop();

// Assert: < 1ms
Assert.True(stopwatch.ElapsedMilliseconds < 1);
```

**期待結果**:
- ✅ 1000件キュー登録時でも1ms未満でトリガー検出完了
- ✅ High Water Mark方式により、既にトリガー済みのキューはスキップ
- ✅ O(n) 線形探索でも十分な性能（1000件 × 1ms = 1ms）

---

### 2. プロジェクト保存/読込パフォーマンス

**テストメソッド**: `ProjectService_ShouldSaveAndLoadLargeProject_WithAcceptablePerformance`

**測定方法**:
```csharp
// 1000件キュープロジェクト作成
var largeProject = new ProjectData { Cues = /* 1000 cues */ };

// 保存パフォーマンス
var saveStopwatch = Stopwatch.StartNew();
projectService.SaveProject(path, largeProject);
saveStopwatch.Stop();

// 読込パフォーマンス
var loadStopwatch = Stopwatch.StartNew();
var loadedProject = projectService.LoadProject(path);
loadStopwatch.Stop();

// Assert: 各処理1秒以内
Assert.True(saveStopwatch.ElapsedMilliseconds < 1000);
Assert.True(loadStopwatch.ElapsedMilliseconds < 1000);
```

**期待結果**:
- ✅ 保存: 1000件キュー → < 1秒（JSON シリアライズ）
- ✅ 読込: 1000件キュー → < 1秒（JSON デシリアライズ）
- ✅ ファイルサイズ: 約200-300KB（圧縮可能範囲）

---

## 手動実機ベンチマーク

### 3. UI応答性測定（macOS実機）

**手順**:
1. macOS版アプリ起動
2. `sample_project_1000_cues.json` を読込
3. タイムコード内部生成モードで開始（60fps）
4. CueListViewのスクロール操作
5. ウィンドウリサイズ操作
6. Activity MonitorでCPU使用率監視

**計測項目**:
| 項目 | 目標値 | 実測値 | 合否 |
|------|--------|--------|------|
| タイムコード更新FPS | 60fps | _____ fps | ⬜ |
| CueListView スクロール遅延 | < 16ms (60fps) | _____ ms | ⬜ |
| CPU使用率（通常時） | < 10% | _____ % | ⬜ |
| メモリ使用量 | < 150MB | _____ MB | ⬜ |

**ツール**:
- macOS Activity Monitor（CPU、メモリ）
- Xcode Instruments Time Profiler（ホットスポット分析）
- Avalonia DevTools（UI レンダリング確認）

---

### 4. トリガー検出リアルタイムレイテンシ測定

**手順**:
1. 1000件キュープロジェクト読込
2. タイムコード内部生成で開始
3. LogViewのタイムスタンプを確認
4. キュートリガー発火時刻とログ記録時刻の差分を計測

**計測方法**:
```
トリガー時刻: 00:08:30:00
ログ記録時刻: 00:08:30:00.0002 (0.2ms後)
→ レイテンシ: 0.2ms ✅
```

**期待結果**:
- ✅ トリガー検出からログ記録まで < 1ms
- ✅ OSC送信までの総レイテンシ < 5ms（ネットワーク遅延含む）

---

## Avalonia UI 仮想化（VirtualizingStackPanel）

### CueListView 仮想化検証

**XAML実装**:
```xml
<DataGrid ItemsSource="{Binding Cues}"
          VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling">
    <!-- 1000件のキューを効率的に表示 -->
</DataGrid>
```

**検証項目**:
- ✅ 仮想化有効化確認（実際にレンダリングされるのは可視範囲のみ）
- ✅ スクロール時のメモリ増加 < 10MB
- ✅ スクロール時のCPU使用率 < 5%

**ツール**:
```bash
# Avalonia DevToolsで仮想化状態確認
# Visual Studioの「Live Visual Tree」相当
```

---

## パフォーマンス最適化ポイント

### 1. High Water Mark方式（CueManager）

```csharp
// 既にトリガー済みのキューはスキップ
foreach (var cue in Cues.Where(c => c.TriggerTimecode >= _highWaterMark))
{
    if (currentTimecode >= cue.TriggerTimecode && !cue.IsMuted)
    {
        CueTriggered?.Invoke(this, cue);
    }
}
_highWaterMark = currentTimecode;
```

**効果**:
- 1000件のキューでも、未トリガー範囲のみ探索
- タイムコードが進むほど探索範囲が減少

---

### 2. CompiledBindings（Avalonia）

```xml
<UserControl xmlns:vm="using:TimecodeBridge.ViewModels"
             x:DataType="vm:MainViewModel">
    <TextBlock Text="{Binding CurrentTimecodeDisplay}" />
</UserControl>
```

**効果**:
- リフレクションなしのバインディング（WPFより高速）
- 60fpsタイムコード更新でも遅延なし

---

### 3. Channel<T>ベース非同期処理

```csharp
// TimecodeEngineでの非同期タイムコード処理
private async Task ProcessTimecodeAsync(CancellationToken token)
{
    await foreach (var timecode in _timecodeChannel.Reader.ReadAllAsync(token))
    {
        // 別スレッドで処理、UIスレッドブロックなし
        ProcessTimecode(timecode);
    }
}
```

**効果**:
- UIスレッドブロックなし
- バックグラウンド処理でタイムコード更新

---

## 実測結果記録

### 自動テスト結果

実施日: ___________

```bash
$ dotnet test --filter "FullyQualifiedName~IntegrationTests"

Test Run Successful.
Total tests: 8
     Passed: 8

✅ CueManager_ShouldHandleLargeCueList_WithAcceptablePerformance
   トリガー検出レイテンシ: _____ ms (< 1ms)

✅ ProjectService_ShouldSaveAndLoadLargeProject_WithAcceptablePerformance
   保存時間: _____ ms (< 1000ms)
   読込時間: _____ ms (< 1000ms)
```

---

### 手動実機ベンチマーク結果

実施日: ___________
環境: macOS _____, CPU: _____, メモリ: _____ GB

| 項目 | 目標値 | 実測値 | 合否 |
|------|--------|--------|------|
| トリガー検出レイテンシ | < 1ms | _____ ms | ⬜ PASS / ⬜ FAIL |
| タイムコード更新FPS | 60fps | _____ fps | ⬜ PASS / ⬜ FAIL |
| CPU使用率（通常時） | < 10% | _____ % | ⬜ PASS / ⬜ FAIL |
| メモリ使用量 | < 150MB | _____ MB | ⬜ PASS / ⬜ FAIL |
| CueListViewスクロール遅延 | < 16ms | _____ ms | ⬜ PASS / ⬜ FAIL |

実施者: ___________

---

## トラブルシューティング

### パフォーマンスが目標に達しない場合

#### CPU使用率が10%を超える場合
1. Xcode Instruments Time Profilerでホットスポット分析
2. CompiledBindingsが有効か確認（x:DataType指定）
3. タイムコード更新頻度を調整（60fps → 30fps）

#### トリガー検出が1msを超える場合
1. High Water Mark方式が正しく機能しているか確認
2. キューリストのソート順序確認（TriggerTimecode昇順）
3. LINQ Where句の最適化

#### UI応答性が低下する場合
1. VirtualizingStackPanel有効化確認
2. DataGrid ItemsSource バインディングモード確認
3. Avalonia DevToolsでレンダリングボトルネック確認

---

## 関連ドキュメント

- [INTEGRATION_TEST_PLAN.md](./INTEGRATION_TEST_PLAN.md) - 統合テスト計画
- [IntegrationTests.cs](./IntegrationTests.cs) - 自動テスト実装
- 要件ドキュメント: `.kiro/specs/mac-app/requirements.md` (要件13.5)
