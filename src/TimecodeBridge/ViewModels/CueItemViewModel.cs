using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TimecodeBridge.Models;

namespace TimecodeBridge.ViewModels;

public partial class CueItemViewModel : ObservableObject
{
    private DispatcherTimer? _triggerTimer;

    public string Id { get; }
    public string Memo { get; private set; }
    public bool SendTriggerTimeAsSeconds { get; private set; }
    public TimecodeOffset? CueOffset { get; private set; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private TimecodeValue _triggerTime;
    [ObservableProperty] private string _oscAddress;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isTriggered;
    [ObservableProperty] private bool _isNextCue;

    public CueItemViewModel(Cue cue)
    {
        Id = cue.Id;
        _name = cue.Name;
        Memo = cue.Memo;
        _triggerTime = cue.TriggerTime;
        _oscAddress = cue.OscAddress;
        IsEnabled = cue.IsEnabled;
        SendTriggerTimeAsSeconds = cue.SendTriggerTimeAsSeconds;
        CueOffset = cue.CueOffset;
    }

    /// <summary>
    /// 編集結果をこのインスタンスへ反映する。項目を差し替えないことで
    /// ListViewの選択状態とハイライトを維持する。
    /// </summary>
    public void Update(Cue cue)
    {
        Name = cue.Name;
        Memo = cue.Memo;
        TriggerTime = cue.TriggerTime;
        OscAddress = cue.OscAddress;
        IsEnabled = cue.IsEnabled;
        SendTriggerTimeAsSeconds = cue.SendTriggerTimeAsSeconds;
        CueOffset = cue.CueOffset;
    }

    /// <summary>
    /// 発火フラッシュを開始する。UIスレッドから呼ぶこと。
    /// 連続発火時はタイマーを仕切り直し、ストーリーボード再始動のため一度falseへ落とす。
    /// </summary>
    public void FlashTriggered()
    {
        if (IsTriggered) IsTriggered = false;
        IsTriggered = true;

        _triggerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _triggerTimer.Stop();
        _triggerTimer.Tick -= OnTriggerTick;
        _triggerTimer.Tick += OnTriggerTick;
        _triggerTimer.Start();
    }

    private void OnTriggerTick(object? sender, EventArgs e)
    {
        IsTriggered = false;
        _triggerTimer?.Stop();
    }
}
