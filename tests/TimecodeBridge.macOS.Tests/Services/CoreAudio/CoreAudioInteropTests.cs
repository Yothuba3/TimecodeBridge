using static TimecodeBridge.macOS.Services.CoreAudio.CoreAudioInterop;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using Xunit;
using TimecodeBridge.macOS.Services.CoreAudio;

namespace TimecodeBridge.Tests.Services.CoreAudio;

/// <summary>
/// CoreAudio P/Invoke署名のテスト
/// </summary>
public class CoreAudioInteropTests
{
    [Fact]
    public void AudioComponentDescription_ShouldHaveCorrectStructureSize()
    {
        // Arrange & Act
        var desc = new AudioComponentDescription
        {
            ComponentType = CoreAudioInterop.kAudioUnitType_Output,
            ComponentSubType = CoreAudioInterop.kAudioUnitSubType_HALOutput,
            ComponentManufacturer = CoreAudioInterop.kAudioUnitManufacturer_Apple,
            ComponentFlags = 0,
            ComponentFlagsMask = 0
        };

        // Assert
        Assert.NotEqual(0u, desc.ComponentType);
        Assert.NotEqual(0u, desc.ComponentSubType);
        Assert.NotEqual(0u, desc.ComponentManufacturer);
    }

    [Fact]
    public void AudioStreamBasicDescription_ShouldHaveCorrectStructureForLTC()
    {
        // Arrange - LTC標準フォーマット: 48kHz, Mono, 16bit PCM
        var asbd = new AudioStreamBasicDescription
        {
            SampleRate = 48000.0,
            FormatID = CoreAudioInterop.kAudioFormatLinearPCM,
            FormatFlags = CoreAudioInterop.kAudioFormatFlagIsSignedInteger | CoreAudioInterop.kAudioFormatFlagIsPacked,
            BytesPerPacket = 2,
            FramesPerPacket = 1,
            BytesPerFrame = 2,
            ChannelsPerFrame = 1,
            BitsPerChannel = 16,
            Reserved = 0
        };

        // Assert
        Assert.Equal(48000.0, asbd.SampleRate);
        Assert.Equal(1u, asbd.ChannelsPerFrame);
        Assert.Equal(16u, asbd.BitsPerChannel);
        Assert.Equal(2u, asbd.BytesPerFrame);
    }

    [Fact]
    public void AudioTimeStamp_ShouldHaveCorrectStructure()
    {
        // Arrange & Act
        var timestamp = new AudioTimeStamp
        {
            SampleTime = 0.0,
            HostTime = 0,
            RateScalar = 1.0,
            WordClockTime = 0,
            SMPTETime = default,
            Flags = CoreAudioInterop.kAudioTimeStampSampleTimeValid,
            Reserved = 0
        };

        // Assert
        Assert.Equal(1.0, timestamp.RateScalar);
        Assert.NotEqual(0u, timestamp.Flags);
    }

    [Fact]
    public void AudioBufferList_ShouldHaveCorrectStructure()
    {
        // Arrange & Act
        var bufferList = new AudioBufferList
        {
            NumberBuffers = 1
        };

        // Assert
        Assert.Equal(1u, bufferList.NumberBuffers);
    }

    [Fact]
    public void CoreAudioConstants_ShouldHaveValidValues()
    {
        // Assert - OSStatus success
        Assert.Equal(0, CoreAudioInterop.noErr);

        // Assert - Property IDs
        Assert.NotEqual(0u, CoreAudioInterop.kAudioUnitProperty_StreamFormat);
        Assert.NotEqual(0u, CoreAudioInterop.kAudioOutputUnitProperty_EnableIO);
        Assert.NotEqual(0u, CoreAudioInterop.kAudioUnitProperty_SetRenderCallback);

        // Assert - Scopes
        Assert.Equal(0u, CoreAudioInterop.kAudioUnitScope_Global);
        Assert.Equal(1u, CoreAudioInterop.kAudioUnitScope_Input);
        Assert.Equal(2u, CoreAudioInterop.kAudioUnitScope_Output);
    }
}
