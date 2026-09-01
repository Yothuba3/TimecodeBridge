using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Tests.Services;

public class AudioDeviceServiceTests
{
    private readonly AudioDeviceService _service;

    public AudioDeviceServiceTests()
    {
        _service = new AudioDeviceService();
    }

    [Fact]
    public void AudioDeviceService_ImplementsIAudioDeviceService()
    {
        // IAudioDeviceService インターフェースを実装していることを確認
        Assert.IsAssignableFrom<IAudioDeviceService>(_service);
    }

    [Fact]
    public void GetCaptureDevices_ReturnsListWithoutException()
    {
        // 例外が発生せずリストが返されることを確認（実デバイスの有無は問わない）
        var result = _service.GetCaptureDevices();

        Assert.NotNull(result);
        Assert.IsAssignableFrom<IReadOnlyList<AudioDeviceInfo>>(result);
    }

    [Fact]
    public void GetRenderDevices_ReturnsListWithoutException()
    {
        // 例外が発生せずリストが返されることを確認
        var result = _service.GetRenderDevices();

        Assert.NotNull(result);
        Assert.IsAssignableFrom<IReadOnlyList<AudioDeviceInfo>>(result);
    }

    [Fact]
    public void GetRenderDevices_AllDevicesAreLoopback()
    {
        // レンダーデバイスはすべて IsLoopback=true であること
        var result = _service.GetRenderDevices();

        foreach (var device in result)
        {
            Assert.True(device.IsLoopback, $"デバイス '{device.DisplayName}' の IsLoopback が false です");
            Assert.Contains("(Loopback)", device.DisplayName);
        }
    }

    [Fact]
    public void GetCaptureDevices_AllDevicesAreNotLoopback()
    {
        // キャプチャデバイスはすべて IsLoopback=false であること
        var result = _service.GetCaptureDevices();

        foreach (var device in result)
        {
            Assert.False(device.IsLoopback, $"デバイス '{device.DisplayName}' の IsLoopback が true です");
        }
    }
}
