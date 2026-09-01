using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface ILtcDecoder : IDisposable
{
    /// <summary>
    /// Processes raw audio samples from a capture device and decodes LTC frames.
    /// </summary>
    void ProcessSamples(byte[] buffer, int bytesRecorded, int sampleRate, int bitsPerSample, int channels);

    /// <summary>
    /// Raised when a valid LTC frame is decoded from the audio stream.
    /// </summary>
    event EventHandler<TimecodeValue> FrameDecoded;
}
