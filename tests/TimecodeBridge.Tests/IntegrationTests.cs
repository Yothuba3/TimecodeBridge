using System.Text.Json;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using Xunit;

namespace TimecodeBridge.Tests;

/// <summary>
/// 全機能統合テスト（Task 7）
/// キュー作成 → タイムコード到達 → OSC送信のE2E確認
/// プロジェクト保存 → 読込の確認
/// Windows版プロジェクトファイルとの互換性確認
/// 1000件キュー登録時のパフォーマンス確認
/// </summary>
public class IntegrationTests
{
    #region E2E Tests: Cue Creation → Timecode → OSC Trigger

    [Fact]
    public void CueManager_ShouldTriggerCue_WhenTimecodeReachesTriggerTime()
    {
        // Arrange
        var cueManager = new CueManager();
        var triggeredCues = new List<Cue>();

        cueManager.CueTriggered += (sender, cue) => triggeredCues.Add(cue);

        var testCue = new Cue
        {
            Id = Guid.NewGuid(),
            Name = "Test Cue",
            TriggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            OscAddress = "/cue/1",
            OscArguments = new List<OscArgument>
            {
                new OscStringArgument("GO")
            },
            IsMuted = false
        };

        cueManager.Cues.Add(testCue);

        // Act: タイムコード更新をシミュレート
        var beforeTimecode = new TimecodeValue(0, 0, 9, 29, FrameRate.Fps30);
        var triggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        var afterTimecode = new TimecodeValue(0, 0, 10, 1, FrameRate.Fps30);

        cueManager.CheckTriggers(beforeTimecode);
        Assert.Empty(triggeredCues); // まだトリガーされていない

        cueManager.CheckTriggers(triggerTimecode);
        Assert.Single(triggeredCues); // トリガーされた
        Assert.Equal(testCue.Id, triggeredCues[0].Id);

        cueManager.CheckTriggers(afterTimecode);
        Assert.Single(triggeredCues); // 重複トリガーされていない
    }

    [Fact]
    public void CueManager_ShouldNotTrigger_WhenCueIsMuted()
    {
        // Arrange
        var cueManager = new CueManager();
        var triggeredCues = new List<Cue>();

        cueManager.CueTriggered += (sender, cue) => triggeredCues.Add(cue);

        var mutedCue = new Cue
        {
            Id = Guid.NewGuid(),
            Name = "Muted Cue",
            TriggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            OscAddress = "/cue/muted",
            IsMuted = true
        };

        cueManager.Cues.Add(mutedCue);

        // Act
        var triggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        cueManager.CheckTriggers(triggerTimecode);

        // Assert
        Assert.Empty(triggeredCues); // ミュートされているのでトリガーされない
    }

    [Fact]
    public void CueManager_ShouldResetHighWaterMark_OnManualReset()
    {
        // Arrange
        var cueManager = new CueManager();
        var triggeredCues = new List<Cue>();

        cueManager.CueTriggered += (sender, cue) => triggeredCues.Add(cue);

        var testCue = new Cue
        {
            Id = Guid.NewGuid(),
            Name = "Reset Test",
            TriggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            OscAddress = "/cue/reset",
            IsMuted = false
        };

        cueManager.Cues.Add(testCue);

        // Act: 最初のトリガー
        var triggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        cueManager.CheckTriggers(triggerTimecode);
        Assert.Single(triggeredCues);

        // リセット
        cueManager.Reset();
        triggeredCues.Clear();

        // 再度トリガー
        cueManager.CheckTriggers(triggerTimecode);

        // Assert: リセット後は再トリガー可能
        Assert.Single(triggeredCues);
    }

    [Fact]
    public void OscSender_ShouldFormatCueAsOscMessage()
    {
        // Arrange
        var oscSender = new OscSender(new HostRegistry());
        var testCue = new Cue
        {
            Id = Guid.NewGuid(),
            Name = "OSC Test",
            TriggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            OscAddress = "/test/address",
            OscArguments = new List<OscArgument>
            {
                new OscInt32Argument(42),
                new OscFloat32Argument(3.14f),
                new OscStringArgument("Hello")
            },
            IsMuted = false
        };

        // Act: メッセージ送信（実際の送信はホストが無効なので行われない）
        // ここでは例外が発生しないことを確認
        var exception = Record.Exception(() => oscSender.SendCue(testCue));

        // Assert
        Assert.Null(exception); // エラーなく処理される
    }

    #endregion

    #region Project Save/Load Tests

    [Fact]
    public void ProjectService_ShouldSaveAndLoadProject_WithFullData()
    {
        // Arrange
        var projectService = new ProjectService();
        var testProjectPath = Path.Combine(Path.GetTempPath(), $"test_project_{Guid.NewGuid()}.json");

        var originalProject = new ProjectData
        {
            Cues = new List<Cue>
            {
                new Cue
                {
                    Id = Guid.NewGuid(),
                    Name = "Cue 1",
                    TriggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                    OscAddress = "/cue/1",
                    OscArguments = new List<OscArgument> { new OscStringArgument("GO") },
                    IsMuted = false
                },
                new Cue
                {
                    Id = Guid.NewGuid(),
                    Name = "Cue 2",
                    TriggerTimecode = new TimecodeValue(0, 0, 20, 15, FrameRate.Fps30),
                    OscAddress = "/cue/2",
                    OscArguments = new List<OscArgument>
                    {
                        new OscInt32Argument(100),
                        new OscFloat32Argument(0.5f)
                    },
                    IsMuted = true
                }
            },
            Hosts = new List<OscHost>
            {
                new OscHost
                {
                    Id = Guid.NewGuid(),
                    Name = "QLab",
                    IpAddress = "192.168.1.100",
                    Port = 53000,
                    IsEnabled = true
                }
            },
            Offset = new TimecodeOffset(hours: 1, minutes: 30, seconds: 45, frames: 10),
            RelaySettings = new RelaySettings
            {
                InputDeviceId = "input-device-1",
                OutputDeviceId = "output-device-1"
            },
            SourceSettings = new TimecodeSourceSettings
            {
                FrameRate = FrameRate.Fps30,
                StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30)
            }
        };

        try
        {
            // Act: 保存
            projectService.SaveProject(testProjectPath, originalProject);
            Assert.True(File.Exists(testProjectPath), "プロジェクトファイルが作成されていません");

            // Act: 読込
            var loadedProject = projectService.LoadProject(testProjectPath);

            // Assert: 完全な一致を確認
            Assert.NotNull(loadedProject);
            Assert.Equal(originalProject.Cues.Count, loadedProject.Cues.Count);
            Assert.Equal(originalProject.Hosts.Count, loadedProject.Hosts.Count);

            // Cue検証
            var originalCue = originalProject.Cues[0];
            var loadedCue = loadedProject.Cues[0];
            Assert.Equal(originalCue.Name, loadedCue.Name);
            Assert.Equal(originalCue.TriggerTimecode.ToFrames(), loadedCue.TriggerTimecode.ToFrames());
            Assert.Equal(originalCue.OscAddress, loadedCue.OscAddress);
            Assert.Equal(originalCue.IsMuted, loadedCue.IsMuted);

            // Host検証
            var originalHost = originalProject.Hosts[0];
            var loadedHost = loadedProject.Hosts[0];
            Assert.Equal(originalHost.Name, loadedHost.Name);
            Assert.Equal(originalHost.IpAddress, loadedHost.IpAddress);
            Assert.Equal(originalHost.Port, loadedHost.Port);
            Assert.Equal(originalHost.IsEnabled, loadedHost.IsEnabled);

            // Offset検証
            Assert.Equal(originalProject.Offset.Hours, loadedProject.Offset.Hours);
            Assert.Equal(originalProject.Offset.Minutes, loadedProject.Offset.Minutes);
            Assert.Equal(originalProject.Offset.Seconds, loadedProject.Offset.Seconds);
            Assert.Equal(originalProject.Offset.Frames, loadedProject.Offset.Frames);
        }
        finally
        {
            // Cleanup
            if (File.Exists(testProjectPath))
            {
                File.Delete(testProjectPath);
            }
        }
    }

    [Fact]
    public void ProjectService_ShouldLoadProject_WithDifferentOscArgumentTypes()
    {
        // Arrange
        var projectService = new ProjectService();
        var testProjectPath = Path.Combine(Path.GetTempPath(), $"test_osc_args_{Guid.NewGuid()}.json");

        var originalProject = new ProjectData
        {
            Cues = new List<Cue>
            {
                new Cue
                {
                    Id = Guid.NewGuid(),
                    Name = "Mixed Args Cue",
                    TriggerTimecode = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
                    OscAddress = "/mixed/args",
                    OscArguments = new List<OscArgument>
                    {
                        new OscInt32Argument(123),
                        new OscFloat32Argument(45.67f),
                        new OscStringArgument("Test String")
                    },
                    IsMuted = false
                }
            }
        };

        try
        {
            // Act
            projectService.SaveProject(testProjectPath, originalProject);
            var loadedProject = projectService.LoadProject(testProjectPath);

            // Assert
            Assert.NotNull(loadedProject);
            Assert.Single(loadedProject.Cues);

            var loadedCue = loadedProject.Cues[0];
            Assert.Equal(3, loadedCue.OscArguments.Count);

            Assert.IsType<OscInt32Argument>(loadedCue.OscArguments[0]);
            Assert.Equal(123, ((OscInt32Argument)loadedCue.OscArguments[0]).Value);

            Assert.IsType<OscFloat32Argument>(loadedCue.OscArguments[1]);
            Assert.Equal(45.67f, ((OscFloat32Argument)loadedCue.OscArguments[1]).Value, precision: 2);

            Assert.IsType<OscStringArgument>(loadedCue.OscArguments[2]);
            Assert.Equal("Test String", ((OscStringArgument)loadedCue.OscArguments[2]).Value);
        }
        finally
        {
            if (File.Exists(testProjectPath))
            {
                File.Delete(testProjectPath);
            }
        }
    }

    #endregion

    #region Windows Compatibility Tests

    [Fact]
    public void ProjectData_ShouldBeCompatible_WithWindowsJsonFormat()
    {
        // Arrange: Windows版互換のJSONを作成
        var windowsJson = @"{
  ""cues"": [
    {
      ""id"": ""00000000-0000-0000-0000-000000000001"",
      ""name"": ""Windows Cue"",
      ""triggerTimecode"": {
        ""hours"": 0,
        ""minutes"": 1,
        ""seconds"": 30,
        ""frames"": 15,
        ""frameRate"": {
          ""rate"": 30.0,
          ""dropFrame"": false
        }
      },
      ""oscAddress"": ""/windows/test"",
      ""oscArguments"": [
        {
          ""type"": ""string"",
          ""value"": ""WIN""
        }
      ],
      ""isMuted"": false
    }
  ],
  ""hosts"": [
    {
      ""id"": ""00000000-0000-0000-0000-000000000002"",
      ""name"": ""Windows Host"",
      ""ipAddress"": ""127.0.0.1"",
      ""port"": 8000,
      ""isEnabled"": true
    }
  ],
  ""relaySettings"": {
    ""inputDeviceId"": ""default"",
    ""outputDeviceId"": ""default""
  },
  ""offset"": {
    ""hours"": 0,
    ""minutes"": 0,
    ""seconds"": 0,
    ""frames"": 0
  },
  ""sourceSettings"": {
    ""frameRate"": {
      ""rate"": 30.0,
      ""dropFrame"": false
    },
    ""startTime"": {
      ""hours"": 0,
      ""minutes"": 0,
      ""seconds"": 0,
      ""frames"": 0,
      ""frameRate"": {
        ""rate"": 30.0,
        ""dropFrame"": false
      }
    }
  }
}";

        var testPath = Path.Combine(Path.GetTempPath(), $"windows_compat_{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(testPath, windowsJson);

            // Act: ProjectServiceで読込
            var projectService = new ProjectService();
            var loadedProject = projectService.LoadProject(testPath);

            // Assert: Windows版のデータが正しく読み込まれる
            Assert.NotNull(loadedProject);
            Assert.Single(loadedProject.Cues);
            Assert.Single(loadedProject.Hosts);

            var cue = loadedProject.Cues[0];
            Assert.Equal("Windows Cue", cue.Name);
            Assert.Equal("/windows/test", cue.OscAddress);
            Assert.Equal(1, cue.TriggerTimecode.Minutes);
            Assert.Equal(30, cue.TriggerTimecode.Seconds);
            Assert.Equal(15, cue.TriggerTimecode.Frames);

            var host = loadedProject.Hosts[0];
            Assert.Equal("Windows Host", host.Name);
            Assert.Equal("127.0.0.1", host.IpAddress);
            Assert.Equal(8000, host.Port);
        }
        finally
        {
            if (File.Exists(testPath))
            {
                File.Delete(testPath);
            }
        }
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void CueManager_ShouldHandleLargeCueList_WithAcceptablePerformance()
    {
        // Arrange: 1000件のキュー作成
        var cueManager = new CueManager();
        var triggeredCues = new List<Cue>();

        cueManager.CueTriggered += (sender, cue) => triggeredCues.Add(cue);

        for (int i = 0; i < 1000; i++)
        {
            var cue = new Cue
            {
                Id = Guid.NewGuid(),
                Name = $"Performance Test Cue {i}",
                TriggerTimecode = new TimecodeValue(0, i / 60, i % 60, 0, FrameRate.Fps30),
                OscAddress = $"/perf/cue/{i}",
                OscArguments = new List<OscArgument>
                {
                    new OscInt32Argument(i)
                },
                IsMuted = false
            };
            cueManager.Cues.Add(cue);
        }

        // Act: トリガー検出のパフォーマンス計測
        var testTimecode = new TimecodeValue(0, 8, 30, 0, FrameRate.Fps30);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        cueManager.CheckTriggers(testTimecode);

        stopwatch.Stop();

        // Assert: トリガー検出レイテンシ < 1ms（要件13.5）
        Assert.True(stopwatch.ElapsedMilliseconds < 1,
            $"1000件キュー登録時のトリガー検出が1ms以上かかりました: {stopwatch.ElapsedMilliseconds}ms");

        // 該当するキューがトリガーされたことを確認
        Assert.Single(triggeredCues);
        Assert.Equal("/perf/cue/510", triggeredCues[0].OscAddress); // 8分30秒 = 510秒
    }

    [Fact]
    public void ProjectService_ShouldSaveAndLoadLargeProject_WithAcceptablePerformance()
    {
        // Arrange: 1000件のキューを持つプロジェクト
        var projectService = new ProjectService();
        var testProjectPath = Path.Combine(Path.GetTempPath(), $"large_project_{Guid.NewGuid()}.json");

        var largeProject = new ProjectData
        {
            Cues = new List<Cue>()
        };

        for (int i = 0; i < 1000; i++)
        {
            largeProject.Cues.Add(new Cue
            {
                Id = Guid.NewGuid(),
                Name = $"Large Project Cue {i}",
                TriggerTimecode = new TimecodeValue(0, i / 60, i % 60, i % 30, FrameRate.Fps30),
                OscAddress = $"/large/cue/{i}",
                OscArguments = new List<OscArgument>
                {
                    new OscInt32Argument(i),
                    new OscStringArgument($"Value_{i}")
                },
                IsMuted = i % 10 == 0 // 10件に1件ミュート
            });
        }

        try
        {
            // Act: 保存パフォーマンス計測
            var saveStopwatch = System.Diagnostics.Stopwatch.StartNew();
            projectService.SaveProject(testProjectPath, largeProject);
            saveStopwatch.Stop();

            // Act: 読込パフォーマンス計測
            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var loadedProject = projectService.LoadProject(testProjectPath);
            loadStopwatch.Stop();

            // Assert: 保存・読込が許容時間内に完了
            Assert.True(saveStopwatch.ElapsedMilliseconds < 1000,
                $"1000件キュープロジェクトの保存が1秒以上かかりました: {saveStopwatch.ElapsedMilliseconds}ms");

            Assert.True(loadStopwatch.ElapsedMilliseconds < 1000,
                $"1000件キュープロジェクトの読込が1秒以上かかりました: {loadStopwatch.ElapsedMilliseconds}ms");

            // データ整合性確認
            Assert.Equal(1000, loadedProject.Cues.Count);
            Assert.Equal(largeProject.Cues[0].Name, loadedProject.Cues[0].Name);
            Assert.Equal(largeProject.Cues[999].Name, loadedProject.Cues[999].Name);
        }
        finally
        {
            if (File.Exists(testProjectPath))
            {
                File.Delete(testProjectPath);
            }
        }
    }

    #endregion
}
