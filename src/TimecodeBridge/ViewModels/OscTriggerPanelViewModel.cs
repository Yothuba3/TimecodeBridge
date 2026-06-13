using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

/// <summary>
/// OSCポン出しパネルのViewModel。<see cref="IOscTriggerPanelManager"/> の状態を
/// セル配列へ射影し、グリッドサイズ変更・送出・編集・クリアを仲介する。
/// </summary>
public partial class OscTriggerPanelViewModel : ObservableObject
{
    private readonly IOscTriggerPanelManager _manager;
    private readonly IOscTriggerDialogService _dialogService;
    private readonly IHostRegistry _hostRegistry;
    private readonly IProjectService _projectService;

    private bool _suppressSizeApply;

    [ObservableProperty] private int _rows;
    [ObservableProperty] private int _columns;
    [ObservableProperty] private bool _isEditMode = true;

    /// <summary>実行モード（編集モードの反転）。クリック送信が有効になる。</summary>
    public bool IsPlayMode
    {
        get => !IsEditMode;
        set => IsEditMode = !value;
    }

    public ObservableCollection<OscTriggerCellViewModel> Cells { get; } = [];

    public OscTriggerPanelViewModel(
        IOscTriggerPanelManager manager,
        IOscTriggerDialogService dialogService,
        IHostRegistry hostRegistry,
        IProjectService projectService)
    {
        _manager = manager;
        _dialogService = dialogService;
        _hostRegistry = hostRegistry;
        _projectService = projectService;

        SyncFromService();
    }

    /// <summary>マネージャの状態からグリッドとセルを再構築する。</summary>
    public void SyncFromService()
    {
        _suppressSizeApply = true;
        Rows = _manager.Rows;
        Columns = _manager.Columns;
        _suppressSizeApply = false;
        RebuildCells();
    }

    private void RebuildCells()
    {
        Cells.Clear();
        for (int r = 0; r < _manager.Rows; r++)
        {
            for (int c = 0; c < _manager.Columns; c++)
            {
                var cell = new OscTriggerCellViewModel(r, c);
                cell.SetButton(_manager.GetButtonAt(r, c));
                Cells.Add(cell);
            }
        }
    }

    partial void OnRowsChanged(int value) => ApplyGridSize();
    partial void OnColumnsChanged(int value) => ApplyGridSize();
    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(IsPlayMode));

    private void ApplyGridSize()
    {
        if (_suppressSizeApply) return;

        var newRows = Math.Max(1, Rows);
        var newColumns = Math.Max(1, Columns);

        // 1未満が入力された場合は表示値も最小値へ補正
        if (newRows != Rows || newColumns != Columns)
        {
            _suppressSizeApply = true;
            Rows = newRows;
            Columns = newColumns;
            _suppressSizeApply = false;
        }

        if (newRows == _manager.Rows && newColumns == _manager.Columns) return;

        // 縮小で範囲外になるボタンがあれば確認
        var outOfRange = _manager.GetOutOfRangeButtons(newRows, newColumns);
        if (outOfRange.Count > 0 && !ConfirmShrink(outOfRange.Count))
        {
            // 取り消し：マネージャの現在値へ戻す
            _suppressSizeApply = true;
            Rows = _manager.Rows;
            Columns = _manager.Columns;
            _suppressSizeApply = false;
            return;
        }

        _manager.SetGridSize(newRows, newColumns);
        _projectService.MarkAsChanged();
        RebuildCells();
    }

    /// <summary>縮小確認ダイアログ。テスト時に差し替え可能。</summary>
    protected virtual bool ConfirmShrink(int count)
    {
        var result = MessageBox.Show(
            $"グリッドの縮小により {count} 個のボタン設定が削除されます。続行しますか？",
            "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        return result == MessageBoxResult.OK;
    }

    [RelayCommand]
    private void TriggerCell(OscTriggerCellViewModel? cell)
    {
        if (cell is null) return;

        // 実行モードでのみクリック送信する（編集モードでは無反応）
        if (IsEditMode) return;
        if (!cell.IsConfigured) return;

        var result = _manager.Trigger(cell.Button!.Id);
        if (result.Sent)
        {
            cell.Flash();
        }
        else if (result.Reason == TriggerSkipReason.NoEnabledTarget)
        {
            NotifyNoTarget();
        }
    }

    [RelayCommand]
    private void EditCell(OscTriggerCellViewModel? cell)
    {
        if (cell is null) return;

        // 編集モードでのみ編集を許可する（実行モードでは無反応）
        if (!IsEditMode) return;

        var isExisting = cell.IsConfigured;
        var template = cell.Button ?? new OscTriggerButton
        {
            Id = Guid.NewGuid().ToString(),
            Row = cell.Row,
            Column = cell.Column,
        };

        var result = _dialogService.ShowEditDialog(template, _hostRegistry.Hosts, "ボタン編集", isExisting);
        switch (result.Action)
        {
            case OscTriggerEditAction.Save when result.Button is not null:
                _manager.UpsertButton(result.Button);
                _projectService.MarkAsChanged();
                cell.SetButton(_manager.GetButtonAt(cell.Row, cell.Column));
                break;

            case OscTriggerEditAction.Delete when isExisting:
                _manager.RemoveButton(template.Id);
                _projectService.MarkAsChanged();
                cell.SetButton(null);
                break;
        }
    }

    /// <summary>送信先未設定/全無効の通知。テスト時に差し替え可能。</summary>
    protected virtual void NotifyNoTarget()
    {
        MessageBox.Show("送信先ホストが設定されていないか、すべて無効です。",
            "送出できません", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
