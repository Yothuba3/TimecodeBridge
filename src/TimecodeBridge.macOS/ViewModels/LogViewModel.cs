using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.macOS.ViewModels;

/// <summary>
/// OSC送信結果ログのViewModel（macOS版、Windows版と同一契約）
/// </summary>
public partial class LogViewModel : DispatcherViewModel
{
    private const int MaxLogEntries = 1000;

    public ObservableCollection<LogEntry> Logs { get; } = [];

    public LogViewModel(IOscSender oscSender)
    {
        oscSender.SendCompleted += OnSendCompleted;
    }

    private void OnSendCompleted(object? sender, OscSendResultEventArgs e)
    {
        var message = e.Success
            ? (string.IsNullOrEmpty(e.ErrorMessage)
                ? $"[OK] {e.OscAddress} -> {e.HostName}"
                : $"[OK] {e.OscAddress} -> {e.HostName} ({e.ErrorMessage})")
            : $"[FAIL] {e.OscAddress} -> {e.HostName}: {e.ErrorMessage}";

        var entry = new LogEntry(DateTime.Now, message, e.Success);

        RunOnUIThread(() => AddEntry(entry));
    }

    private void AddEntry(LogEntry entry)
    {
        // 継続リレー等で毎フレーム同一内容の成功ログが積まれると重要ログが流れ去るため、
        // 直前と同一メッセージの成功ログはタイムスタンプ更新（置換）に留める
        if (entry.IsSuccess && Logs.Count > 0)
        {
            var last = Logs[^1];
            if (last.IsSuccess && last.Message == entry.Message)
            {
                Logs[^1] = entry;
                return;
            }
        }

        Logs.Add(entry);

        while (Logs.Count > MaxLogEntries)
        {
            Logs.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
    }
}
