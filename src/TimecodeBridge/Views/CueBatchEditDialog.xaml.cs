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
                       || ApplySendTcSeconds.IsChecked == true
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

        // Send TC as Seconds
        if (ApplySendTcSeconds.IsChecked == true)
        {
            result.SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false;
        }

        // OSC Arguments
        if (ApplyOscArgs.IsChecked == true)
        {
            result.Arguments = ParseArguments(OscArgsBox.Text);
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

        // Cue Offset
        if (ApplyOffset.IsChecked == true)
        {
            result.ApplyOffset = true;
            result.CueOffset = ParseCueOffset();
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

    private TimecodeOffset? ParseCueOffset()
    {
        if (!int.TryParse(OffsetHoursBox.Text, out var oh)) oh = 0;
        if (!int.TryParse(OffsetMinutesBox.Text, out var om)) om = 0;
        if (!int.TryParse(OffsetSecondsBox.Text, out var os)) os = 0;
        if (!int.TryParse(OffsetFramesBox.Text, out var of2)) of2 = 0;

        if (oh == 0 && om == 0 && os == 0 && of2 == 0)
            return null;

        bool isNegative = OffsetSignBox.SelectedIndex == 1;
        return new TimecodeOffset(isNegative, oh, om, os, of2, _frameRate);
    }

    private static List<OscArgument> ParseArguments(string text)
    {
        var result = new List<OscArgument>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = token.IndexOf(':');
            if (colonIndex < 1 || colonIndex >= token.Length - 1) continue;

            var typePrefix = token[..colonIndex];
            var valueStr = token[(colonIndex + 1)..];

            switch (typePrefix)
            {
                case "i" when int.TryParse(valueStr, out var iv):
                    result.Add(new OscInt32Argument(iv));
                    break;
                case "f" when float.TryParse(valueStr, out var fv):
                    result.Add(new OscFloat32Argument(fv));
                    break;
                case "s":
                    result.Add(new OscStringArgument(valueStr));
                    break;
            }
        }

        return result;
    }
}
