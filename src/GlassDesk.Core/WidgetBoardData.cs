using System.Text;
using System.Text.Json;

namespace GlassDesk.Core;

/// <summary>Pure, bounded data for widgets.json. Provider availability never changes saved instances.</summary>
public static class WidgetBoardData
{
    public const int Version = 1, MaximumInstances = 32, MaximumSettingsBytes = 4096, MaximumSettingsDepth = 32,
        MaximumFileBytes = 256 * 1024, MaximumIdentifierLength = 128;

    public static JsonElement EmptySettings()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    public static WidgetBoardState CreateDefault() => new(Version, Array.AsReadOnly(new[]
    {
        new WidgetInstance("time.default", BuiltInWidgetIds.Time, WidgetCardSize.Small, 0, EmptySettings())
    }));

    // Audit #11: presentation scope is currently only the time card; saved data is never pruned.
    private static bool InPresentationScope(WidgetInstance instance) => instance.ProviderId == BuiltInWidgetIds.Time;

    /// <summary>
    /// Temporary presentation filter: only the offline time card is exposed while the other
    /// built-ins are redesigned. Saved boards keep every instance; this only hides them from
    /// paint, layout and page height until they are re-enabled.
    /// </summary>
    public static WidgetBoardState TimeOnlyForPresentation(WidgetBoardState state)
    {
        var kept = state.Instances.Where(InPresentationScope).ToArray();
        return new(state.Version, Array.AsReadOnly(kept));
    }

    /// <summary>Saved instances hidden by the current presentation scope, in saved order.</summary>
    public static IReadOnlyList<HiddenWidgetInstance> HiddenForPresentation(WidgetBoardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hidden = new List<HiddenWidgetInstance>();
        foreach (var instance in state.Instances)
            if (!InPresentationScope(instance)) hidden.Add(new(instance, WidgetHiddenReason.OutsideCurrentScope));
        return Array.AsReadOnly(hidden.ToArray());
    }

    /// <summary>Capacity always measures complete saved instances, independently of what is visible.</summary>
    public static WidgetBoardCounts Counts(WidgetBoardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int saved = state.Instances.Count, visible = state.Instances.Count(InPresentationScope);
        return new(saved, visible, saved - visible, Math.Max(0, MaximumInstances - saved));
    }

    /// <summary>Adding is governed by saved capacity; hidden instances still occupy their slots.</summary>
    public static bool CanAdd(WidgetBoardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Instances.Count < MaximumInstances;
    }

    /// <summary>
    /// Explicit management removal only (nothing is pruned automatically): drops one instance,
    /// renumbers Order contiguously and leaves every other saved field untouched.
    /// </summary>
    public static bool TryRemove(WidgetBoardState state, string instanceId, out WidgetBoardState next)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (instanceId is null || !state.Instances.Any(i => i.InstanceId == instanceId))
        { next = state; return false; }
        var kept = new List<WidgetInstance>(state.Instances.Count - 1);
        foreach (var instance in state.Instances)
        {
            if (instance.InstanceId == instanceId) continue;
            // Reuse the saved record whenever the contiguous position already matches.
            kept.Add(instance.Order == kept.Count ? instance : instance with { Order = kept.Count });
        }
        next = new(state.Version, Array.AsReadOnly(kept.ToArray()));
        return true;
    }

    public static bool IsIdentifier(string? value) => value is { Length: > 0 and <= MaximumIdentifierLength }
        && value.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c));

    public static JsonElement CloneSettings(JsonElement settings)
    {
        if (settings.ValueKind is JsonValueKind.Undefined)
            throw new InvalidDataException("Widget settings must be JSON.");
        string json = settings.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > MaximumSettingsBytes)
            throw new InvalidDataException("Widget settings exceed 4 KiB.");
        try
        {
            // Keep saved settings readable inside the board's outer JSON structure as well.
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumSettingsDepth });
            return document.RootElement.Clone();
        }
        catch (JsonException error) { throw new InvalidDataException("Widget settings are too deeply nested.", error); }
    }

    /// <summary>Clones caller-owned JSON and collections and creates contiguous, stable order values.</summary>
    public static WidgetBoardState ValidateAndClone(WidgetBoardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != Version || state.Instances is null || state.Instances.Count > MaximumInstances)
            throw new InvalidDataException("Unsupported or oversized widget board.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var instances = new List<WidgetInstance>(state.Instances.Count);
        foreach (var instance in state.Instances)
        {
            if (instance is null || !IsIdentifier(instance.InstanceId) || !IsIdentifier(instance.ProviderId)
                || !ids.Add(instance.InstanceId) || !Enum.IsDefined(instance.Size) || instance.Order < 0)
                throw new InvalidDataException("Invalid widget instance.");
            instances.Add(instance with { Settings = CloneSettings(instance.Settings) });
        }
        return new(Version, Array.AsReadOnly(instances.OrderBy(i => i.Order)
            .Select((i, order) => i with { Order = order }).ToArray()));
    }
}

/// <summary>Why a saved instance is not part of the current presentation.</summary>
public enum WidgetHiddenReason { OutsideCurrentScope }

/// <summary>A saved instance absent from the current presentation; carries the saved record itself.</summary>
public sealed record HiddenWidgetInstance(WidgetInstance Instance, WidgetHiddenReason Reason);

/// <summary>Saved capacity versus what the current presentation scope shows.</summary>
public readonly record struct WidgetBoardCounts(int Saved, int Visible, int Hidden, int Remaining);

public sealed record WidgetCardPlacement(string InstanceId, WidgetCardSize Size, FloatRect Bounds);
public sealed record WidgetBoardPacking(IReadOnlyList<WidgetCardPlacement> Cards, int Columns, float ContentBottom);

/// <summary>Only derives pixels for this presentation; saved size/order remain independent of work area.</summary>
public static class WidgetBoardLayout
{
    // Cards start under the 58–86 DIP toolbar; the bottom inset leaves room for the Details dock.
    public const float Left = 24, Gap = 12, SmallWidth = 150, MediumWidth = 312, CardHeight = 148, Top = 104, BottomInset = 72;

    public static WidgetBoardPacking Pack(WidgetBoardState state, float availableWidth, float top = Top)
    {
        if (!float.IsFinite(availableWidth) || availableWidth <= Left * 2 || !float.IsFinite(top) || top < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        var board = WidgetBoardData.ValidateAndClone(state);
        int columns = availableWidth >= MediumWidth + Left * 2 ? 2 : 1;
        float inner = availableWidth - Left * 2, y = top;
        int column = 0;
        var cards = new List<WidgetCardPlacement>(board.Instances.Count);
        foreach (var instance in board.Instances)
        {
            if (columns == 2 && instance.Size == WidgetCardSize.Medium && column != 0)
            { column = 0; y += CardHeight + Gap; }
            float width = Math.Min(instance.Size == WidgetCardSize.Medium ? MediumWidth : SmallWidth, inner);
            // A lone widget is centred; multiple widgets keep the stable left-aligned grid.
            float x = board.Instances.Count == 1 ? (availableWidth - width) / 2 : Left + column * (SmallWidth + Gap);
            cards.Add(new(instance.InstanceId, instance.Size, new(x, y, width, CardHeight)));
            if (columns == 1 || instance.Size == WidgetCardSize.Medium || ++column == 2)
            { column = 0; y += CardHeight + Gap; }
        }
        float bottom = cards.Count == 0 ? top : cards.Max(c => c.Bounds.Bottom) + Left;
        return new(Array.AsReadOnly(cards.ToArray()), columns, bottom);
    }

    /// <summary>Visible cards plus a single row below/above the viewport; no scheduling is performed here.</summary>
    public static IReadOnlySet<string> EligibleInstances(WidgetBoardPacking packing, FloatRect viewport)
    {
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || !float.IsFinite(viewport.Width)
            || !float.IsFinite(viewport.Height) || viewport.Width < 0 || viewport.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(viewport));
        if (viewport.Width == 0 || viewport.Height == 0) return new HashSet<string>(StringComparer.Ordinal);
        var expanded = new FloatRect(viewport.X, viewport.Y - CardHeight - Gap, viewport.Width,
            viewport.Height + 2 * (CardHeight + Gap));
        return packing.Cards.Where(c => c.Bounds.Intersects(expanded))
            .Select(c => c.InstanceId).ToHashSet(StringComparer.Ordinal);
    }
}
