using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows;
using TimecodeBridge.Core.Models;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

public partial class CueBatchEditDialog : Window
{
    private readonly FrameRate _frameRate;
    public CueBatchEditResult? Result { get; private set; }

    public CueBatchEditDialog(int cueCount, IReadOnlyList<OscHost> allHosts, FrameRate frameRate)
    {
        InitializeComponent();
        _frameRate = frameRate;

        HeaderText.Text = $"{cueCount} 件のキューを一括編集";

        var hostItems = allHosts.Select(h => new HostSelection
        {
            Id = h.Id,
            Name = $"{h.Name} ({h.IpAddress}:{h.Port})",
            IsSelected = false,
        }).ToList();
        HostListBox.ItemsSource = hostItems;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // At least one field must be checked
        bool anyChecked = ApplyOscAddress.IsChecked == true
                       || ApplyAdditionalAddresses.IsChecked == true
                       || ApplySendTcSeconds.IsChecked == true
                       || ApplySendTimecode.IsChecked == true
                       || ApplyOscArgs.IsChecked == true
                       || ApplyTargetHosts.IsChecked == true
                       || ApplyOffset.IsChecked == true
                       || ApplyMemo.IsChecked == true
                       || ApplyEnabled.IsChecked == true;

        if (!anyChecked)
        {
            MessageBox.Show("変更するフィールドを1つ以上選択してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = new CueBatchEditResult();

        // OSC Address
        if (ApplyOscAddress.IsChecked == true)
        {
            var oscAddress = OscAddressBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
            {
                MessageBox.Show("OSCアドレスは '/' で始まる必要があります。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            result.OscAddress = oscAddress;
        }

        // Additional OSC Addresses（1行1件・空=全解除）
        if (ApplyAdditionalAddresses.IsChecked == true)
        {
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
            result.AdditionalOscAddresses = additionalAddresses;
        }

        // Send TC as Seconds
        if (ApplySendTcSeconds.IsChecked == true)
        {
            result.SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false;
        }

        // Send Timecode（「指定」OFF = 解除してトリガー時間を送る）
        if (ApplySendTimecode.IsChecked == true)
        {
            result.ApplySendTimecode = true;
            if (UseSendTimecodeBox.IsChecked == true)
            {
                int maxFrames = _frameRate.FramesPerSecond() - 1;
                if (!int.TryParse(SendTcHoursBox.Text, out var sh) || sh < 0 || sh > 23 ||
                    !int.TryParse(SendTcMinutesBox.Text, out var sm) || sm < 0 || sm > 59 ||
                    !int.TryParse(SendTcSecondsFieldBox.Text, out var ss) || ss < 0 || ss > 59 ||
                    !int.TryParse(SendTcFramesBox.Text, out var sf) || sf < 0 || sf > maxFrames)
                {
                    MessageBox.Show("送信タイムコードを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)",
                        "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result.SendTimecode = new TimecodeValue(sh, sm, ss, sf, _frameRate);
            }
            // 「指定」OFFなら result.SendTimecode は null のまま = 解除
        }

        // OSC Arguments
        if (ApplyOscArgs.IsChecked == true)
        {
            // 不正な引数トークンを黙って捨てると「設定が消えた」ように見えるためエラーにする
            if (!OscArgumentText.TryParse(OscArgsBox.Text, out var arguments, out var invalidToken))
            {
                MessageBox.Show(
                    $"OSC引数の形式が正しくありません: 「{invalidToken}」\n\n" +
                    "「型:値」をスペース区切りで入力してください。\n例: i:1 f:0.5 s:hello（空白を含む文字列は s:\"hello world\"）",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            result.Arguments = arguments;
        }

        // Target Hosts
        if (ApplyTargetHosts.IsChecked == true)
        {
            var selectedHostIds = new List<string>();
            if (HostListBox.ItemsSource is IEnumerable<HostSelection> items)
            {
                selectedHostIds.AddRange(items.Where(x => x.IsSelected).Select(x => x.Id));
            }
            result.TargetHostIds = selectedHostIds;
        }

        // Trigger Offset（発火タイミングのオフセット）
        if (ApplyOffset.IsChecked == true)
        {
            if (!TryParseTriggerOffset(out var triggerOffset))
            {
                MessageBox.Show("トリガーオフセットを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            result.ApplyTriggerOffset = true;
            result.TriggerOffset = triggerOffset;
        }

        // Memo
        if (ApplyMemo.IsChecked == true)
        {
            result.ApplyMemo = true;
            result.Memo = MemoBox.Text;
        }

        // Enabled
        if (ApplyEnabled.IsChecked == true)
        {
            result.IsEnabled = EnabledBox.IsChecked ?? true;
        }

        Result = result;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // 各成分を検証してオフセットを組み立てる（全ゼロ = 解除(null)）。不正入力は false
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
}
