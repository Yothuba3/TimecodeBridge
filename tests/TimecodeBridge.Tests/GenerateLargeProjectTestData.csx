#!/usr/bin/env dotnet script
// 1000件キュープロジェクトファイル生成スクリプト
// 実行方法: dotnet script GenerateLargeProjectTestData.csx
// または: cd /Users/yothuba/TimecodeBridge/tests/TimecodeBridge.Tests && dotnet test --filter GenerateLargeTestData

#r "../../src/TimecodeBridge.Core/bin/Debug/net8.0/TimecodeBridge.Core.dll"

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TimecodeBridge.Core.Models;

var outputPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "TestData",
    "sample_project_1000_cues.json"
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
Console.WriteLine("🔧 1000件のキューを生成中...");

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

    if ((i + 1) % 100 == 0)
    {
        Console.WriteLine($"  進捗: {i + 1}/1000 キュー生成完了");
    }
}

// ディレクトリ作成
var directory = Path.GetDirectoryName(outputPath);
if (!Directory.Exists(directory))
{
    Directory.CreateDirectory(directory!);
}

// JSON出力
Console.WriteLine("💾 JSONファイルを書き込み中...");
var jsonOptions = ProjectData.CreateJsonOptions();
var json = JsonSerializer.Serialize(largeProject, jsonOptions);

File.WriteAllText(outputPath, json);

Console.WriteLine($"✅ 1000件キュープロジェクトファイルを生成しました");
Console.WriteLine($"   出力先: {outputPath}");
Console.WriteLine($"   ファイルサイズ: {new FileInfo(outputPath).Length / 1024} KB");
