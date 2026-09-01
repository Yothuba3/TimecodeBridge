using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TimecodeBridge.macOS.ViewModels;

/// <summary>
/// Avalonia用のベースViewModelクラス
/// UI スレッドへのディスパッチ機能を提供
/// </summary>
public abstract class DispatcherViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// アクションをUIスレッド上で実行する
    /// </summary>
    /// <param name="action">実行するアクション</param>
    protected void RunOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>
    /// アクションをUIスレッド上で非同期実行する
    /// </summary>
    /// <param name="action">実行するアクション</param>
    protected async Task RunOnUIThreadAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>
    /// リソースを解放する
    /// </summary>
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
