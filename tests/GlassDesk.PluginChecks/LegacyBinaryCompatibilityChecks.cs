using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.Plugins;

/// <summary>A19 loads an actual DLL compiled against the archived SDK, never current Core.</summary>
public static class LegacyBinaryCompatibilityChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        root = Path.GetFullPath(root);
        string sdkRoot = Path.Combine(root, "tests", "Fixtures", "LegacySdk");
        string pluginRoot = Path.Combine(root, "tests", "Fixtures", "LegacyPlugin");
        string legacySdk = Path.Combine(sdkRoot, "bin", "Release", "net10.0", "GlassDesk.Core.dll");
        string binary = Path.Combine(pluginRoot, "bin", "Release", "net10.0", "GlassDesk.LegacyPlugin.dll");
        VerifySourceOrigin(root, sdkRoot, check);
        check(File.Exists(legacySdk) && File.Exists(binary), "A19 fixture binaries exist; build tests/Fixtures/LegacyPlugin first");
        if (!File.Exists(legacySdk) || !File.Exists(binary)) return;
        VerifyBinaryReferences(legacySdk, binary, check);

        const string id = "fixture.legacy-sdk";
        string work = Path.Combine(root, "artifacts", "plugin-checks-legacy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string package = Path.Combine(work, "legacy.glassdesk-plugin");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(pluginRoot, "plugin.json"), "plugin.json");
            archive.CreateEntryFromFile(binary, "GlassDesk.LegacyPlugin.dll");
        }
        using (var archive = ZipFile.OpenRead(package))
            check(!archive.Entries.Any(entry => entry.FullName.Equals("GlassDesk.Core.dll", StringComparison.OrdinalIgnoreCase)),
                "A19 package contains only the legacy plugin and manifest, without a private SDK copy");

        await using var host = new PluginHost(new InlineDispatcher(), work);
        CompanionVisualState? current = null;
        int visualUpdates = 0;
        host.CompanionChanged += state => current = state;
        host.VisualsChanged += visuals => { if (visuals.ContainsKey(id + "/legacy-page")) visualUpdates++; };
        var preferences = new WidgetPreferences
        {
            CompanionEnabled = false, LiquidGlassEnabled = false,
            CompanionColor = 0xFFABCDEF, CompanionAppearanceId = CompanionAppearanceIds.CatEar,
            PluginEnabled = new() { [id] = true }
        };
        await host.ApplyPreferencesAsync(preferences);
        check(await host.ImportAsync(package) == id, "A19 archived-SDK plugin package imports through the actual PluginHost");
        await host.ApplyPreferencesAsync(preferences);
        var status = host.Statuses.Single(entry => entry.Id == id);
        check(status.State == PluginRunState.Running, "A19 legacy DLL loads and starts with current contract identity: " + (status.Reason ?? "Running"));
        if (status.State != PluginRunState.Running) return;
        check(visualUpdates > 0 && host.Pages.Single().Items.Single().CommandId == id + "/six" && host.Settings(id).Count == 1,
            "A19 old contribution, context visual and command interfaces bind to the current SDK");
        check(current is { Color: 0x7C39B872, Expression: CompanionExpression.Surprised, Tilt: 13, TransitionMs: 340,
            AppearanceId: CompanionAppearanceIds.Original }, "A19 precompiled eight-argument companion constructor binds and retains third-party alpha/color/form");

        await host.ExecuteAsync(id, "six");
        check(current is { Color: 0x3A1855AA, EyeOffsetX: .25f, EyeOffsetY: -.1f, ScaleX: .98f, ScaleY: 1.02f,
            Tilt: 0, TransitionMs: 220, AppearanceId: CompanionAppearanceIds.Original },
            "A19 precompiled six-argument compatibility constructor retains fields and defaults");
        await host.ExecuteAsync(id, "copy");
        check(current is { Expression: CompanionExpression.Happy, Color: 0x7C39B872, Tilt: 13, TransitionMs: 340,
            AppearanceId: CompanionAppearanceIds.Original }, "A19 old record deconstruction and with-copy remain binary compatible");
        await host.ExecuteAsync(id, "appearance");
        check(current is { Expression: CompanionExpression.Calm, Color: 0x515A2BC4, EyeOffsetX: .1f, EyeOffsetY: .2f,
            AppearanceId: CompanionAppearanceIds.Original }, "A19 old ICompanionAppearance implementation preserves its own presentation semantics");

        using var baseline = JsonDocument.Parse("{\"unrelated\":\"preserved\",\"color\":17,\"appearanceId\":\"third-party.custom\"}");
        await host.SaveConfigurationAsync(id, baseline.RootElement);
        host.PreviewBuiltinAppearance(0xFF102030, CompanionAppearanceIds.CatEar);
        await host.ApplyPreferencesAsync(preferences with { CompanionColor = 0xFF102030 });
        check(current is { Color: 0x515A2BC4, AppearanceId: CompanionAppearanceIds.Original }
            && host.GetConfiguration(id)!.Value.GetRawText() == baseline.RootElement.GetRawText(),
            "A19 built-in color/form authority does not rewrite a third-party state or configuration");
        await host.ExecuteAsync(id, "configuration");
        var configuration = host.GetConfiguration(id)!.Value;
        check(configuration.GetProperty("legacySceneFive").GetBoolean() && configuration.GetProperty("legacySceneSeven").GetBoolean()
            && configuration.GetProperty("runtimeContract").GetString() == "GlassDesk.Core"
            && configuration.GetProperty("runtimeHasNewAppearance").GetBoolean()
            && configuration.GetProperty("originalValue").GetString() == "preserved",
            "A19 precompiled five/seven-argument scene constructors and configuration context execute against current Core");
        check(AssemblyLoadContext.Default.Assemblies.Count(assembly => assembly.GetName().Name == "GlassDesk.Core") == 1,
            "A19 loader shares the current SDK rather than loading archived Core into the default context");

        await host.UpdateSceneAsync(new(WidgetForm.Details, false, MediaSnapshot.Disconnected, false, 1));
        check(host.Statuses.Single(entry => entry.Id == id).State == PluginRunState.Paused && current is null,
            "A19 old pause contract retracts third-party companion contributions");
        await host.UpdateSceneAsync(new(WidgetForm.Details, true, MediaSnapshot.Disconnected, false, 2));
        check(host.Statuses.Single(entry => entry.Id == id).State == PluginRunState.Running && current is { Color: 0x7C39B872 },
            "A19 old start and SceneChanged contracts resume after pause");
        await host.RemoveAsync(id);
        check(!host.Statuses.Any(entry => entry.Id == id) && host.Pages.Count == 0 && current is null,
            "A19 old stop/dispose contracts unload the binary and retract contributions");
    }

    static void VerifySourceOrigin(string root, string sdkRoot, Action<bool, string> check)
    {
        using var origin = JsonDocument.Parse(File.ReadAllText(Path.Combine(sdkRoot, "source-origin.json")));
        string archivePath = Path.Combine(root, origin.RootElement.GetProperty("sourceArchive").GetString()!);
        // The archived SDK snapshot and its per-file fingerprints are tracked.
        // The original full release ZIP is private build evidence, not a checkout dependency.
        // When present locally it remains an additional provenance check.
        if (File.Exists(archivePath))
        {
            using var archiveFile = File.OpenRead(archivePath);
            check(Convert.ToHexString(SHA256.HashData(archiveFile)) == origin.RootElement.GetProperty("archiveSha256").GetString(),
                "A19 protected 0.2.0 source archive matches the recorded SHA-256");
        }
        using var archive = File.Exists(archivePath) ? ZipFile.OpenRead(archivePath) : null;
        if (archive is null)
            Console.WriteLine("INFO A19: original private release ZIP unavailable; verifying tracked legacy SDK snapshot fingerprints and actual binary compatibility.");
        foreach (var file in origin.RootElement.GetProperty("files").EnumerateArray())
        {
            string name = file.GetProperty("file").GetString()!;
            string expected = file.GetProperty("sha256").GetString()!;
            using var copied = File.OpenRead(Path.Combine(sdkRoot, name));
            bool matches = Convert.ToHexString(SHA256.HashData(copied)) == expected;
            if (archive is not null)
            {
                using var archived = (archive.GetEntry(file.GetProperty("entry").GetString()!)
                    ?? throw new InvalidDataException("Legacy SDK origin entry is missing.")).Open();
                matches &= Convert.ToHexString(SHA256.HashData(archived)) == expected;
            }
            check(matches, archive is null
                ? "A19 tracked legacy SDK source matches its recorded SHA-256: " + name
                : "A19 legacy SDK source is byte-identical to the protected archive: " + name);
        }
    }

    static void VerifyBinaryReferences(string sdk, string fixture, Action<bool, string> check)
    {
        using (var file = File.OpenRead(sdk))
        using (var pe = new PEReader(file))
        {
            var reader = pe.GetMetadataReader();
            var identity = reader.GetAssemblyDefinition();
            var state = reader.TypeDefinitions.Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name) == "CompanionVisualState");
            bool hasNewProperty = state.GetProperties().Any(handle => reader.GetString(reader.GetPropertyDefinition(handle).Name) == "AppearanceId");
            check(reader.GetString(identity.Name) == "GlassDesk.Core" && identity.Version == new Version(0, 2, 0, 0) && !hasNewProperty,
                "A19 fixture SDK binary has the original identity and no 0.3 appearance property");
            check(reader.GetGuid(reader.GetModuleDefinition().Mvid) != typeof(IGlassDeskPlugin).Module.ModuleVersionId,
                "A19 archived SDK binary is distinct from the current runtime Core module");
        }
        using (var file = File.OpenRead(fixture))
        using (var pe = new PEReader(file))
        {
            var reader = pe.GetMetadataReader();
            var reference = reader.AssemblyReferences.Select(reader.GetAssemblyReference)
                .Single(assembly => reader.GetString(assembly.Name) == "GlassDesk.Core");
            var companionCounts = new HashSet<int>(); var sceneCounts = new HashSet<int>();
            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                if (member.GetKind() != MemberReferenceKind.Method || reader.GetString(member.Name) != ".ctor"
                    || member.Parent.Kind != HandleKind.TypeReference) continue;
                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                if (reader.GetString(type.Namespace) != "GlassDesk.Core") continue;
                var signature = reader.GetBlobReader(member.Signature);
                var header = signature.ReadSignatureHeader();
                if (header.IsGeneric) signature.ReadCompressedInteger();
                int count = signature.ReadCompressedInteger();
                string name = reader.GetString(type.Name);
                if (name == "CompanionVisualState") companionCounts.Add(count);
                if (name == "PluginSceneState") sceneCounts.Add(count);
            }
            check(reference.Version == new Version(0, 2, 0, 0) && companionCounts.Contains(6) && companionCounts.Contains(8)
                && sceneCounts.Contains(5) && sceneCounts.Contains(7),
                "A19 actual plugin IL references old SDK identity and all retained constructor signatures");
        }
    }

    sealed class InlineDispatcher : IUiDispatcher
    {
        public bool TryEnqueue(Action action) { action(); return true; }
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
    }
}
