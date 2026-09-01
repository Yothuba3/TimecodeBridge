using System.Windows;
using System.Windows.Controls;
using TimecodeBridge.Core.Models;
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
        AdditionalAddressesBox.Text = string.Join(Environment.NewLine, cue.AdditionalOscAddresses);
        OscArgsBox.Text = OscArgumentText.Format(cue.Arguments);
        MemoBox.Text = cue.Memo;
        EnabledBox.IsChecked = cue.IsEnabled;
        SendTcSecondsBox.IsChecked = cue.SendTriggerTimeAsSeconds;

        // Send timecode（未指定 = トリガー時間をそのまま送信）
        if (cue.SendTimecode is { } sendTc)
        {
            UseSendTimecodeBox.IsChecked = true;
            SendTcHoursBox.Text = sendTc.Hours.ToString("D2");
            SendTcMinutesBox.Text = sendTc.Minutes.ToString("D2");
            SendTcSecondsFieldBox.Text = sendTc.Seconds.ToString("D2");
            SendTcFramesBox.Text = sendTc.Frames.ToString("D2");
        }
        else
        {
            UseSendTimecodeBox.IsChecked = false;
            SendTcHoursBox.Text = "00";
            SendTcMinutesBox.Text = "00";
            SendTcSecondsFieldBox.Text = "00";
            SendTcFramesBox.Text = "00";
        }

        // Trigger offset（発火タイミングのオフセット）
        if (cue.TriggerOffset is { } offset)
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

    // 各成分を検証してオフセットを組み立てる（全ゼロ = オフセットなし）。不正入力は false
    private bool TryParseTriggerOffset(out TimecodeOffset? offset)
    {
        offset = null;

        if (!TryParseOffsetField(OffsetHoursBox.Text, 23, out var oh) ||
            !TryParseOffsetField(OffsetMinutesBox.Text, 59, out var om) ||
            !TryParseOffsetField(OffsetSecondsBox.Text, 59, out var os) ||
            !TryParseOffsetField(OffsetFramesBox.Text, _frameRate.FramesPerSecond() - 1, out var of2))
        {
            return false;
        }

        // All zero means no offset
        if (oh == 0 && om == 0 && os == 0 && of2 == 0)
            return true;

        bool isNegative = OffsetSignBox.SelectedIndex == 1;
        offset = new TimecodeOffset(isNegative, oh, om, os, of2, _frameRate);
        return true;
    }

    // 空欄は0扱い、非数値・範囲外はエラー
    private static bool TryParseOffsetField(string text, int max, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }
        return int.TryParse(text, out value) && value >= 0 && value <= max;
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

        // 追加アドレス（1行1件・空行は無視）
        var additionalAddresses = AdditionalAddressesBox.Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (additionalAddresses.Any(a => !a.StartsWith('/')))
        {
            MessageBox.Show("追加アドレスも '/' で始まる必要があります。\n（1行に1アドレスずつ入力してください）", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 不正な引数トークンを黙って捨てると「設定が消えた」ように見えるためエラーにする
        if (!OscArgumentText.TryParse(OscArgsBox.Text, out var arguments, out var invalidToken))
        {
            MessageBox.Show(
                $"OSC引数の形式が正しくありません: 「{invalidToken}」\n\n" +
                "「型:値」をスペース区切りで入力してください。\n例: i:1 f:0.5 s:hello（空白を含む文字列は s:\"hello world\"）",
                "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // トリガーオフセット（非数値・範囲外はエラー、全ゼロ = なし）
        if (!TryParseTriggerOffset(out var triggerOffset))
        {
            MessageBox.Show("トリガーオフセットを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)",
                "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 適用後の発火時刻が0〜24時に収まるか（範囲外だと発火できないキューになる）
        var triggerTime = new TimecodeValue(h, m, s, f, _frameRate);
        if (!Cue.TryApplyTriggerOffset(triggerTime, triggerOffset, out _))
        {
            MessageBox.Show("トリガーオフセット適用後の発火時刻が 00:00:00:00〜23:59:59:FF の範囲を超えます。\nオフセットまたはトリガー時間を調整してください。",
                "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 送信タイムコード（「指定」OFF = トリガー時間をそのまま送る）
        TimecodeValue? sendTimecode = null;
        if (UseSendTimecodeBox.IsChecked == true)
        {
            bool valid = int.TryParse(SendTcHoursBox.Text, out var sh) && sh >= 0 && sh <= 23 &&
                         int.TryParse(SendTcMinutesBox.Text, out var sm) && sm >= 0 && sm <= 59 &&
                         int.TryParse(SendTcSecondsFieldBox.Text, out var ss) && ss >= 0 && ss <= 59 &&
                         int.TryParse(SendTcFramesBox.Text, out var sf) && sf >= 0 && sf <= maxFrames;

            if (valid)
            {
                sendTimecode = new TimecodeValue(
                    int.Parse(SendTcHoursBox.Text), int.Parse(SendTcMinutesBox.Text),
                    int.Parse(SendTcSecondsFieldBox.Text), int.Parse(SendTcFramesBox.Text), _frameRate);
            }
            else if (SendTcSecondsBox.IsChecked == true)
            {
                // 秒数送信が有効なときだけエラーにする（無効時は残った不正値で保存をブロックしない）
                MessageBox.Show("送信タイムコードを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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
            TriggerTime = triggerTime,
            OscAddress = oscAddress,
            AdditionalOscAddresses = additionalAddresses,
            Arguments = arguments,
            TargetHostIds = selectedHostIds,
            Memo = MemoBox.Text,
            IsEnabled = EnabledBox.IsChecked ?? true,
            SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false,
            SendTimecode = sendTimecode,
            TriggerOffset = triggerOffset,
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
