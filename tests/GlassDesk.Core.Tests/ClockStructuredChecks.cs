using System.Globalization;
using GlassDesk.Core;

/// <summary>Structured clock sample checks (audit #04 localized separators, #05 12-hour period).</summary>
static class ClockStructuredChecks
{
    public static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();

    private static async Task<WidgetSnapshot> SampleAsync(IWidgetProvider provider, WidgetCardSize size,
        DateTimeOffset utcNow, TimeWidgetSettings settings) =>
        await provider.GetSnapshotAsync(new("clock.check", size, utcNow,
            BuiltInWidgetProviders.ToJson(settings)), default).ConfigureAwait(false);

    private static async Task RunAsync(Action<bool, string> check)
    {
        var time = BuiltInWidgetProviders.Catalog.Single(p => p.Id == BuiltInWidgetIds.Time).Provider;
        var date = BuiltInWidgetProviders.Catalog.Single(p => p.Id == BuiltInWidgetIds.Date).Provider;
        // Windows ships an installed UTC zone; when it is absent every expectation is derived from
        // the same zone conversion the provider performs instead of hard-coding local values.
        var zone = BuiltInWidgetProviders.InstalledTimeZones()
            .FirstOrDefault(t => string.Equals(t.Id, "UTC", StringComparison.OrdinalIgnoreCase)) ?? TimeZoneInfo.Local;
        bool utc = string.Equals(zone.Id, "UTC", StringComparison.OrdinalIgnoreCase);
        string zoneId = zone.Id;
        var instant = new DateTimeOffset(2026, 10, 19, 5, 24, 37, TimeSpan.Zero);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        int hour = local.Hour, minute = local.Minute, second = local.Second;
        int hour12 = hour % 12 == 0 ? 12 : hour % 12;

        var plain = await SampleAsync(time, WidgetCardSize.Small, instant, new(true, false, zoneId)).ConfigureAwait(false);
        check(plain.Clock is { } plainClock
            && plainClock.Hour24 == hour && plainClock.Minute == minute && plainClock.Second == 0
            && !plainClock.HasSeconds && !plainClock.TwelveHour && plainClock.PeriodText is null,
            "24-hour clock sample without seconds carries hour/minute and no period");
        check(plain.Elements[0].Text == (utc ? "05:24" : FormattableString.Invariant($"{hour:00}:{minute:00}")),
            "24-hour clock text stays a display string only (05:24 in UTC)");

        var seconds = await SampleAsync(time, WidgetCardSize.Small, instant, new(true, true, zoneId)).ConfigureAwait(false);
        check(seconds.Clock is { } secondClock && secondClock.HasSeconds && secondClock.Second == second
            && secondClock.Hour24 == hour && secondClock.Minute == minute
            && !secondClock.TwelveHour && secondClock.PeriodText is null,
            "24-hour clock sample with seconds carries the live second and still no period");
        check(seconds.Elements[0].Text == FormattableString.Invariant($"{hour:00}:{minute:00}:{second:00}")
            && seconds.NextRefreshUtc == new DateTimeOffset(2026, 10, 19, 5, 24, 38, TimeSpan.Zero),
            "seconds clock text includes ss and refreshes at the next whole second");

        check(new WidgetSnapshot(Array.Empty<WidgetElement>()).Clock is null,
            "positional snapshot construction stays compatible and defaults to no structured clock");
        check(new WidgetClockSample(0, 24, 37, true, false, null).Hour12 == 12
            && new WidgetClockSample(12, 24, 37, true, false, null).Hour12 == 12
            && new WidgetClockSample(13, 24, 37, true, false, null).Hour12 == 1
            && new WidgetClockSample(23, 24, 37, true, false, null).Hour12 == 11,
            "clock sample Hour12 maps 00/12/13/23 to 12/12/1/11");

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var twelve = await SampleAsync(time, WidgetCardSize.Medium, instant, new(false, true, zoneId)).ConfigureAwait(false);
            check(twelve.Clock is { TwelveHour: true } twelveClock
                && twelveClock.Hour24 == hour && twelveClock.Hour12 == hour12
                && twelveClock.PeriodText is { Length: > 0 } period
                && period == local.ToString("tt", CultureInfo.CurrentCulture)
                && twelve.Elements[0].Text is { } twelveText
                && (twelveText.EndsWith(" AM", StringComparison.Ordinal) || twelveText.EndsWith(" PM", StringComparison.Ordinal)),
                "en-US 12-hour sample keeps Hour24 and a non-empty AM/PM period");
            var afternoon = new DateTimeOffset(2026, 10, 19, 13, 24, 37, TimeSpan.Zero);
            var afternoonLocal = TimeZoneInfo.ConvertTime(afternoon, zone);
            var afternoonSnapshot = await SampleAsync(time, WidgetCardSize.Medium, afternoon, new(false, true, zoneId))
                .ConfigureAwait(false);
            string designator = afternoonLocal.ToString("tt", CultureInfo.CurrentCulture);
            check(afternoonSnapshot.Clock is { TwelveHour: true } afternoonClock
                && afternoonClock.Hour24 == afternoonLocal.Hour
                && afternoonClock.Hour12 == (afternoonLocal.Hour % 12 == 0 ? 12 : afternoonLocal.Hour % 12)
                && afternoonClock.PeriodText == designator
                && afternoonSnapshot.Elements[0].Text is { } afternoonText
                && afternoonText.EndsWith(" " + designator, StringComparison.Ordinal),
                "12-hour PM sample preserves the afternoon designator in data and display text");
            CultureInfo.CurrentCulture = new CultureInfo("zh-CN");
            var chinese = await SampleAsync(time, WidgetCardSize.Medium, instant, new(false, true, zoneId)).ConfigureAwait(false);
            check(chinese.Clock is { TwelveHour: true } chineseClock
                && chineseClock.PeriodText is { Length: > 0 } chinesePeriod
                && chinesePeriod == local.ToString("tt", CultureInfo.CurrentCulture)
                && chinese.Elements[0].Text is { } chineseText
                && chineseText.EndsWith(" " + chinesePeriod, StringComparison.Ordinal),
                "zh-CN 12-hour sample carries its localized period (上午/下午)");
        }
        finally { CultureInfo.CurrentCulture = previous; }

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            var finnish = await SampleAsync(time, WidgetCardSize.Small, instant, new(true, true, zoneId)).ConfigureAwait(false);
            check(finnish.Clock is { } finnishClock && finnishClock.Hour24 == hour && finnishClock.Minute == minute
                && finnishClock.Second == second && finnishClock.HasSeconds
                && !finnishClock.TwelveHour && finnishClock.PeriodText is null,
                "fi-FI clock sample keeps the same structured hour/minute/second");
            check(finnish.Elements[0].Text is { } finnishText && finnishText.Contains('.')
                && finnishText.IndexOf(':') < 0,
                "fi-FI dot-separated display text never reaches the clock face as parsed data");
        }
        finally { CultureInfo.CurrentCulture = previous; }

        var dateSnapshot = await date.GetSnapshotAsync(new("clock.date", WidgetCardSize.Small, instant,
            BuiltInWidgetProviders.ToJson(new DateWidgetSettings(TimeZoneId: zoneId))), default).ConfigureAwait(false);
        check(dateSnapshot.Clock is null, "date provider is untouched and never reports a structured clock");
    }
}
