using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// Event arguments for timecode status change notifications.
/// </summary>
public class TimecodeStatusChangedEventArgs : EventArgs
{
    public TimecodeReceiveStatus Status { get; }

    /// <summary>
    /// Backward-compatible property: true when Receiving or Freerunning.
    /// </summary>
    public bool IsReceiving => Status != TimecodeReceiveStatus.NotReceiving;

    public TimecodeStatusChangedEventArgs(TimecodeReceiveStatus status)
    {
        Status = status;
    }

    public TimecodeStatusChangedEventArgs(bool isReceiving)
        : this(isReceiving ? TimecodeReceiveStatus.Receiving : TimecodeReceiveStatus.NotReceiving)
    {
    }
}
