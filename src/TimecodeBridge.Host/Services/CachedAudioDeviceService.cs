using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Host.Services;

/// <summary>
/// デバイス一覧を「再スキャン」(audio.refreshDevices)まで使い回す。
/// Windows の列挙はエンドポイントのプロパティ読みで 1 回 0.5 秒ほどかかり、状態を組み立てるたびに列挙すると
/// モード切替や発火(オートミュートで transport を組み直す)のたびに UI スレッドが 1〜2 秒止まっていた。
/// Web へ一覧を送るのは receive/generator の patch だけなので、画面の一覧が更新される契機は従来どおり。
/// 再スキャンは <see cref="RefreshAsync"/> で裏のスレッドで列挙し、終わるまでは古い一覧を返す。
/// </summary>
public sealed class CachedAudioDeviceService : IAudioDeviceService
{
    private readonly IAudioDeviceService _inner;
    private readonly object _lock = new();
    private IReadOnlyList<AudioDeviceInfo>? _capture;
    private IReadOnlyList<AudioDeviceInfo>? _render;
    private Task? _scan;

    public CachedAudioDeviceService(IAudioDeviceService inner) => _inner = inner;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        WaitForFirstScan();
        lock (_lock) return _capture ??= _inner.GetCaptureDevices();
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        WaitForFirstScan();
        lock (_lock) return _render ??= _inner.GetRenderDevices();
    }

    /// <summary>次の取得で(呼び出したスレッドで)列挙し直す。</summary>
    public void Refresh()
    {
        lock (_lock) { _capture = null; _render = null; }
    }

    /// <summary>
    /// 裏のスレッドで列挙し、終わったら一覧を差し替える。その間の取得は古い一覧を返す(一覧がまだ無いときだけ終わるのを待つ)。
    /// 走っている列挙があればそれを返し、二重に列挙しない。起動時の先読みにも使う。
    /// </summary>
    public Task RefreshAsync()
    {
        lock (_lock)
        {
            if (_scan is { IsCompleted: false } running) return running;
            return _scan = Task.Run(() =>
            {
                var capture = _inner.GetCaptureDevices();
                var render = _inner.GetRenderDevices();
                lock (_lock) { _capture = capture; _render = render; }
            });
        }
    }

    private void WaitForFirstScan()
    {
        Task? scan;
        lock (_lock) scan = _capture is null || _render is null ? _scan : null;
        if (scan is null || scan.IsCompleted) return;
        try { scan.Wait(); }
        catch (AggregateException) { /* 失敗したら呼び出し元で同期に列挙し直す */ }
    }
}
