namespace GlassDesk.Core;

public enum WidgetProviderState { Unavailable, Waiting, Updating, Ready, Faulted, TimedOut }
public sealed record WidgetInstanceResult(string InstanceId, long Generation, WidgetProviderState State,
    WidgetSnapshot? Snapshot, string? Error, DateTimeOffset? NextRefreshUtc);

/// <summary>
/// Explicit, timer-free scheduling. The host supplies eligible viewport/prefetch IDs and calls RefreshDueAsync
/// at NextRefreshUtc, after restore, or after clock/time-zone changes. Updated is raised on a background thread.
/// </summary>
public sealed class WidgetBoardScheduler : IDisposable
{
    public const int MaximumConcurrentRequests = 4, MaximumThirdPartyRequests = 3;
    private sealed class Entry(WidgetInstance instance)
    {
        public WidgetInstance Instance = instance;
        public long Generation;
        public bool Removed, Eligible;
        public WidgetSnapshot? Snapshot;
        public WidgetProviderState State = WidgetProviderState.Waiting;
        public string? Error;
        public DateTimeOffset? Due;
        public Task<WidgetSnapshot>? Running;
        public CancellationTokenSource? Cancellation;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginWidget> _providers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _disabledProviders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _commands, _symbols;
    private readonly TimeSpan _requestTimeout;
    private bool _visible, _disposed;
    public bool ThirdPartyCatalogEnabled { get; }
    public event Action? Updated;

    /// <param name="thirdPartyVerified">Keep false until the complete P6 test and native evidence gate passes.</param>
    /// <param name="requestTimeout">Defaults to 2 seconds. Tests may shorten, never lengthen, the bound.</param>
    public WidgetBoardScheduler(IEnumerable<PluginWidget>? contributions = null, IEnumerable<string>? registeredCommands = null,
        IEnumerable<string>? registeredSymbols = null, bool thirdPartyVerified = false, TimeSpan? requestTimeout = null)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _commands = new(registeredCommands ?? [], StringComparer.Ordinal);
        _symbols = new(registeredSymbols ?? [], StringComparer.Ordinal);
        ThirdPartyCatalogEnabled = thirdPartyVerified;
        foreach (var item in BuiltInWidgetProviders.Catalog) _providers.Add(item.Id, item);
        if (thirdPartyVerified && contributions is not null)
            foreach (var item in contributions)
            {
                if (!WidgetBoardData.IsIdentifier(item.Id) || item.Provider is null || item.Descriptor is null
                    || item.Descriptor.Sizes is null || item.Descriptor.Sizes.Count is < 1 or > 2
                    || item.Descriptor.Sizes.Any(s => !Enum.IsDefined(s)))
                    throw new ArgumentException("Invalid widget contribution.", nameof(contributions));
                _providers.Add(item.Id, item with { Descriptor = item.Descriptor with { Sizes = Array.AsReadOnly(item.Descriptor.Sizes.ToArray()) } });
            }
    }

    public int ActiveRequestCount { get { lock (_gate) return _entries.Values.Count(e => e.Running is not null); } }
    public DateTimeOffset? NextRefreshUtc
    {
        get
        {
            lock (_gate)
                return !_visible || _disposed ? null : _entries.Values.Where(e => !e.Removed && e.Eligible
                    && e.Running is null && e.Due.HasValue && Available(e.Instance) && HasRequestCapacity(e))
                    .Select(e => e.Due).OrderBy(t => t).FirstOrDefault();
        }
    }

    public IReadOnlyList<WidgetInstanceResult> Results
    {
        get
        {
            lock (_gate) return Array.AsReadOnly(_entries.Values.Where(e => !e.Removed).OrderBy(e => e.Instance.Order)
                .Select(e => new WidgetInstanceResult(e.Instance.InstanceId, e.Generation, e.State, e.Snapshot, e.Error, e.Due)).ToArray());
        }
    }

    public void Reconcile(WidgetBoardState state, IReadOnlySet<string>? eligibleInstanceIds, DateTimeOffset utcNow)
    {
        var board = WidgetBoardData.ValidateAndClone(state);
        lock (_gate)
        {
            ThrowIfDisposed();
            var present = board.Instances.Select(i => i.InstanceId).ToHashSet(StringComparer.Ordinal);
            foreach (var pair in _entries.ToArray())
                if (!present.Contains(pair.Key) && !pair.Value.Removed)
                {
                    var entry = pair.Value; entry.Removed = true; entry.Generation++; Cancel(entry.Cancellation);
                    if (entry.Running is null) _entries.Remove(pair.Key);
                }
            foreach (var instance in board.Instances)
            {
                bool eligible = eligibleInstanceIds is null || eligibleInstanceIds.Contains(instance.InstanceId);
                if (!_entries.TryGetValue(instance.InstanceId, out var entry))
                { entry = new(instance); _entries.Add(instance.InstanceId, entry); Reset(entry, utcNow); }
                else if (entry.Removed || !Equivalent(entry.Instance, instance))
                {
                    entry.Instance = instance; entry.Removed = false; entry.Snapshot = null;
                    entry.Generation++; Cancel(entry.Cancellation); Reset(entry, utcNow);
                }
                else entry.Instance = instance; // Order-only changes preserve a valid snapshot and request.
                if (entry.Eligible != eligible)
                {
                    entry.Generation++; Cancel(entry.Cancellation);
                    if (eligible) Reset(entry, utcNow);
                    else if (entry.State == WidgetProviderState.Updating) entry.State = WidgetProviderState.Waiting;
                }
                entry.Eligible = eligible;
                if (!Available(instance)) { entry.State = WidgetProviderState.Unavailable; entry.Due = null; }
            }
        }
    }

    public void SetVisible(bool visible, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            ThrowIfDisposed(); if (_visible == visible) return;
            _visible = visible;
            foreach (var entry in _entries.Values)
            {
                entry.Generation++; Cancel(entry.Cancellation);
                if (visible && !entry.Removed) Reset(entry, utcNow);
                else if (entry.State == WidgetProviderState.Updating) entry.State = WidgetProviderState.Waiting;
            }
        }
    }

    public void SetProviderEnabled(string providerId, bool enabled, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (enabled ? !_disabledProviders.Remove(providerId) : !_disabledProviders.Add(providerId)) return;
            foreach (var entry in _entries.Values.Where(e => e.Instance.ProviderId == providerId))
            { entry.Generation++; Cancel(entry.Cancellation); Reset(entry, utcNow); }
        }
    }

    /// <summary>Call on system time/time-zone changes and resume. Passing an ID retries one failed instance explicitly.</summary>
    public void Invalidate(DateTimeOffset utcNow, string? instanceId = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var entry in _entries.Values.Where(e => !e.Removed && (instanceId is null || e.Instance.InstanceId == instanceId)))
            { entry.Generation++; Cancel(entry.Cancellation); Reset(entry, utcNow); }
        }
    }

    public Task RefreshDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var requests = new List<(Entry Entry, long Generation, WidgetCardSize Size, Task<WidgetSnapshot> Task, CancellationTokenSource Cancellation)>();
        lock (_gate)
        {
            ThrowIfDisposed(); cancellationToken.ThrowIfCancellationRequested();
            if (!_visible) return Task.CompletedTask;
            foreach (var entry in _entries.Values.OrderBy(e => IsBuiltIn(e.Instance.ProviderId) ? 0 : 1).ThenBy(e => e.Instance.Order))
            {
                if (entry.Removed || !entry.Eligible || entry.Running is not null || !entry.Due.HasValue
                    || entry.Due > utcNow || !Available(entry.Instance) || !HasRequestCapacity(entry)) continue;
                var provider = _providers[entry.Instance.ProviderId].Provider;
                // CancelAsync below keeps arbitrary provider cancellation callbacks away from the UI caller.
                var cancellation = new CancellationTokenSource();
                var request = new WidgetRequest(entry.Instance.InstanceId, entry.Instance.Size, utcNow, entry.Instance.Settings.Clone());
                // Both synchronous provider work and asynchronous continuations start away from the UI thread.
                var task = Task.Run(async () => await provider.GetSnapshotAsync(request, cancellation.Token).ConfigureAwait(false), CancellationToken.None);
                entry.Running = task; entry.Cancellation = cancellation; entry.State = WidgetProviderState.Updating; entry.Due = null;
                requests.Add((entry, entry.Generation, entry.Instance.Size, task, cancellation));
            }
        }
        return Task.WhenAll(requests.Select(r => ObserveAsync(r.Entry, r.Generation, r.Size, r.Task, r.Cancellation, utcNow, cancellationToken)));
    }

    private async Task ObserveAsync(Entry entry, long generation, WidgetCardSize requestedSize, Task<WidgetSnapshot> task,
        CancellationTokenSource cancellation, DateTimeOffset utcNow, CancellationToken callerCancellation)
    {
        using var registration = callerCancellation.UnsafeRegister(static state => Cancel((CancellationTokenSource)state!), cancellation);
        WidgetSnapshot? snapshot = null; WidgetProviderState state; string? error = null; DateTimeOffset? due = null;
        try
        {
            var raw = await task.WaitAsync(_requestTimeout, cancellation.Token).ConfigureAwait(false);
            snapshot = ValidateSnapshot(raw, requestedSize, _commands, _symbols);
            due = snapshot.NextRefreshUtc;
            if (due.HasValue)
            {
                // A provider cannot force a zero-delay busy loop or an unbounded delay value.
                var minimum = AddBounded(utcNow, TimeSpan.FromMilliseconds(100)); var maximum = AddBounded(utcNow, TimeSpan.FromHours(36));
                due = due <= utcNow ? minimum : due > maximum ? maximum : due;
                snapshot = snapshot with { NextRefreshUtc = due };
            }
            state = WidgetProviderState.Ready;
        }
        catch (TimeoutException) { state = WidgetProviderState.TimedOut; error = "组件响应超时"; Cancel(cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || callerCancellation.IsCancellationRequested)
        { state = WidgetProviderState.Waiting; due = utcNow; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { state = WidgetProviderState.Faulted; error = exception is InvalidDataException ? "组件数据无效" : "组件暂不可用"; }
        bool changed = false;
        lock (_gate)
        {
            if (!_disposed && !entry.Removed && entry.Generation == generation)
            {
                // A cancelled batch may finish normally at the same time. It must never publish that result.
                if (callerCancellation.IsCancellationRequested || state == WidgetProviderState.Waiting)
                { snapshot = null; state = WidgetProviderState.Waiting; error = null; due = utcNow; }
                if (snapshot is not null) entry.Snapshot = snapshot;
                entry.State = state; entry.Error = error; entry.Due = due; changed = true;
            }
        }
        // Timeout/cancellation ends observation, not execution. Retain the slot until uncooperative code really ends.
        if (task.IsCompleted) Release(entry, task, cancellation);
        else _ = ReleaseWhenCompleteAsync(entry, task, cancellation);
        if (changed) Notify();
    }

    private async Task ReleaseWhenCompleteAsync(Entry entry, Task<WidgetSnapshot> task, CancellationTokenSource cancellation)
    {
        try { await task.ConfigureAwait(false); } catch (Exception exception) when (exception is not OutOfMemoryException) { }
        Release(entry, task, cancellation);
        Notify(); // A changed generation may now issue the request that had to wait for this slot.
    }
    private void Release(Entry entry, Task<WidgetSnapshot> task, CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(entry.Running, task))
            {
                entry.Running = null; entry.Cancellation = null;
                if (entry.Removed && _entries.TryGetValue(entry.Instance.InstanceId, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(entry.Instance.InstanceId);
            }
        }
        cancellation.Dispose();
    }
    private void Reset(Entry entry, DateTimeOffset utcNow)
    {
        entry.Error = null;
        entry.State = Available(entry.Instance) ? WidgetProviderState.Waiting : WidgetProviderState.Unavailable;
        entry.Due = Available(entry.Instance) ? utcNow : null;
    }
    private bool Available(WidgetInstance instance) => _providers.TryGetValue(instance.ProviderId, out var provider)
        && !_disabledProviders.Contains(instance.ProviderId) && provider.Descriptor.Sizes.Contains(instance.Size);
    private static bool IsBuiltIn(string id) => id is BuiltInWidgetIds.Time or BuiltInWidgetIds.Date;
    private bool HasRequestCapacity(Entry candidate)
    {
        if (_entries.Values.Count(e => e.Running is not null) >= MaximumConcurrentRequests) return false;
        // Uncooperative third-party calls cannot consume the last slot needed by offline time/date cards.
        return IsBuiltIn(candidate.Instance.ProviderId) || _entries.Values.Count(e => e.Running is not null && !IsBuiltIn(e.Instance.ProviderId)) < MaximumThirdPartyRequests;
    }
    private static DateTimeOffset AddBounded(DateTimeOffset value, TimeSpan interval) =>
        value.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - interval.Ticks ? DateTimeOffset.MaxValue : value.ToUniversalTime().Add(interval);
    private static bool Equivalent(WidgetInstance a, WidgetInstance b) => a.ProviderId == b.ProviderId && a.Size == b.Size
        && a.Settings.GetRawText() == b.Settings.GetRawText();
    private static void Cancel(CancellationTokenSource? cancellation)
    { if (cancellation is not null) _ = CancelQuietlyAsync(cancellation); }
    private static async Task CancelQuietlyAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
    private void Notify()
    {
        lock (_gate) if (_disposed) return;
        if (Updated is { } handlers)
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true; _visible = false; Updated = null;
            foreach (var entry in _entries.Values) { entry.Generation++; Cancel(entry.Cancellation); }
        }
    }

    public static WidgetSnapshot ValidateSnapshot(WidgetSnapshot snapshot, WidgetCardSize size,
        IReadOnlySet<string> registeredCommands, IReadOnlySet<string>? registeredSymbols = null)
    {
        if (snapshot is null || snapshot.Elements is null || snapshot.Elements.Count > 64 || !Enum.IsDefined(size))
            throw new InvalidDataException("Invalid widget snapshot.");
        float width = size == WidgetCardSize.Small ? WidgetBoardLayout.SmallWidth : WidgetBoardLayout.MediumWidth;
        int totalText = 0; var copy = new List<WidgetElement>(snapshot.Elements.Count);
        foreach (var element in snapshot.Elements)
        {
            if (element is null) throw new InvalidDataException("Null widget element.");
            var b = element.Bounds;
            if (!Enum.IsDefined(element.Kind) || !Enum.IsDefined(element.NumericStyle) || !TypographyCatalog.All.ContainsKey(element.TextRole)
                || !float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height)
                || b.X < 0 || b.Y < 0 || b.Width < 0 || b.Height < 0 || b.Right > width || b.Bottom > WidgetBoardLayout.CardHeight
                || element.Text is { Length: > 2048 } || (totalText += element.Text?.Length ?? 0) > 16 * 1024
                || element.CommandId is not null && (!WidgetBoardData.IsIdentifier(element.CommandId) || !registeredCommands.Contains(element.CommandId))
                || element.SymbolKey is { Length: > 128 }
                || element.Kind == WidgetElementKind.Text && (element.Text is null || b.Width == 0 || b.Height == 0)
                || element.Kind == WidgetElementKind.Symbol && (element.SymbolKey is null || registeredSymbols is null || !registeredSymbols.Contains(element.SymbolKey))
                || element.Value is { } value && (!double.IsFinite(value) || value < 0 || value > 1)
                || element.Kind == WidgetElementKind.ProgressValue && !element.Value.HasValue)
                throw new InvalidDataException("Unbounded or unregistered widget element.");
            copy.Add(element with { });
        }
        return snapshot with { Elements = Array.AsReadOnly(copy.ToArray()) };
    }
}
