using System.Globalization;
using System.Text.Json;

namespace GlassDesk.Core;

public sealed record TimeWidgetSettings(bool Use24Hour = true, bool ShowSeconds = false, string? TimeZoneId = null);
public sealed record DateWidgetSettings(bool ShowWeekday = true, string DateFormat = "long", string WeekdayFormat = "long",
    string? TimeZoneId = null);

/// <summary>Offline providers. A snapshot describes its next boundary; providers never own timers or subscriptions.</summary>
public static class BuiltInWidgetProviders
{
    public static IReadOnlyList<PluginWidget> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new PluginWidget(BuiltInWidgetIds.Time, "时间", new(Array.AsReadOnly(new[] { WidgetCardSize.Small, WidgetCardSize.Medium }), "离线时间"), new TimeProvider()),
        new PluginWidget(BuiltInWidgetIds.Date, "日期", new(Array.AsReadOnly(new[] { WidgetCardSize.Small, WidgetCardSize.Medium }), "离线日期"), new DateProvider())
    });

    // Expose only IDs installed by Windows. FindSystemTimeZoneById can additionally translate IANA IDs;
    // using the actual installed list deliberately avoids accepting those aliases or downloading data.
    public static IReadOnlyList<TimeZoneInfo> InstalledTimeZones() => TimeZoneInfo.GetSystemTimeZones();
    public static TimeZoneInfo ResolveTimeZone(string? id) => string.IsNullOrEmpty(id) ? TimeZoneInfo.Local :
        InstalledTimeZones().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? TimeZoneInfo.Local;

    public static TimeWidgetSettings ReadTimeSettings(JsonElement settings) => new(
        Boolean(settings, "use24Hour", true), Boolean(settings, "showSeconds", false), Zone(settings));
    public static DateWidgetSettings ReadDateSettings(JsonElement settings) => new(
        Boolean(settings, "showWeekday", true), Choice(settings, "dateFormat", "long"),
        Choice(settings, "weekdayFormat", "long"), Zone(settings));
    public static JsonElement ToJson(TimeWidgetSettings settings) => JsonSerializer.SerializeToElement(new
    { use24Hour = settings.Use24Hour, showSeconds = settings.ShowSeconds, timeZoneId = ValidZoneId(settings.TimeZoneId) });
    public static JsonElement ToJson(DateWidgetSettings settings) => JsonSerializer.SerializeToElement(new
    {
        showWeekday = settings.ShowWeekday, dateFormat = settings.DateFormat == "short" ? "short" : "long",
        weekdayFormat = settings.WeekdayFormat == "short" ? "short" : "long", timeZoneId = ValidZoneId(settings.TimeZoneId)
    });

    private static bool Boolean(JsonElement s, string key, bool fallback) =>
        s.ValueKind == JsonValueKind.Object && s.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : fallback;
    private static string Choice(JsonElement s, string key, string fallback) =>
        s.ValueKind == JsonValueKind.Object && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == "short"
            ? "short" : fallback;
    private static string? Zone(JsonElement s) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("timeZoneId", out var v)
        && v.ValueKind == JsonValueKind.String ? ValidZoneId(v.GetString()) : null;
    private static string? ValidZoneId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 128
        ? InstalledTimeZones().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))?.Id : null;
    private static float Width(WidgetCardSize size) => size == WidgetCardSize.Medium ? WidgetBoardLayout.MediumWidth : WidgetBoardLayout.SmallWidth;
    private static string ZoneLabel(TimeZoneInfo zone) => zone.DisplayName.Length > 128 ? zone.DisplayName[..128] : zone.DisplayName;

    private sealed class TimeProvider : IWidgetProvider
    {
        public ValueTask<WidgetSnapshot> GetSnapshotAsync(WidgetRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = ReadTimeSettings(request.Settings); var zone = ResolveTimeZone(options.TimeZoneId);
            var local = TimeZoneInfo.ConvertTime(request.UtcNow, zone);
            var culture = CultureInfo.CurrentCulture;
            string format = options.Use24Hour ? "HH:mm" : "hh:mm";
            if (options.ShowSeconds) format += ":ss";
            float width = Width(request.Size) - 24;
            // Seconds stay in one numeric field; the compact card uses the established 20 DIP role.
            var role = request.Size == WidgetCardSize.Small && options.ShowSeconds ? TextRole.MediaDetailsTitle : TextRole.WidgetClock;
            var elements = new List<WidgetElement>
            {
                new(WidgetElementKind.Text, new(12, 12, width, 16), "时间", TextRole.WidgetLabel),
                new(WidgetElementKind.Text, new(12, 38, width, 44), local.ToString(format, culture), role, WidgetNumericStyle.Tabular)
            };
            if (!options.Use24Hour)
                elements.Add(new(WidgetElementKind.Text, new(12, 83, width, 16), local.ToString("tt", culture), TextRole.WidgetLabel));
            elements.Add(new(WidgetElementKind.Text, new(12, 112, width, 16), ZoneLabel(zone), TextRole.WidgetLabel));
            long interval = options.ShowSeconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
            long ticks = request.UtcNow.UtcTicks;
            DateTimeOffset? next = ticks > DateTimeOffset.MaxValue.UtcTicks - interval ? null : new(ticks - ticks % interval + interval, TimeSpan.Zero);
            return ValueTask.FromResult(new WidgetSnapshot(Array.AsReadOnly(elements.ToArray()), next));
        }
    }

    private sealed class DateProvider : IWidgetProvider
    {
        public ValueTask<WidgetSnapshot> GetSnapshotAsync(WidgetRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = ReadDateSettings(request.Settings); var zone = ResolveTimeZone(options.TimeZoneId);
            var local = TimeZoneInfo.ConvertTime(request.UtcNow, zone); var culture = CultureInfo.CurrentCulture;
            float width = Width(request.Size) - 24;
            var elements = new List<WidgetElement>
            {
                new(WidgetElementKind.Text, new(12, 12, width, 16), "日期", TextRole.WidgetLabel),
                new(WidgetElementKind.Text, new(12, 32, width, 56), local.ToString("dd", culture), TextRole.WidgetDateValue, WidgetNumericStyle.Tabular),
                new(WidgetElementKind.Text, new(12, 91, width, 20),
                    local.ToString(options.DateFormat == "short" ? "yyyy-MM" : "Y", culture), TextRole.Body, WidgetNumericStyle.DigitsOnly)
            };
            if (options.ShowWeekday)
                elements.Add(new(WidgetElementKind.Text, new(12, 117, width, 16),
                    local.ToString(options.WeekdayFormat == "short" ? "ddd" : "dddd", culture), TextRole.WidgetLabel));
            return ValueTask.FromResult(new WidgetSnapshot(Array.AsReadOnly(elements.ToArray()), NextMidnight(request.UtcNow, zone)));
        }
    }

    public static DateTimeOffset? NextMidnight(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        if (local.Date >= DateTime.MaxValue.Date) return null;
        var next = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        // Some installed zones skip midnight, and historical transitions can skip an entire date.
        for (int minute = 0; minute < 2880 && zone.IsInvalidTime(next); minute++)
        {
            if (next > DateTime.MaxValue.AddMinutes(-1)) return null;
            next = next.AddMinutes(1);
        }
        if (zone.IsInvalidTime(next)) return null;
        if (zone.IsAmbiguousTime(next))
            return zone.GetAmbiguousTimeOffsets(next).Select(offset => new DateTimeOffset(next, offset).ToUniversalTime())
                .Where(value => value > utcNow).OrderBy(value => value).Select(value => (DateTimeOffset?)value).FirstOrDefault();
        try { return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(next, zone), TimeSpan.Zero); }
        catch (ArgumentException) { return null; }
    }
}
