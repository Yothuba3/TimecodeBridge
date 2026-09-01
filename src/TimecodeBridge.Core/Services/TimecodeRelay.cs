using System.Diagnostics;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

public class TimecodeRelay : ITimecodeRelay
{
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IOscSender _oscSender;
    private readonly Stopwatch _intervalStopwatch = new();

    public string OscAddressPattern { get; set; } = "/timecode";
    public RelayInterval ContinuousInterval { get; set; } = new(RelayIntervalMode.EveryFrame, 0);
    public IReadOnlyList<string> TargetHostIds { get; set; } = [];
    public bool IsContinuousEnabled { get; set; }

    public TimecodeRelay(ITimecodeEngine timecodeEngine, IOscSender oscSender)
    {
        _timecodeEngine = timecodeEngine;
        _oscSender = oscSender;

        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
        _timecodeEngine.StatusChanged += OnStatusChanged;
    }

    public void TriggerOneShot()
    {
        var timecode = _timecodeEngine.CurrentOffsetTimecode;
        SendTimecode(OscAddressPattern, timecode);
    }

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e)
    {
        if (!IsContinuousEnabled)
            return;

        if (ContinuousInterval.Mode == RelayIntervalMode.EveryFrame)
        {
            SendTimecode(OscAddressPattern, e.OffsetTimecode);
        }
        else // Custom interval
        {
            if (!_intervalStopwatch.IsRunning || _intervalStopwatch.ElapsedMilliseconds >= ContinuousInterval.IntervalMs)
            {
                SendTimecode(OscAddressPattern, e.OffsetTimecode);
                _intervalStopwatch.Restart();
            }
        }
    }

    private void OnStatusChanged(object? sender, TimecodeStatusChangedEventArgs e)
    {
        if (e.Status == Models.TimecodeReceiveStatus.NotReceiving)
        {
            // Signal lost (after freerun expired or freerun disabled) - send notification
            var lostAddress = OscAddressPattern + "/lost";
            _oscSender.Send(lostAddress, [], TargetHostIds);
        }
    }

    private void SendTimecode(string address, TimecodeValue timecode)
    {
        float totalSeconds = timecode.TotalFrames() / (float)timecode.FrameRate.FramesPerSecond();
        var arguments = new List<OscArgument> { new OscFloat32Argument(totalSeconds) };
        _oscSender.Send(address, arguments, TargetHostIds);
    }
}
