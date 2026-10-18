using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using GlassDesk.Core;

internal static class WidgetBoardChecks
{
    public static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();

    public static async Task RunAsync(Action<bool, string> check)
    {
        DataAndEditorChecks(check);
        SnapshotValidationChecks(check);
        await BuiltInChecksAsync(check).ConfigureAwait(false);
        await SchedulerChecksAsync(check).ConfigureAwait(false);
    }

    private static JsonElement Json(string json)
    { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    // Independent implementation of the compact caption contract (sign plus HH:mm).
    private static string ZoneCaptionText(TimeZoneInfo zone, DateTimeOffset instant)
    {
        var offset = zone.GetUtcOffset(instant).Duration();
        return $"UTC{(zone.GetUtcOffset(instant) < TimeSpan.Zero ? "-" : "+")}{offset.Hours:00}:{offset.Minutes:00}";
    }
    private static WidgetInstance Item(string id, string provider = "test.provider", WidgetCardSize size = WidgetCardSize.Small,
        int order = 0, JsonElement? settings = null) => new(id, provider, size, order, settings ?? WidgetBoardData.EmptySettings());
    private static WidgetBoardState Board(params WidgetInstance[] instances) => new(WidgetBoardData.Version, instances);
    private static WidgetSnapshot Snapshot(string text = "ok", DateTimeOffset? next = null) =>
        new(Array.AsReadOnly(new[] { new WidgetElement(WidgetElementKind.Text, new(12, 12, 100, 20), text) }), next);
    private static bool Invalid(Action action)
    { try { action(); return false; } catch (InvalidDataException) { return true; } }

    private static void DataAndEditorChecks(Action<bool, string> check)
    {
        var defaults = WidgetBoardData.CreateDefault();
        check(defaults.Version == 1 && defaults.Instances.Count == 1
            && defaults.Instances[0].ProviderId == BuiltInWidgetIds.Time
            && defaults.Instances[0].Size == WidgetCardSize.Small,
            "widgets default to one offline time card; other widgets are intentionally absent");
        // Editor scenarios still exercise multi-card behaviour with explicit instances.
        var twoCards = Board(Item("time.default", BuiltInWidgetIds.Time, order: 0),
            Item("date.default", BuiltInWidgetIds.Date, order: 1));
        WidgetBoardState clone;
        using (var document = JsonDocument.Parse("{\"custom\":7}"))
            clone = WidgetBoardData.ValidateAndClone(Board(Item("retained", "missing.plugin", settings: document.RootElement)));
        check(clone.Instances[0].Settings.GetProperty("custom").GetInt32() == 7 && clone.Instances[0].ProviderId == "missing.plugin",
            "widget board owns cloned settings and retains unavailable provider IDs");
        check(WidgetBoardData.ValidateAndClone(Board()).Instances.Count == 0, "valid empty widget board remains empty");
        check(WidgetBoardData.ValidateAndClone(Board(Enumerable.Range(0, 32).Select(n => Item("id" + n, order: n)).ToArray())).Instances.Count == 32
            && Invalid(() => WidgetBoardData.ValidateAndClone(Board(Enumerable.Range(0, 33).Select(n => Item("id" + n)).ToArray()))),
            "widget board accepts 32 and rejects 33 instances");
        var limit = Json("\"" + new string('x', 4094) + "\"");
        check(WidgetBoardData.CloneSettings(limit).GetRawText().Length == 4096
            && Invalid(() => WidgetBoardData.CloneSettings(Json("\"" + new string('x', 4095) + "\"")))
            && Invalid(() => WidgetBoardData.CloneSettings(Json("\"" + new string('中', 1365) + "\""))),
            "widget settings enforce UTF-8 byte limits, including the exact 4 KiB boundary");
        check(Invalid(() => WidgetBoardData.ValidateAndClone(Board(Item("same"), Item("same"))))
            && Invalid(() => WidgetBoardData.ValidateAndClone(Board(Item("bad id"))))
            && Invalid(() => WidgetBoardData.ValidateAndClone(Board(Item("id", size: (WidgetCardSize)9))))
            && Invalid(() => WidgetBoardData.ValidateAndClone(Board(Item("id", order: -1))))
            && Invalid(() => WidgetBoardData.CloneSettings(default))
            && Invalid(() => WidgetBoardData.CloneSettings(Json(new string('[', 33) + "0" + new string(']', 33)))),
            "widget board rejects duplicate IDs, invalid size/order, undefined and excessive-depth settings");

        var packed = WidgetBoardLayout.Pack(defaults, 360);
        check(packed.Columns == 2 && packed.Cards.Count == 1
            && packed.Cards[0].Bounds == new FloatRect(105, 104, 150, 148) && packed.ContentBottom == 276,
            "a single widget is centred below the toolbar with the shared 150x148 footprint");
        var mixed = Board(Item("a"), Item("b", size: WidgetCardSize.Medium, order: 1), Item("c", order: 2),
            Item("d", order: 3), Item("e", size: WidgetCardSize.Medium, order: 4));
        var mixedPacking = WidgetBoardLayout.Pack(mixed, 360);
        check(mixedPacking.Cards.Select(c => c.Bounds.Y).SequenceEqual(new[] { 104f, 264f, 424f, 424f, 584f })
            && mixedPacking.Cards[1].Bounds.Width == 312 && mixedPacking.Cards[3].Bounds.X == 186,
            "deterministic packing flushes partial rows before full-width medium cards");
        var narrow = WidgetBoardLayout.Pack(mixed, 300);
        check(narrow.Columns == 1 && narrow.Cards[1].Bounds.Width == 252 && narrow.Cards[1].Size == WidgetCardSize.Medium
            && narrow.Cards.Select(c => c.InstanceId).SequenceEqual(mixed.Instances.Select(i => i.InstanceId))
            && mixed.Instances[1].Size == WidgetCardSize.Medium
            && narrow.Cards.Zip(narrow.Cards.Skip(1), (a, b) => !a.Bounds.Intersects(b.Bounds)).All(x => x),
            "single-column fallback preserves persistent order and size while fitting available width");
        var eligible = WidgetBoardLayout.EligibleInstances(mixedPacking, new(24, 104, 312, 148));
        check(eligible.SetEquals(new[] { "a", "b" }), "widget prefetch eligibility includes only the viewport and one adjacent row");

        var editor = new WidgetBoardEditor(twoCards); editor.Begin();
        editor.Resize("time.default", WidgetCardSize.Medium);
        editor.Configure("time.default", Json("{\"showSeconds\":true}"));
        editor.Add(BuiltInWidgetIds.Time, WidgetCardSize.Small, WidgetBoardData.EmptySettings(), "second.time");
        editor.Reorder("second.time", 0); editor.Remove("date.default");
        check(editor.State.Instances.Count == 2 && editor.State.Instances[0].InstanceId == "second.time"
            && editor.State.Instances[1].Settings.GetProperty("showSeconds").GetBoolean(),
            "widget edit draft supports add, remove, multi-instance, order, resize and independent settings");
        var cancelled = editor.Cancel();
        check(!editor.IsEditing && cancelled.Instances.Select(i => i.InstanceId).SequenceEqual(twoCards.Instances.Select(i => i.InstanceId))
            && cancelled.Instances[0].Size == WidgetCardSize.Small && !cancelled.Instances[0].Settings.TryGetProperty("showSeconds", out _),
            "cancel edit restores the complete pre-edit board");
        editor.Begin(); editor.Resize("date.default", WidgetCardSize.Medium);
        editor.PointerDown("time.default", new(30, 140));
        check(!editor.PointerMove(new(36, 140)) && editor.PointerMove(new(36.1f, 140), 1),
            "card capture begins only after movement exceeds 6 DIP");
        var outside = editor.PointerUp(new(500, 200), new(0, 0, 360, 700));
        check(outside == WidgetDragRelease.CancelledOutside && !editor.IsDragging
            && editor.State.Instances[0].InstanceId == "time.default" && editor.State.Instances[1].Size == WidgetCardSize.Medium,
            "outside drop cancels only the current drag and preserves earlier valid edits");
        editor.PointerDown("time.default", new(30, 140));
        check(editor.PointerUp(new(200, 300), new(0, 0, 360, 700), 1) == WidgetDragRelease.Committed,
            "release displacement captures and reorders when the last pointer move was absent");
        var committed = editor.Commit();
        check(!editor.IsEditing && committed.Instances[0].InstanceId == "date.default" && committed.Instances[0].Size == WidgetCardSize.Medium,
            "outside collapse can commit the last valid layout without a dialog");
        editor.Begin(); foreach (var instance in editor.State.Instances) editor.Remove(instance.InstanceId);
        check(editor.Commit().Instances.Count == 0, "removing every card commits a valid empty state");
    }

    private static void SnapshotValidationChecks(Action<bool, string> check)
    {
        var commands = new HashSet<string>(["registered.command"], StringComparer.Ordinal);
        var symbols = new HashSet<string>(["clock"], StringComparer.Ordinal);
        bool Reject(WidgetElement element) => Invalid(() => WidgetBoardScheduler.ValidateSnapshot(new([element]), WidgetCardSize.Small, commands, symbols));
        var text = new WidgetElement(WidgetElementKind.Text, new(0, 0, 150, 148), "value", CommandId: "registered.command");
        check(WidgetBoardScheduler.ValidateSnapshot(new([text]), WidgetCardSize.Small, commands).Elements.Count == 1
            && Reject(text with { CommandId = "not.registered" }), "widget snapshots can reference only registered commands");
        check(Reject(text with { Bounds = new(float.NaN, 0, 1, 1) })
            && Reject(text with { Bounds = new(0, float.PositiveInfinity, 1, 1) })
            && Reject(text with { Bounds = new(-1, 0, 1, 1) }) && Reject(text with { Bounds = new(0, 0, 151, 148) })
            && Reject(text with { Bounds = new(0, 0, 1, 149) }) && Reject(text with { TextRole = (TextRole)999 })
            && Reject(text with { Kind = (WidgetElementKind)999 }) && Reject(text with { NumericStyle = (WidgetNumericStyle)999 }),
            "widget snapshots reject non-finite/outside coordinates and invalid semantic enums");
        check(Reject(text with { Text = new string('x', 2049) })
            && Invalid(() => WidgetBoardScheduler.ValidateSnapshot(new(Enumerable.Repeat(text, 65).ToArray()), WidgetCardSize.Small, commands))
            && Invalid(() => WidgetBoardScheduler.ValidateSnapshot(new(Enumerable.Repeat(text with { Text = new string('x', 2048) }, 9).ToArray()), WidgetCardSize.Small, commands))
            && WidgetBoardScheduler.ValidateSnapshot(new(Enumerable.Repeat(text, 64).ToArray()), WidgetCardSize.Small, commands).Elements.Count == 64,
            "widget snapshots bound element count, per-string length and aggregate text");
        var symbol = new WidgetElement(WidgetElementKind.Symbol, new(12, 12, 20, 20), SymbolKey: "clock");
        var progress = new WidgetElement(WidgetElementKind.ProgressValue, new(12, 100, 100, 4)) { Value = .5 };
        check(!Reject(symbol) && Reject(symbol with { SymbolKey = "unknown" }) && !Reject(progress)
            && Reject(progress with { Value = double.NaN }) && Reject(progress with { Value = double.PositiveInfinity })
            && Reject(progress with { Value = -.1 }) && Reject(progress with { Value = 1.1 }) && Reject(progress with { Value = null }),
            "host symbols are registered and progress values are finite bounded fractions");
        WidgetElement[] array = [text];
        var validated = WidgetBoardScheduler.ValidateSnapshot(new(array), WidgetCardSize.Small, commands);
        array[0] = text with { Text = "mutated later" };
        check(validated.Elements[0].Text == "value", "provider arrays cannot mutate an accepted snapshot later");
    }

    private static async Task BuiltInChecksAsync(Action<bool, string> check)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var utcZone = BuiltInWidgetProviders.InstalledTimeZones().FirstOrDefault(t => t.Id == "UTC");
            check(utcZone is not null, "Windows installed time-zone catalog exposes UTC without downloaded data");
            string zoneId = utcZone?.Id ?? TimeZoneInfo.Local.Id;
            var zone = BuiltInWidgetProviders.ResolveTimeZone(zoneId);
            var instant = new DateTimeOffset(2026, 10, 18, 17, 34, 56, 250, TimeSpan.Zero);
            string offsetCaption = ZoneCaptionText(zone, instant);
            var time = BuiltInWidgetProviders.Catalog.Single(p => p.Id == BuiltInWidgetIds.Time).Provider;
            var date = BuiltInWidgetProviders.Catalog.Single(p => p.Id == BuiltInWidgetIds.Date).Provider;
            var plain = await time.GetSnapshotAsync(new("a", WidgetCardSize.Small, instant,
                BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(TimeZoneId: zoneId))), default);
            check(plain.Elements.Count == 2 && plain.Elements[0].Text == "17:34"
                && plain.Elements[0].TextRole == TextRole.WidgetClock && plain.Elements[0].NumericStyle == WidgetNumericStyle.Tabular
                && plain.Elements[0].Bounds == new FloatRect(12, 34, 126, 44)
                && plain.Elements[1].Text == offsetCaption && plain.Elements[1].TextRole == TextRole.WidgetLabel
                && plain.Elements[1].Bounds == new FloatRect(12, 112, 126, 16)
                && plain.NextRefreshUtc == new DateTimeOffset(2026, 10, 18, 17, 35, 0, TimeSpan.Zero),
                "small clock keeps two text layers: the value and its UTC offset caption");
            using (var clockScheduler = new WidgetBoardScheduler())
            {
                var justBefore = new DateTimeOffset(2026, 10, 18, 17, 34, 59, 999, TimeSpan.Zero);
                clockScheduler.Reconcile(Board(Item("clock.boundary", BuiltInWidgetIds.Time,
                    settings: BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(TimeZoneId: zoneId)))), null, justBefore);
                clockScheduler.SetVisible(true, justBefore); await clockScheduler.RefreshDueAsync(justBefore).ConfigureAwait(false);
                check(clockScheduler.NextRefreshUtc == new DateTimeOffset(2026, 10, 18, 17, 35, 0, TimeSpan.Zero),
                    "scheduler preserves an imminent real minute boundary instead of adding arbitrary latency");
            }
            var seconds = await time.GetSnapshotAsync(new("b", WidgetCardSize.Medium, instant,
                BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(false, true, zoneId))), default);
            check(seconds.Elements.Count == 2 && seconds.Elements[0].Text == "05:34:56 PM"
                && seconds.Elements[0].Bounds == new FloatRect(12, 34, 140, 44)
                && seconds.Elements[1].Text == offsetCaption && seconds.Elements[1].TextRole == TextRole.Body
                && seconds.Elements[1].Bounds == new FloatRect(160, 46, 140, 20)
                && seconds.NextRefreshUtc == new DateTimeOffset(2026, 10, 18, 17, 34, 57, TimeSpan.Zero),
                "12-hour/seconds choices fold the AM/PM designator into the medium value and schedule only the next second");
            var withWeekday = await date.GetSnapshotAsync(new("c", WidgetCardSize.Small, instant,
                BuiltInWidgetProviders.ToJson(new DateWidgetSettings(TimeZoneId: zoneId))), default);
            var fullWeekday = await date.GetSnapshotAsync(new("d", WidgetCardSize.Small, instant,
                BuiltInWidgetProviders.ToJson(new DateWidgetSettings(true, "long", "long", zoneId))), default);
            var withoutWeekday = await date.GetSnapshotAsync(new("e", WidgetCardSize.Small, instant,
                BuiltInWidgetProviders.ToJson(new DateWidgetSettings(false, "short", "short", zoneId))), default);
            check(withWeekday.Elements.Count == 2 && withWeekday.Elements[0].Text == "18"
                && withWeekday.Elements[0].TextRole == TextRole.WidgetDateValue
                && withWeekday.Elements[0].Bounds == new FloatRect(12, 28, 126, 56)
                && withWeekday.Elements[1].Text == "10月18日 · Sun" && withWeekday.Elements[1].TextRole == TextRole.Body
                && withWeekday.Elements[1].Bounds == new FloatRect(12, 96, 126, 20)
                && fullWeekday.Elements[1].Text == "10月18日 · Sunday"
                && withoutWeekday.Elements.Count == 2 && withoutWeekday.Elements[1].Text == "10/18"
                && withWeekday.NextRefreshUtc == new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
                "date card keeps two text layers and still respects weekday/date format settings");
            // Outside the current year the caption gains a yyyy年 prefix; the shown date and the
            // viewer's current year are derived independently so the check works on any system zone.
            var yearEnd = new DateTimeOffset(2025, 12, 31, 16, 30, 0, TimeSpan.Zero);
            var yearEndSnapshot = await date.GetSnapshotAsync(new("f", WidgetCardSize.Small, yearEnd,
                BuiltInWidgetProviders.ToJson(new DateWidgetSettings(TimeZoneId: zoneId))), default);
            var yearEndLocal = TimeZoneInfo.ConvertTime(yearEnd, zone);
            string yearEndDate = yearEndLocal.ToString("M月d日", CultureInfo.InvariantCulture);
            if (yearEndLocal.Year != TimeZoneInfo.ConvertTime(yearEnd, TimeZoneInfo.Local).Year)
                yearEndDate = yearEndLocal.Year.ToString(CultureInfo.InvariantCulture) + "年" + yearEndDate;
            check(yearEndSnapshot.Elements[1].Text == yearEndDate + " · " + yearEndLocal.ToString("ddd", CultureInfo.InvariantCulture)
                && yearEndSnapshot.NextRefreshUtc == new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "date caption prefixes the year only when the shown date is outside the current year");
            var negativeZone = BuiltInWidgetProviders.InstalledTimeZones()
                .FirstOrDefault(t => t.GetUtcOffset(instant) < TimeSpan.Zero && !string.Equals(t.Id, zoneId, StringComparison.OrdinalIgnoreCase));
            if (negativeZone is not null)
            {
                var negative = await time.GetSnapshotAsync(new("g", WidgetCardSize.Small, instant,
                    BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(TimeZoneId: negativeZone.Id))), default);
                check(negative.Elements[1].Text is { } negativeCaption && negativeCaption == ZoneCaptionText(negativeZone, instant)
                    && negativeCaption.StartsWith("UTC-", StringComparison.Ordinal),
                    "negative UTC offsets keep their explicit minus sign in the compact caption");
            }
            check(BuiltInWidgetProviders.ReadTimeSettings(Json("{\"timeZoneId\":\"not.installed\",\"showSeconds\":7}")).TimeZoneId is null
                && !BuiltInWidgetProviders.ReadTimeSettings(Json("{\"showSeconds\":7}")).ShowSeconds
                && BuiltInWidgetProviders.ResolveTimeZone("not.installed").Id == TimeZoneInfo.Local.Id,
                "invalid clock options fall back locally and only installed Windows zone IDs are accepted");
            foreach (var snapshot in new[] { plain, seconds, withWeekday, fullWeekday, withoutWeekday, yearEndSnapshot })
                WidgetBoardScheduler.ValidateSnapshot(snapshot, snapshot == seconds ? WidgetCardSize.Medium : WidgetCardSize.Small, new HashSet<string>());
            check(true, "all built-in snapshots satisfy the same bounded element contract as plugin snapshots");

            var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 8);
            var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1);
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
            var dst = TimeZoneInfo.CreateCustomTimeZone("headless.dst", TimeSpan.FromHours(-5), "test", "standard", "daylight", [rule]);
            var spring = new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero);
            var fall = new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero);
            check(BuiltInWidgetProviders.NextMidnight(spring, dst) - spring == TimeSpan.FromHours(23)
                && BuiltInWidgetProviders.NextMidnight(fall, dst) - fall == TimeSpan.FromHours(25),
                "date refresh uses zone midnight rather than assuming every date lasts 24 hours");
            var midnightStart = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 8);
            var midnightEnd = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1);
            var midnightRule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), midnightStart, midnightEnd);
            var midnightZone = TimeZoneInfo.CreateCustomTimeZone("headless.midnight", TimeSpan.FromHours(-5), "test", "standard", "daylight", [midnightRule]);
            var skipped = BuiltInWidgetProviders.NextMidnight(new(2026, 3, 7, 5, 0, 0, TimeSpan.Zero), midnightZone);
            var repeated = BuiltInWidgetProviders.NextMidnight(new(2026, 10, 31, 4, 0, 0, TimeSpan.Zero), midnightZone);
            check(skipped == new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero)
                && repeated == new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero),
                "invalid midnight advances to a valid instant and ambiguous midnight selects its first occurrence");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private sealed class FakeProvider(Func<WidgetRequest, CancellationToken, ValueTask<WidgetSnapshot>> function) : IWidgetProvider
    {
        private int _calls, _active, _maximum;
        public int Calls => Volatile.Read(ref _calls);
        public int MaximumActive => Volatile.Read(ref _maximum);
        public async ValueTask<WidgetSnapshot> GetSnapshotAsync(WidgetRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls); int active = Interlocked.Increment(ref _active);
            int maximum;
            do { maximum = Volatile.Read(ref _maximum); if (maximum >= active) break; }
            while (Interlocked.CompareExchange(ref _maximum, active, maximum) != maximum);
            try { return await function(request, cancellationToken).ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
    private static PluginWidget Contribution(FakeProvider provider) => new("test.provider", "Test", new([WidgetCardSize.Small, WidgetCardSize.Medium]), provider);
    private static WidgetBoardScheduler Scheduler(FakeProvider provider, TimeSpan? timeout = null) => new([Contribution(provider)],
        thirdPartyVerified: true, requestTimeout: timeout);
    private static TaskCompletionSource<WidgetSnapshot> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task UntilAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Widget check did not reach its expected state.");
            await Task.Delay(5).ConfigureAwait(false);
        }
    }

    private static async Task SchedulerChecksAsync(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var quick = new FakeProvider((_, _) => ValueTask.FromResult(Snapshot()));
        using (var gated = new WidgetBoardScheduler([Contribution(quick)]))
        {
            gated.Reconcile(Board(Item("custom"), Item("time", BuiltInWidgetIds.Time, order: 1)), null, now);
            gated.SetVisible(true, now); await gated.RefreshDueAsync(now).ConfigureAwait(false);
            check(!gated.ThirdPartyCatalogEnabled && quick.Calls == 0 && gated.Results[0].State == WidgetProviderState.Unavailable
                && gated.Results[1].State == WidgetProviderState.Ready,
                "unverified third-party catalog is closed while offline built-in cards remain usable");
        }

        using (var scheduler = Scheduler(quick))
        {
            scheduler.Reconcile(Board(Item("a"), Item("b", order: 1)), new HashSet<string>(["a"]), now);
            await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
            check(quick.Calls == 0 && scheduler.NextRefreshUtc is null, "hidden widget page performs no provider work or periodic scheduling");
            scheduler.SetVisible(true, now); await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
            check(quick.Calls == 1 && scheduler.Results[0].State == WidgetProviderState.Ready && scheduler.Results[1].Snapshot is null,
                "scheduler requests only explicit visible/prefetch instances");
            scheduler.SetVisible(false, now); await scheduler.RefreshDueAsync(now.AddDays(1)).ConfigureAwait(false);
            scheduler.SetVisible(true, now.AddDays(1)); await scheduler.RefreshDueAsync(now.AddDays(1)).ConfigureAwait(false);
            check(quick.Calls == 2 && scheduler.ActiveRequestCount == 0, "reopening immediately samples current time without hidden timers");
        }

        using (var release = new ManualResetEventSlim())
        {
            SynchronizationContext? seen = null; var marker = new SynchronizationContext();
            var background = new FakeProvider((_, _) =>
            {
                seen = SynchronizationContext.Current;
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Provider was invoked inline.");
                return ValueTask.FromResult(Snapshot("background"));
            });
            using var scheduler = Scheduler(background);
            scheduler.Reconcile(Board(Item("background")), null, now); scheduler.SetVisible(true, now);
            var prior = SynchronizationContext.Current; Task request;
            try { SynchronizationContext.SetSynchronizationContext(marker); request = scheduler.RefreshDueAsync(now); }
            finally { SynchronizationContext.SetSynchronizationContext(prior); release.Set(); }
            await request.ConfigureAwait(false);
            check(!ReferenceEquals(seen, marker) && scheduler.Results[0].State == WidgetProviderState.Ready,
                "synchronous provider code executes off the caller's UI synchronization context");
        }

        int phase = 0;
        var fault = new FakeProvider((_, _) => phase++ == 0 ? ValueTask.FromResult(Snapshot("last good")) : throw new InvalidOperationException("fault"));
        using (var scheduler = Scheduler(fault))
        {
            scheduler.Reconcile(Board(Item("fault"), Item("time", BuiltInWidgetIds.Time, order: 1)), null, now); scheduler.SetVisible(true, now);
            await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
            scheduler.Invalidate(now.AddMinutes(1)); await scheduler.RefreshDueAsync(now.AddMinutes(1)).ConfigureAwait(false);
            check(scheduler.Results[0].State == WidgetProviderState.Faulted && scheduler.Results[0].Snapshot?.Elements[0].Text == "last good"
                && scheduler.Results[0].NextRefreshUtc is null && scheduler.Results[1].State == WidgetProviderState.Ready,
                "a provider exception retains its last success and cannot break another built-in card");
        }
        var selfCancelled = new FakeProvider((_, _) => throw new OperationCanceledException("not a host cancellation"));
        using (var scheduler = Scheduler(selfCancelled))
        {
            scheduler.Reconcile(Board(Item("self.cancel")), null, now); scheduler.SetVisible(true, now);
            await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
            check(scheduler.Results[0].State == WidgetProviderState.Faulted && scheduler.NextRefreshUtc is null,
                "a provider's unsolicited cancellation is a fault rather than an immediate retry loop");
        }

        var timeoutPending = Pending(); int timeoutPhase = 0;
        var timeoutProvider = new FakeProvider((_, _) => ++timeoutPhase == 1 ? new(timeoutPending.Task) : ValueTask.FromResult(Snapshot("fresh")));
        using (var scheduler = Scheduler(timeoutProvider, TimeSpan.FromMilliseconds(500)))
        {
            scheduler.Reconcile(Board(Item("timeout")), null, now); scheduler.SetVisible(true, now);
            await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
            check(scheduler.Results[0].State == WidgetProviderState.TimedOut && scheduler.ActiveRequestCount == 1 && scheduler.NextRefreshUtc is null,
                "timeout stops refresh but retains an uncooperative provider's in-flight slot");
            scheduler.Invalidate(now.AddSeconds(3)); await scheduler.RefreshDueAsync(now.AddSeconds(3)).ConfigureAwait(false);
            check(timeoutProvider.Calls == 1 && scheduler.ActiveRequestCount == 1, "explicit retry cannot overlap the old timed-out invocation");
            timeoutPending.SetResult(Snapshot("late old")); await UntilAsync(() => scheduler.ActiveRequestCount == 0).ConfigureAwait(false);
            check(scheduler.Results[0].Snapshot is null, "a timed-out late result cannot publish into a newer generation");
            await scheduler.RefreshDueAsync(now.AddSeconds(4)).ConfigureAwait(false);
            check(timeoutProvider.Calls == 2 && timeoutProvider.MaximumActive == 1 && scheduler.Results[0].Snapshot?.Elements[0].Text == "fresh",
                "retry resumes only after the previous provider actually finishes");
        }

        var cancelPending = Pending(); int cancelPhase = 0;
        var cancelProvider = new FakeProvider((_, _) => ++cancelPhase == 1 ? new(cancelPending.Task) : ValueTask.FromResult(Snapshot("after cancel")));
        using (var scheduler = Scheduler(cancelProvider))
        using (var cancellation = new CancellationTokenSource())
        {
            scheduler.Reconcile(Board(Item("cancel")), null, now); scheduler.SetVisible(true, now);
            var request = scheduler.RefreshDueAsync(now, cancellation.Token);
            await UntilAsync(() => cancelProvider.Calls == 1).ConfigureAwait(false); cancellation.Cancel();
            await request.ConfigureAwait(false);
            check(scheduler.Results[0].State == WidgetProviderState.Waiting && scheduler.ActiveRequestCount == 1,
                "caller cancellation restores a waiting state without pretending uncooperative work stopped");
            cancelPending.SetResult(Snapshot("cancelled late")); await UntilAsync(() => scheduler.ActiveRequestCount == 0).ConfigureAwait(false);
            await scheduler.RefreshDueAsync(now.AddSeconds(1)).ConfigureAwait(false);
            check(cancelProvider.Calls == 2 && cancelProvider.MaximumActive == 1 && scheduler.Results[0].Snapshot?.Elements[0].Text == "after cancel",
                "cancelled late results are ignored and a new batch can recover without manual invalidation");
        }

        await GenerationChecksAsync(check, now).ConfigureAwait(false);
        await CapacityChecksAsync(check, now).ConfigureAwait(false);
    }

    private static async Task GenerationChecksAsync(Action<bool, string> check, DateTimeOffset now)
    {
        foreach (string change in new[] { "resize/configure", "hide/reopen", "disable/enable", "remove/re-add" })
        {
            var pending = Pending(); int phase = 0; WidgetRequest? newest = null;
            var provider = new FakeProvider((request, _) =>
            {
                newest = request;
                return ++phase == 1 ? new(pending.Task) : ValueTask.FromResult(Snapshot("current"));
            });
            using var scheduler = Scheduler(provider);
            scheduler.Reconcile(Board(Item("stable")), null, now); scheduler.SetVisible(true, now);
            var first = scheduler.RefreshDueAsync(now); await UntilAsync(() => provider.Calls == 1).ConfigureAwait(false);
            var revised = Board(Item("stable", size: WidgetCardSize.Medium, settings: Json("{\"revision\":2}")));
            switch (change)
            {
                case "resize/configure": scheduler.Reconcile(revised, null, now.AddSeconds(1)); break;
                case "hide/reopen":
                    scheduler.SetVisible(false, now); scheduler.SetVisible(true, now.AddSeconds(1)); break;
                case "disable/enable":
                    scheduler.SetProviderEnabled("test.provider", false, now);
                    check(scheduler.Results[0].State == WidgetProviderState.Unavailable, "disabled provider becomes unavailable without deleting saved instance");
                    scheduler.SetProviderEnabled("test.provider", true, now.AddSeconds(1)); break;
                case "remove/re-add":
                    scheduler.Reconcile(Board(), null, now);
                    check(scheduler.Results.Count == 0 && scheduler.ActiveRequestCount == 1, "removed card is absent while its old provider slot remains tracked");
                    scheduler.Reconcile(revised, null, now.AddSeconds(1)); break;
            }
            await first.ConfigureAwait(false);
            await scheduler.RefreshDueAsync(now.AddSeconds(1)).ConfigureAwait(false);
            check(provider.Calls == 1, change + " waits for the old invocation instead of overlapping it");
            pending.SetResult(Snapshot("old generation")); await UntilAsync(() => scheduler.ActiveRequestCount == 0).ConfigureAwait(false);
            check(scheduler.Results[0].Snapshot is null, change + " rejects the late old result");
            await scheduler.RefreshDueAsync(now.AddSeconds(2)).ConfigureAwait(false);
            check(provider.Calls == 2 && provider.MaximumActive == 1 && scheduler.Results[0].Snapshot?.Elements[0].Text == "current"
                && (change is not ("resize/configure" or "remove/re-add") || newest is { Size: WidgetCardSize.Medium }
                    && newest.Settings.GetProperty("revision").GetInt32() == 2),
                change + " publishes only current size/settings and generation");
        }

        var cycleProvider = new FakeProvider((r, _) => ValueTask.FromResult(Snapshot(r.UtcNow.ToString("O"), r.UtcNow.AddMinutes(1))));
        using (var scheduler = Scheduler(cycleProvider))
        {
            scheduler.Reconcile(Board(Item("cycle")), null, now);
            for (int cycle = 0; cycle < 20; cycle++)
            {
                scheduler.SetVisible(true, now.AddMinutes(cycle)); await scheduler.RefreshDueAsync(now.AddMinutes(cycle)).ConfigureAwait(false);
                scheduler.SetVisible(false, now.AddMinutes(cycle)); await scheduler.RefreshDueAsync(now.AddDays(1)).ConfigureAwait(false);
            }
            check(cycleProvider.Calls == 20 && cycleProvider.MaximumActive == 1 && scheduler.ActiveRequestCount == 0 && scheduler.NextRefreshUtc is null,
                "repeated hide/restore releases requests and returns to an unscheduled hidden state");
        }

        var disposePending = Pending(); var disposeProvider = new FakeProvider((_, _) => new(disposePending.Task));
        var disposed = Scheduler(disposeProvider); int notifications = 0; disposed.Updated += () => Interlocked.Increment(ref notifications);
        disposed.Reconcile(Board(Item("dispose")), null, now); disposed.SetVisible(true, now);
        var work = disposed.RefreshDueAsync(now); await UntilAsync(() => disposeProvider.Calls == 1).ConfigureAwait(false);
        disposed.Dispose(); await work.ConfigureAwait(false); int beforeLate = notifications;
        disposePending.SetResult(Snapshot("late after dispose")); await UntilAsync(() => disposed.ActiveRequestCount == 0).ConfigureAwait(false);
        check(notifications == beforeLate && disposed.NextRefreshUtc is null && disposed.Results[0].Snapshot is null,
            "dispose cancels observation, ignores late output and removes future update notifications");
    }

    private static async Task CapacityChecksAsync(Action<bool, string> check, DateTimeOffset now)
    {
        var pending = Pending(); var provider = new FakeProvider((_, _) => new(pending.Task));
        using var scheduler = Scheduler(provider, TimeSpan.FromMilliseconds(500));
        var instances = Enumerable.Range(0, 10).Select(n => Item("slow" + n, order: n + 2))
            .Prepend(Item("date", BuiltInWidgetIds.Date, order: 1)).Prepend(Item("time", BuiltInWidgetIds.Time)).ToArray();
        scheduler.Reconcile(Board(instances), null, now); scheduler.SetVisible(true, now);
        await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
        await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
        check(provider.Calls == WidgetBoardScheduler.MaximumThirdPartyRequests && scheduler.ActiveRequestCount == 3
            && provider.MaximumActive == 3 && scheduler.Results.Where(r => r.InstanceId is "date" or "time").All(r => r.State == WidgetProviderState.Ready),
            "bounded scheduler reserves capacity for offline cards while third-party tasks remain uncooperative");
        scheduler.Invalidate(now.AddMinutes(1), "time"); await scheduler.RefreshDueAsync(now.AddMinutes(1)).ConfigureAwait(false);
        check(scheduler.Results.Single(r => r.InstanceId == "time").State == WidgetProviderState.Ready && provider.Calls == 3,
            "hung third-party calls cannot prevent later built-in time refreshes");
        scheduler.Reconcile(Board(), null, now);
        scheduler.Reconcile(Board(Enumerable.Range(0, 32).Select(n => Item("replacement" + n, order: n)).ToArray()), null, now);
        await scheduler.RefreshDueAsync(now).ConfigureAwait(false);
        check(provider.Calls == 3 && scheduler.ActiveRequestCount == 3,
            "removing and replacing boards cannot accumulate unbounded uncooperative background calls");
        pending.SetResult(Snapshot("retired")); await UntilAsync(() => scheduler.ActiveRequestCount == 0).ConfigureAwait(false);
        check(scheduler.Results.All(r => r.Snapshot is null), "retired provider calls cannot publish into replacement instances");
    }
}
