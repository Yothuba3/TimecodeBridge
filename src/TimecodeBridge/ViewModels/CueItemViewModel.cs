using CommunityToolkit.Mvvm.ComponentModel;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.ViewModels;

public partial class CueItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Memo { get; }
    public TimecodeValue TriggerTime { get; }
    public string OscAddress { get; }
    public bool SendTriggerTimeAsSeconds { get; }
    public TimecodeOffset? CueOffset { get; }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isTriggered;
    [ObservableProperty] private bool _isNextCue;

    public CueItemViewModel(Cue cue)
    {
        Id = cue.Id;
        Name = cue.Name;
        Memo = cue.Memo;
        TriggerTime = cue.TriggerTime;
        OscAddress = cue.OscAddress;
        IsEnabled = cue.IsEnabled;
        SendTriggerTimeAsSeconds = cue.SendTriggerTimeAsSeconds;
        CueOffset = cue.CueOffset;
    }
}
