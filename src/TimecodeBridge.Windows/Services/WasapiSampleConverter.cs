using NAudio.Wave;

namespace TimecodeBridge.Windows.Services;

/// <summary>WASAPI から届く PCM バッファを、先頭チャンネルの float モノラル(−1..1)へ揃える。</summary>
public static class WasapiSampleConverter
{
    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT(WAVEFORMATEXTENSIBLE の SubFormat)
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    public static float[] ToMonoFloat(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int channels = Math.Max(1, format.Channels);
        int bytesPerSample = format.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        if (bytesPerSample == 0 || bytesRecorded <= 0) return Array.Empty<float>();
        int frames = Math.Min(bytesRecorded, buffer.Length) / frameBytes;
        var result = new float[frames];
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubtype);

        for (int i = 0; i < frames; i++)
        {
            int o = i * frameBytes;
            float v = format.BitsPerSample switch
            {
                32 when isFloat => BitConverter.ToSingle(buffer, o),
                32 => BitConverter.ToInt32(buffer, o) / 2147483648f,
                24 => ((buffer[o + 2] << 24) | (buffer[o + 1] << 16) | (buffer[o] << 8)) / 2147483648f,
                16 => BitConverter.ToInt16(buffer, o) / 32768f,
                8 => (buffer[o] - 128) / 128f,
                _ => 0f,
            };
            // 整数 PCM を float と誤読した場合などの非有限値は 0 にする(デコーダの永久停止を防ぐ)
            result[i] = float.IsFinite(v) ? Math.Clamp(v, -1f, 1f) : 0f;
        }
        return result;
    }
}
