using System.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.ViewModels;

/// <summary>
/// Cue-Syncワンショット送信のViewModel。
/// 押した瞬間の現在TCを直前キューの送信タイムコード軸へ変換して1回送信する。
/// </summary>
public partial class CueSyncViewModel : ObservableObject, IDisposable
{
    private readonly ICueManager _cueManager;
    private readonly IHostRegistry _hostRegistry;
    private readonly IProjectService _projectService;
    private bool _suppressDirty;

    [ObservableProperty] private string _oscAddress = "/cuesync";

    public ObservableCollection<HostSelection> HostSelections { get; } = [];

    /// <summary>選択中の送信先ホストID。</summary>
    public List<string> TargetHostIds { get; private set; } = [];

    public CueSyncViewModel(ICueManager cueManager, IHostRegistry hostRegistry, IProjectService projectService)
    {
        _cueManager = cueManager;
        _hostRegistry = hostRegistry;
        _projectService = projectService;

        RefreshHostSelections();
        _hostRegistry.HostChanged += OnHostChanged;
    }

    [RelayCommand]
    private void SendCueSync()
    {
        _cueManager.SendCueSync(OscAddress, TargetHostIds);
    }

    [RelayCommand]
    private void SelectAllHosts() => SetAllHosts(true);

    [RelayCommand]
    private void ClearAllHosts() => SetAllHosts(false);

    private void SetAllHosts(bool selected)
    {
        foreach (var host in HostSelections)
        {
            host.IsSelected = selected;
        }
        UpdateHostSelections();
    }

    [RelayCommand]
    private void UpdateHostSelections()
    {
        var ids = HostSelections.Where(h => h.IsSelected).Select(h => h.Id).ToList();

        // コンテナ生成時のCheckedイベント（プロジェクト読込直後など）で誤dirtyしない
        if (ids.SequenceEqual(TargetHostIds)) return;

        TargetHostIds = ids;
        MarkDirty();
    }

    partial void OnOscAddressChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (!_suppressDirty) _projectService.MarkAsChanged();
    }

    public CueSyncSettings GetSettings() => new()
    {
        OscAddress = OscAddress,
        TargetHostIds = TargetHostIds.ToList(),
    };

    public void LoadSettings(CueSyncSettings settings)
    {
        _suppressDirty = true;
        OscAddress = string.IsNullOrWhiteSpace(settings.OscAddress) ? "/cuesync" : settings.OscAddress;
        TargetHostIds = settings.TargetHostIds.ToList();
        _suppressDirty = false;
        RefreshHostSelections();
    }

    private void OnHostChanged(object? sender, HostChangedEventArgs e) => RefreshHostSelections();

    private void RefreshHostSelections()
    {
        var selectedIds = TargetHostIds;
        foreach (var old in HostSelections) old.PropertyChanged -= OnHostSelectionChanged;
        HostSelections.Clear();
        foreach (var host in _hostRegistry.Hosts)
        {
            var selection = new HostSelection
            {
                Id = host.Id,
                Name = host.Name,
                IsSelected = selectedIds.Contains(host.Id),
            };
            // チェックボックスの TwoWay バインディングが IsSelected を書いた後に反映する
            // (View の IsCheckedChanged はバインディングより先に走り、古い選択を読んでいた)
            selection.PropertyChanged += OnHostSelectionChanged;
            HostSelections.Add(selection);
        }
    }

    private void OnHostSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HostSelection.IsSelected)) UpdateHostSelections();
    }

    public void Dispose()
    {
        _hostRegistry.HostChanged -= OnHostChanged;
        foreach (var selection in HostSelections) selection.PropertyChanged -= OnHostSelectionChanged;
    }
}
