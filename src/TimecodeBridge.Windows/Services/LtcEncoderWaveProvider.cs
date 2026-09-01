using NAudio.Wave;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Windows.Services;

/// <summary>
/// Core側のILtcEncoder（プラットフォーム非依存）をNAudioのIWaveProviderとして
/// WasapiOutへ接続するためのアダプタ。
/// </summary>
public class LtcEncoderWaveProvider : IWaveProvider
{
    private readonly ILtcEncoder _encoder;

    public LtcEncoderWaveProvider(ILtcEncoder encoder)
    {
        _encoder = encoder;
    }

    public WaveFormat WaveFormat => new(_encoder.WaveFormat.SampleRate, _encoder.WaveFormat.BitsPerSample, _encoder.WaveFormat.Channels);

    public int Read(byte[] buffer, int offset, int count) => _encoder.Read(buffer, offset, count);
}
