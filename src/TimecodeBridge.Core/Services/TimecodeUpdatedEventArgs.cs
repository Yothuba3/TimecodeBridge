using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// Event arguments for timecode update notifications.
/// </summary>
public class TimecodeUpdatedEventArgs : EventArgs
{
    public TimecodeValue RawTimecode { get; }
    public TimecodeValue OffsetTimecode { get; }

    public TimecodeUpdatedEventArgs(TimecodeValue rawTimecode, TimecodeValue offsetTimecode)
    {
        RawTimecode = rawTimecode;
        OffsetTimecode = offsetTimecode;
    }
}
