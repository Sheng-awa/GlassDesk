using System.Globalization;
using System.Text.Json;

namespace GlassDesk.Core;

public sealed record TimeWidgetSettings(bool Use24Hour = true, bool ShowSeconds = false, string? TimeZoneId = null);
// The compact card prints the weekday inside one caption line, so the short form is the
// default; both options still round-trip and "完整星期" remains available per instance.
public sealed record DateWidgetSettings(bool ShowWeekday = true, string DateFormat = "long", string WeekdayFormat = "short",
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
        WeekdayChoice(settings), Zone(settings));
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
    // Weekday choices default to the compact form; only an explicit long setting uses the full name.
    private static string WeekdayChoice(JsonElement s) =>
        s.ValueKind == JsonValueKind.Object && s.TryGetProperty("weekdayFormat", out var v) && v.ValueKind == JsonValueKind.String
            && v.GetString() == "long" ? "long" : "short";
    private static string? Zone(JsonElement s) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("timeZoneId", out var v)
        && v.ValueKind == JsonValueKind.String ? ValidZoneId(v.GetString()) : null;
    private static string? ValidZoneId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 128
        ? InstalledTimeZones().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))?.Id : null;
    private static float Width(WidgetCardSize size) => size == WidgetCardSize.Medium ? WidgetBoardLayout.MediumWidth : WidgetBoardLayout.SmallWidth;
    // The card caption shows a short numeric offset instead of the long installed-zone display name.
    private static string ZoneCaption(TimeZoneInfo zone, DateTimeOffset instant)
    {
        TimeSpan offset = zone.GetUtcOffset(instant), magnitude = offset.Duration();
        return $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{magnitude.Hours:00}:{magnitude.Minutes:00}";
    }
    // One quiet caption line: "10月19日 · 周一", a "yyyy年" prefix only outside the current
    // year, and the configured long/short date and weekday forms.
    private static string DateCaption(DateWidgetSettings options, DateTimeOffset local, DateTimeOffset utcNow)
    {
        var culture = CultureInfo.CurrentCulture;
        string date = local.ToString(options.DateFormat == "short" ? "M/d" : "M月d日", culture);
        if (local.Year != TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.Local).Year)
            date = local.ToString("yyyy年", culture) + date;
        return options.ShowWeekday
            ? date + " · " + local.ToString(options.WeekdayFormat == "short" ? "ddd" : "dddd", culture)
            : date;
    }

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
            bool medium = request.Size == WidgetCardSize.Medium;
            // Two text layers only: one value line and one quiet offset caption. A 12-hour
            // suffix or seconds would not fit the small card at the 36 DIP display size, so
            // those instances keep the narrower details-title role inside the same slot.
            var role = !options.Use24Hour || !medium && options.ShowSeconds
                ? TextRole.MediaDetailsTitle : TextRole.WidgetClock;
            string value = local.ToString(format, culture);
            if (!options.Use24Hour) value += " " + local.ToString("tt", culture);
            var elements = new List<WidgetElement>
            {
                new(WidgetElementKind.Text, medium ? new(12, 34, 140, 44) : new(12, 34, width, 44),
                    value, role, WidgetNumericStyle.Tabular),
                new(WidgetElementKind.Text, medium ? new(160, 46, 140, 20) : new(12, 112, width, 16),
                    ZoneCaption(zone, request.UtcNow), medium ? TextRole.Body : TextRole.WidgetLabel)
            };
            long interval = options.ShowSeconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
            long ticks = request.UtcNow.UtcTicks;
            DateTimeOffset? next = ticks > DateTimeOffset.MaxValue.UtcTicks - interval ? null : new(ticks - ticks % interval + interval, TimeSpan.Zero);
            // Display text stays culture-formatted; the structured sample is what the clock face
            // consumes so localized separators and the 12-hour period never need re-parsing.
            return ValueTask.FromResult(new WidgetSnapshot(Array.AsReadOnly(elements.ToArray()), next)
            {
                Clock = new(local.Hour, local.Minute, options.ShowSeconds ? local.Second : 0, options.ShowSeconds,
                    !options.Use24Hour, options.Use24Hour ? null : local.ToString("tt", culture))
            });
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
            bool medium = request.Size == WidgetCardSize.Medium;
            // Two text layers only: the day number and one caption line.
            var elements = new List<WidgetElement>
            {
                new(WidgetElementKind.Text, medium ? new(12, 28, 140, 56) : new(12, 28, width, 56),
                    local.ToString("dd", culture), TextRole.WidgetDateValue, WidgetNumericStyle.Tabular),
                new(WidgetElementKind.Text, medium ? new(160, 46, 140, 20) : new(12, 96, width, 20),
                    DateCaption(options, local, request.UtcNow), TextRole.Body)
            };
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
