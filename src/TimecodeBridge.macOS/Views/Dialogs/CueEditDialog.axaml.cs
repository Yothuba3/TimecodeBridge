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
        AdditionalAddressesBox.Text = string.Join(Environment.NewLine, cue.AdditionalOscAddresses);
        OscArgsBox.Text = OscArgumentText.Format(cue.Arguments);
        MemoBox.Text = cue.Memo;
        EnabledBox.IsChecked = cue.IsEnabled;
        SendTcSecondsBox.IsChecked = cue.SendTriggerTimeAsSeconds;

        // Send timecode（未指定 = トリガー時間をそのまま送信）
        var sendTc = cue.SendTimecode ?? new TimecodeValue(0, 0, 0, 0, frameRate);
        UseSendTimecodeBox.IsChecked = cue.SendTimecode is not null;
        SendTcHoursBox.Text = sendTc.Hours.ToString("D2");
        SendTcMinutesBox.Text = sendTc.Minutes.ToString("D2");
        SendTcSecondsFieldBox.Text = sendTc.Seconds.ToString("D2");
        SendTcFramesBox.Text = sendTc.Frames.ToString("D2");

        // Trigger offset（発火タイミングのオフセット）
        var offset = cue.TriggerOffset ?? TimecodeOffset.Zero(frameRate);
        OffsetSignBox.SelectedIndex = offset.IsNegative ? 1 : 0;
        OffsetHoursBox.Text = offset.Hours.ToString("D2");
        OffsetMinutesBox.Text = offset.Minutes.ToString("D2");
        OffsetSecondsBox.Text = offset.Seconds.ToString("D2");
        OffsetFramesBox.Text = offset.Frames.ToString("D2");

        _hostItems = DialogInputs.ToHostSelections(allHosts, cue.TargetHostIds);
        HostList.ItemsSource = _hostItems;
        NoHostsHint.IsVisible = _hostItems.Count == 0;
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

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        int maxFrames = _frameRate.FramesPerSecond() - 1;
        if (!int.TryParse(HoursBox.Text, out var h) || h < 0 || h > 23
            || !int.TryParse(MinutesBox.Text, out var m) || m < 0 || m > 59
            || !int.TryParse(SecondsBox.Text, out var s) || s < 0 || s > 59
            || !int.TryParse(FramesBox.Text, out var f) || f < 0 || f > maxFrames)
        {
            await ShowTimeError();
            return;
        }

        var oscAddress = (OscAddressBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "OSCアドレスは '/' で始まる必要があります。");
            return;
        }

        // 追加アドレス（1行1件・空行は無視）
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

        // 不正な引数トークンを黙って捨てると「設定が消えた」ように見えるためエラーにする
        if (!OscArgumentText.TryParse(OscArgsBox.Text ?? string.Empty, out var arguments, out var invalidToken))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー",
                $"OSC引数の形式が正しくありません: 「{invalidToken}」\n\n" +
                "「型:値」をスペース区切りで入力してください。\n例: i:1 f:0.5 s:hello（空白を含む文字列は s:\"hello world\"）");
            return;
        }

        // トリガーオフセット（非数値・範囲外はエラー、全ゼロ = なし）
        if (!TryParseTriggerOffset(out var triggerOffset))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー",
                "トリガーオフセットを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)");
            return;
        }

        // 適用後の発火時刻が0〜24時に収まるか（範囲外だと発火できないキューになる）
        var triggerTime = new TimecodeValue(h, m, s, f, _frameRate);
        if (!Cue.TryApplyTriggerOffset(triggerTime, triggerOffset, out _))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー",
                "トリガーオフセット適用後の発火時刻が 00:00:00:00〜23:59:59:FF の範囲を超えます。\nオフセットまたはトリガー時間を調整してください。");
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
                    int.Parse(SendTcHoursBox.Text!), int.Parse(SendTcMinutesBox.Text!),
                    int.Parse(SendTcSecondsFieldBox.Text!), int.Parse(SendTcFramesBox.Text!), _frameRate);
            }
            else if (SendTcSecondsBox.IsChecked == true)
            {
                // 秒数送信が有効なときだけエラーにする（無効時は残った不正値で保存をブロックしない）
                await ModalDialog.ShowMessageAsync(this, "入力エラー",
                    "送信タイムコードを正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)");
                return;
            }
        }

        var result = new Cue
        {
            Id = string.Empty, // caller will set
            Name = (NameBox.Text ?? string.Empty).Trim(),
            TriggerTime = triggerTime,
            OscAddress = oscAddress,
            AdditionalOscAddresses = additionalAddresses,
            Arguments = arguments,
            TargetHostIds = _hostItems.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
            Memo = MemoBox.Text ?? string.Empty,
            IsEnabled = EnabledBox.IsChecked ?? true,
            SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false,
            SendTimecode = sendTimecode,
            TriggerOffset = triggerOffset,
        };

        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private Task ShowTimeError()
        => ModalDialog.ShowMessageAsync(this, "入力エラー",
            "トリガー時間を正しい形式で入力してください。\nHH(0-23) MM(0-59) SS(0-59) FF(0-max)");
}
