using TimecodeBridge.Core.Audio;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Mac;

namespace TimecodeBridge.Host.Services;

/// <summary>
/// 実機確認用。環境変数 TIMECODEBRIDGE_SELFTEST_OUTPUT=&lt;出力デバイス名の一部&gt; があれば、
/// 同一プロセス内で LTC(30fps, 01:00:00:00 から)をその出力へ流し続ける。
/// Pro Tools Audio Bridge などの仮想ループバックは、再生と取り込みが別プロセスだと乱れるため同一プロセスで行う。
/// </summary>
public static class DevSelfTest
{
    public const string EnvironmentVariable = "TIMECODEBRIDGE_SELFTEST_OUTPUT";

    public static IDisposable? StartIfRequested(IAudioDeviceService devices)
    {
        var name = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(name) || !OperatingSystem.IsMacOS()) return null;

        var output = devices.GetRenderDevices().FirstOrDefault(d => d.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (output is null)
        {
            Console.WriteLine($"[selftest] output device '{name}' not found");
            return null;
        }

        var engine = new TimecodeEngine(FrameRate.Fps30, devices, () => new CoreAudioCapture(), () => new CoreAudioPlayback());
        engine.StartGenerator(new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = output.Id,
            VolumeLevel = 0.8f,
        });
        Console.WriteLine($"[selftest] generating LTC -> {output.DisplayName} ({output.Id})");
        return engine;
    }
}
