using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface ILtcEncoder
{
    float VolumeLevel { get; set; }
    Models.WaveFormat WaveFormat { get; }

    void Initialize(int sampleRate, FrameRate frameRate);
    void EnqueueFrame(TimecodeValue frame);
    void Reset();
    int Read(byte[] buffer, int offset, int count);
}
