using GlassDesk.Core;

/// <summary>Structured provider clock sample checks (filled by the clock workstream).</summary>
static class ClockStructuredChecks
{
    public static void Run(Action<bool, string> check) =>
        check(true, "clock structured checks placeholder");
}
