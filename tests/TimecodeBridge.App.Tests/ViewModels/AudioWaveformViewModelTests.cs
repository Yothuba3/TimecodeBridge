using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Tests.ViewModels;

public class AudioWaveformViewModelTests
{
    private sealed class SampleSource : ITimecodeEngine
    {
        public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
        public void Feed(float[] samples) => AudioSamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));

        public TimecodeValue CurrentRawTimecode => default;
        public TimecodeValue CurrentOffsetTimecode => default;
        public TimecodeOffset Offset { get; set; }
        public FrameRate FrameRate { get; set; }
        public TimecodeSourceType ActiveSource => TimecodeSourceType.Ltc;
        public bool IsReceiving => false;
        public double FreerunDurationSeconds { get; set; }
        public bool IsFreerunning => false;
        public LtcSignalCounts LtcSignalCounts => default;
        public bool LtcAutoRecoverOnSignalLoss { get; set; } = true;
        public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
        public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
        public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
        public void StartGenerator(GeneratorSettings settings) { }
        public void ResumeGenerator() { }
        public void ResetGenerator() { }
        public void ResetGenerator(TimecodeValue startTime) { }
        public void StopGenerator() { }
        public void Stop() { }
    }

    [Fact]
    public void 末尾の生サンプルを時系列順に取り出す()
    {
        var src = new SampleSource();
        var vm = new AudioWaveformViewModel(src);

        // 0,1,2,...,999 を流す
        var input = new float[1000];
        for (int i = 0; i < input.Length; i++) input[i] = i;
        src.Feed(input);

        var buf = new float[10];
        vm.CopyRecent(buf, 10);

        // 直近10サンプル = 990..999
        for (int i = 0; i < 10; i++) Assert.Equal(990 + i, buf[i]);
    }

    [Fact]
    public void 間引かないのでピークを取りこぼさない()
    {
        var src = new SampleSource();
        var vm = new AudioWaveformViewModel(src);

        // ほぼ0だが1点だけ1.0のスパイクを含む区間
        var input = new float[800];
        input[400] = 1.0f;
        src.Feed(input);

        var buf = new float[800];
        vm.CopyRecent(buf, 800);

        Assert.Contains(1.0f, buf);
    }

    [Fact]
    public void 貯まりきっていないと先頭を無音で埋める()
    {
        var src = new SampleSource();
        var vm = new AudioWaveformViewModel(src);

        src.Feed([0.5f, 0.6f, 0.7f]); // 3サンプルだけ

        var buf = new float[10];
        vm.CopyRecent(buf, 10);

        // 先頭7つは0、末尾3つが実データ
        for (int i = 0; i < 7; i++) Assert.Equal(0f, buf[i]);
        Assert.Equal(0.5f, buf[7]);
        Assert.Equal(0.6f, buf[8]);
        Assert.Equal(0.7f, buf[9]);
    }

    [Fact]
    public void ConsumeUpdateはピークを返し2回目はfalse()
    {
        var src = new SampleSource();
        var vm = new AudioWaveformViewModel(src);

        src.Feed([0.1f, -0.8f, 0.3f]);

        Assert.True(vm.ConsumeUpdate(out var peak));
        Assert.Equal(0.8f, peak, 3);
        Assert.False(vm.ConsumeUpdate(out _)); // 新データ無し
    }
}
