using System.Diagnostics;
using System.Security.Cryptography;
using GlassDesk.Core;
using Windows.Graphics.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace GlassDesk.Platform;

/// <summary>
/// Event-driven GSMTC adapter. IUiDispatcher must install the UI SynchronizationContext;
/// every broker/session operation and continuation belongs to the creating UI/COM thread.
/// </summary>
public sealed class NetEaseMediaAdapter : IMediaAdapter
{
    public const string SourceAppId = "cloudmusic.exe";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(8);
    private readonly IUiDispatcher _dispatcher;
    private readonly Diagnostics _diagnostics;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly LinkedList<(string Key, byte[] Bytes)> _covers = new();
    private CancellationTokenSource _contentCancellation = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? _mediaChangedHandler;
    private TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? _playbackChangedHandler;
    private TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? _timelineChangedHandler;
    private int _ownerThread;
    private long _generation;
    private long _contentVersion;
    private string _sessionToken = "";
    private bool _refreshRunning;
    private bool _refreshPending;
    private bool _metadataPending;
    private volatile bool _disposed;
    private MediaSnapshot _current = MediaSnapshot.Disconnected;
    public MediaSnapshot Current => Volatile.Read(ref _current);
    public event Action<MediaSnapshot>? SnapshotChanged;

    public NetEaseMediaAdapter(IUiDispatcher dispatcher, Diagnostics? diagnostics = null)
    {
        _dispatcher = dispatcher;
        _diagnostics = diagnostics ?? new Diagnostics();
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => _dispatcher.InvokeAsync(async () =>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_manager is not null) return true;
        _ownerThread = Environment.CurrentManagedThreadId;
        if (SynchronizationContext.Current is null)
            throw new InvalidOperationException("GSMTC requires an owning UI SynchronizationContext.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(linked.Token).WaitAsync(OperationTimeout, linked.Token);
            EnsureOwner();
            if (_disposed) return false;
            _manager = manager;
            _manager.SessionsChanged += OnSessionsChanged;
            Rebind();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed) { throw; }
        catch (Exception error)
        {
            if (error is TimeoutException) CancelTimedOutOperation(linked);
            _diagnostics.RecordError("media.start", error);
            Publish(MediaSnapshot.Disconnected with { Notice = "暂时无法读取 Windows 媒体会话，可在设置中重试。" });
            return false;
        }
    });

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Media continuation left its owning UI/COM thread.");
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => Enqueue(Rebind);
    private void Enqueue(Action action)
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { EnsureOwner(); action(); }
            catch (Exception error) { _diagnostics.RecordError("media.event", error); }
        });
    }

    private void Rebind()
    {
        EnsureOwner();
        if (_disposed || _manager is null) return;
        try
        {
            var matches = _manager.GetSessions().Where(session =>
                string.Equals(session.SourceAppUserModelId, SourceAppId, StringComparison.OrdinalIgnoreCase) &&
                session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed).ToArray();
            // Preserve a valid NetEase binding even when a browser becomes the current global media source.
            var selected = matches.FirstOrDefault(session => Equals(session, _session)) ??
                matches.FirstOrDefault(session => session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) ??
                matches.FirstOrDefault();
            if (selected is not null && Equals(selected, _session)) { RequestRefresh(metadata: true); return; }
            DetachSession();
            _contentCancellation.Cancel();
            _generation++;
            _contentVersion++;
            _session = selected;
            _sessionToken = selected is null ? "" : Guid.NewGuid().ToString("N");
            if (selected is null) { Publish(MediaSnapshot.Disconnected); return; }
            var binding = _generation;
            _mediaChangedHandler = (_, _) => Enqueue(() => { if (binding == _generation) RequestRefresh(metadata: true); });
            _playbackChangedHandler = (_, _) => Enqueue(() =>
            {
                if (binding != _generation || _session is null) return;
                if (_session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed) Rebind();
                else RequestRefresh(metadata: false);
            });
            _timelineChangedHandler = (_, _) => Enqueue(() => { if (binding == _generation) RequestRefresh(metadata: false); });
            selected.MediaPropertiesChanged += _mediaChangedHandler;
            selected.PlaybackInfoChanged += _playbackChangedHandler;
            selected.TimelinePropertiesChanged += _timelineChangedHandler;
            // Never show the preceding session's content during a new session's metadata request.
            Publish(new(_sessionToken, _contentVersion, true, "", "", "", null, PlaybackState.None, null, null, new(), "正在读取网易云音乐…"));
            RequestRefresh(metadata: true);
        }
        catch (Exception error)
        {
            _diagnostics.RecordError("media.bind", error);
            DetachSession();
            _generation++;
            _session = null;
            Publish(MediaSnapshot.Disconnected with { Notice = "网易云媒体会话暂时不可用。" });
        }
    }

    private void DetachSession()
    {
        if (_session is null) return;
        if (_mediaChangedHandler is not null) _session.MediaPropertiesChanged -= _mediaChangedHandler;
        if (_playbackChangedHandler is not null) _session.PlaybackInfoChanged -= _playbackChangedHandler;
        if (_timelineChangedHandler is not null) _session.TimelinePropertiesChanged -= _timelineChangedHandler;
        _mediaChangedHandler = null; _playbackChangedHandler = null; _timelineChangedHandler = null;
    }

    private void RequestRefresh(bool metadata)
    {
        EnsureOwner();
        if (_disposed || _session is null) return;
        _refreshPending = true;
        if (metadata)
        {
            _contentCancellation.Cancel(); _contentCancellation.Dispose(); _contentCancellation = new();
            _contentVersion++; _metadataPending = true;
        }
        if (!_refreshRunning) _ = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        _refreshRunning = true;
        try
        {
            while (_refreshPending && !_disposed && _session is not null)
            {
                _refreshPending = false;
                var metadata = _metadataPending;
                _metadataPending = false;
                var session = _session;
                var generation = _generation;
                var version = _contentVersion;
                var snapshot = Current;
                using var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _contentCancellation.Token);
                var refreshToken = refreshCancellation.Token;
                try
                {
                    if (metadata)
                    {
                        var properties = await session.TryGetMediaPropertiesAsync().AsTask(refreshToken).WaitAsync(OperationTimeout, refreshToken);
                        EnsureOwner();
                        if (!IsCurrent(session, generation, version)) continue;
                        var title = properties.Title ?? "";
                        var artist = properties.Artist ?? "";
                        var album = properties.AlbumTitle ?? "";
                        // Clear previous art immediately; a late art result cannot be published for another track.
                        snapshot = snapshot with { ContentVersion = version, Title = title, Artist = artist, Album = album, Cover = null, Notice = null };
                        snapshot = ReadPlayback(session, snapshot);
                        Publish(snapshot);
                        if (properties.Thumbnail is not null)
                        {
                            var cover = await ReadCoverAsync(properties.Thumbnail, refreshToken);
                            EnsureOwner();
                            if (!IsCurrent(session, generation, version)) continue;
                            snapshot = Current with { Cover = cover };
                        }
                    }
                    if (!IsCurrent(session, generation, version)) continue;
                    Publish(ReadPlayback(session, snapshot) with { Notice = null });
                }
                catch (OperationCanceledException) when (refreshToken.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (error is TimeoutException) CancelTimedOutOperation(refreshCancellation);
                    _diagnostics.RecordError("media.refresh", error);
                    if (IsCurrent(session, generation, version)) Publish(Current with { Notice = "媒体信息更新失败，将在下次会话事件后重试。" });
                }
            }
        }
        finally { _refreshRunning = false; }
    }

    private bool IsCurrent(GlobalSystemMediaTransportControlsSession session, long generation, long version)
        => !_disposed && ReferenceEquals(session, _session) && generation == _generation && version == _contentVersion;

    private static MediaSnapshot ReadPlayback(GlobalSystemMediaTransportControlsSession session, MediaSnapshot snapshot)
    {
        var playback = session.GetPlaybackInfo();
        var controls = playback.Controls;
        var state = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackState.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState.Changing,
            _ => PlaybackState.None
        };
        var sourceTimeline = session.GetTimelineProperties();
        MediaTimeline? timeline = null;
        if (sourceTimeline.EndTime > sourceTimeline.StartTime && sourceTimeline.Position >= sourceTimeline.StartTime && sourceTimeline.Position <= sourceTimeline.EndTime)
        {
            var minimum = sourceTimeline.MinSeekTime > sourceTimeline.StartTime ? sourceTimeline.MinSeekTime : sourceTimeline.StartTime;
            var maximum = sourceTimeline.MaxSeekTime < sourceTimeline.EndTime ? sourceTimeline.MaxSeekTime : sourceTimeline.EndTime;
            var reportedPosition = sourceTimeline.Position;
            // Position belongs to LastUpdatedTime. A playback/metadata event can arrive later than
            // the last timeline event; do not re-anchor an old position as though it arrived now.
            var age = DateTimeOffset.UtcNow - sourceTimeline.LastUpdatedTime;
            if (state == PlaybackState.Playing && age >= TimeSpan.Zero && age <= TimeSpan.FromDays(1))
            {
                var rateForEstimate = playback.PlaybackRate is { } value && double.IsFinite(value) && value >= 0 ? value : 1;
                var advancedTicks = Math.Min((double)sourceTimeline.EndTime.Ticks,
                    reportedPosition.Ticks + age.Ticks * rateForEstimate);
                reportedPosition = TimeSpan.FromTicks((long)Math.Clamp(advancedTicks, sourceTimeline.StartTime.Ticks, sourceTimeline.EndTime.Ticks));
            }
            timeline = new(sourceTimeline.StartTime, sourceTimeline.EndTime, reportedPosition, minimum, maximum, Stopwatch.GetTimestamp());
        }
        var canToggle = controls.IsPlayPauseToggleEnabled || (state == PlaybackState.Playing ? controls.IsPauseEnabled : controls.IsPlayEnabled);
        var canSeek = controls.IsPlaybackPositionEnabled && timeline is { Valid: true } && timeline.MaxSeek > timeline.MinSeek;
        var rate = playback.PlaybackRate;
        return snapshot with
        {
            State = state, Rate = rate is { } r && double.IsFinite(r) && r >= 0 ? rate : null, Timeline = timeline,
            Capabilities = new(controls.IsPreviousEnabled, canToggle, controls.IsNextEnabled, canSeek)
        };
    }

    private async Task<byte[]?> ReadCoverAsync(IRandomAccessStreamReference thumbnail, CancellationToken cancellationToken)
    {
        using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken).WaitAsync(OperationTimeout, cancellationToken);
        EnsureOwner();
        if (stream.Size is 0 or > 16 * 1024 * 1024) return null;
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var length = checked((uint)stream.Size);
        var loaded = await reader.LoadAsync(length).AsTask(cancellationToken).WaitAsync(OperationTimeout, cancellationToken);
        EnsureOwner();
        if (loaded != length) return null;
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        var cached = _covers.First;
        while (cached is not null)
        {
            if (cached.Value.Key == key)
            {
                _covers.Remove(cached); _covers.AddFirst(cached); return cached.Value.Bytes;
            }
            cached = cached.Next;
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).WaitAsync(OperationTimeout, cancellationToken);
        EnsureOwner();
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 64 * 1024 * 1024) return null;
        var longest = Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        var factor = Math.Min(1, 768d / longest);
        var width = Math.Max(1, (uint)Math.Round(decoder.OrientedPixelWidth * factor));
        var height = Math.Max(1, (uint)Math.Round(decoder.OrientedPixelHeight * factor));
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken).WaitAsync(OperationTimeout, cancellationToken);
        EnsureOwner();
        using var destination = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination).AsTask(cancellationToken).WaitAsync(OperationTimeout, cancellationToken);
        EnsureOwner();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels.DetachPixelData());
        await encoder.FlushAsync().AsTask(cancellationToken).WaitAsync(OperationTimeout, cancellationToken);
        EnsureOwner();
        using var outputReader = new DataReader(destination.GetInputStreamAt(0));
        var outputLength = checked((uint)destination.Size);
        if (outputLength > 4 * 1024 * 1024) return null;
        if (await outputReader.LoadAsync(outputLength).AsTask(cancellationToken) != outputLength) return null;
        EnsureOwner();
        var bounded = new byte[outputLength];
        outputReader.ReadBytes(bounded);
        _covers.AddFirst((key, bounded));
        while (_covers.Count > 3) _covers.RemoveLast();
        return bounded;
    }

    public Task<CommandResult> ExecuteAsync(MediaCommand command, string sessionToken, TimeSpan? position = null, CancellationToken cancellationToken = default)
        => _dispatcher.InvokeAsync<CommandResult>(async () =>
    {
        if (_disposed) return new(false, false, "组件已经退出。");
        if (_ownerThread == 0) return new(false, false, "媒体连接尚未启动。");
        EnsureOwner();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (!await _commandGate.WaitAsync(TimeSpan.FromSeconds(5), linked.Token)) return new(false, false, "上一项媒体操作仍在处理，请稍后重试。");
        }
        catch (OperationCanceledException) { return new(false, false, "操作已取消。"); }
        var accepted = false;
        try
        {
            linked.CancelAfter(TimeSpan.FromSeconds(12));
            EnsureOwner();
            var session = _session;
            var generation = _generation;
            if (session is null || sessionToken != _sessionToken) return new(false, false, "音乐会话已改变，请重试。");
            var before = ReadPlayback(session, Current);
            if (command is MediaCommand.Previous or MediaCommand.Next)
            {
                var properties = await session.TryGetMediaPropertiesAsync().AsTask(linked.Token).WaitAsync(OperationTimeout, linked.Token);
                EnsureOwner();
                if (_disposed || generation != _generation || !ReferenceEquals(session, _session)) return new(false, false, "音乐会话已改变，请重试。");
                before = before with { Title = properties.Title ?? "", Artist = properties.Artist ?? "", Album = properties.AlbumTitle ?? "" };
            }
            var capabilities = before.Capabilities;
            if (!(command switch { MediaCommand.Previous => capabilities.Previous, MediaCommand.TogglePlayPause => capabilities.Toggle,
                MediaCommand.Next => capabilities.Next, MediaCommand.Seek => capabilities.Seek && position.HasValue, _ => false }))
                return new(false, false, "网易云当前未提供此操作。");
            var requested = position;
            if (command == MediaCommand.Seek && before.Timeline is { } timeline)
                requested = TimeSpan.FromTicks(Math.Clamp(position!.Value.Ticks, timeline.MinSeek.Ticks, timeline.MaxSeek.Ticks));
            var controls = session.GetPlaybackInfo().Controls;
            var expectedState = before.State == PlaybackState.Playing ? PlaybackState.Paused : PlaybackState.Playing;
            var operation = command switch
            {
                MediaCommand.Previous => session.TrySkipPreviousAsync(),
                MediaCommand.Next => session.TrySkipNextAsync(),
                MediaCommand.Seek => session.TryChangePlaybackPositionAsync(requested!.Value.Ticks),
                MediaCommand.TogglePlayPause when before.State == PlaybackState.Playing && controls.IsPauseEnabled => session.TryPauseAsync(),
                MediaCommand.TogglePlayPause when before.State != PlaybackState.Playing && controls.IsPlayEnabled => session.TryPlayAsync(),
                _ => session.TryTogglePlayPauseAsync()
            };
            accepted = await operation.AsTask(linked.Token).WaitAsync(OperationTimeout, linked.Token);
            EnsureOwner();
            if (!accepted) return new(false, false, "网易云未接受操作。");
            // This bounded readback runs only after a user command; there is no background media polling loop.
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(250, linked.Token);
                EnsureOwner();
                if (_disposed || generation != _generation || !ReferenceEquals(session, _session)) return new(true, false, "操作后音乐会话已改变。");
                var after = ReadPlayback(session, Current);
                var verified = command == MediaCommand.TogglePlayPause ? after.State == expectedState :
                    command == MediaCommand.Seek && after.Timeline is { } actual && requested is { } desired && Math.Abs((actual.Position - desired).TotalSeconds) <= 3;
                if (command is MediaCommand.Previous or MediaCommand.Next)
                {
                    var properties = await session.TryGetMediaPropertiesAsync().AsTask(linked.Token).WaitAsync(OperationTimeout, linked.Token);
                    EnsureOwner();
                    if (_disposed || generation != _generation || !ReferenceEquals(session, _session)) return new(true, false, "操作后音乐会话已改变。");
                    verified = properties.Title != before.Title || properties.Artist != before.Artist || properties.AlbumTitle != before.Album;
                }
                RequestRefresh(metadata: verified && (command is MediaCommand.Previous or MediaCommand.Next));
                if (verified) return new(true, true);
            }
            RequestRefresh(metadata: command is MediaCommand.Previous or MediaCommand.Next);
            return new(true, false, "操作已发送，但尚未观察到相应的播放状态变化。");
        }
        catch (OperationCanceledException) { return new(accepted, false, cancellationToken.IsCancellationRequested || _disposed ? "操作已取消。" : "网易云响应超时。"); }
        catch (TimeoutException error) { CancelTimedOutOperation(linked); _diagnostics.RecordError("media.command", error); return new(accepted, false, "网易云响应超时。"); }
        catch (Exception error) { _diagnostics.RecordError("media.command", error); return new(accepted, false, "媒体操作失败，请重试。"); }
        finally { _commandGate.Release(); }
    });

    /// <summary>Call after session resume; refreshes the broker binding and media timeline on its owner.</summary>
    public void Refresh() => Enqueue(Rebind);

    private void CancelTimedOutOperation(CancellationTokenSource requestCancellation)
    {
        EnsureOwner();
        // WaitAsync only times out the managed wait. Cancel the token supplied to AsTask
        // while still on the COM owner so the underlying WinRT request is also cancelled.
        try { requestCancellation.Cancel(); }
        catch (Exception error) { _diagnostics.RecordError("media.timeout-cancel", error); }
    }

    private static bool SameSnapshot(MediaSnapshot previous, MediaSnapshot next)
    {
        if (ReferenceEquals(previous.Cover, next.Cover)) return previous == next;
        if (previous.Cover is null || next.Cover is null) return false;
        // Record equality includes content version, state, notice, capabilities and every
        // timeline value (including its monotonic timestamp). Only artwork needs byte equality.
        return (previous with { Cover = next.Cover }) == next && previous.Cover.AsSpan().SequenceEqual(next.Cover);
    }

    private void Publish(MediaSnapshot snapshot)
    {
        EnsureOwner();
        if (SameSnapshot(Current, snapshot)) return;
        Volatile.Write(ref _current, snapshot);
        _diagnostics.RecordCapability(snapshot.Connected, snapshot.Capabilities);
        if (SnapshotChanged is { } changed)
        {
            foreach (Action<MediaSnapshot> observer in changed.GetInvocationList())
            {
                try { observer(snapshot); }
                catch (Exception error) { _diagnostics.RecordError("media.snapshot-callback", error); }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _contentCancellation.Cancel();
        void Cleanup()
        {
            DetachSession();
            if (_manager is not null) _manager.SessionsChanged -= OnSessionsChanged;
            _generation++; _session = null; _manager = null; _covers.Clear(); SnapshotChanged = null;
            Volatile.Write(ref _current, MediaSnapshot.Disconnected);
        }
        if (_ownerThread == 0 || Environment.CurrentManagedThreadId == _ownerThread) Cleanup();
        else if (!_dispatcher.TryEnqueue(Cleanup)) _diagnostics.RecordEvent("media.dispose", "owner-queue-unavailable");
    }
}
