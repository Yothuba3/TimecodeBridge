using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// デコード済みLTCフレームの連続性ゲート。
/// 長時間の無信号中はノイズが偶然フレームとして解析されることがあり、それを採用すると
/// フレームレート自動判定が汚染され、本物のTC復帰後も誤解釈が続く。
/// 直近フレームと連続する2フレームが揃うまで採用しないことで孤立ノイズを排除する。
///
/// 保留は直近数フレームのリングとして持つ。本物とノイズが交互に届いても、
/// リング内の本物同士が連続と判定されて再ロックできる（1フレームだけの保留では
/// 本物とノイズが交互だと永久に噛み合わず、TCが一切進まなくなる）。
/// </summary>
public sealed class LtcContinuityGate
{
    // ToOrdinalは1秒=30の固定基準。次フレームとの差は同一秒内で1、
    // 秒境界では 30-(fps-1) となるため、24fps(フレーム23→0)の7までを連続とみなす。
    private const int MaxContinuousDiff = 7;
    private const int RingSize = 8;

    private readonly (long Ordinal, TimecodeValue Frame)[] _pending = new (long, TimecodeValue)[RingSize];
    private int _pendingCount;
    private long _lastAcceptedOrdinal = -1;

    // 累積カウンタ（信号エラー率の算出用）。Writeで入力を、Acceptで採用を数える。
    // 差分だけを使うので単調増加のままでよい（オーバーフローは実運用では起きない）。
    private long _totalWritten;
    private long _totalAccepted;

    /// <summary>ゲートに入力された総フレーム数（デコーダが出した数）。</summary>
    public long TotalWritten => Interlocked.Read(ref _totalWritten);

    /// <summary>採用された総フレーム数。</summary>
    public long TotalAccepted => Interlocked.Read(ref _totalAccepted);

    /// <summary>採用されたフレームを順に通知する。</summary>
    public event Action<TimecodeValue>? FrameAccepted;

    public void Reset()
    {
        _lastAcceptedOrdinal = -1;
        _pendingCount = 0;
        Interlocked.Exchange(ref _totalWritten, 0);
        Interlocked.Exchange(ref _totalAccepted, 0);
    }

    public void Write(TimecodeValue frame)
    {
        Interlocked.Increment(ref _totalWritten);
        long ord = frame.ToOrdinal();

        // 直前の採用フレームと連続していれば即採用
        if (_lastAcceptedOrdinal >= 0 && IsContinuous(_lastAcceptedOrdinal, ord))
        {
            Accept(frame);
            return;
        }

        // 保留の中に、このフレームの直前として連続する相手がいれば、それごと採用（再ロック）
        for (int i = 0; i < _pendingCount; i++)
        {
            if (IsContinuous(_pending[i].Ordinal, ord))
            {
                var earlier = _pending[i].Frame;
                Accept(earlier);
                Accept(frame);
                return;
            }
        }

        // 連続する相手がいなければ保留に積む（満杯なら最古を押し出す）
        if (_pendingCount < RingSize)
        {
            _pending[_pendingCount++] = (ord, frame);
        }
        else
        {
            Array.Copy(_pending, 1, _pending, 0, RingSize - 1);
            _pending[RingSize - 1] = (ord, frame);
        }
    }

    private void Accept(TimecodeValue frame)
    {
        _lastAcceptedOrdinal = frame.ToOrdinal();
        _pendingCount = 0;
        Interlocked.Increment(ref _totalAccepted);
        FrameAccepted?.Invoke(frame);
    }

    private static bool IsContinuous(long previous, long next)
    {
        long diff = next - previous;
        return diff >= 1 && diff <= MaxContinuousDiff;
    }
}
