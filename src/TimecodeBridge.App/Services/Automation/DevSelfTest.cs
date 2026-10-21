using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.App.Services.CoreAudio;
using TimecodeBridge.App.ViewModels;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.Services.Automation;

/// <summary>
/// 実機確認用の LTC 自己テスト(macOS)。環境変数 TIMECODEBRIDGE_SELFTEST_OUTPUT=&lt;出力デバイス名の一部&gt; があれば、
/// アプリ本体の TimecodeEngine とは別のエンジンで同じプロセス内から LTC を出し続ける(Pro Tools Audio Bridge 等の
/// ループバックデバイスで、そのまま入力に戻る)。TIMECODEBRIDGE_SELFTEST_START=HH:MM:SS:FF で開始時刻を変えられる。
/// TIMECODEBRIDGE_AUTOSTART_INPUT=&lt;入力デバイス名の一部&gt; があれば、起動後にその入力を選んで受信を始める。
/// 環境変数が無ければ何もしない。
/// </summary>
public sealed class DevSelfTest : IDisposable
{
    public const string OutputVariable = "TIMECODEBRIDGE_SELFTEST_OUTPUT";
    public const string StartVariable = "TIMECODEBRIDGE_SELFTEST_START";
    public const string AutoStartInputVariable = "TIMECODEBRIDGE_AUTOSTART_INPUT";

    private TimecodeEngine? _generator;

    public static DevSelfTest? StartIfRequested(IServiceProvider services, MainViewModel viewModel)
    {
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        var input = Environment.GetEnvironmentVariable(AutoStartInputVariable);
        if (string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(input)) return null;
        var selfTest = new DevSelfTest();
        // デバイス列挙と VM の初期化が終わった後に動かす
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(output)) selfTest.StartGenerator(services, output);
                if (!string.IsNullOrWhiteSpace(input)) SelectInput(viewModel, input);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[selftest] failed: {ex.Message}");
            }
        }, DispatcherPriority.Background);
        return selfTest;
    }

    private void StartGenerator(IServiceProvider services, string outputNeedle)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.WriteLine("[selftest] 自己テスト LTC は macOS(CoreAudio)だけに対応しています");
            return;
        }
        var devices = services.GetRequiredService<IAudioDeviceService>();
        var output = devices.GetRenderDevices().FirstOrDefault(d => d.DisplayName.Contains(outputNeedle, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"出力デバイスが見つかりません: {outputNeedle}");
        var start = ParseStart(Environment.GetEnvironmentVariable(StartVariable)) ?? new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        _generator = new TimecodeEngine(FrameRate.Fps30, devices, () => new CoreAudioCapture(), () => new CoreAudioPlayback());
        _generator.AudioErrorOccurred += (_, e) => Console.WriteLine($"[selftest] audio error: {e.Message}");
        _generator.StartGenerator(new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = start,
            OutputDeviceId = output.Id,
            VolumeLevel = 0.8f,
        });
        Console.WriteLine($"[selftest] generating LTC from {start} on {output.DisplayName}");
    }

    private static void SelectInput(MainViewModel viewModel, string inputNeedle)
    {
        var tc = viewModel.TimecodeViewModel;
        var device = tc.AudioDevices.FirstOrDefault(d => d.DisplayName.Contains(inputNeedle, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"入力デバイスが見つかりません: {inputNeedle}(候補: {string.Join(", ", tc.AudioDevices.Select(d => d.DisplayName))})");
        tc.IsLtcMode = true;
        tc.SelectedDevice = device;
        Console.WriteLine($"[selftest] receiving LTC on {device.DisplayName}");
    }

    /// <summary>"HH:MM:SS:FF"(30fps)。形式が違えば null。</summary>
    public static TimecodeValue? ParseStart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(':');
        if (parts.Length != 4) return null;
        var n = new int[4];
        for (var i = 0; i < 4; i++) if (!int.TryParse(parts[i], out n[i])) return null;
        if (n[0] is < 0 or > 23 || n[1] is < 0 or > 59 || n[2] is < 0 or > 59 || n[3] is < 0 or > 29) return null;
        return new TimecodeValue(n[0], n[1], n[2], n[3], FrameRate.Fps30);
    }

    public void Dispose()
    {
        try { _generator?.StopGenerator(); _generator?.Dispose(); } catch { /* 終了中 */ }
        _generator = null;
    }
}
