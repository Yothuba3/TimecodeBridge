namespace TimecodeBridge.Core.Services;

/// <summary>
/// オーディオエラーイベント引数
/// </summary>
public class AudioErrorEventArgs : EventArgs
{
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 例外（存在する場合）
    /// </summary>
    public Exception? Exception { get; }

    public AudioErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}
