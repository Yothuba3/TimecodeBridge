namespace TimecodeBridge.Core.Services;

public class AudioSamplesEventArgs : EventArgs
{
    public float[] Samples { get; }

    public AudioSamplesEventArgs(float[] samples)
    {
        Samples = samples;
    }
}
