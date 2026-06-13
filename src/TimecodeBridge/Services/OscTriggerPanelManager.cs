using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Services;

/// <summary>
/// <see cref="IOscTriggerPanelManager"/> の実装。グリッド寸法とボタン集合を保持し、
/// 送出は既存の <see cref="IOscSender"/>、送信先解決は <see cref="IHostRegistry"/> に委譲する。
/// </summary>
public class OscTriggerPanelManager : IOscTriggerPanelManager
{
    public const int DefaultRows = 4;
    public const int DefaultColumns = 4;
    private const int MinSize = 1;

    private readonly IOscSender _oscSender;
    private readonly IHostRegistry _hostRegistry;
    private readonly List<OscTriggerButton> _buttons = [];

    private int _rows = DefaultRows;
    private int _columns = DefaultColumns;

    public OscTriggerPanelManager(IOscSender oscSender, IHostRegistry hostRegistry)
    {
        _oscSender = oscSender;
        _hostRegistry = hostRegistry;
    }

    public int Rows => _rows;
    public int Columns => _columns;
    public IReadOnlyList<OscTriggerButton> Buttons => _buttons.AsReadOnly();

    public void SetGridSize(int rows, int columns)
    {
        var newRows = Math.Max(MinSize, rows);
        var newColumns = Math.Max(MinSize, columns);
        if (newRows == _rows && newColumns == _columns) return;

        _rows = newRows;
        _columns = newColumns;
        _buttons.RemoveAll(b => b.Row >= _rows || b.Column >= _columns);
        OnChanged();
    }

    public OscTriggerButton? GetButtonAt(int row, int column)
        => _buttons.FirstOrDefault(b => b.Row == row && b.Column == column);

    public IReadOnlyList<OscTriggerButton> GetOutOfRangeButtons(int rows, int columns)
    {
        var r = Math.Max(MinSize, rows);
        var c = Math.Max(MinSize, columns);
        return _buttons.Where(b => b.Row >= r || b.Column >= c).ToList().AsReadOnly();
    }

    public void UpsertButton(OscTriggerButton button)
    {
        // 同一セルを占有する別ボタンを除去し、1セル最大1ボタンの不変条件を保つ
        _buttons.RemoveAll(b => b.Id != button.Id && b.Row == button.Row && b.Column == button.Column);

        var index = _buttons.FindIndex(b => b.Id == button.Id);
        if (index >= 0)
            _buttons[index] = button;
        else
            _buttons.Add(button);

        OnChanged();
    }

    public void RemoveButton(string buttonId)
    {
        if (_buttons.RemoveAll(b => b.Id == buttonId) > 0)
            OnChanged();
    }

    public TriggerResult Trigger(string buttonId)
    {
        var button = _buttons.FirstOrDefault(b => b.Id == buttonId);
        if (button is null || string.IsNullOrWhiteSpace(button.OscAddress))
            return new TriggerResult(false, TriggerSkipReason.NotConfigured);

        var enabledHosts = _hostRegistry.GetEnabledHosts(button.TargetHostIds);
        if (enabledHosts.Count == 0)
            return new TriggerResult(false, TriggerSkipReason.NoEnabledTarget);

        _oscSender.Send(button.OscAddress, button.Arguments, button.TargetHostIds);
        return new TriggerResult(true, TriggerSkipReason.None);
    }

    public OscTriggerPanelSettings GetSettings()
        => new()
        {
            Rows = _rows,
            Columns = _columns,
            Buttons = _buttons.ToList(),
        };

    public void LoadSettings(OscTriggerPanelSettings settings)
    {
        _rows = Math.Max(MinSize, settings.Rows);
        _columns = Math.Max(MinSize, settings.Columns);
        _buttons.Clear();
        _buttons.AddRange(settings.Buttons);
        OnChanged();
    }

    public void Clear()
    {
        _rows = DefaultRows;
        _columns = DefaultColumns;
        _buttons.Clear();
        OnChanged();
    }

    public event EventHandler? Changed;

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
