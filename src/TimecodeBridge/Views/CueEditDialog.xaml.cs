using System.Windows;
using System.Windows.Controls;
using TimecodeBridge.Models;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

public partial class CueEditDialog : Window
{
    private readonly FrameRate _frameRate;
    public Cue ResultCue { get; private set; } = null!;

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
        OscArgsBox.Text = OscArgumentText.Format(cue.Arguments);
        MemoBox.Text = cue.Memo;
        EnabledBox.IsChecked = cue.IsEnabled;
        SendTcSecondsBox.IsChecked = cue.SendTriggerTimeAsSeconds;

        // Cue offset
        if (cue.CueOffset is { } offset)
        {
            OffsetSignBox.SelectedIndex = offset.IsNegative ? 1 : 0;
            OffsetHoursBox.Text = offset.Hours.ToString("D2");
            OffsetMinutesBox.Text = offset.Minutes.ToString("D2");
            OffsetSecondsBox.Text = offset.Seconds.ToString("D2");
            OffsetFramesBox.Text = offset.Frames.ToString("D2");
        }
        else
        {
            OffsetSignBox.SelectedIndex = 0;
            OffsetHoursBox.Text = "00";
            OffsetMinutesBox.Text = "00";
            OffsetSecondsBox.Text = "00";
            OffsetFramesBox.Text = "00";
        }

        // Populate host selection
        var hostItems = allHosts.Select(h => new HostSelection
        {
            Id = h.Id,
            Name = $"{h.Name} ({h.IpAddress}:{h.Port})",
            IsSelected = cue.TargetHostIds.Contains(h.Id),
        }).ToList();
        HostListBox.ItemsSource = hostItems;
        NoHostsHint.Visibility = hostItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TimecodeOffset? ParseCueOffset()
    {
        if (!int.TryParse(OffsetHoursBox.Text, out var oh)) oh = 0;
        if (!int.TryParse(OffsetMinutesBox.Text, out var om)) om = 0;
        if (!int.TryParse(OffsetSecondsBox.Text, out var os)) os = 0;
        if (!int.TryParse(OffsetFramesBox.Text, out var of2)) of2 = 0;

        // 範囲外の成分はオフセットとして有効な値へ丸める（負値は符号コンボで指定）
        oh = Math.Clamp(oh, 0, 23);
        om = Math.Clamp(om, 0, 59);
        os = Math.Clamp(os, 0, 59);
        of2 = Math.Clamp(of2, 0, _frameRate.FramesPerSecond() - 1);

        // All zero means no offset
        if (oh == 0 && om == 0 && os == 0 && of2 == 0)
            return null;

        bool isNegative = OffsetSignBox.SelectedIndex == 1;
        return new TimecodeOffset(isNegative, oh, om, os, of2, _frameRate);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HoursBox.Text, out var h) || h < 0 || h > 23)
        {
            ShowTimeError(); return;
        }
        if (!int.TryParse(MinutesBox.Text, out var m) || m < 0 || m > 59)
        {
            ShowTimeError(); return;
        }
        if (!int.TryParse(SecondsBox.Text, out var s) || s < 0 || s > 59)
        {
            ShowTimeError(); return;
        }
        int maxFrames = _frameRate.FramesPerSecond() - 1;
        if (!int.TryParse(FramesBox.Text, out var f) || f < 0 || f > maxFrames)
        {
            ShowTimeError(); return;
        }

        var oscAddress = OscAddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
        {
            MessageBox.Show("OSCアドレスは '/' で始まる必要があります。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedHostIds = new List<string>();
        if (HostListBox.ItemsSource is IEnumerable<HostSelection> items)
        {
            selectedHostIds.AddRange(items.Where(x => x.IsSelected).Select(x => x.Id));
        }

        ResultCue = new Cue
        {
            Id = string.Empty, // caller will set
            Name = NameBox.Text.Trim(),
            TriggerTime = new TimecodeValue(h, m, s, f, _frameRate),
            OscAddress = oscAddress,
            Arguments = OscArgumentText.Parse(OscArgsBox.Text),
            TargetHostIds = selectedHostIds,
            Memo = MemoBox.Text,
            IsEnabled = EnabledBox.IsChecked ?? true,
            SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false,
            CueOffset = ParseCueOffset(),
        };

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static void ShowTimeError()
    {
        MessageBox.Show("トリガー時間を正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)",
            "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
