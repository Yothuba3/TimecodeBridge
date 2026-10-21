// LTC の耐障害性を測る。エンコーダが作った音にノイズを混ぜてデコーダへ流し、
// どこまで受信を保てるか、信号が戻ったとき正しく復帰するかを数字で出す。
//
//   dotnet run -- sweep [フレームレート]        ノイズを段階的に上げて限界を探す
//   dotnet run -- recover <ノイズ比> [秒]       強いノイズ → 無音 → 正常信号 の復帰を見る
//   dotnet run -- dropout <欠落秒>              信号が途切れて戻るときの挙動を見る
//   dotnet run -- rates                         全フレームレートでクリーン信号を通す
//   dotnet run -- cues <ノイズ比>               ノイズ下でキューが何回発火するかを数える
//   dotnet run -- e2e <ノイズ比> [--log]        ノイズ → 無音 → 復帰 を通してキュー発火まで見る
//
// 判定の基準は LtcContinuityGate の設計意図(クラスのコメント)。
// ・孤立したノイズは採用しない(2 フレーム連続して初めてロック)
// ・ロック中は 1 秒までの飛びを欠落とみなしてロックを保つ
// ・それを超える前進や巻き戻しはロックを外し、2 フレーム連続で再ロック

using System.Globalization;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Ltc;

const int SampleRate = 48000;

var mode = args.Length > 0 ? args[0] : "sweep";
return mode switch
{
    "sweep" => Sweep(ParseRate(args.ElementAtOrDefault(1)) ?? FrameRate.Fps30),
    "recover" => Recover(double.Parse(args.ElementAtOrDefault(1) ?? "1.0", CultureInfo.InvariantCulture),
                         int.Parse(args.ElementAtOrDefault(2) ?? "3", CultureInfo.InvariantCulture)),
    "dropout" => Dropout(int.Parse(args.ElementAtOrDefault(1) ?? "2", CultureInfo.InvariantCulture)),
    "rates" => Rates(),
    "cues" => Cues(double.Parse(args.ElementAtOrDefault(1) ?? "1.2", CultureInfo.InvariantCulture)),
    "e2e" => EndToEnd(double.Parse(args.ElementAtOrDefault(1) ?? "1.5", CultureInfo.InvariantCulture),
                      args.Contains("--log")),
    "dump" => Dump(ParseRate(args.ElementAtOrDefault(1)) ?? FrameRate.Fps30),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("usage: ltc-resilience sweep [24|25|29.97df|30] | recover <noise> [秒] | dropout <秒>");
    Console.WriteLine("       ltc-resilience rates | cues <noise> | e2e <noise> [--log] | dump [レート]");
    return 2;
}

static FrameRate? ParseRate(string? text) => text switch
{
    null => null,
    "24" => FrameRate.Fps24,
    "25" => FrameRate.Fps25,
    "29.97df" or "2997" => FrameRate.Fps2997Drop,
    "30" => FrameRate.Fps30,
    _ => null,
};

static string Name(FrameRate r) => r switch
{
    FrameRate.Fps24 => "24",
    FrameRate.Fps25 => "25",
    FrameRate.Fps2997Drop => "29.97df",
    _ => "30",
};

/// <summary>ノイズ比を上げながら、受信できたフレームの割合と時刻の正しさを測る。</summary>
static int Sweep(FrameRate rate)
{
    Console.WriteLine($"== ノイズ耐性(フレームレート {Name(rate)}、各 5 秒) ==");
    Console.WriteLine("ノイズ比は信号の振幅に対する雑音の振幅。1.0 で信号と同じ大きさ。");
    Console.WriteLine();
    Console.WriteLine("  ノイズ比 | 復号   | 採用   | 連続  | 時刻のずれ | 判定");
    Console.WriteLine("  ---------|--------|--------|-------|------------|------");

    double lastGood = 0;
    foreach (var noise in new[] { 0.0, 0.5, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 2.0 })
    {
        var r = Run(rate, seconds: 5, noise: noise, startAt: new TimecodeValue(1, 0, 0, 0, rate));
        var ratio = r.Expected == 0 ? 0 : (double)r.Accepted / r.Expected;
        var verdict = ratio >= 0.95 && r.MaxDrift == 0 ? "良好"
            : ratio >= 0.5 ? "劣化"
            : ratio > 0 ? "断続"
            : "受信不可";
        if (ratio >= 0.95 && r.MaxDrift == 0) lastGood = noise;
        Console.WriteLine($"  {noise,8:F2} | {r.Decoded,6} | {r.Accepted,6} | {ratio,4:P0} | {r.MaxDrift,10} | {verdict}");
    }
    Console.WriteLine();
    Console.WriteLine($"→ 時刻のずれ無しで受信できた上限: ノイズ比 {lastGood:F2}");
    return 0;
}

/// <summary>強いノイズ → 無音 → 正常信号。ノイズで汚れたあと正しく復帰するかを見る。</summary>
static int Recover(double noise, int seconds)
{
    Console.WriteLine($"== 復帰(ノイズ比 {noise:F2} を {seconds} 秒 → 無音 {seconds} 秒 → 正常 {seconds} 秒) ==");
    Console.WriteLine();

    var rate = FrameRate.Fps30;
    var gate = new LtcContinuityGate();
    var accepted = new List<TimecodeValue>();
    var phaseOf = new List<string>();
    var phase = "ノイズ";
    gate.FrameAccepted += f => { accepted.Add(f); phaseOf.Add(phase); };

    using var decoder = new LibltcDecoder();
    decoder.Initialize(SampleRate);
    decoder.FrameDecoded += (_, f) => gate.Write(f);

    // 1) 強いノイズを乗せた信号
    Feed(decoder, Encode(rate, seconds, new TimecodeValue(1, 0, 0, 0, rate)), noise);
    int afterNoise = accepted.Count;

    // 2) 無音(信号が切れた状態)
    phase = "無音";
    Feed(decoder, new float[SampleRate * seconds], 0);
    int afterSilence = accepted.Count;

    // 3) 正常な信号。時刻は続きから
    phase = "正常";
    var resumeAt = new TimecodeValue(2, 0, 0, 0, rate);
    Feed(decoder, Encode(rate, seconds, resumeAt), 0);

    var clean = accepted.Skip(afterSilence).ToList();
    var cleanPhase = phaseOf.Skip(afterSilence).ToList();
    int expected = seconds * rate.FramesPerSecond();

    Console.WriteLine($"  ノイズ中に採用したフレーム: {afterNoise}");
    Console.WriteLine($"  無音中に採用したフレーム  : {afterSilence - afterNoise}  (0 であるべき)");
    Console.WriteLine($"  復帰後に採用したフレーム  : {clean.Count} / {expected}");
    if (clean.Count > 0)
    {
        var first = clean[0];
        var drift = MaxDrift(clean, resumeAt, rate);
        Console.WriteLine($"  復帰後の最初の時刻        : {first}  (期待 {resumeAt} 近辺)");
        Console.WriteLine($"  復帰後の時刻のずれ        : {drift} フレーム");
        Console.WriteLine();
        Console.WriteLine(drift == 0 && clean.Count >= expected * 0.95
            ? "→ 正常信号に戻ると、ノイズの影響を引きずらず正しい時刻で受信を再開する"
            : "→ 復帰後に乱れが残っている");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("→ 復帰できていない");
    }
    return 0;
}

/// <summary>信号が途切れて戻る。欠落中に時刻が進んでいても追従できるかを見る。</summary>
static int Dropout(int gapSeconds)
{
    Console.WriteLine($"== 信号の途切れ({gapSeconds} 秒の無音をはさむ) ==");
    Console.WriteLine();
    var rate = FrameRate.Fps30;
    var gate = new LtcContinuityGate();
    var accepted = new List<TimecodeValue>();
    gate.FrameAccepted += f => accepted.Add(f);

    using var decoder = new LibltcDecoder();
    decoder.Initialize(SampleRate);
    decoder.FrameDecoded += (_, f) => gate.Write(f);

    Feed(decoder, Encode(rate, 3, new TimecodeValue(1, 0, 0, 0, rate)), 0);
    int before = accepted.Count;
    var lastBefore = accepted.Count > 0 ? accepted[^1] : default;

    Feed(decoder, new float[SampleRate * gapSeconds], 0);

    // 途切れている間も外の時間は進む。戻ったときの時刻はその分先になる
    var resumeAt = TimecodeValue.FromTotalFrames(
        lastBefore.TotalFrames() + (gapSeconds + 1) * rate.FramesPerSecond(), rate);
    Feed(decoder, Encode(rate, 3, resumeAt), 0);

    var after = accepted.Skip(before).ToList();
    Console.WriteLine($"  戻ってからの最初の 5 件: {string.Join(", ", after.Take(5))}");
    Console.WriteLine($"  戻ってからの最後の 3 件: {string.Join(", ", after.TakeLast(3))}");
    Console.WriteLine($"  途切れる前の最後の時刻: {lastBefore}");
    Console.WriteLine($"  戻ったときの時刻      : {resumeAt}");
    Console.WriteLine($"  戻ってから採用した数  : {after.Count} / {3 * rate.FramesPerSecond()}");
    // 途切れる直前に保留されていた 1 枚が、再ロックのときに一緒に採用されることがある。
    // それは乱れではないので、新しい時刻に入ってからの並びで判定する
    int resumeIndex = after.FindIndex(f => f.Hours == resumeAt.Hours && f.Minutes == resumeAt.Minutes && f.Seconds >= resumeAt.Seconds);
    var tracked = resumeIndex >= 0 ? after.Skip(resumeIndex).ToList() : after;
    if (tracked.Count > 0)
    {
        Console.WriteLine($"  新しい時刻の最初     : {tracked[0]}  (期待 {resumeAt} 近辺)");
        Console.WriteLine($"  新しい時刻での乱れ   : {MaxDrift(tracked, tracked[0], rate)} フレーム");
        if (resumeIndex > 0) Console.WriteLine($"  (途切れ前の保留 {resumeIndex} 枚が先に出ている)");
    }
    Console.WriteLine();
    bool ok = tracked.Count >= 3 * rate.FramesPerSecond() * 0.9 && MaxDrift(tracked, tracked.Count > 0 ? tracked[0] : resumeAt, rate) == 0;
    Console.WriteLine(ok
        ? "→ 大きく時刻が飛んでいても、2 フレーム連続で再ロックして新しい時刻に追従する"
        : "→ 追従できていない");
    return 0;
}

/// <summary>全フレームレートでクリーンな信号を通し、どれも同じように受信できるかを見る。</summary>
static int Rates()
{
    Console.WriteLine("== フレームレートごとのクリーン信号(各 5 秒) ==");
    Console.WriteLine();
    Console.WriteLine("  レート   | 期待  | 復号  | 採用  | 時刻のずれ | 判定");
    Console.WriteLine("  ---------|-------|-------|-------|------------|------");
    int bad = 0;
    foreach (var rate in new[] { FrameRate.Fps24, FrameRate.Fps25, FrameRate.Fps2997Drop, FrameRate.Fps30 })
    {
        var r = Run(rate, seconds: 5, noise: 0, startAt: new TimecodeValue(1, 0, 0, 0, rate));
        var ok = r.Accepted >= r.Expected * 0.95 && r.MaxDrift == 0;
        if (!ok) bad++;
        Console.WriteLine($"  {Name(rate),-8} | {r.Expected,5} | {r.Decoded,5} | {r.Accepted,5} | {r.MaxDrift,10} | {(ok ? "良好" : "問題あり")}");
    }
    Console.WriteLine();
    Console.WriteLine(bad == 0 ? "→ どのレートでも取りこぼしなく受信できる" : $"→ {bad} 件のレートで問題");
    return bad == 0 ? 0 : 1;
}

static int Dump(FrameRate rate)
{
    var gate = new LtcContinuityGate();
    var accepted = new List<TimecodeValue>();
    gate.FrameAccepted += f => accepted.Add(f);
    using var decoder = new LibltcDecoder();
    decoder.Initialize(SampleRate);
    decoder.FrameDecoded += (_, f) => gate.Write(f);
    var start = new TimecodeValue(1, 0, 0, 0, rate);
    var noise = Environment.GetEnvironmentVariable("DUMP_NOISE") is { } n ? double.Parse(n, CultureInfo.InvariantCulture) : 0;
    Feed(decoder, Encode(rate, 5, start), noise);
    Console.WriteLine($"開始 {start} / 採用 {accepted.Count} 件");
    Console.WriteLine("最初の 5: " + string.Join(", ", accepted.Take(5)));
    Console.WriteLine("最後の 5: " + string.Join(", ", accepted.TakeLast(5)));
    Console.WriteLine("全部: " + string.Join(", ", accepted.Select(x=>x.ToString())));
    Console.WriteLine($"開始の TotalFrames={start.TotalFrames()} / 先頭の TotalFrames={(accepted.Count>0?accepted[0].TotalFrames():0)}");
    return 0;
}

/// <summary>
/// ノイズの誤採用がキュー発火に及ぶか。ゲートが通した時刻をそのまま CueManager へ入れ、
/// 同じキューが二重に発火しないかを見る。
/// </summary>
static int Cues(double noise)
{
    Console.WriteLine($"== ノイズ(比 {noise:F2})がキュー発火に及ぼす影響 ==");
    Console.WriteLine();
    var rate = FrameRate.Fps30;
    var gate = new LtcContinuityGate();
    var timeline = new List<TimecodeValue>();
    gate.FrameAccepted += f => timeline.Add(f);
    using (var decoder = new LibltcDecoder())
    {
        decoder.Initialize(SampleRate);
        decoder.FrameDecoded += (_, f) => gate.Write(f);
        Feed(decoder, Encode(rate, 5, new TimecodeValue(1, 0, 0, 0, rate)), noise);
    }

    // ゲートが通した時刻を順に CueManager へ。01:00:00:10 のキューが何回出るかを数える
    var engine = new ReplayEngine();
    var sender = new CountingSender();
    var cues = new CueManager(engine, sender);
    cues.AddCue(new Cue
    {
        Id = "c1", Name = "本番 GO",
        TriggerTime = new TimecodeValue(1, 0, 0, 10, rate),
        OscAddress = "/go", TargetHostIds = ["h1"],
    });

    foreach (var tc in timeline) engine.Emit(tc);

    Console.WriteLine($"  ゲートが通した時刻 : {timeline.Count} 件");
    Console.WriteLine($"  うち 00 時台(ノイズ由来): {timeline.Count(t => t.Hours == 0)} 件");
    Console.WriteLine($"  キューの発火回数   : {sender.Sent} 回  (1 回であるべき)");
    Console.WriteLine();
    Console.WriteLine(sender.Sent switch
    {
        1 => "→ 期待どおり 1 回だけ発火した",
        0 => "→ 発火しなかった。ゲートが通した時刻に発火時刻が含まれていない(取りこぼし)",
        _ => $"→ 二重発火({sender.Sent} 回)。ノイズ由来の巻き戻しで発火済みが取り消されている",
    });
    return 0;
}

/// <summary>
/// ノイズ → 無音 → 正常信号 を一続きに流し、復帰後のキューが
/// 本来の 1 回だけ発火するかを見る。本番で起きる「一度信号が壊れたが戻った」状況。
/// </summary>
static int EndToEnd(double noise, bool log)
{
    Console.WriteLine($"== 通し(ノイズ比 {noise:F2} 3 秒 → 無音 3 秒 → 正常 3 秒) ==");
    Console.WriteLine();
    var rate = FrameRate.Fps30;
    var gate = new LtcContinuityGate();
    var timeline = new List<(string Phase, TimecodeValue Tc)>();
    var phase = "ノイズ";
    gate.FrameAccepted += f => timeline.Add((phase, f));

    using (var decoder = new LibltcDecoder())
    {
        decoder.Initialize(SampleRate);
        decoder.FrameDecoded += (_, f) => gate.Write(f);
        Feed(decoder, Encode(rate, 3, new TimecodeValue(1, 0, 0, 0, rate)), noise);
        phase = "無音";
        Feed(decoder, new float[SampleRate * 3], 0);
        phase = "正常";
        Feed(decoder, Encode(rate, 3, new TimecodeValue(1, 0, 10, 0, rate)), 0);
    }

    var engine = new ReplayEngine();
    var sender = new CountingSender();
    var cues = new CueManager(engine, sender);
    // 復帰後の時刻にキューを置く。ノイズ区間を通り抜けても取りこぼさないことを見る
    var triggerAt = new TimecodeValue(1, 0, 10, 15, rate);
    cues.AddCue(new Cue
    {
        Id = "c1", Name = "復帰後 GO",
        TriggerTime = triggerAt,
        OscAddress = "/go", TargetHostIds = ["h1"],
    });

    foreach (var (p, tc) in timeline)
    {
        int before = sender.Sent;
        engine.Emit(tc);
        if (log) Console.WriteLine($"  [{p,-4}] {tc}{(sender.Sent > before ? "  ← 発火" : "")}");
    }
    if (log) Console.WriteLine();

    foreach (var p in new[] { "ノイズ", "無音", "正常" })
        Console.WriteLine($"  {p,-4} 区間で採用: {timeline.Count(t => t.Phase == p)} フレーム");
    Console.WriteLine($"  キュー({triggerAt})の発火: {sender.Sent} 回  (1 回であるべき)");
    Console.WriteLine();
    Console.WriteLine(sender.Sent == 1 && timeline.Count(t => t.Phase == "無音") == 0
        ? "→ 想定どおり。無音中は採用せず、復帰後のキューを 1 回だけ発火した"
        : "→ 想定と違う");
    return 0;
}

static RunResult Run(FrameRate rate, int seconds, double noise, TimecodeValue startAt)
{
    var gate = new LtcContinuityGate();
    var accepted = new List<TimecodeValue>();
    gate.FrameAccepted += f => accepted.Add(f);
    int decoded = 0;

    using var decoder = new LibltcDecoder();
    decoder.Initialize(SampleRate);
    decoder.FrameDecoded += (_, f) => { decoded++; gate.Write(f); };

    Feed(decoder, Encode(rate, seconds, startAt), noise);

    return new RunResult(seconds * rate.FramesPerSecond(), decoded, accepted.Count, MaxDrift(accepted, startAt, rate));
}

/// <summary>
/// 採用した時刻の並びの乱れ(最大)。本物の LTC は 1 フレームずつ前進するので、
/// 逆行や飛びがあれば乱れとして数える。欠落は乱れではない(前進はしている)。
///
/// デコーダが返す FrameRate は観測した最大フレーム番号からの推定で、短い信号では
/// 確定しない。そのため TotalFrames での比較はせず、時・分・秒・フレームの並びだけを見る。
/// </summary>
static long MaxDrift(IReadOnlyList<TimecodeValue> accepted, TimecodeValue startAt, FrameRate rate)
{
    int fps = rate.FramesPerSecond();
    long Linear(TimecodeValue t) => ((t.Hours * 60L + t.Minutes) * 60L + t.Seconds) * fps + t.Frames;

    long worst = 0;
    long expectedStart = Linear(startAt);
    for (int i = 0; i < accepted.Count; i++)
    {
        long actual = Linear(accepted[i]);
        if (i == 0)
        {
            // 最初の 1 枚が開始時刻より手前なら、それ自体が乱れ
            worst = Math.Max(worst, Math.Abs(actual - expectedStart));
            continue;
        }
        long previous = Linear(accepted[i - 1]);
        // 前進していなければ逆行。前進していても 1 秒を超える飛びは乱れとみなす
        long step = actual - previous;
        if (step <= 0) worst = Math.Max(worst, 1 - step);
        else if (step > fps) worst = Math.Max(worst, step - fps);
    }
    return worst;
}

/// <summary>エンコーダは 16bit 整数モノラルを出す。扱いやすいよう -1..1 の実数へ直す。</summary>
static float[] Encode(FrameRate rate, int seconds, TimecodeValue startAt)
{
    var encoder = new LtcEncoder();
    encoder.Initialize(SampleRate, rate);
    encoder.VolumeLevel = 0.8f;
    int frames = seconds * rate.FramesPerSecond();
    var current = startAt;
    for (int i = 0; i < frames; i++)
    {
        encoder.EnqueueFrame(current);
        current = TimecodeValue.FromTotalFrames(current.TotalFrames() + 1, rate);
    }
    var bytes = new byte[SampleRate * seconds * 2 * 2];
    int read = encoder.Read(bytes, 0, bytes.Length);
    var samples = new float[read / 2];
    for (int i = 0; i < samples.Length; i++)
    {
        samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
    }
    return samples;
}

static void Feed(LibltcDecoder decoder, float[] samples, double noise)
{
    var rng = new Random(12345);
    var noisy = new float[samples.Length];
    for (int i = 0; i < samples.Length; i++)
    {
        // 一様乱数の白色雑音。ノイズ比は信号の振幅(0.8)に対する倍率
        double n = noise == 0 ? 0 : (rng.NextDouble() * 2 - 1) * 0.8 * noise;
        noisy[i] = (float)Math.Clamp(samples[i] + n, -1.0, 1.0);
    }
    // 実機と同じくブロック単位で渡す(1 回あたり 1024 サンプル)
    const int block = 1024;
    var buffer = new byte[block * 4];
    for (int offset = 0; offset < noisy.Length; offset += block)
    {
        int count = Math.Min(block, noisy.Length - offset);
        Buffer.BlockCopy(noisy, offset * 4, buffer, 0, count * 4);
        decoder.ProcessSamples(buffer, count * 4, SampleRate, 32, 1);
    }
}

readonly record struct RunResult(int Expected, int Decoded, int Accepted, long MaxDrift);

/// <summary>与えた時刻をそのまま流すだけのエンジン。CueManager の発火判定を動かすため。</summary>
sealed class ReplayEngine : ITimecodeEngine
{
    public TimecodeValue CurrentRawTimecode { get; private set; }
    public TimecodeValue CurrentOffsetTimecode => CurrentRawTimecode;
    public TimecodeOffset Offset { get; set; }
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeSourceType ActiveSource => TimecodeSourceType.Ltc;
    public bool IsReceiving => true;
    public double FreerunDurationSeconds { get; set; }
    public bool IsFreerunning => false;
    public LtcSignalCounts LtcSignalCounts => new(0, 0);
    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
    public event EventHandler<AudioErrorEventArgs>? AudioErrorOccurred;
    public void Emit(TimecodeValue tc)
    {
        CurrentRawTimecode = tc;
        TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(tc, tc));
    }
    public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
    public void StartGenerator(GeneratorSettings settings) { }
    public void ResumeGenerator() { }
    public void StopGenerator() { }
    public void ResetGenerator() { }
    public void ResetGenerator(TimecodeValue startTime) { }
    public void Stop() { }
    public void Dispose() { }
}

/// <summary>送信回数だけ数える。</summary>
sealed class CountingSender : IOscSender
{
    public int Sent { get; private set; }
    public event EventHandler<OscSendResultEventArgs>? SendCompleted;
    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) => Sent++;
    public OscDispatchResult SendWithResult(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
    {
        Sent++;
        return new OscDispatchResult(1, Array.Empty<string>());
    }
    public void SendPing(string hostId) { }
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
}
