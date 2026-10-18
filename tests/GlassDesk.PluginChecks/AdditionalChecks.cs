using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.Plugins;

/// <summary>Behavior regressions for the real queued-dispatch/lifetime boundary.
/// Isolated data only; no windows, native materials, real profile or player.</summary>
public static class AdditionalChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var work = Path.Combine(Path.GetFullPath(root), "artifacts", "plugin-checks-extra", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        await QueuedCallbackRevocationAsync(Path.Combine(work, "queued"), check);
        await InitialColorAsync(Path.Combine(work, "color"), check);
        await AppearanceAuthorityAsync(Path.Combine(work,"appearance"),check);
        await InvalidInstalledManifestsAsync(root, Path.Combine(work, "invalid"), check);
        await ConfigurationAsync(Path.Combine(work, "configuration"), check);
    }

    static async Task AppearanceAuthorityAsync(string work,Action<bool,string> check)
    {
        var dispatcher=new QueuedDispatcher();
        await using var host=new PluginHost(dispatcher,work);
        CompanionVisualState? current=null;host.CompanionChanged+=value=>current=value;
        var preferences=new WidgetPreferences {CompanionColor=0xff123456};
        await host.ApplyPreferencesAsync(preferences);
        using var json=JsonDocument.Parse("{\"custom-expression\":{\"weight\":7},\"color\":1}");
        await host.SaveConfigurationAsync("glassdesk.companion",json.RootElement);
        host.PreviewBuiltinAppearance(0xffabcdef,CompanionAppearanceIds.CatEar);
        host.PreviewBuiltinAppearance(0xff39a789,CompanionAppearanceIds.Original);
        await host.ApplyPreferencesAsync(preferences with {CompanionColor=0xffabcdef,CompanionAppearanceId=CompanionAppearanceIds.CatEar});
        dispatcher.Drain();
        check(current is {Color:0xff39a789,AppearanceId:CompanionAppearanceIds.Original},"queued expression and older save cannot override the latest live appearance");
        var latest=preferences with {CompanionColor=0xff39a789,CompanionAppearanceId=CompanionAppearanceIds.Original};
        await host.ApplyPreferencesAsync(latest);dispatcher.Drain();
        check(current?.Color==latest.CompanionColor&&current.AppearanceId==latest.CompanionAppearanceId,"last valid appearance persists after its matching save");
        var configuration=host.GetConfiguration("glassdesk.companion")!.Value;
        check(configuration.GetProperty("custom-expression").GetProperty("weight").GetInt32()==7&&configuration.GetProperty("appearanceId").GetString()==latest.CompanionAppearanceId,"appearance merge preserves unknown nested plugin configuration");
    }

    private static async Task QueuedCallbackRevocationAsync(string work, Action<bool, string> check)
    {
        var dispatcher = new QueuedDispatcher();
        await using var host = new PluginHost(dispatcher, work);
        var applied = new List<bool>();
        host.MaterialChanged += (id, active) => { if (id == "liquidglass") applied.Add(active); };
        var preferences = new WidgetPreferences { CompanionEnabled = false, LiquidGlassEnabled = true };
        await host.ApplyPreferencesAsync(preferences);
        // The first SetMaterial(true) remains queued while the same Context is
        // paused then reactivated, reproducing a real UI queue backlog.
        await host.UpdateSceneAsync(new(WidgetForm.Capsule, false, MediaSnapshot.Disconnected, false, 1));
        check(host.Statuses.Single(status => status.Id == "glassdesk.liquidglass") is { Enabled: true, State: PluginRunState.Paused },
            "queued lifecycle hide pauses without changing enabled intent");
        await host.UpdateSceneAsync(new(WidgetForm.Capsule, true, MediaSnapshot.Disconnected, false, 2));
        check(dispatcher.Pending >= 2, "callback revocation check holds both old and current activation requests");
        applied.Clear();
        dispatcher.Drain();
        check(applied.Count == 1 && applied[0], "pre-hide material callback never replays after resume");

        // Deactivation must also reject an already queued current request while
        // the plugin remains paused rather than depending on a later resume.
        await host.UpdateSceneAsync(new(WidgetForm.Capsule, false, MediaSnapshot.Disconnected, false, 3));
        await host.UpdateSceneAsync(new(WidgetForm.Capsule, true, MediaSnapshot.Disconnected, false, 4));
        await host.UpdateSceneAsync(new(WidgetForm.Capsule, false, MediaSnapshot.Disconnected, false, 5));
        applied.Clear();
        dispatcher.Drain();
        check(!applied.Any(value => value), "queued material requests cannot activate resources while hidden");
    }

    private static async Task InitialColorAsync(string work, Action<bool, string> check)
    {
        const uint desired = 0xff27a3cf;
        var dispatcher = new QueuedDispatcher();
        await using var host = new PluginHost(dispatcher, work);
        var colors = new List<uint>();
        host.CompanionChanged += state => { if (state is not null) colors.Add(state.Color); };
        await host.ApplyPreferencesAsync(new() { CompanionEnabled = true, CompanionColor = desired, LiquidGlassEnabled = false });
        check(dispatcher.Pending > 0, "first companion color check observes an actual queued initial frame");
        dispatcher.Drain();
        check(colors.Count >= 2 && colors.All(color => color == desired),
            "first companion frame and queued startup frame both use the saved custom color");
    }

    private static async Task InvalidInstalledManifestsAsync(string root, string work, Action<bool, string> check)
    {
        var samplePath = Path.Combine(root, "samples", "StatusPlugin", "plugin.json");
        var sample = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(samplePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing sample manifest.");
        var installations = Path.Combine(work, "plugins");
        Directory.CreateDirectory(installations);
        WriteManifest(Path.Combine(installations, "example.bad-api"),
            JsonSerializer.Serialize(sample with { Id = "example.bad-api", ApiVersion = PluginApi.Version + 1 }));
        WriteManifest(Path.Combine(installations, "example.missing-dll"),
            JsonSerializer.Serialize(sample with { Id = "example.missing-dll", EntryAssembly = "missing.dll" }));
        WriteManifest(Path.Combine(installations, "example.missing-fields"), "{}");
        var dispatcher = new QueuedDispatcher();
        await using (var host = new PluginHost(dispatcher, work))
        {
            check(host.Statuses.Count == 2 && host.Statuses.All(status => status.BuiltIn),
                "invalid installed API, missing DLL and absent manifest fields cannot block host construction");
            await host.ApplyPreferencesAsync(new() { CompanionEnabled = false, LiquidGlassEnabled = false });
            check(host.Statuses.All(status => status.State == PluginRunState.Disabled),
                "host remains usable after rejecting broken installed plugins");
        }

        var validFields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(samplePath))!;
        foreach (var field in new[] { "id", "name", "version", "entryAssembly", "entryType", "resourceDescription" })
        {
            var fields = new Dictionary<string, JsonElement>(validFields);
            fields.Remove(field);
            var directory = Path.Combine(work, "omission-" + field);
            WriteManifest(directory, JsonSerializer.Serialize(fields));
            var rejected = false;
            try { PluginPackage.ReadManifest(directory); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, $"manifest omission of {field} reports InvalidDataException");
        }
    }

    private static void WriteManifest(string directory, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.json"), text);
    }

    private static async Task ConfigurationAsync(string work, Action<bool, string> check)
    {
        const string id = "glassdesk.liquidglass";
        var dispatcher = new QueuedDispatcher();
        await using var host = new PluginHost(dispatcher, work);
        using var baseline = JsonDocument.Parse("{\"revision\":-1,\"note\":\"baseline\"}");
        await host.SaveConfigurationAsync(id, baseline.RootElement);
        var path = Path.Combine(work, "plugin-config", id + ".json");
        var original = File.ReadAllText(path);

        // Parse raw Unicode rather than using the default serializer encoder,
        // whose \u escapes would make character and byte counts indistinguishable.
        using var oversized = JsonDocument.Parse("{\"note\":\"" + new string('中', 23000) + "\"}");
        var raw = oversized.RootElement.GetRawText();
        check(raw.Length < 65536 && Encoding.UTF8.GetByteCount(raw) > 65536,
            "UTF-8 limit regression uses fewer than 64 Ki characters but more than 64 KiB");
        var tooLargeRejected = false;
        try { await host.SaveConfigurationAsync(id, oversized.RootElement); }
        catch (InvalidDataException) { tooLargeRejected = true; }
        check(tooLargeRejected && File.ReadAllText(path) == original && host.GetConfiguration(id)!.Value.GetRawText() == original,
            "oversized Unicode configuration is rejected without altering disk or cache");

        for (var round = 0; round < 3; round++)
        {
            var batch = round;
            await Task.WhenAll(Enumerable.Range(0, 32).Select(async inputRevision =>
            {
                using var configuration = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["revision"] = batch * 32 + inputRevision,
                    ["payload"] = new string('x', 3500 + inputRevision % 11 * 100)
                }));
                await host.SaveConfigurationAsync(id, configuration.RootElement);
            }));
            var persisted = File.ReadAllText(path);
            using var document = JsonDocument.Parse(persisted);
            var revision = document.RootElement.GetProperty("revision").GetInt32();
            check(host.GetConfiguration(id)!.Value.GetRawText() == persisted && revision >= batch * 32 && revision < (batch + 1) * 32,
                $"concurrent configuration round {round + 1} leaves one complete matching disk/cache revision");
        }
        check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0,
            "concurrent configuration saves leave no staging files");
        var beforeCancellation = File.ReadAllText(path);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellationObserved = false;
        try { await host.SaveConfigurationAsync(id, baseline.RootElement, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        check(cancellationObserved && File.ReadAllText(path) == beforeCancellation
            && host.GetConfiguration(id)!.Value.GetRawText() == beforeCancellation,
            "cancelled configuration save preserves the matching disk and cache");
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _actions = new();
        public int Pending => _actions.Count;
        public bool TryEnqueue(Action action) { _actions.Enqueue(action); return true; }
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
        public void Drain()
        {
            var maximum = 4096;
            while (_actions.TryDequeue(out var action))
            {
                if (--maximum == 0) throw new InvalidOperationException("Unexpected self-sustaining dispatcher work.");
                action();
            }
        }
    }
}
