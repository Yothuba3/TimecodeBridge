// macOS実機でのLTCループバック検証ツール（開発用・配布物には含まない）。
// 仮想ループバックデバイス（Pro Tools Audio Bridge, BlackHole など。出力がそのまま入力に戻るもの）に
// ジェネレータでLTCを出力し、同じデバイスからキャプチャして復号できるかを CoreAudio 実装ごしに確かめる。
//
//   dotnet run -- list                              デバイス一覧と動作サンプルレート
//   dotnet run -- run "Bridge 2-A" [秒=15] [レート=0]  ループバックE2E（レート指定時は一時的に切替えて復元）
//   dotnet run -- set-rate <deviceId> <rate>         デバイスの動作レート変更（96kなど、切替後は数秒待って run する）
//   dotnet run -- probe-out <deviceId> / probe-in <deviceId>   再生・キャプチャ単体の起動確認
//
// 合格条件: 期間中に毎秒25フレーム以上復号、逆行なし、欠落は開始直後の1回まで、
// ジェネレータとの遅延8フレーム以内（実測は出力先行100ms＋バッファ＋LTC1フレーム長で5〜6フレーム）。

using TimecodeBridge.Core.Audio;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TimecodeBridge.App.Services;
using TimecodeBridge.Mac;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;

var devSvc = new CoreAudioDeviceService();
var inputs = devSvc.GetCaptureDevices();
var outputs = devSvc.GetRenderDevices();

if (args.Length == 0 || args[0] == "list")
{
    Console.WriteLine("-- inputs --");
    foreach (var d in inputs) Console.WriteLine($"{d.Id,6}  {d.DisplayName}  nsrt={Ca.GetNominalRate(uint.Parse(d.Id))}");
    Console.WriteLine("-- outputs --");
    foreach (var d in outputs) Console.WriteLine($"{d.Id,6}  {d.DisplayName}  nsrt={Ca.GetNominalRate(uint.Parse(d.Id))}");
    return 0;
}

if (args[0] == "set-rate")
{
    uint id = uint.Parse(args[1]); double rate = double.Parse(args[2]);
    Console.WriteLine($"device {id}: {Ca.GetNominalRate(id)} -> set {rate}");
    Ca.SetNominalRate(id, rate);
    Thread.Sleep(500);
    Console.WriteLine($"device {id}: now {Ca.GetNominalRate(id)}");
    return 0;
}

if (args[0] == "gen")
{
    // 指定した出力デバイスへ LTC(30fps, 01:00:00:00 から)を流し続ける。Host アプリの実機確認用
    var name = args.Length > 1 ? args[1] : throw new ArgumentException("gen \"<出力デバイス名の一部>\" [秒]");
    int genSeconds = args.Length > 2 ? int.Parse(args[2]) : 60;
    var genOut = outputs.First(d => d.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
    var g = new TimecodeEngine(FrameRate.Fps30, devSvc, () => new CoreAudioCapture(), () => new CoreAudioPlayback());
    g.StartGenerator(new GeneratorSettings { FrameRate = FrameRate.Fps30, StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30), OutputDeviceId = genOut.Id, VolumeLevel = 0.8f });
    Console.WriteLine($"gen -> {genOut.DisplayName} ({genOut.Id}) for {genSeconds}s");
    Thread.Sleep(genSeconds * 1000);
    g.Stop();
    return 0;
}
if (args[0] == "rx")
{
    // 受信だけを行い、連続性(欠落・逆行)を数える。別プロセスの gen と組み合わせて、プロセスをまたぐループバックの挙動を調べる
    var name = args.Length > 1 ? args[1] : throw new ArgumentException("rx \"<入力デバイス名の一部>\" [秒]");
    int rxSeconds = args.Length > 2 ? int.Parse(args[2]) : 20;
    var rxIn = inputs.First(d => d.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
    Func<TimecodeBridge.Core.Services.Interfaces.ILtcDecoder>? rxFactory =
        Environment.GetEnvironmentVariable("TCB_DECODER") == "libltc" ? () => new TimecodeBridge.Ltc.LibltcDecoder() : null;
    var rx = new TimecodeEngine(FrameRate.Fps30, devSvc, () => new CoreAudioCapture(), () => new CoreAudioPlayback(), rxFactory);
    long gaps = 0, backwards = 0, total = 0; TimecodeValue? prev = null; var firstBad = new List<string>();
    rx.TimecodeUpdated += (_, e) =>
    {
        total++;
        if (prev is { } p)
        {
            long d = e.RawTimecode.ToOrdinal() - p.ToOrdinal();
            if (d < 0) { backwards++; if (firstBad.Count < 5) firstBad.Add($"{p}->{e.RawTimecode}"); }
            else if (d > 1) { gaps++; if (firstBad.Count < 5) firstBad.Add($"{p}->{e.RawTimecode}(+{d})"); }
        }
        prev = e.RawTimecode;
    };
    rx.StartLtc(rxIn.Id);
    Console.WriteLine($"rx <- {rxIn.DisplayName} ({rxIn.Id}) decoder={(rxFactory is null ? "managed" : "libltc")} for {rxSeconds}s");
    Thread.Sleep(rxSeconds * 1000);
    rx.Stop();
    Console.WriteLine($"rx frames={total} gaps={gaps} backwards={backwards} gate={rx.LtcSignalCounts.Accepted}/{rx.LtcSignalCounts.Written} first={string.Join(" ", firstBad)}");
    return (gaps == 0 && backwards == 0 && total > 0) ? 0 : 1;
}
if (args[0] == "probe-out" || args[0] == "probe-in")
{
    var id = args[1];
    var dev = (args[0] == "probe-out" ? outputs : inputs).First(d => d.Id == id);
    Console.WriteLine($"probe {args[0]} on {dev.DisplayName} ({dev.Id})");
    var t = Stopwatch.StartNew();
    if (args[0] == "probe-out")
    {
        var pb = new CoreAudioPlayback();
        pb.Start(dev); Console.WriteLine($"[{t.Elapsed.TotalSeconds:F3}] started");
        Thread.Sleep(1000);
        pb.Stop(); pb.Dispose(); Console.WriteLine($"[{t.Elapsed.TotalSeconds:F3}] stopped");
    }
    else
    {
        long n = 0; double sumSq = 0;
        var cap = new CoreAudioCapture();
        cap.ErrorOccurred += (_, e) => Console.WriteLine($"[{t.Elapsed.TotalSeconds:F3}] error: {e.Message}");
        cap.AudioSamplesAvailable += (_, e) => { Interlocked.Add(ref n, e.Samples.Length); foreach (var v in e.Samples) sumSq += v * v; };
        cap.Start(dev); Console.WriteLine($"[{t.Elapsed.TotalSeconds:F3}] started");
        Thread.Sleep(2000);
        cap.Stop(); cap.Dispose();
        Console.WriteLine($"[{t.Elapsed.TotalSeconds:F3}] stopped samples={n} rms={(n > 0 ? Math.Sqrt(sumSq / n) : 0):F4}");
    }
    return 0;
}

string needle = args[1];
int seconds = args.Length > 2 ? int.Parse(args[2]) : 15;
double wantRate = args.Length > 3 ? double.Parse(args[3]) : 0;

var inDev = inputs.FirstOrDefault(d => d.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
    ?? throw new Exception($"input device not found: {needle}");
var outDev = outputs.FirstOrDefault(d => d.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
    ?? throw new Exception($"output device not found: {needle}");
uint devId = uint.Parse(inDev.Id);
double origRate = Ca.GetNominalRate(devId);
Console.WriteLine($"device: {inDev.DisplayName} (in={inDev.Id}, out={outDev.Id}) nominal={origRate}");

if (wantRate > 0 && Math.Abs(wantRate - origRate) > 0.5)
{
    Ca.SetNominalRate(devId, wantRate);
    Thread.Sleep(500);
    Console.WriteLine($"nominal rate set -> {Ca.GetNominalRate(devId)}");
    if (uint.Parse(outDev.Id) != devId) { Ca.SetNominalRate(uint.Parse(outDev.Id), wantRate); Thread.Sleep(500); }
}

var received = new List<(double T, TimecodeValue Tc)>();
var statuses = new List<(double T, string S)>();
long sampleCount = 0; float peak = 0;
var sw = Stopwatch.StartNew();

var gen = new TimecodeEngine(FrameRate.Fps30, devSvc, () => new CoreAudioCapture(), () => new CoreAudioPlayback());
// 環境変数 TCB_DECODER=libltc で受信側デコーダを libltc(v3)に切り替え、同条件で自作デコーダと比較する
Func<TimecodeBridge.Core.Services.Interfaces.ILtcDecoder>? decoderFactory =
    Environment.GetEnvironmentVariable("TCB_DECODER") == "libltc" ? () => new TimecodeBridge.Ltc.LibltcDecoder() : null;
Console.WriteLine($"decoder: {(decoderFactory is null ? "managed(LtcDecoder)" : "libltc")}");
var ltc = new TimecodeEngine(FrameRate.Fps30, devSvc, () => new CoreAudioCapture(), () => new CoreAudioPlayback(), decoderFactory);
gen.AudioErrorOccurred += (_, e) => Console.WriteLine($"[gen audio error] {e.Message}");
ltc.AudioErrorOccurred += (_, e) => Console.WriteLine($"[ltc audio error] {e.Message}");
ltc.TimecodeUpdated += (_, e) => { lock (received) received.Add((sw.Elapsed.TotalSeconds, e.RawTimecode)); };
ltc.StatusChanged += (_, e) => { lock (statuses) statuses.Add((sw.Elapsed.TotalSeconds, e.Status.ToString())); };
ltc.AudioSamplesAvailable += (_, e) =>
{
    Interlocked.Add(ref sampleCount, e.Samples.Length);
    foreach (var s in e.Samples) { var a = Math.Abs(s); if (a > peak) peak = a; }
};

try
{
    gen.StartGenerator(new GeneratorSettings
    {
        FrameRate = FrameRate.Fps30,
        StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30),
        OutputDeviceId = outDev.Id,
        VolumeLevel = 0.8f,
    });
    Console.WriteLine("generator started");
    ltc.StartLtc(inDev.Id);
    Console.WriteLine("ltc capture started");

    var lags = new List<long>();
    for (int i = 0; i < seconds; i++)
    {
        Thread.Sleep(1000);
        var g = gen.CurrentRawTimecode; var l = ltc.CurrentRawTimecode;
        long lag = g.TotalFrames() - l.TotalFrames();
        lags.Add(lag);
        int n; lock (received) n = received.Count;
        Console.WriteLine($"t={sw.Elapsed.TotalSeconds,5:F1}s gen={g} ltc={l} lag={lag}f recv={n} receiving={ltc.IsReceiving} fps={ltc.FrameRate} samples={Interlocked.Read(ref sampleCount)} peak={peak:F3}");
    }

    // ---- summary ----
    List<(double T, TimecodeValue Tc)> r; lock (received) r = received.ToList();
    int gaps = 0, dups = 0, backwards = 0;
    var gapNotes = new List<string>();
    for (int i = 1; i < r.Count; i++)
    {
        long d = r[i].Tc.TotalFrames() - r[i - 1].Tc.TotalFrames();
        if (d == 1) continue;
        if (d == 0) dups++; else if (d < 0) backwards++; else { gaps++; gapNotes.Add($"{r[i].T:F2}s {r[i - 1].Tc}->{r[i].Tc}(+{d})"); }
    }
    var counts = ltc.LtcSignalCounts;
    Console.WriteLine("---- summary ----");
    Console.WriteLine($"frames received : {r.Count} (first={(r.Count > 0 ? r[0].Tc.ToString() : "-")} last={(r.Count > 0 ? r[^1].Tc.ToString() : "-")})");
    Console.WriteLine($"continuity      : gaps={gaps} dups={dups} backwards={backwards} {string.Join(" ", gapNotes)}");
    Console.WriteLine($"gate counts     : written={counts.Written} accepted={counts.Accepted}");
    Console.WriteLine($"lag (gen-ltc)   : min={lags.Min()} max={lags.Max()} frames");
    Console.WriteLine($"statuses        : {string.Join(", ", statuses.Select(s => $"{s.T:F1}s:{s.S}"))}");
    Console.WriteLine($"audio           : samples={sampleCount} peak={peak:F3}");
    // 遅延は出力先行(100ms)+デバイスバッファ+LTC1フレーム長で5〜6フレームが実測値。欠落は開始直後の同期待ちを許容
    bool ok = r.Count > seconds * 25 && gaps <= 1 && backwards == 0 && lags.Max() <= 8;
    Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
    return ok ? 0 : 2;
}
finally
{
    ltc.Stop(); ltc.Dispose();
    gen.Stop(); gen.Dispose();
    if (wantRate > 0 && Math.Abs(Ca.GetNominalRate(devId) - origRate) > 0.5)
    {
        Ca.SetNominalRate(devId, origRate);
        if (uint.Parse(outDev.Id) != devId) Ca.SetNominalRate(uint.Parse(outDev.Id), origRate);
        Thread.Sleep(300);
        Console.WriteLine($"nominal rate restored -> {Ca.GetNominalRate(devId)}");
    }
}

static class Ca
{
    const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const uint kNominalSampleRate = 0x6E737274; // 'nsrt'
    [StructLayout(LayoutKind.Sequential)] struct Addr { public uint Selector, Scope, Element; }
    [DllImport(CoreAudio)] static extern int AudioObjectGetPropertyData(uint id, ref Addr a, uint qs, IntPtr q, ref uint size, IntPtr data);
    [DllImport(CoreAudio)] static extern int AudioObjectSetPropertyData(uint id, ref Addr a, uint qs, IntPtr q, uint size, IntPtr data);
    public static double GetNominalRate(uint id)
    {
        var a = new Addr { Selector = kNominalSampleRate, Scope = 0x676C6F62, Element = 0 };
        uint size = 8; IntPtr p = Marshal.AllocHGlobal(8);
        try { int st = AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, p); return st == 0 ? Marshal.PtrToStructure<double>(p) : -st; }
        finally { Marshal.FreeHGlobal(p); }
    }
    public static void SetNominalRate(uint id, double rate)
    {
        var a = new Addr { Selector = kNominalSampleRate, Scope = 0x676C6F62, Element = 0 };
        IntPtr p = Marshal.AllocHGlobal(8);
        try { Marshal.StructureToPtr(rate, p, false); int st = AudioObjectSetPropertyData(id, ref a, 0, IntPtr.Zero, 8, p); if (st != 0) Console.WriteLine($"[warn] set nominal rate failed: {st}"); }
        finally { Marshal.FreeHGlobal(p); }
    }
}
