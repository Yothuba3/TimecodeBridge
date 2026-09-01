using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.Core.Models;
using TimecodeBridge.macOS.Services;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views.Dialogs;

/// <summary>
/// キュー一括編集ダイアログ。適用で CueBatchEditResult、キャンセルで null を返す。
/// </summary>
public partial class CueBatchEditDialog : Window
{
    private readonly FrameRate _frameRate;
    private readonly List<HostSelection> _hostItems;

    public CueBatchEditDialog() : this(0, [], FrameRate.Fps30)
    {
    }

    public CueBatchEditDialog(int cueCount, IReadOnlyList<OscHost> allHosts, FrameRate frameRate)
    {
        InitializeComponent();
        _frameRate = frameRate;

        HeaderText.Text = $"{cueCount} 件のキューを一括編集";

        _hostItems = DialogInputs.ToHostSelections(allHosts, []);
        HostList.ItemsSource = _hostItems;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
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
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "変更するフィールドを1つ以上選択してください。");
            return;
        }

        var result = new CueBatchEditResult();

        if (ApplyOscAddress.IsChecked == true)
        {
            var oscAddress = (OscAddressBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
            {
                await ModalDialog.ShowMessageAsync(this, "入力エラー", "OSCアドレスは '/' で始まる必要があります。");
                return;
            }
            result.OscAddress = oscAddress;
        }

        // Additional OSC Addresses（1行1件・空=全解除）
        if (ApplyAdditionalAddresses.IsChecked == true)
        {
            var additionalAddresses = (AdditionalAddressesBox.Text ?? string.Empty)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            if (additionalAddresses.Any(a => !a.StartsWith('/')))
            {
                await ModalDialog.ShowMessageAsync(this, "入力エラー",
                    "追加アドレスも '/' で始まる必要があります。\n（1行に1アドレスずつ入力してください）");
                return;
            }
            result.AdditionalOscAddresses = additionalAddresses;
        }

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
                    await ModalDialog.ShowMessageAsync(this, "入力エラー",
                        "送信タイムコードを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)");
                    return;
                }
                result.SendTimecode = new TimecodeValue(sh, sm, ss, sf, _frameRate);
            }
            // 「指定」OFFなら result.SendTimecode は null のまま = 解除
        }

        if (ApplyOscArgs.IsChecked == true)
        {
            // 不正な引数トークンを黙って捨てると「設定が消えた」ように見えるためエラーにする
            if (!OscArgumentText.TryParse(OscArgsBox.Text ?? string.Empty, out var arguments, out var invalidToken))
            {
                await ModalDialog.ShowMessageAsync(this, "入力エラー",
                    $"OSC引数の形式が正しくありません: 「{invalidToken}」\n\n" +
                    "「型:値」をスペース区切りで入力してください。\n例: i:1 f:0.5 s:hello（空白を含む文字列は s:\"hello world\"）");
                return;
            }
            result.Arguments = arguments;
        }

        if (ApplyTargetHosts.IsChecked == true)
        {
            result.TargetHostIds = _hostItems.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        }

        // Trigger Offset（発火タイミングのオフセット）
        if (ApplyOffset.IsChecked == true)
        {
            if (!TryParseTriggerOffset(out var triggerOffset))
            {
                await ModalDialog.ShowMessageAsync(this, "入力エラー",
                    "トリガーオフセットを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)");
                return;
            }
            result.ApplyTriggerOffset = true;
            result.TriggerOffset = triggerOffset;
        }

        if (ApplyMemo.IsChecked == true)
        {
            result.ApplyMemo = true;
            result.Memo = MemoBox.Text ?? string.Empty;
        }

        if (ApplyEnabled.IsChecked == true)
        {
            result.IsEnabled = EnabledBox.IsChecked ?? true;
        }

        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
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
    private static bool TryParseOffsetField(string? text, int max, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }
        return int.TryParse(text, out value) && value >= 0 && value <= max;
    }
}
