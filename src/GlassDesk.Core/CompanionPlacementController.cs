using System.Numerics;

namespace GlassDesk.Core;

public enum CompanionCoverEdge { Top, Right, Bottom, Left }

/// <summary>Actual layout in widget-local DIPs, including text, hit targets, waveforms and popups.</summary>
public sealed record CompanionPlacementLayout(WidgetForm Form, FloatRect OuterBounds, float OuterCornerRadius,
    float BodySize, Vector2 PreferredAnchor, IReadOnlyList<FloatRect> ProtectedRegions,
    IReadOnlyList<HideAnchor>? CoverAnchors = null);

/// <summary>
/// One character's placement, local hiding and finite movement schedule. The host calls
/// Tick only during NeedsTick, and wakes once at NextWakeTime while resting. All times
/// are from the same monotonic millisecond clock; this class owns no timer or callback.
/// </summary>
public sealed class CompanionPlacementController
{
    public const float GridSize = 8, SafetyGap = 8;
    readonly Random _random;
    CompanionPlacementLayout? _layout;
    string _appearance = CompanionAppearanceIds.Original;
    bool _enabled = true, _visible = true, _reducedMotion, _initialized, _revealPressed;
    PointerCaptureOwner _capture;
    long _openSequence = -1;
    bool _introConsumed = true;
    List<Vector2>? _path;
    CompanionPathMotion? _motion;
    CompanionPathMotion? _liquidMotion;
    int _segmentIndex;
    double? _wake;
    HideAnchor? _occluder;
    CompanionPlacementMode _arrivalMode;
    public CompanionPose Pose { get; private set; }
    public Vector2 Velocity { get; private set; }
    public CompanionLiquidFrame LiquidFrame { get; private set; } = CompanionLiquidFrame.Rest;
    public HideAnchor? ActiveOccluder => _occluder;
    public FloatRect? RevealHitRegion => Available && _path is null && Pose.PlacementMode == CompanionPlacementMode.CoverPeek &&
        _occluder is { } hidden && Vector2.DistanceSquared(Pose.BodyAnchor, hidden.BodyAnchor) < .01f ? hidden.RevealHitRegion : null;
    public bool NeedsTick => Available && _capture == PointerCaptureOwner.None && _path is not null;
    public bool LiquidMotionActive => Available && _capture == PointerCaptureOwner.None && _liquidMotion is not null;
    public double? NextWakeTime => CanIdle && _path is null ? _wake : null;
    public long OpenSequence => _openSequence;
    public bool IntroConsumed => _introConsumed;
    public IReadOnlyList<Vector2> PlannedPath => _path?.AsReadOnly() ?? (IReadOnlyList<Vector2>)Array.Empty<Vector2>();
    bool Available => _enabled && _visible;
    bool CanIdle => Available && !_reducedMotion && _capture == PointerCaptureOwner.None && !_revealPressed &&
        _layout?.Form is WidgetForm.Peek or WidgetForm.Details && Pose.PlacementMode == CompanionPlacementMode.Front;

    public CompanionPlacementController(int? randomSeed = null)
        => _random = randomSeed is { } seed ? new Random(seed) : new Random();

    public void UpdateLayout(CompanionPlacementLayout layout, double now)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout = layout with { BodySize = Math.Clamp(layout.BodySize, 16, 96) };
        if (_layout is { } existing && SameLayout(existing, layout)) return;
        TickMotionOnly(now);
        WidgetForm? previousForm = _layout?.Form;
        HideAnchor? previousOccluder = _occluder;
        // Snapshot the host's mutable layout buffers. Old cover identities cannot
        // survive a track replacement even when their bounds happen to match.
        _layout = layout with { ProtectedRegions = layout.ProtectedRegions.ToArray(), CoverAnchors = layout.CoverAnchors?.ToArray() };
        bool keepHide = _occluder is { } hidden && (
            Pose.PlacementMode == CompanionPlacementMode.NavigationIntro && layout.Form == WidgetForm.Details ||
            _layout.CoverAnchors?.Any(a => a == hidden) == true) &&
            (Pose.PlacementMode == CompanionPlacementMode.NavigationIntro ? ValidIntro(hidden) : ValidHide(hidden));
        bool invalidatedOccluder = _occluder is not null && !keepHide;
        if (!keepHide) { _occluder = null; _revealPressed = false; }
        if (_path is not null && previousForm == layout.Form && !invalidatedOccluder && RemainingPathSafe())
        {
            SetPose(Pose.BodyAnchor, Pose.PlacementMode, Pose.ShapePose);
            return;
        }
        bool wasMoving = _path is not null;
        StopMotion();
        if (invalidatedOccluder && previousForm == layout.Form && previousOccluder is { } retired && !IsSafe(Pose.BodyAnchor))
        {
            // A track replacement at the same cover geometry retires its old
            // identity immediately, then exits locally through the new cover.
            // It never jumps across another content island to clear the clip.
            var replacement = _layout.CoverAnchors?.FirstOrDefault(a => a.OccluderBounds == retired.OccluderBounds);
            if (replacement is not null)
            {
                _occluder = replacement;
                if (_capture != PointerCaptureOwner.None)
                {
                    SetPose(Pose.BodyAnchor, CompanionPlacementMode.Revealing, CompanionShapePose.Peek); _wake = null;
                }
                else CancelOcclusion(now);
                return;
            }
        }
        if (!_initialized || layout.Form == WidgetForm.Capsule)
        {
            _initialized = true; _occluder = null;
            Vector2 anchor = IsSafe(layout.PreferredAnchor) ? layout.PreferredAnchor : NearestSafe(layout.PreferredAnchor) ?? layout.PreferredAnchor;
            SetPose(anchor, CompanionPlacementMode.Front, CompanionShapePose.Rounded);
        }
        else if (keepHide && !wasMoving) SetPose(_occluder!.BodyAnchor, Pose.PlacementMode, CompanionShapePose.Peek);
        else
        {
            _occluder = null;
            Vector2 anchor = Pose.BodyAnchor;
            if (!IsSafe(anchor)) anchor = NearestSafe(anchor) ?? layout.PreferredAnchor;
            SetPose(anchor, CompanionPlacementMode.Front, CompanionShapePose.Rounded);
        }
        Schedule(now);
    }

    public void SetAppearance(string appearanceId, double now)
    {
        string normalized = CompanionAppearanceIds.Normalize(appearanceId);
        if (_appearance == normalized) return;
        TickMotionOnly(now);
        _appearance = normalized;
        // Every placement already reserves both appearances. This changes only
        // render bounds; no path, idle deadline or body anchor is reset.
        SetPose(Pose.BodyAnchor, Pose.PlacementMode, Pose.ShapePose);
    }

    public void SetAvailability(bool enabled, bool visible, bool reducedMotion, double now)
    {
        if (_enabled == enabled && _visible == visible && _reducedMotion == reducedMotion) return;
        Tick(now);
        bool reenabled = !_enabled && enabled;
        _enabled = enabled; _visible = visible; _reducedMotion = reducedMotion;
        if (!Available || reducedMotion)
        {
            bool partialHide = _path is not null && _occluder is not null && Pose.PlacementMode != CompanionPlacementMode.NavigationIntro;
            StopMotion(); _wake = null; _revealPressed = false;
            SetPose(Pose.BodyAnchor, partialHide ? CompanionPlacementMode.Revealing : Pose.PlacementMode, CompanionShapePose.Rounded);
        }
        // Enabling inside an existing Details cycle never replays its intro.
        if (reenabled)
        {
            _introConsumed = true; CancelOcclusion(now);
        }
        else if (Available && Pose.PlacementMode == CompanionPlacementMode.Revealing && _path is null) CancelOcclusion(now);
        else Schedule(now);
    }

    public void SetCapture(PointerCaptureOwner owner, double now)
    {
        if (_capture == owner) return;
        Tick(now); _capture = owner;
        if (owner != PointerCaptureOwner.None)
        {
            bool partialHide = _path is not null && _occluder is not null && Pose.PlacementMode != CompanionPlacementMode.NavigationIntro;
            StopMotion(); _wake = null;
            if (partialHide) SetPose(Pose.BodyAnchor, CompanionPlacementMode.Revealing, CompanionShapePose.Peek);
        }
        else if (Pose.PlacementMode == CompanionPlacementMode.Revealing && _path is null) CancelOcclusion(now);
        else Schedule(now);
    }

    public void BeginDetailsIntro(long openSequence, HideAnchor? anchor, double now)
    {
        if (openSequence <= _openSequence) return;
        Tick(now); _openSequence = openSequence; _introConsumed = !Available;
        _revealPressed = false; StopMotion(); _wake = null;
        if (!_introConsumed && anchor is not null && _layout?.Form == WidgetForm.Details && ValidIntro(anchor))
        {
            _occluder = anchor;
            SetPose(anchor.BodyAnchor, CompanionPlacementMode.NavigationIntro, _reducedMotion ? CompanionShapePose.Rounded : CompanionShapePose.Peek);
        }
        else { _introConsumed = true; CancelOcclusion(now); }
    }

    public bool LeaveNavigationIntro(long openSequence, double now)
    {
        if (openSequence != _openSequence || _introConsumed) return false;
        _introConsumed = true;
        CancelOcclusion(now);
        return true;
    }

    public bool TryCaptureReveal(Vector2 pointer, long openSequence)
    {
        // Keep the exposed input area owned until the escape finishes. A second
        // complete click must not fall through to the Peek body's expand action.
        FloatRect? inputRegion = RevealHitRegion ?? (Available && Pose.PlacementMode == CompanionPlacementMode.Revealing
            && _occluder is { } revealing ? revealing.RevealHitRegion : null);
        if (!Available || _capture != PointerCaptureOwner.None || _revealPressed || openSequence != _openSequence ||
            inputRegion is not { } hit || !hit.Contains(pointer.X, pointer.Y)) return false;
        _revealPressed = true; _wake = null;
        return true;
    }

    /// <summary>Consumes the captured click even when canceled or invalidated by layout.</summary>
    public bool CompleteReveal(long openSequence, double now, bool canceled = false)
    {
        if (!_revealPressed) return false;
        _revealPressed = false;
        if (openSequence != _openSequence || canceled || !Available) return true;
        Reveal(now);
        return true;
    }

    public void Reveal(double now)
    {
        if (Pose.PlacementMode is not (CompanionPlacementMode.CoverPeek or CompanionPlacementMode.Revealing)) return;
        CancelOcclusion(now);
    }

    public bool Tick(double now)
    {
        CompanionPose before = Pose;
        if (NeedsTick && _path is { } path && _motion is { } motion)
        {
            var sample = motion.Sample(now); _segmentIndex = sample.SegmentIndex; Velocity = sample.Velocity;
            SetPose(sample.Position, Pose.PlacementMode, _occluder is not null && CoverEdge(_occluder) is not null ? CompanionShapePose.Peek :
                sample.Progress < .18f ? CompanionShapePose.Stretch : sample.Progress > .82f ? CompanionShapePose.Squash : CompanionShapePose.Droplet);
            if (sample.Complete)
            {
                SetPose(path[^1], _arrivalMode, _arrivalMode == CompanionPlacementMode.CoverPeek ? CompanionShapePose.Peek : CompanionShapePose.Rounded);
                StopMotion(keepLiquid: true); Schedule(now);
            }
        }
        else if (CanIdle && _wake is { } wake && now >= wake)
        {
            _wake = null;
            if (_layout?.CoverAnchors is { Count: > 0 } covers && _random.NextDouble() < .30)
            {
                if (!TryHideAvailable(now)) TryRandomMove(now);
            }
            else TryRandomMove(now);
            if (_path is null) Schedule(now);
        }
        if (_liquidMotion is { } liquid)
        {
            LiquidFrame = CompanionLiquidMotion.Sample(liquid, now, _layout?.BodySize ?? 24, (a, b) => SegmentSafe(a, b, null));
            if (!LiquidFrame.Active) _liquidMotion = null;
        }
        return before != Pose || LiquidFrame.Active;
    }

    public bool MoveOnOpen(double now)
    {
        if (!CanIdle) return false;
        TickMotionOnly(now); TryRandomMove(now);
        return _path is not null;
    }

    /// <summary>Explicit safe destination, useful for host requests and deterministic checks.</summary>
    public bool TryMoveTo(Vector2 target, double now)
    {
        if (!CanIdle || !IsSafe(target)) return false;
        TickMotionOnly(now);
        var path = Plan(Pose.BodyAnchor, target, null);
        if (path is null) return false;
        _occluder = null; Start(path, CompanionPlacementMode.Front, now);
        return true;
    }

    public bool TryHide(HideAnchor anchor, double now)
    {
        if (!CanIdle || !ValidHide(anchor) || _layout?.CoverAnchors?.Contains(anchor) != true) return false;
        TickMotionOnly(now);
        Vector2 staging = GetCoverStagingAnchor(anchor, _layout.BodySize);
        var path = Plan(Pose.BodyAnchor, staging, null);
        if (path is null || !SegmentSafe(staging, anchor.BodyAnchor, anchor.OccluderBounds)) return false;
        path.Add(anchor.BodyAnchor); _occluder = anchor;
        // Clip while entering as well as while resting; only this registered cover
        // is exempted, and its replacement cancels the entire occlusion relation.
        Start(path, CompanionPlacementMode.CoverPeek, now);
        return true;
    }

    /// <summary>Randomizes candidates, then checks every candidate until one is reachable in the current component.</summary>
    public bool TryHideAvailable(double now)
    {
        if (!CanIdle || _layout?.CoverAnchors is not { Count: > 0 } covers) return false;
        int first = _random.Next(covers.Count);
        for (int i = 0; i < covers.Count; i++) if (TryHide(covers[(first + i) % covers.Count], now)) return true;
        return false;
    }

    /// <summary>Top-edge peek: both eyes and at least 28×20 DIP remain above the cover.</summary>
    public static HideAnchor CreateCoverPeek(string stableId, FloatRect cover, float radius, float bodySize)
        => CreateCoverPeek(stableId, cover, radius, bodySize, CompanionCoverEdge.Top);

    /// <summary>Choose an actual free edge; tangentFraction runs left-to-right or top-to-bottom along it.</summary>
    public static HideAnchor CreateCoverPeek(string stableId, FloatRect cover, float radius, float bodySize, CompanionCoverEdge edge, float tangentFraction = .5f)
    {
        FloatRect eyes = CompanionContour.Union(CompanionContour.EyeBounds(Vector2.Zero, bodySize, CompanionShapePose.Rounded),
            CompanionContour.EyeBounds(Vector2.Zero, bodySize, CompanionShapePose.Peek));
        float fraction = Math.Clamp(tangentFraction, 0, 1);
        Vector2 anchor = edge switch
        {
            CompanionCoverEdge.Right => new(cover.Right - eyes.X + .5f, cover.Y + cover.Height * fraction),
            CompanionCoverEdge.Bottom => new(cover.X + cover.Width * fraction, cover.Bottom - eyes.Y + .5f),
            CompanionCoverEdge.Left => new(cover.X - eyes.Right - .5f, cover.Y + cover.Height * fraction),
            _ => new(cover.X + cover.Width * fraction, cover.Y - eyes.Bottom - .5f)
        };
        eyes = eyes with { X = eyes.X + anchor.X, Y = eyes.Y + anchor.Y };
        float width = Math.Max(28, eyes.Width + 4), height = Math.Max(20, eyes.Height + 4);
        FloatRect hit = new(eyes.X - 2, eyes.Y - 2, width, height);
        hit = edge switch
        {
            CompanionCoverEdge.Right => hit with { X = Math.Max(cover.Right, hit.X) },
            CompanionCoverEdge.Bottom => hit with { Y = Math.Max(cover.Bottom, hit.Y) },
            CompanionCoverEdge.Left => hit with { X = Math.Min(cover.X - width, hit.X) },
            _ => hit with { Y = Math.Min(cover.Y - height, hit.Y) }
        };
        return new(stableId, cover, radius, anchor, eyes, hit);
    }

    public static CompanionCoverEdge? CoverEdge(HideAnchor anchor)
    {
        FloatRect eyes = anchor.VisibleEyeRegion, cover = anchor.OccluderBounds;
        if (eyes.Bottom <= cover.Y + .01f) return CompanionCoverEdge.Top;
        if (eyes.X >= cover.Right - .01f) return CompanionCoverEdge.Right;
        if (eyes.Y >= cover.Bottom - .01f) return CompanionCoverEdge.Bottom;
        if (eyes.Right <= cover.X + .01f) return CompanionCoverEdge.Left;
        return null;
    }
    public static Vector2 CoverNormal(CompanionCoverEdge edge) => edge switch
    {
        CompanionCoverEdge.Top => -Vector2.UnitY, CompanionCoverEdge.Right => Vector2.UnitX,
        CompanionCoverEdge.Bottom => Vector2.UnitY, _ => -Vector2.UnitX
    };
    public static Vector2 GetCoverStagingAnchor(HideAnchor anchor, float bodySize)
    {
        FloatRect e = CompanionContour.PlacementBounds(Vector2.Zero, bodySize), cover = anchor.OccluderBounds;
        return CoverEdge(anchor) switch
        {
            CompanionCoverEdge.Right => new(cover.Right - e.X + SafetyGap + .1f, anchor.BodyAnchor.Y),
            CompanionCoverEdge.Bottom => new(anchor.BodyAnchor.X, cover.Bottom - e.Y + SafetyGap + .1f),
            CompanionCoverEdge.Left => new(cover.X - e.Right - SafetyGap - .1f, anchor.BodyAnchor.Y),
            _ => new(anchor.BodyAnchor.X, cover.Y - e.Bottom - SafetyGap - .1f)
        };
    }

    /// <summary>Navigation owns intro input; the same avatar exposes complete eyes inside its registered shell.</summary>
    public static HideAnchor CreateNavigationIntro(string stableId, FloatRect navigationAndBadge, float radius, Vector2 anchor, float bodySize)
    {
        FloatRect eyes = CompanionContour.Union(CompanionContour.EyeBounds(anchor, bodySize, CompanionShapePose.Peek),
            CompanionContour.EyeBounds(anchor, bodySize, CompanionShapePose.Rounded));
        return new(stableId, navigationAndBadge, radius, anchor, eyes, eyes.Inflate(2));
    }

    public bool IsSafe(Vector2 anchor) => Safe(anchor, null);
    public bool IsPathSafe(IReadOnlyList<Vector2> path)
    {
        if (path.Count == 0) return false;
        for (int i = 0; i < path.Count; i++)
            if (!IsSafe(path[i]) || i > 0 && !SegmentSafe(path[i - 1], path[i], null)) return false;
        return true;
    }

    void TickMotionOnly(double now)
    {
        double? saved = _wake; bool moving = _path is not null;
        _wake = null; Tick(now);
        // Finishing a movement creates a fresh idle deadline. Preserve it;
        // suppress only an idle wake caused by a configuration/layout call.
        if (!moving) _wake = saved;
    }
    void CancelOcclusion(double now)
    {
        TickMotionOnly(now); StopMotion();
        HideAnchor? hidden = _occluder; _occluder = null;
        if (_layout is null) return;
        if (hidden is not null)
        {
            Vector2 staging = GetCoverStagingAnchor(hidden, _layout.BodySize);
            if (!_reducedMotion && Available && _capture is PointerCaptureOwner.None or PointerCaptureOwner.CompanionReveal && IsSafe(staging) && SegmentSafe(Pose.BodyAnchor, staging, hidden.OccluderBounds))
            {
                _occluder = hidden;
                Start([Pose.BodyAnchor, staging], CompanionPlacementMode.Front, now);
                SetPose(Pose.BodyAnchor, CompanionPlacementMode.Revealing, CompanionShapePose.Peek);
                return;
            }
        }
        Vector2 safe = IsSafe(Pose.BodyAnchor) ? Pose.BodyAnchor : NearestSafe(Pose.BodyAnchor) ?? _layout.PreferredAnchor;
        if (hidden is not null && !_reducedMotion && Available &&
            _capture is PointerCaptureOwner.None or PointerCaptureOwner.CompanionReveal &&
            IsSafe(safe) && Plan(Pose.BodyAnchor, safe, hidden.OccluderBounds) is { } escape)
        {
            _occluder = hidden;
            Start(escape, CompanionPlacementMode.Front, now);
            SetPose(Pose.BodyAnchor, CompanionPlacementMode.Revealing, CompanionShapePose.Droplet);
            return;
        }
        SetPose(safe, CompanionPlacementMode.Front, CompanionShapePose.Rounded); Schedule(now);
    }

    void TryRandomMove(double now)
    {
        if (_layout is null) return;
        Grid grid = MakeGrid();
        int start = grid.FindStart(Pose.BodyAnchor, this);
        if (start < 0) return;
        List<int> reachable = grid.Component(start, this);
        int first = reachable.Count > 0 ? _random.Next(reachable.Count) : 0;
        for (int attempt = 0; attempt < reachable.Count; attempt++)
        {
            Vector2 point = grid.Point(reachable[(first + attempt) % reachable.Count]);
            Vector2 jitter = point + new Vector2((float)(_random.NextDouble() - .5) * 6.4f, (float)(_random.NextDouble() - .5) * 6.4f);
            foreach (Vector2 target in new[] { jitter, point })
            {
                if (Vector2.DistanceSquared(Pose.BodyAnchor, target) < 64 || !IsSafe(target)) continue;
                var path = Plan(Pose.BodyAnchor, target, null);
                if (path is not null) { Start(path, CompanionPlacementMode.Front, now); return; }
            }
        }
    }

    void Start(List<Vector2> path, CompanionPlacementMode arrival, double now)
    {
        for (int i = path.Count - 1; i > 0; i--)
            if (Vector2.DistanceSquared(path[i], path[i - 1]) < .0001f) path.RemoveAt(i);
        if (path.Count < 2) { Schedule(now); return; }
        Vector2 velocity = Velocity;
        // Preserve a short forward tangent when safe. A newly arrived obstacle
        // wins over velocity continuity; motion never crosses it to hide a snap.
        if (velocity.LengthSquared() > 1 && arrival == CompanionPlacementMode.Front)
        {
            Vector2 lead = path[0] + Vector2.Normalize(velocity) * Math.Min(6, velocity.Length() * .04f);
            if (Vector2.DistanceSquared(lead, path[1]) > .0001f && SegmentSafe(path[0], lead, null) && SegmentSafe(lead, path[1], null)) path.Insert(1, lead);
        }
        float distance = 0;
        for (int i = 1; i < path.Count; i++) distance += Vector2.Distance(path[i - 1], path[i]);
        _path = path; _motion = new(path, now, Math.Clamp(450 + distance * 1.7, 450, 900), velocity); _segmentIndex = 1;
        _liquidMotion = arrival == CompanionPlacementMode.Front && !_reducedMotion ? _motion : null;
        _arrivalMode = arrival; _wake = null;
        SetPose(Pose.BodyAnchor, arrival == CompanionPlacementMode.CoverPeek ? CompanionPlacementMode.CoverPeek : Pose.PlacementMode,
            _occluder is not null && CoverEdge(_occluder) is not null ? CompanionShapePose.Peek : CompanionShapePose.Stretch);
        if (_reducedMotion) { SetPose(path[^1], arrival, CompanionShapePose.Rounded); StopMotion(); Schedule(now); }
    }

    void StopMotion(bool keepLiquid = false)
    {
        _path = null; _motion = null; Velocity = Vector2.Zero;
        if (!keepLiquid) { _liquidMotion = null; LiquidFrame = CompanionLiquidFrame.Rest; }
    }
    void Schedule(double now)
    {
        if (Pose.PlacementMode == CompanionPlacementMode.Front) _occluder = null;
        _wake = CanIdle && _path is null ? now + 12000 + _random.NextDouble() * 8000 : null;
    }
    void SetPose(Vector2 anchor, CompanionPlacementMode mode, CompanionShapePose shape)
    {
        float size = _layout?.BodySize ?? 24;
        Pose = new(anchor, CompanionContour.FullBounds(_appearance, anchor, size), mode, _reducedMotion ? CompanionShapePose.Rounded : shape,
            _occluder?.StableId);
    }
    bool ValidHide(HideAnchor anchor)
    {
        if (_layout is null || anchor.RevealHitRegion.Width < 28 || anchor.RevealHitRegion.Height < 20 ||
            CoverEdge(anchor) is null || anchor.VisibleEyeRegion.Intersects(anchor.OccluderBounds) ||
            anchor.RevealHitRegion.Intersects(anchor.OccluderBounds)) return false;
        return Safe(anchor.BodyAnchor, anchor.OccluderBounds);
    }
    bool ValidIntro(HideAnchor anchor) => _layout is not null && _layout.ProtectedRegions.Contains(anchor.OccluderBounds) &&
        anchor.VisibleEyeRegion.Width >= 18 && anchor.VisibleEyeRegion.Height >= 10 && Safe(anchor.BodyAnchor, anchor.OccluderBounds);

    static bool SameLayout(CompanionPlacementLayout a, CompanionPlacementLayout b)
        => a.Form == b.Form && a.OuterBounds == b.OuterBounds && a.OuterCornerRadius == b.OuterCornerRadius &&
            a.BodySize == b.BodySize && a.PreferredAnchor == b.PreferredAnchor && a.ProtectedRegions.SequenceEqual(b.ProtectedRegions) &&
            (a.CoverAnchors ?? Array.Empty<HideAnchor>()).SequenceEqual(b.CoverAnchors ?? Array.Empty<HideAnchor>());

    bool RemainingPathSafe()
    {
        if (_path is null) return false;
        FloatRect? ignored = _occluder?.OccluderBounds;
        Vector2 previous = Pose.BodyAnchor;
        for (int i = _segmentIndex; i < _path.Count; i++)
        {
            if (!SegmentSafe(previous, _path[i], ignored)) return false;
            previous = _path[i];
        }
        return true;
    }
    Vector2? NearestSafe(Vector2 anchor)
    {
        Grid grid = MakeGrid(); float nearest = float.MaxValue; Vector2? result = null;
        for (int i = 0; i < grid.Count; i++) if (grid.Free[i])
        {
            float distance = Vector2.DistanceSquared(anchor, grid.Point(i));
            if (distance < nearest) { nearest = distance; result = grid.Point(i); }
        }
        return result;
    }
    bool Safe(Vector2 anchor, FloatRect? ignored)
    {
        if (_layout is null || !float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y)) return false;
        FloatRect footprint = CompanionContour.PlacementBounds(anchor, _layout.BodySize);
        FloatRect outer = _layout.OuterBounds;
        if (!InsideRounded(footprint.X, footprint.Y, outer, _layout.OuterCornerRadius) ||
            !InsideRounded(footprint.Right, footprint.Y, outer, _layout.OuterCornerRadius) ||
            !InsideRounded(footprint.X, footprint.Bottom, outer, _layout.OuterCornerRadius) ||
            !InsideRounded(footprint.Right, footprint.Bottom, outer, _layout.OuterCornerRadius)) return false;
        foreach (FloatRect region in _layout.ProtectedRegions)
            if (region != ignored && footprint.Intersects(region.Inflate(SafetyGap))) return false;
        return true;
    }
    static bool InsideRounded(float x, float y, FloatRect rect, float radius)
    {
        if (x < rect.X || x > rect.Right || y < rect.Y || y > rect.Bottom) return false;
        float r = Math.Clamp(radius, 0, Math.Min(rect.Width, rect.Height) / 2);
        float cx = Math.Clamp(x, rect.X + r, rect.Right - r), cy = Math.Clamp(y, rect.Y + r, rect.Bottom - r);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r + .001f;
    }
    bool SegmentSafe(Vector2 a, Vector2 b, FloatRect? ignored)
    {
        if (!Safe(a, ignored) || !Safe(b, ignored) || _layout is null) return false;
        FloatRect e = CompanionContour.PlacementBounds(Vector2.Zero, _layout.BodySize);
        foreach (FloatRect rect in _layout.ProtectedRegions)
        {
            if (rect == ignored) continue;
            FloatRect expanded = new(rect.X - SafetyGap - e.Right, rect.Y - SafetyGap - e.Bottom,
                rect.Width + 2 * SafetyGap + e.Width, rect.Height + 2 * SafetyGap + e.Height);
            if (LineIntersects(a, b, expanded)) return false;
        }
        return true; // Rounded outer shell and its rectangular erosion are convex.
    }
    static bool LineIntersects(Vector2 a, Vector2 b, FloatRect rect)
    {
        Vector2 d = b - a; float lo = 0, hi = 1;
        static bool Axis(float p, float v, float min, float max, ref float lo, ref float hi)
        {
            if (Math.Abs(v) < .00001f) return p >= min && p <= max;
            float t0 = (min - p) / v, t1 = (max - p) / v;
            if (t0 > t1) (t0, t1) = (t1, t0);
            lo = Math.Max(lo, t0); hi = Math.Min(hi, t1); return lo <= hi;
        }
        return Axis(a.X, d.X, rect.X, rect.Right, ref lo, ref hi) && Axis(a.Y, d.Y, rect.Y, rect.Bottom, ref lo, ref hi);
    }
    Grid MakeGrid(FloatRect? ignored = null) => new(_layout!, this, ignored);
    List<Vector2>? Plan(Vector2 start, Vector2 target, FloatRect? ignored)
    {
        if (!Safe(start, ignored) || !Safe(target, ignored)) return null;
        if (SegmentSafe(start, target, ignored)) return [start, target];
        Grid grid = MakeGrid(ignored); int from = grid.FindStart(start, this), to = grid.FindStart(target, this);
        if (from < 0 || to < 0) return null;
        var frontier = new PriorityQueue<int, float>(); var cost = new float[grid.Count]; Array.Fill(cost, float.PositiveInfinity);
        var previous = new int[grid.Count]; Array.Fill(previous, -1); cost[from] = 0; frontier.Enqueue(from, 0);
        while (frontier.TryDequeue(out int current, out _))
        {
            if (current == to) break;
            foreach (int next in grid.Neighbors(current, this))
            {
                float candidate = cost[current] + Vector2.Distance(grid.Point(current), grid.Point(next));
                if (candidate >= cost[next]) continue;
                cost[next] = candidate; previous[next] = current;
                frontier.Enqueue(next, candidate + Vector2.Distance(grid.Point(next), grid.Point(to)));
            }
        }
        if (from != to && previous[to] < 0) return null;
        var path = new List<Vector2> { target };
        for (int at = to; at != from; at = previous[at]) path.Add(grid.Point(at));
        path.Add(grid.Point(from)); path.Add(start); path.Reverse();
        var simplified = new List<Vector2> { start };
        for (int i = 0; i < path.Count - 1;)
        {
            int next = path.Count - 1;
            while (next > i + 1 && !SegmentSafe(path[i], path[next], ignored)) next--;
            simplified.Add(path[next]); i = next;
        }
        return Round(simplified, ignored);
    }
    List<Vector2> Round(List<Vector2> path, FloatRect? ignored)
    {
        if (path.Count < 3) return path;
        var rounded = new List<Vector2> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2 corner = path[i]; float radius = Math.Min(5, Math.Min(Vector2.Distance(path[i - 1], corner), Vector2.Distance(corner, path[i + 1])) * .22f);
            if (radius < .05f) { rounded.Add(corner); continue; }
            Vector2 a = corner + Vector2.Normalize(path[i - 1] - corner) * radius;
            Vector2 b = corner + Vector2.Normalize(path[i + 1] - corner) * radius;
            rounded.Add(a);
            for (int step = 1; step <= 6; step++)
            {
                float t = step / 6f; rounded.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * corner + t * t * b);
            }
        }
        rounded.Add(path[^1]);
        for (int i = 1; i < rounded.Count; i++) if (!SegmentSafe(rounded[i - 1], rounded[i], ignored)) return path;
        return rounded;
    }
    sealed class Grid
    {
        readonly FloatRect _bounds; readonly int _columns, _rows;
        readonly FloatRect? _ignored;
        readonly Vector2[] _continuous;
        int RegularCount => _columns * _rows;
        public bool[] Free { get; }
        public int Count => Free.Length;
        public Grid(CompanionPlacementLayout layout, CompanionPlacementController owner, FloatRect? ignored)
        {
            _bounds = layout.OuterBounds; _ignored = ignored;
            _columns = Math.Max(1, (int)Math.Ceiling(_bounds.Width / GridSize));
            _rows = Math.Max(1, (int)Math.Ceiling(_bounds.Height / GridSize));
            var continuous = new List<Vector2>();
            void Add(Vector2 point)
            {
                if (owner.Safe(point, ignored) && !continuous.Any(p => Vector2.DistanceSquared(p, point) < .0001f)) continuous.Add(point);
            }
            // Narrow but real free strips can lie entirely between grid rows.
            // Preserve host fallback and current continuous anchors, and connect
            // registered cover staging points only through checked line segments.
            Add(layout.PreferredAnchor);
            if (owner._initialized) Add(owner.Pose.BodyAnchor);
            if (layout.CoverAnchors is { } covers)
                foreach (HideAnchor cover in covers) Add(GetCoverStagingAnchor(cover, layout.BodySize));
            _continuous = continuous.ToArray();
            Free = new bool[RegularCount + _continuous.Length];
            for (int i = 0; i < Count; i++) Free[i] = owner.Safe(Point(i), ignored);
        }
        public Vector2 Point(int index) => index >= RegularCount ? _continuous[index - RegularCount] :
            new(_bounds.X + (index % _columns + .5f) * GridSize, _bounds.Y + (index / _columns + .5f) * GridSize);
        public int FindStart(Vector2 position, CompanionPlacementController owner)
        {
            float nearest = float.MaxValue; int result = -1;
            for (int i = 0; i < Count; i++) if (Free[i])
            {
                float distance = Vector2.DistanceSquared(position, Point(i));
                if (distance < nearest && owner.SegmentSafe(position, Point(i), _ignored)) { nearest = distance; result = i; }
            }
            return result;
        }
        public IEnumerable<int> Neighbors(int index, CompanionPlacementController owner)
        {
            if (index >= RegularCount)
            {
                for (int next = 0; next < Count; next++)
                    if (next != index && Free[next] && owner.SegmentSafe(Point(index), Point(next), _ignored)) yield return next;
                yield break;
            }
            int x = index % _columns, y = index / _columns;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= _columns || ny >= _rows) continue;
                int next = ny * _columns + nx;
                if (Free[next] && owner.SegmentSafe(Point(index), Point(next), _ignored)) yield return next;
            }
            for (int next = RegularCount; next < Count; next++)
                if (Free[next] && owner.SegmentSafe(Point(index), Point(next), _ignored)) yield return next;
        }
        public List<int> Component(int start, CompanionPlacementController owner)
        {
            var seen = new bool[Count]; var pending = new Queue<int>(); var result = new List<int>(); seen[start] = true; pending.Enqueue(start);
            while (pending.TryDequeue(out int index))
            {
                result.Add(index);
                foreach (int next in Neighbors(index, owner)) if (!seen[next]) { seen[next] = true; pending.Enqueue(next); }
            }
            return result;
        }
    }
}

/// <summary>
/// Pure finite path timing. Sharp polyline corners decelerate to zero before
/// changing direction; small changes along a collision-checked rounded turn
/// retain speed. Segment Hermite curves change distance only, never geometry.
/// </summary>
public sealed class CompanionPathMotion
{
    readonly Vector2[] _points;
    readonly float[] _lengths, _speeds;
    readonly double[] _times;
    readonly double _started, _duration;
    public double StartTime => _started;
    public double EndTime => _started + _duration;

    public CompanionPathMotion(IReadOnlyList<Vector2> path, double started, double durationMilliseconds, Vector2 initialVelocity)
    {
        ArgumentNullException.ThrowIfNull(path);
        _points = path.ToArray();
        if (_points.Length < 2 || !double.IsFinite(started) || !double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0 ||
            _points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            throw new ArgumentException("A finite movement requires at least two distinct finite points and a positive duration.", nameof(path));
        _started = started; _duration = durationMilliseconds;
        _lengths = new float[_points.Length]; _speeds = new float[_points.Length]; _times = new double[_points.Length];
        var ratios = new float[_points.Length]; float distance = 0, weightedDistance = 0;
        for (int i = 1; i < _points.Length; i++)
        {
            _lengths[i] = Vector2.Distance(_points[i - 1], _points[i]);
            if (_lengths[i] < .0001f) throw new ArgumentException("Adjacent movement points must be distinct.", nameof(path));
            distance += _lengths[i];
        }
        for (int i = 1; i < _points.Length - 1; i++)
        {
            Vector2 incoming = (_points[i] - _points[i - 1]) / _lengths[i];
            Vector2 outgoing = (_points[i + 1] - _points[i]) / _lengths[i + 1];
            float cosine = Math.Clamp(Vector2.Dot(incoming, outgoing), -1, 1);
            ratios[i] = cosine < .9f ? 0 : Math.Clamp((cosine - .9f) / .1f, 0, 1);
        }
        for (int i = 1; i < _points.Length; i++)
            weightedDistance += _lengths[i] * (2 - (ratios[i - 1] + ratios[i]) / 2);
        float referenceSpeed = distance * 1000 / (float)_duration;
        for (int i = 1; i < _points.Length - 1; i++) _speeds[i] = ratios[i] * referenceSpeed;
        float inherited = Vector2.Dot(initialVelocity, (_points[1] - _points[0]) / _lengths[1]);
        _speeds[0] = float.IsFinite(inherited) ? Math.Max(0, inherited) : 0;
        for (int i = 1; i < _points.Length; i++)
        {
            double duration = _duration * _lengths[i] * (2 - (ratios[i - 1] + ratios[i]) / 2) / weightedDistance;
            _times[i] = _times[i - 1] + duration;
            // Monotone cubic sufficient condition. The cap is shared by both
            // segments at a vertex, preserving its speed through the join.
            float cap = 3 * _lengths[i] * 1000 / (float)duration;
            _speeds[i - 1] = Math.Min(_speeds[i - 1], cap);
            _speeds[i] = Math.Min(_speeds[i], cap);
        }
        _times[^1] = _duration;
    }

    public (Vector2 Position, Vector2 Velocity, float Progress, int SegmentIndex, bool Complete) Sample(double now)
    {
        double elapsed = Math.Clamp(now - _started, 0, _duration);
        if (elapsed >= _duration) return (_points[^1], Vector2.Zero, 1, _points.Length - 1, true);
        int segment = 1;
        while (segment < _times.Length - 1 && elapsed >= _times[segment]) segment++;
        double duration = _times[segment] - _times[segment - 1];
        float t = (float)((elapsed - _times[segment - 1]) / duration), t2 = t * t, t3 = t2 * t;
        float a = _speeds[segment - 1] * (float)duration / 1000 / _lengths[segment];
        float b = _speeds[segment] * (float)duration / 1000 / _lengths[segment];
        float position = (-2 * t3 + 3 * t2) + (t3 - 2 * t2 + t) * a + (t3 - t2) * b;
        float rate = (-6 * t2 + 6 * t) + (3 * t2 - 4 * t + 1) * a + (3 * t2 - 2 * t) * b;
        Vector2 direction = (_points[segment] - _points[segment - 1]) / _lengths[segment];
        return (Vector2.Lerp(_points[segment - 1], _points[segment], Math.Clamp(position, 0, 1)),
            direction * (Math.Max(0, rate) * _lengths[segment] * 1000 / (float)duration), (float)(elapsed / _duration), segment, false);
    }
}
