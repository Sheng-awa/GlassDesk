using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.Platform;

if (args.Length >= 3 && args[0] == "--render-only")
{
    using var generatedTone = new ToneOutput(int.Parse(args[1]), int.Parse(args[2]));
    await Task.Delay(int.Parse(args[2]));
    return;
}

var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks.Add(name);
}
async Task WaitFor(Func<bool> condition, string name, int milliseconds = 2500)
{
    var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds / 1000d * Stopwatch.Frequency);
    while (!condition() && Stopwatch.GetTimestamp() < deadline) await Task.Delay(10);
    Check(condition(), name);
}
var analyzer = new AudioSpectrumAnalyzer();
var signal = new float[AudioSpectrumAnalyzer.WindowFrames * 2];
var bands = new float[8];
foreach (var (frequency, expectedBand) in new[] { (80, 1), (250, 2), (750, 3), (1500, 4), (4000, 5), (9000, 6), (18000, 7) })
{
    for (var frame = 0; frame < signal.Length / 2; frame++) signal[frame * 2] = signal[frame * 2 + 1] = .5f * (float)Math.Sin(2 * Math.PI * frequency * frame / 48000);
    var rms = analyzer.Analyze(signal, 48000, bands);
    var strongest = Array.IndexOf(bands, bands.Max());
    Check(strongest == expectedBand, $"known {frequency} Hz sine peaks in band {expectedBand}");
    Check(Math.Abs(rms - Math.Sqrt(.125)) < .007, $"known {frequency} Hz RMS agrees with amplitude/sqrt(2)");
}
for (var frame = 0; frame < signal.Length / 2; frame++) { signal[frame * 2] = .5f * (float)Math.Sin(2 * Math.PI * 1500 * frame / 48000); signal[frame * 2 + 1] = -signal[frame * 2]; }
Check(analyzer.Analyze(signal, 48000, bands) > .35f && bands[4] > .8f, "anti-phase stereo retains real energy");
Array.Clear(signal);
Check(analyzer.Analyze(signal, 48000, bands) == 0 && bands.All(value => value == 0), "silent samples produce exact zero RMS and bands");
Check(analyzer.Analyze([], 48000, bands) == 0 && bands.All(value => value == 0), "missing packets produce exact zero");
signal[0] = float.NaN; signal[1] = float.PositiveInfinity;
Check(analyzer.Analyze(signal, 48000, bands) == 0 && bands.All(float.IsFinite), "nonfinite input is sanitized");

AudioProcessIdentity? Identity(uint id) => new(id, id == 12 ? 200UL : 100UL);
Check(NetEaseProcessResolver.SelectUniqueTree([], Identity).Identity is null, "no NetEase process is unavailable");
Check(NetEaseProcessResolver.SelectUniqueTree([new(10, 1, "cloudmusic.exe"), new(12, 10, "cloudmusic.exe"), new(99, 1, "other.exe")], Identity).Identity?.ProcessId == 10,
    "one NetEase tree resolves root and ignores unrelated players");
Check(NetEaseProcessResolver.SelectUniqueTree([new(10, 1, "cloudmusic.exe"), new(12, 1, "cloudmusic.exe")], Identity).Identity is null,
    "multiple NetEase roots are unavailable rather than guessed");
Check(NetEaseProcessResolver.SelectUniqueTree([new(10, 1, "cloudmusic.exe")], _ => null).Identity is null, "unverified executable identity is unavailable");
Check(NetEaseProcessResolver.SelectUniqueTree([new(10, 12, "cloudmusic.exe"), new(12, 1, "cloudmusic.exe")], Identity).Identity is null,
    "reused newer parent PID cannot merge independent process trees");
Check(NetEaseProcessResolver.SelectUniqueTree([new(10, 1, "cloudmusic.exe"), new(11, 10, "helper.exe"), new(12, 11, "cloudmusic.exe")],
    id => new(id, id == 10 ? 100UL : id == 11 ? 150UL : 200UL)).Identity?.ProcessId == 10,
    "verified intermediary ancestor preserves one process tree");
Check(NetEaseProcessResolver.SelectUniqueTree([new(10, 1, "cloudmusic.exe"), new(11, 10, "helper.exe"), new(12, 11, "cloudmusic.exe")],
    id => new(id, id == 10 ? 100UL : id == 11 ? 300UL : 200UL)).Identity is null,
    "reused intermediary parent PID cannot merge independent process trees");
Check(new NetEaseProcessResolver().Resolve(MediaSnapshot.Disconnected).Identity is null, "disconnected source never resolves an audio target");

var playing = new MediaSnapshot("session-a", 1, true, "test", "", "", null, PlaybackState.Playing, null, null, new());
var resolver = new FakeResolver(); var factory = new FakeFactory();
using (var source = new ProcessAudioVisualizationSource(resolver: resolver, captureFactory: factory))
{
    source.SetTarget(playing, false);
    await Task.Delay(100);
    Check(factory.Created == 0 && source.Current.State == AudioVisualizationState.Inactive, "hidden/no-visible-bars does not capture");
    source.SetTarget(playing, true);
    await WaitFor(() => source.Current.State == AudioVisualizationState.Capturing, "playing visible target captures real samples");
    var created = factory.Created;
    source.SetTarget(playing with { ContentVersion = 2, Title = "changed" }, true);
    await Task.Delay(100);
    Check(factory.Created == created, "same-session metadata updates do not restart capture");
    factory.Silent = true;
    await WaitFor(() => source.Current.State == AudioVisualizationState.Silent && source.Current.Bands.All(value => value == 0), "real silence clears bars");
    source.SetTarget(playing with { State = PlaybackState.Paused }, true);
    Check(source.Current.State == AudioVisualizationState.Inactive && source.Current.Rms == 0, "pause immediately publishes zero");
    await WaitFor(() => factory.Disposed == factory.Created, "pause releases native capture");
    factory.Silent = false;
    source.SetTarget(playing, true);
    await WaitFor(() => source.Current.State == AudioVisualizationState.Capturing, "resume starts capture again");
    source.SetTarget(playing with { SessionToken = "session-b" }, true);
    await WaitFor(() => source.Current.SessionToken == "session-b" && factory.Created > created + 1, "session replacement restarts capture and never leaks prior token");
    resolver.Identity = new(20, 400);
    created = factory.Created;
    await WaitFor(() => factory.Created > created && factory.LastIdentity.ProcessId == 20, "PID change/restart rebinds capture");
    resolver.Identity = null;
    await WaitFor(() => source.Current.State == AudioVisualizationState.Unavailable && factory.Disposed == factory.Created, "ambiguous target stops capture and stays zero");
    Check(source.Current.Bands.All(value => value == 0), "unavailable never uses synthetic spectrum");
}
Check(factory.Created == factory.Disposed, "source disposal releases every capture exactly once");

var failedFactory = new FakeFactory { Fail = true };
using (var source = new ProcessAudioVisualizationSource(resolver: new FakeResolver(), captureFactory: failedFactory))
{
    source.SetTarget(playing, true);
    await WaitFor(() => source.Current.State == AudioVisualizationState.Unavailable, "native activation failure is unavailable and zero");
    Check(source.Current.Rms == 0 && source.Current.Bands.All(value => value == 0), "activation failure retains no samples");
}
var throwingFactory = new FakeFactory();
using (var source = new ProcessAudioVisualizationSource(resolver: new FakeResolver(), captureFactory: throwingFactory))
{
    source.FrameChanged += _ => throw new InvalidOperationException("subscriber failure");
    source.SetTarget(playing, true);
    await WaitFor(() => source.Current.State == AudioVisualizationState.Capturing, "subscriber exceptions do not kill audio worker");
}
var disposingFactory = new FakeFactory();
var disposingSource = new ProcessAudioVisualizationSource(resolver: new FakeResolver(), captureFactory: disposingFactory);
disposingSource.FrameChanged += frame => { if (frame.State == AudioVisualizationState.Capturing) disposingSource.Dispose(); };
disposingSource.SetTarget(playing, true);
await WaitFor(() => disposingFactory.Created > 0 && disposingFactory.Created == disposingFactory.Disposed, "dispose inside callback does not deadlock and releases capture");
disposingSource.Dispose();

if (args.Contains("--live-native"))
{
    var nativePath = args.SkipWhile(value => value != "--live-native").Skip(1).First();
    NativeLibrary.Load(Path.GetFullPath(nativePath));
    var realResolution = new NetEaseProcessResolver().Resolve(playing);
    Check(realResolution.Identity is not null, "live NetEase source maps uniquely to verified process");
    using var capture = new NativeProcessAudioCaptureFactory().Create(realResolution.Identity!.Value);
    var nativeRms = new List<float>();
    for (var index = 0; index < 25; index++)
    {
        await Task.Delay(40);
        var packet = capture.Read(signal);
        nativeRms.Add(analyzer.Analyze(signal.AsSpan(0, packet.Frames * 2), packet.SampleRate, bands));
    }
    Console.WriteLine(JsonSerializer.Serialize(new { LivePid = realResolution.Identity.Value.ProcessId, Samples = nativeRms.Count, MaxRms = nativeRms.Max() }));
    Check(nativeRms.All(float.IsFinite), "live native capture returns finite real samples");
}
if (args.Contains("--native-signal"))
{
    var nativePath = args.SkipWhile(value => value != "--native-signal").Skip(1).First();
    NativeLibrary.Load(Path.GetFullPath(nativePath));
    using var thisProcess = Process.GetCurrentProcess();
    var identity = new AudioProcessIdentity((uint)thisProcess.Id, (ulong)thisProcess.StartTime.ToUniversalTime().ToFileTimeUtc());
    using var capture = new NativeProcessAudioCaptureFactory().Create(identity);
    IProcessAudioCapture? otherCapture = null;
    if (args.Contains("--other-process"))
    {
        var otherId = int.Parse(args.SkipWhile(value => value != "--other-process").Skip(1).First());
        using var otherProcess = Process.GetProcessById(otherId);
        otherCapture = new NativeProcessAudioCaptureFactory().Create(new((uint)otherId, (ulong)otherProcess.StartTime.ToUniversalTime().ToFileTimeUtc()));
    }
    using var otherLifetime = otherCapture;
    var otherObserved = new List<float>();
    var exclusionRms = new List<float>();
    for (var index = 0; index < 8; index++)
    {
        await Task.Delay(40);
        var packet = capture.Read(signal);
        exclusionRms.Add(analyzer.Analyze(signal.AsSpan(0, packet.Frames * 2), packet.SampleRate, bands));
        if (otherCapture is not null)
        {
            var otherPacket = otherCapture.Read(signal);
            var otherRms = analyzer.Analyze(signal.AsSpan(0, otherPacket.Frames * 2), otherPacket.SampleRate, bands);
            if (otherRms > .001f && Array.IndexOf(bands, bands.Max()) == 5) otherObserved.Add(otherRms);
        }
    }
    Check(exclusionRms.All(value => value == 0), "native process loopback excludes sound from other processes");
    if (otherCapture is not null) Check(otherObserved.Count >= 5, "excluded sibling independently emits verified 4300 Hz audio");
    var first = Stopwatch.GetTimestamp();
    using var tone = new ToneOutput(1500, 1400);
    double? onsetMs = null;
    var observed = new List<float>();
    var correctBand = true;
    for (var index = 0; index < 30; index++)
    {
        await Task.Delay(40);
        var packet = capture.Read(signal);
        var rms = analyzer.Analyze(signal.AsSpan(0, packet.Frames * 2), packet.SampleRate, bands);
        if (rms > .001f)
        {
            onsetMs ??= Stopwatch.GetElapsedTime(first).TotalMilliseconds;
            observed.Add(rms);
            correctBand &= Array.IndexOf(bands, bands.Max()) == 4;
        }
    }
    Check(observed.Count >= 20, "native generated tone produces sustained real captured samples");
    Check(correctBand, "native generated 1500 Hz tone reaches expected band");
    Check(onsetMs <= 150, "native known-tone onset response is within 150 ms");
    Console.WriteLine(JsonSerializer.Serialize(new { NativeSignalPid = thisProcess.Id, OnsetMs = onsetMs, CapturedFrames = observed.Count,
        MaxRms = observed.Max(), OtherProcessMaxRms = exclusionRms.Max(), OtherProcessObservedRms = otherObserved.DefaultIfEmpty().Max() }));
}
Console.WriteLine(JsonSerializer.Serialize(new { Passed = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));

sealed class FakeResolver : INetEaseProcessResolver
{
    public AudioProcessIdentity? Identity = new(10, 100);
    public AudioProcessResolution Resolve(MediaSnapshot media) => new(Identity, Identity is null ? "ambiguous" : null);
}
sealed class FakeFactory : IProcessAudioCaptureFactory
{
    public int Created, Disposed;
    public bool Silent, Fail;
    public AudioProcessIdentity LastIdentity;
    public IProcessAudioCapture Create(AudioProcessIdentity process)
    {
        if (Fail) throw new COMException("test activation", unchecked((int)0x88890008));
        Interlocked.Increment(ref Created); LastIdentity = process;
        return new Capture(this);
    }
    private sealed class Capture(FakeFactory owner) : IProcessAudioCapture
    {
        private bool _disposed;
        public ProcessAudioPacket Read(float[] samples)
        {
            for (var frame = 0; frame < samples.Length / 2; frame++) samples[frame * 2] = samples[frame * 2 + 1] = owner.Silent ? 0 : .5f * (float)Math.Sin(2 * Math.PI * 1500 * frame / 48000);
            return new(samples.Length / 2, 48000, Stopwatch.GetTimestamp());
        }
        public void Dispose() { if (!_disposed) { _disposed = true; Interlocked.Increment(ref owner.Disposed); } }
    }
}
