using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.Platform;

internal static class WidgetStoreChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        // Match PlatformChecks' existing workspace-owned fixtures. Windows sandbox temp paths can be
        // brokered/virtualized differently from the preview profile where atomic renames actually run.
        string testParent = Path.GetFullPath(Path.Combine("artifacts", "platform-checks", "widget-store"));
        string root = Path.Combine(testParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new WidgetBoardStore(root);
            byte[] preferences = Encoding.UTF8.GetBytes("{\"Version\":4,\"ClockShowTime\":false,\"sentinel\":\"untouched\"}");
            string preferencePath = Path.Combine(root, "settings.json");
            await File.WriteAllBytesAsync(preferencePath, preferences).ConfigureAwait(false);
            var defaults = await store.LoadAsync().ConfigureAwait(false);
            bool defaultsPersisted = File.Exists(store.SettingsPath);
            string? defaultNotice = store.TakeLoadNotice();
            check(defaults.Instances.Count == 2 && defaults.Instances.All(i => i.Size == WidgetCardSize.Small)
                && defaultsPersisted && Path.GetFileName(store.SettingsPath) == "widgets.json",
                $"missing widgets.json creates and persists the balanced offline time/date board [cards={defaults.Instances.Count}, saved={defaultsPersisted}, notice={defaultNotice ?? "none"}, path={store.SettingsPath}]");
            check((await File.ReadAllBytesAsync(preferencePath).ConfigureAwait(false)).SequenceEqual(preferences),
                "widget storage does not modify the independent primary settings file");

            await store.SaveAsync(new(1, [])).ConfigureAwait(false);
            check((await store.LoadAsync().ConfigureAwait(false)).Instances.Count == 0,
                "a saved empty widget board stays empty on restart");

            WidgetBoardState owned;
            using (var document = JsonDocument.Parse("{\"providerSpecific\":\"retain me\",\"nested\":{\"value\":7}}"))
            {
                var source = new WidgetBoardState(1, new[]
                {
                    new WidgetInstance("unknown", "missing.local.provider", WidgetCardSize.Medium, 2, document.RootElement),
                    new WidgetInstance("time.two", BuiltInWidgetIds.Time, WidgetCardSize.Small, 1,
                        BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(false, true))),
                    new WidgetInstance("time.one", BuiltInWidgetIds.Time, WidgetCardSize.Small, 0,
                        BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(true, false)))
                });
                await store.SaveAsync(source).ConfigureAwait(false);
                owned = await store.LoadAsync().ConfigureAwait(false);
            }
            check(owned.Instances.Select(i => i.InstanceId).SequenceEqual(new[] { "time.one", "time.two", "unknown" })
                && owned.Instances.Select(i => i.Order).SequenceEqual(new[] { 0, 1, 2 })
                && owned.Instances[2].ProviderId == "missing.local.provider" && owned.Instances[2].Size == WidgetCardSize.Medium
                && owned.Instances[2].Settings.GetProperty("nested").GetProperty("value").GetInt32() == 7,
                "widget store owns JSON, normalizes order and retains missing-provider configuration");
            check(!BuiltInWidgetProviders.ReadTimeSettings(owned.Instances[0].Settings).ShowSeconds
                && BuiltInWidgetProviders.ReadTimeSettings(owned.Instances[1].Settings).ShowSeconds
                && !BuiltInWidgetProviders.ReadTimeSettings(owned.Instances[1].Settings).Use24Hour,
                "multiple clock instances preserve independent settings across storage");
            using (var document = JsonDocument.Parse(await File.ReadAllBytesAsync(store.SettingsPath).ConfigureAwait(false)))
            {
                var instances = document.RootElement.GetProperty("instances");
                check(document.RootElement.GetProperty("version").GetInt32() == 1
                    && instances[0].GetProperty("size").GetString() == "small" && instances[2].GetProperty("size").GetString() == "medium"
                    && !instances[0].TryGetProperty("x", out _) && !instances[0].TryGetProperty("y", out _),
                    "widgets.json stores version, order and size names without work-area pixel positions");
            }
            using (var unicode = JsonDocument.Parse("\"" + new string('中', 1364) + "\""))
                await store.SaveAsync(new(1, [new("unicode", "missing.local.provider", WidgetCardSize.Small, 0, unicode.RootElement)])).ConfigureAwait(false);
            var unicodeRoundTrip = await store.LoadAsync().ConfigureAwait(false);
            check(unicodeRoundTrip.Instances[0].Settings.GetString()?.Length == 1364 && store.TakeLoadNotice() is null,
                "near-limit non-ASCII settings round-trip without JSON-encoder expansion breaking the 4 KiB limit");
            await store.SaveAsync(owned).ConfigureAwait(false);

            byte[] beforeInvalidSave = await File.ReadAllBytesAsync(store.SettingsPath).ConfigureAwait(false);
            bool tooManyRejected = false, tooLargeRejected = false, cancelled = false;
            try
            {
                await store.SaveAsync(new(1, Enumerable.Range(0, 33).Select(n =>
                    new WidgetInstance("id" + n, BuiltInWidgetIds.Time, WidgetCardSize.Small, n, WidgetBoardData.EmptySettings())).ToArray())).ConfigureAwait(false);
            }
            catch (InvalidDataException) { tooManyRejected = true; }
            try
            {
                using var oversized = JsonDocument.Parse("\"" + new string('x', 4095) + "\"");
                await store.SaveAsync(new(1, [new("large", BuiltInWidgetIds.Time, WidgetCardSize.Small, 0, oversized.RootElement)])).ConfigureAwait(false);
            }
            catch (InvalidDataException) { tooLargeRejected = true; }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await store.SaveAsync(new(1, []), cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { cancelled = true; }
            }
            check(tooManyRejected && tooLargeRejected && cancelled
                && (await File.ReadAllBytesAsync(store.SettingsPath).ConfigureAwait(false)).SequenceEqual(beforeInvalidSave)
                && Directory.GetFiles(root, ".widgets-*.tmp").Length == 0,
                "invalid or pre-cancelled saves preserve the last complete file and leave no temporary files");

            await RecoveryChecksAsync(check, store, root).ConfigureAwait(false);
            await AtomicChecksAsync(check, store).ConfigureAwait(false);
            check((await File.ReadAllBytesAsync(preferencePath).ConfigureAwait(false)).SequenceEqual(preferences),
                "corruption recovery and concurrent widget saves still leave primary preferences untouched");

            string otherProfile = Path.Combine(root, "separate-profile");
            using var other = new WidgetBoardStore(otherProfile);
            await other.SaveAsync(new(1, [])).ConfigureAwait(false);
            check((await other.LoadAsync().ConfigureAwait(false)).Instances.Count == 0
                && (await store.LoadAsync().ConfigureAwait(false)).Instances.Count == 1,
                "explicit preview profiles isolate widget boards from one another");
        }
        finally
        {
            // This test owns exactly the generated child. Never recursively clean an unchecked computed path.
            string resolved = Path.GetFullPath(root);
            if (!string.Equals(Path.GetDirectoryName(resolved), testParent, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unverified widget test directory.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task RecoveryChecksAsync(Action<bool, string> check, WidgetBoardStore store, string root)
    {
        const string one = "{\"instanceId\":\"same\",\"providerId\":\"missing.provider\",\"size\":\"small\",\"order\":0,\"settings\":{}}";
        string[] corrupt =
        [
            "{truncated", "null", "{\"version\":1,\"instances\":null}",
            "{\"version\":2,\"instances\":[]}", "{\"version\":\"1\",\"instances\":[]}",
            "{\"version\":1,\"Version\":1,\"instances\":[]}",
            "{\"version\":1,\"instances\":[" + one + "," + one + "]}",
            "{\"version\":1,\"instances\":[" + one.Replace("\"small\"", "\"future-size\"") + "]}",
            "{\"version\":1,\"instances\":[" + one.Replace("\"order\":0", "\"order\":\"0\"") + "]}",
            "{\"version\":1,\"instances\":[" + one.Replace("\"settings\":{}", "\"settings\":\"" + new string('x', 4095) + "\"") + "]}",
            "{\"version\":1,\"instances\":[" + string.Join(',', Enumerable.Range(0, 33).Select(n => one.Replace("\"same\"", "\"id" + n + "\""))) + "]}"
        ];
        bool allRecovered = true, allBackedUp = true, allNotices = true;
        foreach (string invalid in corrupt)
        {
            var before = Directory.GetFiles(root, "widgets.corrupt-*.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            byte[] original = Encoding.UTF8.GetBytes(invalid);
            await File.WriteAllBytesAsync(store.SettingsPath, original).ConfigureAwait(false);
            var recovered = await store.LoadAsync().ConfigureAwait(false);
            string? notice = store.TakeLoadNotice();
            var added = Directory.GetFiles(root, "widgets.corrupt-*.json").Where(f => !before.Contains(f)).ToArray();
            allRecovered &= recovered.Instances.Count == 2 && recovered.Instances[0].ProviderId == BuiltInWidgetIds.Time;
            allBackedUp &= added.Length == 1 && (await File.ReadAllBytesAsync(added[0]).ConfigureAwait(false)).SequenceEqual(original);
            allNotices &= notice is not null && store.TakeLoadNotice() is null;
        }
        check(allRecovered && allBackedUp && allNotices,
            "invalid JSON/version/types/duplicates/size/count/settings are backed up exactly and recover usable defaults once");

        string empty = "{\"version\":1,\"instances\":[]}";
        byte[] exact = Encoding.UTF8.GetBytes(empty + new string(' ', WidgetBoardData.MaximumFileBytes - empty.Length));
        int backups = Directory.GetFiles(root, "widgets.corrupt-*.json").Length;
        await File.WriteAllBytesAsync(store.SettingsPath, exact).ConfigureAwait(false);
        var accepted = await store.LoadAsync().ConfigureAwait(false);
        check(accepted.Instances.Count == 0 && store.TakeLoadNotice() is null
            && Directory.GetFiles(root, "widgets.corrupt-*.json").Length == backups,
            "exactly 256 KiB valid widget files are accepted without replacing a legitimate empty board");
        byte[] oversized = [.. exact, (byte)' '];
        await File.WriteAllBytesAsync(store.SettingsPath, oversized).ConfigureAwait(false);
        var restored = await store.LoadAsync().ConfigureAwait(false);
        var newest = Directory.GetFiles(root, "widgets.corrupt-*.json")
            .Single(f => new FileInfo(f).Length == oversized.Length);
        check(restored.Instances.Count == 2 && store.TakeLoadNotice() is not null
            && (await File.ReadAllBytesAsync(newest).ConfigureAwait(false)).SequenceEqual(oversized),
            "files larger than 256 KiB are rejected before unbounded reading and preserved in a backup");
    }

    private static async Task AtomicChecksAsync(Action<bool, string> check, WidgetBoardStore store)
    {
        var errors = new ConcurrentQueue<string>(); int readCount = 0;
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var stream = new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                    using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                    if (document.RootElement.GetProperty("version").GetInt32() != 1
                        || document.RootElement.GetProperty("instances").ValueKind != JsonValueKind.Array)
                        errors.Enqueue("incomplete structure");
                    Interlocked.Increment(ref readCount); started.TrySetResult();
                }
                catch (Exception error) when (error is IOException or JsonException) { errors.Enqueue(error.GetType().Name); started.TrySetResult(); }
                try { await Task.Delay(1, stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        });
        await started.Task.ConfigureAwait(false);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 12).Select(n => store.SaveAsync(new(1,
            [new("revision" + n, BuiltInWidgetIds.Time, WidgetCardSize.Small, 0, BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(ShowSeconds: n % 2 == 0)))]))))
                .ConfigureAwait(false);
        }
        finally { stop.Cancel(); await reader.ConfigureAwait(false); }
        var final = await store.LoadAsync().ConfigureAwait(false);
        check(errors.IsEmpty && readCount > 0 && final.Instances.Count == 1
            && Directory.GetFiles(store.DirectoryPath, ".widgets-*.tmp").Length == 0,
            "serialized atomic widget writes expose complete JSON to concurrent readers and remove temporary files");
    }
}
