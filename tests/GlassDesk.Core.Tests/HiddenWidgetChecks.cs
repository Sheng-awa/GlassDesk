using GlassDesk.Core;

/// <summary>Hidden-instance and capacity checks (audit #11: saved instances versus visible presentation).</summary>
static class HiddenWidgetChecks
{
    private static WidgetInstance Item(string id, string provider, int order) =>
        new(id, provider, WidgetCardSize.Small, order, WidgetBoardData.EmptySettings());

    private static WidgetBoardState Board(params WidgetInstance[] instances) =>
        new(WidgetBoardData.Version, instances);

    public static void Run(Action<bool, string> check)
    {
        var time = Item("time.default", BuiltInWidgetIds.Time, 0);
        var date = Item("date.default", BuiltInWidgetIds.Date, 1);
        var unknown = Item("plugin.local-status", "example.local-status", 2);
        var mixed = Board(time, date, unknown);

        var hidden = WidgetBoardData.HiddenForPresentation(mixed);
        check(hidden.Select(h => h.Instance.InstanceId).SequenceEqual(new[] { "date.default", "plugin.local-status" }),
            "hidden presentation lists saved out-of-scope instances in saved order");
        check(hidden.All(h => h.Reason == WidgetHiddenReason.OutsideCurrentScope)
            && ReferenceEquals(hidden[0].Instance, date) && ReferenceEquals(hidden[1].Instance, unknown),
            "hidden presentation keeps the saved instance references and reports the outside-scope reason");

        check(WidgetBoardData.Counts(mixed) == new WidgetBoardCounts(3, 1, 2, 29),
            "counts separate saved, visible, hidden and remaining capacity for a mixed board");
        check(WidgetBoardData.CanAdd(mixed)
            && WidgetBoardData.Counts(WidgetBoardData.CreateDefault()) == new WidgetBoardCounts(1, 1, 0, 31),
            "a partially filled board accepts widgets and reports its remaining saved slots");

        var hiddenDates = Enumerable.Range(0, 32).Select(n => Item("date." + n, BuiltInWidgetIds.Date, n)).ToArray();
        var fullHidden = Board(hiddenDates);
        check(WidgetBoardData.Counts(fullHidden) == new WidgetBoardCounts(32, 0, 32, 0)
            && !WidgetBoardData.CanAdd(fullHidden) && WidgetBoardData.HiddenForPresentation(fullHidden).Count == 32,
            "32 hidden instances fill saved capacity even when nothing is visible");
        var almostFull = Board(hiddenDates.Take(31).ToArray());
        check(WidgetBoardData.CanAdd(almostFull)
            && WidgetBoardData.Counts(almostFull) == new WidgetBoardCounts(31, 0, 31, 1),
            "31 saved instances leave exactly one capacity slot");
        var fullWithVisible = Board(hiddenDates.Take(31).Append(time).ToArray());
        check(!WidgetBoardData.CanAdd(fullWithVisible)
            && WidgetBoardData.Counts(fullWithVisible) == new WidgetBoardCounts(32, 1, 31, 0),
            "one visible card plus 31 hidden instances still exhausts the 32 saved slots");

        var filtered = WidgetBoardData.TimeOnlyForPresentation(mixed);
        check(filtered.Instances.Count == 1 && filtered.Instances[0].ProviderId == BuiltInWidgetIds.Time
            && filtered.Instances[0].InstanceId == "time.default" && filtered.Version == mixed.Version
            && mixed.Instances.Count == 3 && mixed.Instances.Select(i => i.InstanceId).SequenceEqual(new[] { "time.default", "date.default", "plugin.local-status" }),
            "time-only presentation filters without mutating the saved board");
        var hiddenOnly = Board(date, unknown);
        var emptyPresentation = WidgetBoardData.TimeOnlyForPresentation(hiddenOnly);
        check(emptyPresentation.Instances.Count == 0 && hiddenOnly.Instances.Count == 2
            && WidgetBoardData.HiddenForPresentation(hiddenOnly).Count == 2,
            "a board of only out-of-scope instances presents empty while keeping both saved entries");

        check(WidgetBoardData.HiddenForPresentation(Board()).Count == 0
            && WidgetBoardData.Counts(Board()) == new WidgetBoardCounts(0, 0, 0, 32)
            && WidgetBoardData.CanAdd(Board()),
            "an empty board has no hidden instances and all capacity free");

        bool removed = WidgetBoardData.TryRemove(mixed, "date.default", out var afterRemove);
        check(removed && afterRemove.Instances.Select(i => i.InstanceId).SequenceEqual(new[] { "time.default", "plugin.local-status" })
            && afterRemove.Instances.Select(i => i.Order).SequenceEqual(new[] { 0, 1 })
            && mixed.Instances.Count == 3 && mixed.Instances[1].Order == 1,
            "explicit removal drops one saved instance and renumbers the rest contiguously without touching the input");
        check(ReferenceEquals(afterRemove.Instances[0], time)
            && afterRemove.Instances[1].InstanceId == unknown.InstanceId
            && afterRemove.Instances[1].ProviderId == unknown.ProviderId
            && afterRemove.Instances[1].Settings.GetRawText() == unknown.Settings.GetRawText(),
            "removal reuses the untouched instance and carries the shifted instance data");

        check(!WidgetBoardData.TryRemove(mixed, "missing.instance", out var unchanged)
            && unchanged.Instances.Count == 3 && WidgetBoardData.HiddenForPresentation(unchanged).Count == 2,
            "removing an unknown instance reports false and leaves the board unchanged");

        var emptyBoard = Board();
        bool emptyRemoved = WidgetBoardData.TryRemove(emptyBoard, "anything", out var stillEmpty);
        check(!emptyRemoved && stillEmpty.Instances.Count == 0,
            "removing from an empty board reports false without throwing");

        bool removedLast = WidgetBoardData.TryRemove(Board(time), "time.default", out var emptied);
        check(removedLast && emptied.Instances.Count == 0 && WidgetBoardData.CanAdd(emptied)
            && WidgetBoardData.Counts(emptied) == new WidgetBoardCounts(0, 0, 0, 32),
            "removing the last saved instance yields a valid empty board");
    }
}
