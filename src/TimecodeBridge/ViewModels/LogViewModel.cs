using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

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

        RunOnUiThread(() => AddEntry(entry));
    }

    private void AddEntry(LogEntry entry)
    {
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
