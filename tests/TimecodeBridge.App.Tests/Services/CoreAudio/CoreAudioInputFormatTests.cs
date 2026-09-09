using TimecodeBridge.App.Services.CoreAudio;

namespace TimecodeBridge.App.Tests.Services.CoreAudio;

public class CoreAudioInputFormatTests
{
    [Fact]
    public void CreateLtcFormat_指定したサンプルレートのMono16bitPCMになる()
    {
        var asbd = CoreAudioInterop.CreateLtcFormat(44100.0);

        Assert.Equal(44100.0, asbd.SampleRate);
        Assert.Equal(1u, asbd.ChannelsPerFrame);
        Assert.Equal(16u, asbd.BitsPerChannel);
        Assert.Equal(2u, asbd.BytesPerFrame);
    }

    [Fact]
    public void 入力コールバックはAUHAL専用のSetInputCallbackを使う()
    {
        // 出力用の SetRenderCallback(23) を入力に使うとコールバックが一度も呼ばれない
        Assert.Equal(2005u, CoreAudioInterop.kAudioOutputUnitProperty_SetInputCallback);
        Assert.NotEqual(CoreAudioInterop.kAudioUnitProperty_SetRenderCallback, CoreAudioInterop.kAudioOutputUnitProperty_SetInputCallback);
    }
}
