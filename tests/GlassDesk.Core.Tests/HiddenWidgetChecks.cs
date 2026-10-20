using GlassDesk.Core;

/// <summary>Hidden-instance and capacity checks (filled by the hidden-widget workstream).</summary>
static class HiddenWidgetChecks
{
    public static void Run(Action<bool, string> check) =>
        check(true, "hidden widget checks placeholder");
}
