using TimecodeBridge.Models;

namespace TimecodeBridge.Services.Interfaces;

public interface ILtcDecoder : IDisposable
{
    /// <summary>
    /// Processes raw audio samples from a capture device and decodes LTC frames.
    /// </summary>
    /// <param name="is32BitFloat">
    /// 32bit入力の解釈。true=IEEE float、false=符号付き整数PCM。16bit時は無視される。
    /// 整数PCMをfloatとして誤読すると NaN/Inf が混入しデコードが永久停止するため、
    /// 呼び出し側は入力フォーマットに合わせて必ず指定する。
    /// </param>
    void ProcessSamples(byte[] buffer, int bytesRecorded, int sampleRate, int bitsPerSample, int channels, bool is32BitFloat = true);

    /// <summary>
    /// Raised when a valid LTC frame is decoded from the audio stream.
    /// </summary>
    event EventHandler<TimecodeValue> FrameDecoded;
}
