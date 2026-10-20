using System.Numerics;
using System.Text.Json;

namespace GlassDesk.Core;

public enum WidgetDragRelease { NoDrag, Committed, CancelledOutside }

/// <summary>A draft transaction; an outside collapse calls Commit, while an outside drag release cancels only that drag.</summary>
public sealed class WidgetBoardEditor
{
    public const float DragThresholdDip = 6;
    private WidgetBoardState _committed;
    private List<WidgetInstance>? _draft;
    private WidgetBoardState? _beforeDrag;
    private string? _pressedId;
    private Vector2 _press;
    public bool IsEditing => _draft is not null;
    public bool IsDragging { get; private set; }
    public WidgetBoardState State => WidgetBoardData.ValidateAndClone(
        _draft is null ? _committed : new(WidgetBoardData.Version, _draft));

    public WidgetBoardEditor(WidgetBoardState state) => _committed = WidgetBoardData.ValidateAndClone(state);

    public void Begin()
    {
        if (IsEditing) return;
        _draft = _committed.Instances.ToList();
    }

    public WidgetBoardState Commit()
    {
        // A collapse during capture must not save an invalid floating drop.
        if (IsDragging) RestoreDrag();
        if (_draft is not null) _committed = WidgetBoardData.ValidateAndClone(new(WidgetBoardData.Version, _draft));
        _draft = null;
        ClearDrag();
        return State;
    }

    public WidgetBoardState Cancel()
    {
        _draft = null;
        ClearDrag();
        return State;
    }

    public void Add(string providerId, WidgetCardSize size, JsonElement settings, string? instanceId = null)
    {
        var draft = Draft();
        var candidate = draft.Append(new WidgetInstance(instanceId ?? Guid.NewGuid().ToString("N"),
            providerId, size, draft.Count, settings)).ToArray();
        _draft = WidgetBoardData.ValidateAndClone(new(WidgetBoardData.Version, candidate)).Instances.ToList();
    }

    public bool Remove(string instanceId)
    {
        var draft = Draft();
        bool removed = draft.RemoveAll(i => i.InstanceId == instanceId) != 0;
        Renumber();
        if (_pressedId == instanceId) ClearDrag();
        return removed;
    }

    /// <summary>Removes a saved instance outside an edit transaction (hidden-item management).</summary>
    public bool RemoveSaved(string instanceId)
    {
        if (_draft is not null) return Remove(instanceId);
        var remaining = _committed.Instances.Where(i => i.InstanceId != instanceId).ToArray();
        if (remaining.Length == _committed.Instances.Count) return false;
        _committed = WidgetBoardData.ValidateAndClone(new(WidgetBoardData.Version,
            remaining.Select((instance, order) => instance with { Order = order }).ToArray()));
        return true;
    }

    /// <summary>Restores a previously removed saved instance at its original index (undo).</summary>
    public bool InsertSaved(WidgetInstance instance, int index)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_draft is not null) throw new InvalidOperationException("Finish the edit transaction before restoring a saved instance.");
        if (_committed.Instances.Any(i => i.InstanceId == instance.InstanceId)) return false;
        if (_committed.Instances.Count >= WidgetBoardData.MaximumInstances) return false;
        var list = _committed.Instances.ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), instance);
        _committed = WidgetBoardData.ValidateAndClone(new(WidgetBoardData.Version,
            list.Select((item, order) => item with { Order = order }).ToArray()));
        return true;
    }

    public void Configure(string instanceId, JsonElement settings) => Replace(instanceId,
        i => i with { Settings = WidgetBoardData.CloneSettings(settings) });

    public void Resize(string instanceId, WidgetCardSize size)
    {
        if (!Enum.IsDefined(size)) throw new ArgumentOutOfRangeException(nameof(size));
        Replace(instanceId, i => i with { Size = size });
    }

    public void Reorder(string instanceId, int targetIndex)
    {
        var draft = Draft();
        int source = draft.FindIndex(i => i.InstanceId == instanceId);
        if (source < 0) throw new KeyNotFoundException(instanceId);
        if (targetIndex < 0 || targetIndex >= draft.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
        var item = draft[source];
        draft.RemoveAt(source); draft.Insert(targetIndex, item); Renumber();
    }

    public void PointerDown(string instanceId, Vector2 position)
    {
        if (!Draft().Any(i => i.InstanceId == instanceId)) throw new KeyNotFoundException(instanceId);
        CheckPoint(position); CancelDrag();
        _pressedId = instanceId; _press = position; _beforeDrag = State;
    }

    /// <returns>True only after movement exceeds 6 DIP, at which point the host may capture the pointer.</returns>
    public bool PointerMove(Vector2 position, int? targetIndex = null)
    {
        CheckPoint(position);
        if (_pressedId is null) return false;
        if (!IsDragging && Vector2.DistanceSquared(_press, position) > DragThresholdDip * DragThresholdDip)
            IsDragging = true;
        if (IsDragging && targetIndex.HasValue) Reorder(_pressedId, targetIndex.Value);
        return IsDragging;
    }

    public WidgetDragRelease PointerUp(Vector2 position, FloatRect boardBounds, int? targetIndex = null)
    {
        bool moved = PointerMove(position);
        if (!moved) { ClearDrag(); return WidgetDragRelease.NoDrag; }
        if (!boardBounds.Contains(position.X, position.Y))
        { RestoreDrag(); ClearDrag(); return WidgetDragRelease.CancelledOutside; }
        if (targetIndex.HasValue && _pressedId is not null) Reorder(_pressedId, targetIndex.Value);
        ClearDrag(); return WidgetDragRelease.Committed;
    }

    public void CancelDrag() { RestoreDrag(); ClearDrag(); }
    private List<WidgetInstance> Draft() => _draft ?? throw new InvalidOperationException("Begin an edit transaction first.");
    private void Replace(string id, Func<WidgetInstance, WidgetInstance> update)
    {
        var draft = Draft(); int index = draft.FindIndex(i => i.InstanceId == id);
        if (index < 0) throw new KeyNotFoundException(id);
        draft[index] = update(draft[index]);
    }
    private void Renumber() { for (int i = 0; _draft is not null && i < _draft.Count; i++) _draft[i] = _draft[i] with { Order = i }; }
    private void RestoreDrag() { if (_beforeDrag is not null && _draft is not null) _draft = _beforeDrag.Instances.ToList(); }
    private void ClearDrag() { _pressedId = null; _beforeDrag = null; IsDragging = false; }
    private static void CheckPoint(Vector2 point)
    { if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point)); }
}
