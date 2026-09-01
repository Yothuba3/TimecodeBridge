using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;
using TimecodeBridge.App.Views;

namespace TimecodeBridge.App.Tests.Views;

/// <summary>
/// ポン出しの行/列入力の確定動作（空文字・不正値の補正、Enter即時反映）
/// </summary>
public class SizeInputCommitTests
{
    private sealed class StubOscSender : IOscSender
    {
        public event EventHandler<OscSendResultEventArgs>? SendCompleted;
        public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }
        public void SendPing(string hostId) { }
        public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
    }

    private sealed class StubHostRegistry : IHostRegistry
    {
        public IReadOnlyList<OscHost> Hosts => [];
        public event EventHandler<HostChangedEventArgs>? HostChanged;
        public void AddHost(OscHost host) { }
        public void UpdateHost(string hostId, OscHost updatedHost) { }
        public void RemoveHost(string hostId) { }
        public void SetHostEnabled(string hostId, bool enabled) { }
        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) => [];
    }

    private sealed class StubProjectService : IProjectService
    {
        public string? CurrentFilePath => null;
        public bool HasUnsavedChanges => false;
        public event EventHandler<EventArgs>? UnsavedChangesStatusChanged;
        public event EventHandler<EventArgs>? ChangeCommitted;
        public ProjectData LoadProject(string filePath) => new();
        public void SaveProject(string filePath, ProjectData data) { }
        public void MarkAsChanged() { }
        public void Reset() { }
    }

    private sealed class StubTriggerDialogService : IOscTriggerDialogService
    {
        public OscTriggerEditResult ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title, bool canDelete)
            => new(OscTriggerEditAction.Cancel, null);
    }

    private static (OscTriggerPanelView View, OscTriggerPanelViewModel Vm, TextBox RowsBox) CreateView()
    {
        var registry = new StubHostRegistry();
        var manager = new OscTriggerPanelManager(new StubOscSender(), registry);
        var vm = new OscTriggerPanelViewModel(manager, new StubTriggerDialogService(), registry, new StubProjectService());
        var view = new OscTriggerPanelView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        var rowsBox = view.FindControl<TextBox>("RowsBox")!;
        return (view, vm, rowsBox);
    }

    private static void RaiseEnter(TextBox box)
        => box.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            Source = box,
        });

    private static void RaiseLostFocus(TextBox box)
        => box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent, box));

    [AvaloniaFact]
    public void 空文字でEnterすると1に補正される()
    {
        var (_, vm, rowsBox) = CreateView();

        rowsBox.Text = "";
        RaiseEnter(rowsBox);

        Assert.Equal(1, vm.Rows);
        Assert.Equal("1", rowsBox.Text);
    }

    [AvaloniaFact]
    public void 数値以外や0以下はフォーカス喪失時に1へ補正される()
    {
        var (_, vm, rowsBox) = CreateView();

        rowsBox.Text = "abc";
        RaiseLostFocus(rowsBox);
        Assert.Equal(1, vm.Rows);

        rowsBox.Text = "0";
        RaiseLostFocus(rowsBox);
        Assert.Equal(1, vm.Rows);
        Assert.Equal("1", rowsBox.Text);
    }

    [AvaloniaFact]
    public void Enterで即時にグリッドへ反映される()
    {
        var (_, vm, rowsBox) = CreateView();

        rowsBox.Text = "6";
        RaiseEnter(rowsBox);

        Assert.Equal(6, vm.Rows);
        Assert.Equal(6 * vm.Columns, vm.Cells.Count);
    }

    [AvaloniaFact]
    public void 上限超えはViewModel側の最大値へ丸められ表示にも戻る()
    {
        var (_, vm, rowsBox) = CreateView();

        rowsBox.Text = "99";
        RaiseEnter(rowsBox);

        Assert.Equal(OscTriggerPanelManager.MaxSize, vm.Rows);
        Assert.Equal(OscTriggerPanelManager.MaxSize.ToString(), rowsBox.Text);
    }
}
