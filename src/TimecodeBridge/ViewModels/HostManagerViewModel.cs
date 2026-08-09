using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

public partial class HostManagerViewModel : ObservableObject, IDisposable
{
    private readonly IHostRegistry _hostRegistry;
    private readonly IOscSender _oscSender;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IHostDialogService _hostDialogService;
    private readonly IProjectService _projectService;

    public ObservableCollection<OscHost> Hosts { get; } = [];

    public HostManagerViewModel(IHostRegistry hostRegistry, IOscSender oscSender, ITimecodeEngine timecodeEngine, IHostDialogService hostDialogService, IProjectService projectService)
    {
        _hostRegistry = hostRegistry;
        _oscSender = oscSender;
        _timecodeEngine = timecodeEngine;
        _hostDialogService = hostDialogService;
        _projectService = projectService;

        // Sync existing hosts
        foreach (var host in _hostRegistry.Hosts)
        {
            Hosts.Add(host);
        }

        // Subscribe to changes
        _hostRegistry.HostChanged += OnHostChanged;
    }

    private void OnHostChanged(object? sender, HostChangedEventArgs e)
    {
        switch (e.ChangeType)
        {
            case HostChangeType.Added:
                var addedHost = _hostRegistry.Hosts.FirstOrDefault(h => h.Id == e.HostId);
                if (addedHost is not null && !Hosts.Any(h => h.Id == e.HostId))
                {
                    Hosts.Add(addedHost);
                }
                break;

            case HostChangeType.Removed:
                var toRemove = Hosts.FirstOrDefault(h => h.Id == e.HostId);
                if (toRemove is not null)
                {
                    Hosts.Remove(toRemove);
                }
                break;

            case HostChangeType.Updated:
                var existingIndex = -1;
                for (int i = 0; i < Hosts.Count; i++)
                {
                    if (Hosts[i].Id == e.HostId)
                    {
                        existingIndex = i;
                        break;
                    }
                }
                if (existingIndex >= 0)
                {
                    var updatedHost = _hostRegistry.Hosts.FirstOrDefault(h => h.Id == e.HostId);
                    if (updatedHost is not null)
                    {
                        Hosts[existingIndex] = updatedHost;
                    }
                }
                break;
        }
    }

    [RelayCommand]
    private void AddHost()
    {
        var template = new OscHost
        {
            Id = string.Empty,
            Name = "New Host",
            IpAddress = "127.0.0.1",
            Port = 9000,
        };

        var result = _hostDialogService.ShowEditDialog(template);
        if (result is not null)
        {
            result.Id = Guid.NewGuid().ToString();
            _hostRegistry.AddHost(result);
            _projectService.MarkAsChanged();
        }
    }

    [RelayCommand]
    private void EditHost(string hostId)
    {
        var host = _hostRegistry.Hosts.FirstOrDefault(h => h.Id == hostId);
        if (host is null) return;

        var result = _hostDialogService.ShowEditDialog(host);
        if (result is not null)
        {
            result.Id = hostId;
            _hostRegistry.UpdateHost(hostId, result);
            _projectService.MarkAsChanged();
        }
    }

    [RelayCommand]
    private void RemoveHost(string hostId)
    {
        var host = _hostRegistry.Hosts.FirstOrDefault(h => h.Id == hostId);
        if (host is null) return;

        if (!ConfirmRemoveHost(host)) return;

        _hostRegistry.RemoveHost(hostId);
        _projectService.MarkAsChanged();
    }

    /// <summary>ホスト削除の確認。テスト時に差し替え可能。</summary>
    protected virtual bool ConfirmRemoveHost(OscHost host)
    {
        var result = System.Windows.MessageBox.Show(
            $"ホスト「{host.Name}」({host.IpAddress}:{host.Port}) を削除しますか？\n\n" +
            "このホストを送信先にしているキュー・OSCポン出しボタン・リレー設定からは送信できなくなります。",
            "ホスト削除の確認", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        return result == System.Windows.MessageBoxResult.OK;
    }

    [RelayCommand]
    private void ToggleHostEnabled(string hostId)
    {
        var host = _hostRegistry.Hosts.FirstOrDefault(h => h.Id == hostId);
        if (host is not null)
        {
            _hostRegistry.SetHostEnabled(hostId, !host.IsEnabled);
            _projectService.MarkAsChanged();
        }
    }

    [RelayCommand]
    private async Task PingHost(string hostId)
    {
        var fps = _timecodeEngine.FrameRate.FramesPerSecond();
        await _oscSender.SendIcmpPingAsync(hostId, fps);
    }

    public void Dispose()
    {
        _hostRegistry.HostChanged -= OnHostChanged;
    }
}
