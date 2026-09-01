using System.Text.Json;
using TimecodeBridge.Core.Models;
using Xunit;

namespace TimecodeBridge.Tests;

/// <summary>
/// 1000件キュープロジェクトファイル生成ユーティリティ
/// テスト実行時に1回だけ実行してTestDataディレクトリに出力
/// </summary>
public class LargeProjectTestDataGenerator
{
    [Fact(Skip = "手動実行用: 1000件キュープロジェクトファイル生成")]
    public void GenerateLargeProjectWithThousandCues()
    {
        // Arrange
        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", // tests/TimecodeBridge.Tests/bin/Debug/net8.0 から戻る
            "TestData",
            "sample_project_1000_cues_generated.json"
        );

        var largeProject = new ProjectData
        {
            Cues = new List<Cue>(),
            Hosts = new List<OscHost>
            {
                new OscHost
                {
                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
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

        // ディレクトリ作成
        var directory = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        // Act: JSON出力
        var jsonOptions = ProjectData.CreateJsonOptions();
        var json = JsonSerializer.Serialize(largeProject, jsonOptions);
        File.WriteAllText(outputPath, json);

        // Assert
        Assert.True(File.Exists(outputPath), $"ファイルが生成されていません: {outputPath}");

        var fileInfo = new FileInfo(outputPath);
        Assert.True(fileInfo.Length > 100_000, "ファイルサイズが小さすぎます（100KB未満）");

        // 検証: 読み込んで件数確認
        var projectService = new ProjectService();
        var loadedProject = projectService.LoadProject(outputPath);
        Assert.Equal(1000, loadedProject.Cues.Count);

        Console.WriteLine($"✅ 1000件キュープロジェクトファイルを生成しました");
        Console.WriteLine($"   出力先: {outputPath}");
        Console.WriteLine($"   ファイルサイズ: {fileInfo.Length / 1024} KB");
    }

    /// <summary>
    /// Windows版互換形式のサンプルプロジェクト生成
    /// </summary>
    [Fact(Skip = "手動実行用: Windows版互換サンプルプロジェクト生成")]
    public void GenerateWindowsCompatibleSampleProject()
    {
        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..",
            "TestData",
            "sample_project_windows_compatible.json"
        );

        var project = new ProjectData
        {
            Cues = new List<Cue>
            {
                new Cue
                {
                    Id = Guid.NewGuid(),
                    Name = "Windows Compatible Cue 1",
                    TriggerTimecode = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                    OscAddress = "/qlab/cue/1/start",
                    OscArguments = new List<OscArgument>
                    {
                        new OscStringArgument("GO")
                    },
                    IsMuted = false
                },
                new Cue
                {
                    Id = Guid.NewGuid(),
                    Name = "Mixed Arguments Cue",
                    TriggerTimecode = new TimecodeValue(0, 1, 0, 0, FrameRate.Fps30),
                    OscAddress = "/mixer/channel/5/level",
                    OscArguments = new List<OscArgument>
                    {
                        new OscInt32Argument(5),
                        new OscFloat32Argument(0.75f)
                    },
                    IsMuted = false
                },
                new Cue
                {
                    Id = Guid.NewGuid(),
                    Name = "DropFrame Test",
                    TriggerTimecode = new TimecodeValue(0, 2, 0, 0, FrameRate.Fps2997DF),
                    OscAddress = "/test/dropframe",
                    OscArguments = new List<OscArgument>
                    {
                        new OscStringArgument("DF Test")
                    },
                    IsMuted = false
                }
            },
            Hosts = new List<OscHost>
            {
                new OscHost
                {
                    Id = Guid.NewGuid(),
                    Name = "QLab Main",
                    IpAddress = "192.168.1.100",
                    Port = 53000,
                    IsEnabled = true
                },
                new OscHost
                {
                    Id = Guid.NewGuid(),
                    Name = "Resolume Arena",
                    IpAddress = "192.168.1.101",
                    Port = 7000,
                    IsEnabled = false
                }
            },
            Offset = new TimecodeOffset(hours: 1, minutes: 30, seconds: 0, frames: 0),
            RelaySettings = new RelaySettings
            {
                InputDeviceId = "windows-audio-device-1",
                OutputDeviceId = "windows-audio-device-2"
            },
            SourceSettings = new TimecodeSourceSettings
            {
                FrameRate = FrameRate.Fps30,
                StartTime = new TimecodeValue(10, 0, 0, 0, FrameRate.Fps30)
            }
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        var jsonOptions = ProjectData.CreateJsonOptions();
        var json = JsonSerializer.Serialize(project, jsonOptions);
        File.WriteAllText(outputPath, json);

        Assert.True(File.Exists(outputPath));
        Console.WriteLine($"✅ Windows版互換サンプルプロジェクトを生成しました: {outputPath}");
    }
}
