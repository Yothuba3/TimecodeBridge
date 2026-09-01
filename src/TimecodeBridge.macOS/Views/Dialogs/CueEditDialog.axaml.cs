using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.Core.Models;
using TimecodeBridge.macOS.Services;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views.Dialogs;

/// <summary>
/// キュー編集ダイアログ。OKで編集結果の Cue、キャンセルで null を返す。
/// </summary>
public partial class CueEditDialog : Window
{
    private readonly FrameRate _frameRate;
    private readonly List<HostSelection> _hostItems;

    public CueEditDialog() : this(
        new Cue { Id = string.Empty, Name = string.Empty, TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30), OscAddress = "/" },
        [],
        FrameRate.Fps30)
    {
    }

    public CueEditDialog(Cue cue, IReadOnlyList<OscHost> allHosts, FrameRate frameRate)
    {
        InitializeComponent();
        _frameRate = frameRate;

        NameBox.Text = cue.Name;
        HoursBox.Text = cue.TriggerTime.Hours.ToString("D2");
        MinutesBox.Text = cue.TriggerTime.Minutes.ToString("D2");
        SecondsBox.Text = cue.TriggerTime.Seconds.ToString("D2");
        FramesBox.Text = cue.TriggerTime.Frames.ToString("D2");
        OscAddressBox.Text = cue.OscAddress;
        OscArgsBox.Text = DialogInputs.FormatArguments(cue.Arguments);
        MemoBox.Text = cue.Memo;
        EnabledBox.IsChecked = cue.IsEnabled;
        SendTcSecondsBox.IsChecked = cue.SendTriggerTimeAsSeconds;

        var offset = cue.CueOffset ?? TimecodeOffset.Zero(frameRate);
        OffsetSignBox.SelectedIndex = offset.IsNegative ? 1 : 0;
        OffsetHoursBox.Text = offset.Hours.ToString("D2");
        OffsetMinutesBox.Text = offset.Minutes.ToString("D2");
        OffsetSecondsBox.Text = offset.Seconds.ToString("D2");
        OffsetFramesBox.Text = offset.Frames.ToString("D2");

        _hostItems = DialogInputs.ToHostSelections(allHosts, cue.TargetHostIds);
        HostList.ItemsSource = _hostItems;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        int maxFrames = _frameRate.FramesPerSecond() - 1;
        if (!int.TryParse(HoursBox.Text, out var h) || h < 0 || h > 23
            || !int.TryParse(MinutesBox.Text, out var m) || m < 0 || m > 59
            || !int.TryParse(SecondsBox.Text, out var s) || s < 0 || s > 59
            || !int.TryParse(FramesBox.Text, out var f) || f < 0 || f > maxFrames)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー",
                $"トリガー時間を正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-{maxFrames})");
            return;
        }

        var oscAddress = (OscAddressBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "OSCアドレスは '/' で始まる必要があります。");
            return;
        }

        var result = new Cue
        {
            Id = string.Empty, // caller will set
            Name = (NameBox.Text ?? string.Empty).Trim(),
            TriggerTime = new TimecodeValue(h, m, s, f, _frameRate),
            OscAddress = oscAddress,
            Arguments = DialogInputs.ParseArguments(OscArgsBox.Text),
            TargetHostIds = _hostItems.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
            Memo = MemoBox.Text ?? string.Empty,
            IsEnabled = EnabledBox.IsChecked ?? true,
            SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false,
            CueOffset = DialogInputs.ParseOffset(
                OffsetSignBox.SelectedIndex == 1,
                OffsetHoursBox.Text, OffsetMinutesBox.Text, OffsetSecondsBox.Text, OffsetFramesBox.Text,
                _frameRate),
        };

        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
