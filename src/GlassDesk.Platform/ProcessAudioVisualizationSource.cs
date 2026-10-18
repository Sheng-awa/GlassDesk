using System.Diagnostics;
using System.Runtime.InteropServices;
using GlassDesk.Core;

namespace GlassDesk.Platform;

public readonly record struct ProcessAudioPacket(int Frames, int SampleRate, long Timestamp);
public interface IProcessAudioCapture : IDisposable
{
    ProcessAudioPacket Read(float[] stereoSamples);
}
public interface IProcessAudioCaptureFactory
{
    IProcessAudioCapture Create(AudioProcessIdentity process);
}

/// <summary>25 Hz analysis, strictly bound to the selected NetEase process tree.
/// Pass the host UI dispatcher for FrameChanged events. Without one, callbacks
/// run on the analysis worker. SetTarget is nonblocking and active means that a
/// waveform is actually visible and enabled; playback is checked independently.</summary>
public sealed class ProcessAudioVisualizationSource : IAudioVisualizationSource
{
    [ThreadStatic] private static ProcessAudioVisualizationSource? _raisingSource;
    private readonly object _gate = new();
    private readonly IUiDispatcher? _dispatcher;
    private readonly INetEaseProcessResolver _resolver;
    private readonly IProcessAudioCaptureFactory _factory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Task _worker;
    private MediaSnapshot _media = MediaSnapshot.Disconnected;
    private bool _active, _disposed;
    private long _generation;
    private AudioVisualizationFrame _current = AudioVisualizationFrame.Inactive;
    public AudioVisualizationFrame Current => Volatile.Read(ref _current);
    public event Action<AudioVisualizationFrame>? FrameChanged;

    public ProcessAudioVisualizationSource(IUiDispatcher? dispatcher = null,
        INetEaseProcessResolver? resolver = null, IProcessAudioCaptureFactory? captureFactory = null)
    {
        _dispatcher = dispatcher; _resolver = resolver ?? new NetEaseProcessResolver();
        _factory = captureFactory ?? new NativeProcessAudioCaptureFactory();
        _worker = Task.Run(RunAsync);
    }

    public void SetTarget(MediaSnapshot media, bool active)
    {
        long generation;
        bool shouldCapture;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            shouldCapture = active && media.Connected && media.State == PlaybackState.Playing && !string.IsNullOrEmpty(media.SessionToken);
            if (_active != shouldCapture || _media.SessionToken != media.SessionToken) _generation++;
            _active = shouldCapture; _media = media; generation = _generation;
        }
        if (!shouldCapture) PublishZero(AudioVisualizationState.Inactive, media.SessionToken, generation);
        Wake();
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task RunAsync()
    {
        var samples = new float[AudioSpectrumAnalyzer.WindowFrames * 2];
        var analyzer = new AudioSpectrumAnalyzer();
        IProcessAudioCapture? capture = null;
        AudioProcessIdentity? bound = null;
        long workerGeneration = -1;
        long identityCheck = 0, retryAt = 0, analysisAt = 0;
        var cancellation = _lifetime.Token;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                MediaSnapshot media;
                bool active;
                long generation;
                lock (_gate) { media = _media; active = _active; generation = _generation; }
                if (workerGeneration != generation || !active)
                {
                    capture?.Dispose(); capture = null; bound = null;
                    workerGeneration = generation; identityCheck = retryAt = analysisAt = 0;
                    Array.Clear(samples);
                    if (active) PublishZero(AudioVisualizationState.Starting, media.SessionToken, generation);
                }
                if (!active) { await _wake.WaitAsync(cancellation).ConfigureAwait(false); continue; }
                var now = Stopwatch.GetTimestamp();
                try
                {
                    if (now >= identityCheck)
                    {
                        identityCheck = now + Stopwatch.Frequency / 2;
                        var resolved = _resolver.Resolve(media);
                        if (resolved.Identity is null)
                        {
                            capture?.Dispose(); capture = null; bound = null;
                            PublishZero(AudioVisualizationState.Unavailable, media.SessionToken, generation, resolved.Reason);
                        }
                        else if (capture is not null && bound != resolved.Identity)
                        {
                            capture.Dispose(); capture = null; bound = null; retryAt = 0;
                        }
                        if (capture is null && resolved.Identity is { } process && now >= retryAt)
                        {
                            PublishZero(AudioVisualizationState.Starting, media.SessionToken, generation);
                            capture = _factory.Create(process); bound = process;
                            // A pause/hide/session switch during activation must never leak
                            // a frame or leave the newly activated stream running.
                            lock (_gate) active = !_disposed && _active && _generation == generation;
                            if (!active) { capture.Dispose(); capture = null; bound = null; continue; }
                        }
                    }
                    if (capture is not null && now >= analysisAt)
                    {
                        analysisAt = now + Stopwatch.Frequency / 25;
                        var packet = capture.Read(samples);
                        if (packet.Frames < 0 || packet.Frames > AudioSpectrumAnalyzer.WindowFrames || packet.SampleRate <= 0)
                            throw new InvalidOperationException("Invalid process audio packet.");
                        var bands = new float[AudioSpectrumAnalyzer.BandCount];
                        var rms = analyzer.Analyze(samples.AsSpan(0, packet.Frames * 2), packet.SampleRate, bands);
                        var timestamp = packet.Timestamp > 0 ? packet.Timestamp : Stopwatch.GetTimestamp();
                        Publish(new(rms == 0 ? AudioVisualizationState.Silent : AudioVisualizationState.Capturing,
                            media.SessionToken, timestamp, rms, bands), generation);
                    }
                }
                catch (Exception error) when (error is COMException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    capture?.Dispose(); capture = null; bound = null;
                    retryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    PublishZero(AudioVisualizationState.Unavailable, media.SessionToken, generation,
                        $"进程音频采集不可用（0x{error.HResult:X8}）。");
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    capture?.Dispose(); capture = null; bound = null;
                    retryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    PublishZero(AudioVisualizationState.Faulted, media.SessionToken, generation,
                        $"音频分析已停止（{error.GetType().Name}）。");
                }
                await _wake.WaitAsync(40, cancellation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { capture?.Dispose(); Array.Clear(samples); }
    }

    private void PublishZero(AudioVisualizationState state, string token, long generation, string? reason = null)
        => Publish(new(state, token, Stopwatch.GetTimestamp(), 0, new float[AudioSpectrumAnalyzer.BandCount], reason), generation);

    private void Publish(AudioVisualizationFrame frame, long generation)
    {
        lock (_gate)
        {
            if (_disposed || _generation != generation) return;
            var previous = _current;
            // A silent/inactive source does not continuously repaint the host.
            if (previous.State == frame.State && previous.SessionToken == frame.SessionToken && previous.Reason == frame.Reason
                && previous.Rms == 0 && frame.Rms == 0) return;
            Volatile.Write(ref _current, frame);
        }
        void Raise()
        {
            lock (_gate) if (_disposed || _generation != generation || !ReferenceEquals(_current, frame)) return;
            // A host subscriber cannot break resource cleanup or the capture loop.
            var previousSource = _raisingSource;
            try { _raisingSource = this; FrameChanged?.Invoke(frame); }
            catch (Exception) { }
            finally { _raisingSource = previousSource; }
        }
        if (_dispatcher is null) Raise(); else _dispatcher.TryEnqueue(Raise);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _generation++; _active = false;
            Volatile.Write(ref _current, AudioVisualizationFrame.Inactive);
            FrameChanged = null;
        }
        _lifetime.Cancel(); Wake();
        // Worker owns every native handle; activation and sample waits are bounded.
        if (ReferenceEquals(_raisingSource, this))
        {
            // Disposing inside a worker callback must not wait for its own task.
            _ = _worker.ContinueWith(_ => ReleaseSignals(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
        _worker.GetAwaiter().GetResult();
        ReleaseSignals();
    }
    private void ReleaseSignals() { _wake.Dispose(); _lifetime.Dispose(); }
}

public sealed class NativeProcessAudioCaptureFactory : IProcessAudioCaptureFactory
{
    public IProcessAudioCapture Create(AudioProcessIdentity process)
    {
        var result = NativeMethods.GD_AudioCreate(process.ProcessId, process.CreationFileTime, out var handle);
        if (result < 0 || handle == 0) throw new COMException("Process loopback activation failed.", result < 0 ? result : unchecked((int)0x80004005));
        return new NativeCapture(handle);
    }
    private sealed class NativeCapture(nint handle) : IProcessAudioCapture
    {
        private nint _handle = handle;
        public ProcessAudioPacket Read(float[] stereoSamples)
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            var result = NativeMethods.GD_AudioRead(_handle, stereoSamples, (uint)(stereoSamples.Length / 2), out var frames, out var rate, out var timestamp);
            if (result < 0) throw new COMException("Process loopback read failed.", result);
            return new((int)frames, (int)rate, timestamp);
        }
        public void Dispose() { var capture = Interlocked.Exchange(ref _handle, 0); if (capture != 0) NativeMethods.GD_AudioDestroy(capture); }
    }
    private static class NativeMethods
    {
        [DllImport("GlassDesk.Native.dll", CallingConvention = CallingConvention.StdCall)] internal static extern int GD_AudioCreate(uint processId, ulong processCreationTime, out nint capture);
        [DllImport("GlassDesk.Native.dll", CallingConvention = CallingConvention.StdCall)] internal static extern int GD_AudioRead(nint capture, [Out] float[] stereoSamples, uint capacityFrames, out uint frames, out uint sampleRate, out long qpcTimestamp);
        [DllImport("GlassDesk.Native.dll", CallingConvention = CallingConvention.StdCall)] internal static extern void GD_AudioDestroy(nint capture);
    }
}
