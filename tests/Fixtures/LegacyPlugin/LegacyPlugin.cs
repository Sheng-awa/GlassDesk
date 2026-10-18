using System.Text.Json;
using GlassDesk.Core;

namespace GlassDesk.Fixtures.Legacy;

/// <summary>Compiled only against the byte-identical archived 0.2.0 contracts.</summary>
public sealed class LegacyPlugin : IGlassDeskPlugin
{
    IPluginContext? _context;
    bool _subscribed;
    static CompanionVisualState Six() => new(CompanionExpression.Curious, 0x3A1855AA, .25f, -.1f, .98f, 1.02f);
    static CompanionVisualState Eight() => new(CompanionExpression.Surprised, 0x7C39B872, .15f, -.2f, 1.1f, .9f, 13, 340);
    public void Initialize(IPluginContext context) => _context = context;
    public IEnumerable<IPluginContribution> RegisterContributions() =>
    [
        new PluginCompanion("legacy-companion", "Legacy companion", new LegacyAppearance()),
        new PluginContentPage("legacy-page", "Legacy page", [new("Old SDK binary", "No new appearance property reference", "six")]),
        new PluginSetting("legacy-setting", "Legacy setting", PluginSettingKind.Text, "unchanged"),
        new PluginCommand("six", "Six argument constructor", _ => Submit(Six())),
        new PluginCommand("eight", "Eight argument constructor", _ => Submit(Eight())),
        new PluginCommand("copy", "Legacy record copy and deconstruction", _ => SubmitCopy()),
        new PluginCommand("appearance", "Legacy appearance implementation", _ => Submit(new LegacyAppearance().Present(CompanionExpression.Calm, 0x515A2BC4))),
        new PluginCommand("configuration", "Legacy scene and context contract", SaveConfigurationAsync)
    ];
    ValueTask Submit(CompanionVisualState state) { _context!.SubmitCompanion(state); return ValueTask.CompletedTask; }
    ValueTask SubmitCopy()
    {
        var state = Eight();
        state.Deconstruct(out var expression, out var color, out var x, out var y, out var sx, out var sy, out var tilt, out var transition);
        if (expression != CompanionExpression.Surprised || color != 0x7C39B872 || x != .15f || y != -.2f ||
            sx != 1.1f || sy != .9f || tilt != 13 || transition != 340) throw new InvalidOperationException("Legacy record deconstruction changed.");
        return Submit(state with { Expression = CompanionExpression.Happy });
    }
    async ValueTask SaveConfigurationAsync(CancellationToken cancellationToken)
    {
        var context = _context!;
        var source = context.Scene;
        var five = new PluginSceneState(source.Form, source.Visible, source.Media, source.ReducedMotion, source.Timestamp);
        var seven = new PluginSceneState(source.Form, source.Visible, source.Media, source.ReducedMotion, source.Timestamp,
            CompanionInteraction.Attention, 73);
        // A default serializer cache must not retain a plugin-defined payload Type.
        // Emit primitives directly: this fixture tests SDK binding and unloading.
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("legacySceneFive", five.Interaction == CompanionInteraction.None && five.InteractionSerial == 0);
            writer.WriteBoolean("legacySceneSeven", seven.Interaction == CompanionInteraction.Attention && seven.InteractionSerial == 73);
            writer.WriteString("runtimeContract", typeof(IGlassDeskPlugin).Assembly.GetName().Name);
            writer.WriteBoolean("runtimeHasNewAppearance", typeof(CompanionVisualState).GetProperty("AppearanceId") is not null);
            writer.WriteString("originalValue", context.ReadConfiguration()?.GetProperty("unrelated").GetString());
            writer.WriteEndObject();
        }
        using var json = JsonDocument.Parse(payload.ToArray());
        await context.SaveConfigurationAsync(json.RootElement, cancellationToken);
    }
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed) { _context!.SceneChanged += OnScene; _subscribed = true; }
        _context!.SubmitVisual("legacy-page", [new(PluginDrawKind.Text, 0, 0, 160, 24, 0xFF1855AA, "Legacy SDK binary", 12)]);
        _context.ReportResourceUsage("Legacy 0.2.0 contract binary; API 1; no background work");
        return Submit(Eight());
    }
    void OnScene(PluginSceneState scene) { if (scene.Visible) _context!.SubmitCompanion(Eight()); }
    public ValueTask PauseAsync(CancellationToken cancellationToken)
    {
        if (_subscribed && _context is not null) _context.SceneChanged -= OnScene;
        _subscribed = false; return ValueTask.CompletedTask;
    }
    public ValueTask StopAsync(CancellationToken cancellationToken) => PauseAsync(cancellationToken);
    public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None); _context = null; }

    sealed class LegacyAppearance : ICompanionAppearance
    {
        public string Id => "fixture.legacy.appearance";
        public CompanionVisualState Present(CompanionExpression expression, uint color) => new(expression, color, .1f, .2f, 1, 1);
    }
}
