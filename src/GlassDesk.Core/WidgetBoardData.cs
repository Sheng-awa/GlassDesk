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
        new WidgetInstance("time.default", BuiltInWidgetIds.Time, WidgetCardSize.Small, 0, EmptySettings()),
        new WidgetInstance("date.default", BuiltInWidgetIds.Date, WidgetCardSize.Small, 1, EmptySettings())
    }));

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
            cards.Add(new(instance.InstanceId, instance.Size,
                new(Left + column * (SmallWidth + Gap), y, width, CardHeight)));
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
