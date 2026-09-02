using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// 受信したLTCフレームの番号からフレームレートを判定する。
/// 「先頭から終わりまで観測できた秒」を直近2つ分ためて、その最大フレーム番号とDFフラグの多数決で確定する。
/// 設定値に依存せず、途中で信号源が変わっても数秒で上下どちらにも追従する。
/// 確定値では表せない大きいフレーム番号が来たときだけ、その場でレートを引き上げる
/// （24fps扱いのままフレーム27を演算すると値が壊れるため）。
/// </summary>
public sealed class LtcFrameRateDetector
{
    private const int RequiredCompleteSeconds = 2;
    // 半分以上欠けた秒は最大フレーム番号を取り逃している可能性があるので証拠にしない
    private const int MinFramesPerSecond = 12;

    private readonly record struct SecondStats(int MaxFrame, int Frames, int DropFrames);

    private readonly List<SecondStats> _completeSeconds = [];
    private long _currentSecond = -1;
    private bool _currentStartedAtBoundary;
    private int _currentMaxFrame;
    private int _currentFrames;
    private int _currentDropFrames;
    private FrameRate _confirmed;

    public LtcFrameRateDetector(FrameRate initial)
    {
        _confirmed = initial;
    }

    /// <summary>証拠から確定したレート。証拠が揃うまでは初期値のまま。</summary>
    public FrameRate Confirmed => _confirmed;

    public void Reset(FrameRate initial)
    {
        _confirmed = initial;
        _completeSeconds.Clear();
        _currentSecond = -1;
        _currentStartedAtBoundary = false;
    }

    /// <summary>
    /// フレームを1つ観測し、そのフレームに適用すべきレートを返す。
    /// </summary>
    public FrameRate Observe(TimecodeValue frame)
    {
        long second = frame.Hours * 3600L + frame.Minutes * 60 + frame.Seconds;
        if (second != _currentSecond)
        {
            if (_currentStartedAtBoundary && _currentFrames >= MinFramesPerSecond)
            {
                _completeSeconds.Add(new SecondStats(_currentMaxFrame, _currentFrames, _currentDropFrames));
                if (_completeSeconds.Count > RequiredCompleteSeconds)
                {
                    _completeSeconds.RemoveAt(0);
                }
            }

            // 秒が1つ進んだか、フレーム0から始まったときだけ「先頭から観測した秒」になる。
            // 巻き戻しやジャンプの後は別の信号源かもしれないので証拠を捨てて仕切り直す
            bool consecutive = _currentSecond >= 0 && second == _currentSecond + 1;
            if (!consecutive)
            {
                _completeSeconds.Clear();
            }
            _currentStartedAtBoundary = consecutive || frame.Frames == 0;

            _currentSecond = second;
            _currentMaxFrame = 0;
            _currentFrames = 0;
            _currentDropFrames = 0;
        }

        _currentFrames++;
        if (frame.FrameRate.IsDropFrame()) _currentDropFrames++;
        if (frame.Frames > _currentMaxFrame) _currentMaxFrame = frame.Frames;

        if (_completeSeconds.Count >= RequiredCompleteSeconds)
        {
            int maxFrame = 0, frames = 0, dropFrames = 0;
            foreach (var s in _completeSeconds)
            {
                maxFrame = Math.Max(maxFrame, s.MaxFrame);
                frames += s.Frames;
                dropFrames += s.DropFrames;
            }
            _confirmed = LtcDecoder.DetermineFrameRate(maxFrame, dropFrames * 2 > frames);
        }

        if (frame.Frames >= _confirmed.FramesPerSecond())
        {
            return LtcDecoder.DetermineFrameRate(frame.Frames, frame.FrameRate.IsDropFrame());
        }
        return _confirmed;
    }
}
