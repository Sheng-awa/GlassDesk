using System.Numerics;
using GlassDesk.Core;

internal static class UiCompanionChecks
{
    public static void Run(Action<bool, string> check)
    {
        bool contourValid = true, morphValid = true;
        foreach (string appearance in CompanionAppearanceIds.All)
            foreach (CompanionShapePose pose in Enum.GetValues<CompanionShapePose>())
            {
                var points = CompanionContour.GetPoints(appearance, pose);
                contourValid &= points.Count == 32 && ReferenceEquals(points, CompanionContour.GetPoints(appearance, pose)) && Simple(points);
                FloatRect bounds = CompanionContour.FullBounds(appearance, Vector2.Zero, 24);
                foreach (Vector2 point in points)
                {
                    Vector2 relative = point - CompanionContour.BodyCenter;
                    contourValid &= relative.X >= bounds.X && relative.X <= bounds.Right && relative.Y >= bounds.Y && relative.Y <= bounds.Bottom;
                }
                var other = CompanionContour.GetPoints(appearance == CompanionAppearanceIds.Original ? CompanionAppearanceIds.CatEar : CompanionAppearanceIds.Original, CompanionShapePose.Droplet);
                for (int step = 0; step <= 10; step++)
                    morphValid &= Simple(points.Select((point, i) => Vector2.Lerp(point, other[i], step / 10f)).ToArray());
            }
        check(contourValid, "companion contours are cached, simple and enclosed through every common pose");
        check(morphValid, "matched 32-point appearance and shape morphs do not self-intersect");
        check(ReferenceEquals(CompanionContour.GetPoints("unknown", CompanionShapePose.Rounded), CompanionContour.GetPoints(CompanionAppearanceIds.Original, CompanionShapePose.Rounded)), "unknown companion appearance falls back without changing geometry anchor");

        var sharp = new CompanionPathMotion([new(0, 0), new(100, 0), new(100, 100)], 0, 1000, Vector2.Zero);
        var cruise = sharp.Sample(250); var approaching = sharp.Sample(499); var atCorner = sharp.Sample(500); var departing = sharp.Sample(501);
        check(cruise.Velocity.Length() > 100 && approaching.Velocity.Length() < cruise.Velocity.Length() * .1f &&
            departing.Velocity.Length() < cruise.Velocity.Length() * .1f && atCorner.Velocity.Length() < .001f && atCorner.Position == new Vector2(100, 0),
            "unsafe-to-round polyline slows before its sharp corner and changes direction at zero speed");
        bool noOvershoot = true;
        for (int time = 0; time <= 1100; time++)
        {
            var sample = sharp.Sample(time);
            noOvershoot &= sample.Position.X >= 0 && sample.Position.X <= 100 && sample.Position.Y >= 0 && sample.Position.Y <= 100 &&
                (sample.Position.Y < .001f || Math.Abs(sample.Position.X - 100) < .001f) &&
                float.IsFinite(sample.Velocity.X) && float.IsFinite(sample.Velocity.Y);
        }
        var inherited = new CompanionPathMotion([new(0, 0), new(100, 0), new(200, 0)], 0, 1000, new(200, 0));
        check(noOvershoot && sharp.Sample(1000).Complete && sharp.Sample(1000).Velocity == Vector2.Zero &&
            inherited.Sample(0).Velocity == new Vector2(200, 0), "polyline timing stays on collision-checked segments and preserves safe inherited velocity without overshoot");

        bool fullBounds = true;
        var geometryRandom = new Random(19);
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            string appearance = CompanionAppearanceIds.All[iteration % 3];
            var pose = (CompanionShapePose)(iteration % 5);
            float angle = (float)(geometryRandom.NextDouble() * 28 - 14) * MathF.PI / 180;
            Vector2 scale = new((float)(.92 + geometryRandom.NextDouble() * .14), (float)(.92 + geometryRandom.NextDouble() * .14));
            FloatRect envelope = CompanionContour.FullBounds(appearance, Vector2.Zero, 40);
            foreach (Vector2 point in CompanionContour.GetPoints(appearance, pose))
                foreach (float shadowOffset in new[] { 0, 1.3f })
                {
                    Vector2 v = (point - CompanionContour.BodyCenter + new Vector2(0, shadowOffset)) * scale;
                    Vector2 actual = new Vector2(v.X * MathF.Cos(angle) - v.Y * MathF.Sin(angle), v.X * MathF.Sin(angle) + v.Y * MathF.Cos(angle)) * (40f / 24);
                    fullBounds &= envelope.Contains(actual.X, actual.Y);
                }
        }
        check(fullBounds, "1,000 independent expression scales and tilts keep actual contour and shadow inside full bounds");

        var capsule = new CompanionPlacementController(8);
        var capsuleLayout = new CompanionPlacementLayout(WidgetForm.Capsule, new(0, 0, 232, 52), 24, 32, new(40, 26), []);
        capsule.UpdateLayout(capsuleLayout, 0);
        capsule.SetAppearance(CompanionAppearanceIds.CatEar, 1);
        check(capsule.Pose.BodyAnchor == new Vector2(40, 26) && capsule.IsSafe(capsule.Pose.BodyAnchor),
            "32 DIP skin envelope keeps the 40,26 anchor safely inside its 52 DIP rounded shell");
        capsule.SetAppearance(CompanionAppearanceIds.Original, 2);
        check(capsule.Pose.BodyAnchor == new Vector2(40, 26), "switching capsule appearance does not jump the body anchor");

        var obstacle = new FloatRect(170, 60, 60, 160);
        var layout = new CompanionPlacementLayout(WidgetForm.Details, new(0, 0, 400, 300), 24, 24, new(60, 150), [obstacle]);
        var controller = new CompanionPlacementController(73); controller.UpdateLayout(layout, 0);
        check(controller.TryMoveTo(new(340, 150), 0) && controller.PlannedPath.Count > 2 && controller.IsPathSafe(controller.PlannedPath), "A-star routes around actual protected content and verifies rounded segments");
        bool safeMotion = true;
        for (int time = 0; time <= 950; time += 7) { controller.Tick(time); safeMotion &= controller.IsSafe(controller.Pose.BodyAnchor); }
        check(safeMotion && !controller.NeedsTick && Vector2.Distance(controller.Pose.BodyAnchor, new(340, 150)) < .01f, "every sampled motion footprint stays clear of protected content and settles");
        check(controller.NextWakeTime is >= 12000 and <= 21000, "settled companion schedules one 12-20 second idle wake");
        double? idleDeadline = controller.NextWakeTime;
        controller.UpdateLayout(layout with { ProtectedRegions = layout.ProtectedRegions.ToArray() }, 960);
        controller.SetAvailability(true, true, false, 960);
        controller.SetAppearance(CompanionAppearanceIds.CatEar, 960);
        check(controller.NextWakeTime == idleDeadline && !controller.NeedsTick,
            "repeated equal layout, availability and appearance updates preserve the settled idle deadline");
        controller.SetAppearance(CompanionAppearanceIds.Original, 960);
        controller.TryMoveTo(new(60, 150), 1000); controller.Tick(1200); Vector2 before = controller.Pose.BodyAnchor;
        controller.TryMoveTo(new(330, 270), 1200);
        check(Vector2.Distance(before, controller.Pose.BodyAnchor) < .001f && controller.IsPathSafe(controller.PlannedPath), "mid-motion redirection starts from the actual sampled position with a safe tangent");
        var continuing = new CompanionPlacementController(31); continuing.UpdateLayout(layout, 0);
        continuing.TryMoveTo(new(340, 150), 0); continuing.Tick(100);
        Vector2 continuousAnchor = continuing.Pose.BodyAnchor;
        continuing.UpdateLayout(layout with { ProtectedRegions = [obstacle] }, 100);
        continuing.SetAppearance(CompanionAppearanceIds.CatEar, 100);
        check(continuing.NeedsTick && continuing.Pose.BodyAnchor == continuousAnchor && continuing.IsPathSafe(continuing.PlannedPath),
            "equal layout and appearance change preserve the active path and exact sampled anchor");
        continuing.UpdateLayout(layout with { ProtectedRegions = [obstacle, new(370, 20, 10, 10)] }, 100);
        check(continuing.NeedsTick && continuing.Pose.BodyAnchor == continuousAnchor,
            "a harmless changed layout preserves the remaining safe movement");
        continuing.SetAppearance(CompanionAppearanceIds.Original, 1000);
        check(!continuing.NeedsTick && continuing.NextWakeTime is > 1000,
            "an appearance update that samples motion completion preserves the newly scheduled idle wake");
        controller.SetCapture(PointerCaptureOwner.Timeline, 1220); Vector2 captured = controller.Pose.BodyAnchor; controller.Tick(30000);
        check(!controller.NeedsTick && controller.NextWakeTime is null && controller.Pose.BodyAnchor == captured, "timeline capture stops movement and idle scheduling");
        controller.SetCapture(PointerCaptureOwner.None, 30000);
        controller.SetAvailability(true, false, false, 30001);
        check(!controller.NeedsTick && controller.NextWakeTime is null, "hidden companion owns no active position schedule");
        controller.SetAvailability(true, true, true, 30002);
        check(!controller.NeedsTick && controller.NextWakeTime is null && controller.Pose.ShapePose == CompanionShapePose.Rounded, "reduced motion settles the body and disables roaming");

        var wall = layout with { ProtectedRegions = [new(190, 0, 20, 300)] };
        controller = new(11); controller.UpdateLayout(wall, 0); before = controller.Pose.BodyAnchor;
        check(!controller.TryMoveTo(new(340, 150), 0) && controller.Pose.BodyAnchor == before, "disconnected destination keeps the current safe position");
        controller.UpdateLayout(layout with { PreferredAnchor = new(60, 150), ProtectedRegions = [new(20, 100, 100, 100)] }, 1);
        check(controller.IsSafe(controller.Pose.BodyAnchor), "layout replacement relocates an invalid current footprint to the nearest safe point");

        var cover = new FloatRect(140, 140, 120, 100);
        HideAnchor hide = CompanionPlacementController.CreateCoverPeek("track-a", cover, 18, 32);
        var coverLayout = new CompanionPlacementLayout(WidgetForm.Details, new(0, 0, 400, 300), 24, 32, new(90, 60), [cover], [hide]);
        controller = new(17); controller.UpdateLayout(coverLayout, 0); controller.BeginDetailsIntro(1, hide, 0);
        check(controller.Pose.PlacementMode == CompanionPlacementMode.NavigationIntro && controller.RevealHitRegion is null, "navigation intro eyes belong only to navigation input");
        check(controller.LeaveNavigationIntro(1, 1) && !controller.LeaveNavigationIntro(1, 2), "navigation intro is consumed once in its open sequence");
        controller.Tick(1000); controller.BeginDetailsIntro(1, hide, 1001);
        check(controller.IntroConsumed && controller.Pose.PlacementMode == CompanionPlacementMode.Front, "old intro requests cannot re-hide a consumed open cycle");
        check(controller.TryHide(hide, 1100), "registered cover peek has a safe approach path");
        controller.Tick(2100);
        check(controller.Pose.PlacementMode == CompanionPlacementMode.CoverPeek && controller.RevealHitRegion is { Width: >= 28, Height: >= 20 } && !hide.VisibleEyeRegion.Intersects(cover), "cover peek exposes both eyes and a real reveal target");
        Vector2 hit = new(hide.RevealHitRegion.X + 14, hide.RevealHitRegion.Y + 10);
        check(!controller.TryCaptureReveal(hit, 0) && controller.TryCaptureReveal(hit, 1) && !controller.TryCaptureReveal(hit, 1), "reveal ownership rejects stale open sequences and repeated presses");
        check(controller.CompleteReveal(1, 2101) && !controller.CompleteReveal(1, 2102), "one captured eye click produces exactly one reveal transition");
        check(controller.TryCaptureReveal(hit, 1) && controller.CompleteReveal(1, 2103),
            "a second completed click stays owned during revealing rather than expanding the Peek body");
        controller.Tick(3100);
        check(controller.Pose.PlacementMode == CompanionPlacementMode.Front && controller.ActiveOccluder is null && controller.IsSafe(controller.Pose.BodyAnchor), "revealed character returns to safe foreground and clears its local clip");
        controller.TryHide(hide, 3200); controller.Tick(4200);
        Vector2 replacingAnchor = controller.Pose.BodyAnchor;
        controller.UpdateLayout(coverLayout with { CoverAnchors = [hide with { StableId = "track-b" }] }, 4201);
        check(controller.Pose.BodyAnchor == replacingAnchor && controller.ActiveOccluder?.StableId != hide.StableId,
            "artwork replacement retires old hiding identity without jumping the current body anchor");
        controller.Tick(5201);
        check(controller.ActiveOccluder is null && controller.Pose.PlacementMode == CompanionPlacementMode.Front && controller.IsSafe(controller.Pose.BodyAnchor), "artwork replacement exits locally into safe foreground at identical bounds");

        controller.UpdateLayout(coverLayout, 5300); controller.TryHide(hide, 5301); controller.Tick(6301);
        controller.SetAvailability(true, true, true, 6302); controller.Reveal(6303);
        check(controller.Pose.PlacementMode == CompanionPlacementMode.Front && !controller.NeedsTick && controller.IsSafe(controller.Pose.BodyAnchor), "reduced motion still reveals a hidden character immediately");

        FloatRect navigation = new(36, 12, 288, 68), artwork = new(84, 88, 192, 192);
        var introLayout = new CompanionPlacementLayout(WidgetForm.Details, new(0, 0, 360, 484), 24, 40, new(32, 130), [navigation, artwork]);
        HideAnchor navigationIntro = CompanionPlacementController.CreateNavigationIntro("nav/1", navigation, 24, new(180, 36), 40);
        var intro = new CompanionPlacementController(53); intro.UpdateLayout(introLayout, 0);
        intro.SetAppearance(CompanionAppearanceIds.CatEar, 0); intro.BeginDetailsIntro(1, navigationIntro, 0);
        FloatRect eyeBounds = CompanionContour.EyeBounds(new(180, 36), 40, CompanionShapePose.Peek);
        check(intro.Pose.PlacementMode == CompanionPlacementMode.NavigationIntro && intro.Pose.BodyAnchor == new Vector2(180, 36) &&
            navigationIntro.VisibleEyeRegion.X <= eyeBounds.X && navigationIntro.VisibleEyeRegion.Right >= eyeBounds.Right &&
            navigationIntro.VisibleEyeRegion.Y <= eyeBounds.Y && navigationIntro.VisibleEyeRegion.Bottom >= eyeBounds.Bottom &&
            !intro.Pose.FullVisualBounds.Intersects(artwork.Inflate(8)),
            "actual 40 DIP cat-ear Details intro fits nav top12 and exposes full expressive eyes clear of artwork");
        intro.UpdateLayout(introLayout with { ProtectedRegions = [navigation, artwork] }, 1);
        check(intro.Pose.PlacementMode == CompanionPlacementMode.NavigationIntro && !intro.IntroConsumed,
            "repeated actual Details layout does not cancel the pending navigation intro");
        intro.LeaveNavigationIntro(1, 2); intro.Tick(1002);
        check(intro.Pose.PlacementMode == CompanionPlacementMode.Front && intro.IsSafe(intro.Pose.BodyAnchor),
            "top-of-shell navigation intro takes a checked exit into safe foreground");
        var splitIntro = new CompanionPlacementController(53);
        splitIntro.UpdateLayout(introLayout with { ProtectedRegions = [new(36, 12, 288, 48), new(140, 62, 80, 16), artwork] }, 0);
        splitIntro.BeginDetailsIntro(1, navigationIntro, 0);
        check(splitIntro.Pose.PlacementMode != CompanionPlacementMode.NavigationIntro,
            "intro rejects a mismatched registered occluder instead of crossing an unrelated protected badge");
        controller.UpdateLayout(layout with { Form = WidgetForm.Capsule, PreferredAnchor = new(60, 40) }, 6400);
        before = controller.Pose.BodyAnchor; controller.Tick(999999);
        check(before == controller.Pose.BodyAnchor && !controller.NeedsTick && controller.NextWakeTime is null, "capsule remains at its stable host anchor without idle motion");

        CheckActualCoverLayouts(check);

        bool races = true;
        controller = new(29); controller.UpdateLayout(coverLayout, 0);
        for (int index = 0; index < 1000; index++)
        {
            double now = index * 37;
            switch (index % 10)
            {
                case 0: controller.BeginDetailsIntro(index + 1, hide, now); break;
                case 1: controller.LeaveNavigationIntro(controller.OpenSequence, now); break;
                case 2: controller.TryHide(hide, now); break;
                case 3: controller.SetCapture(PointerCaptureOwner.WidgetDrag, now); break;
                case 4: controller.SetCapture(PointerCaptureOwner.None, now); break;
                case 5: controller.SetAvailability(false, true, false, now); break;
                case 6: controller.SetAvailability(true, true, true, now); break;
                case 7: controller.SetAvailability(true, true, false, now); break;
                case 8: controller.UpdateLayout(coverLayout with { CoverAnchors = index % 20 == 8 ? [] : [hide] }, now); break;
                case 9: controller.CompleteReveal(controller.OpenSequence - 1, now); controller.Tick(now + 15); break;
            }
            races &= float.IsFinite(controller.Pose.BodyAnchor.X) && float.IsFinite(controller.Pose.BodyAnchor.Y) &&
                (controller.ActiveOccluder is not null || controller.IsSafe(controller.Pose.BodyAnchor));
        }
        controller.SetAvailability(false, false, false, 40000);
        check(races && !controller.NeedsTick && controller.NextWakeTime is null, "1,000 layout, intro, capture and visibility interleavings remain finite and leave no timer work");
    }

    static void CheckActualCoverLayouts(Action<bool, string> check)
    {
        FloatRect peekCover = new(24, 56, 56, 56), detailsCover = new(84, 88, 192, 192);
        HideAnchor bottom = CompanionPlacementController.CreateCoverPeek("peek/track-a", peekCover, 20, 40, CompanionCoverEdge.Bottom);
        HideAnchor left = CompanionPlacementController.CreateCoverPeek("details/track-a/left", detailsCover, 20, 40, CompanionCoverEdge.Left, .375f);
        HideAnchor right = CompanionPlacementController.CreateCoverPeek("details/track-a/right", detailsCover, 20, 40, CompanionCoverEdge.Right, .375f);
        var peekLayout = new CompanionPlacementLayout(WidgetForm.Peek, new(0, 0, 320, 176), 24, 40, new(52, 150.4f),
            [new(72, 18, 176, 32), new(96, 56, 164, 50), new(86, 124, 148, 44), new(266, 66, 40, 24), peekCover], [bottom]);
        var detailsLayout = new CompanionPlacementLayout(WidgetForm.Details, new(0, 0, 360, 512), 24, 40, new(32, 130),
            [new(36, 12, 288, 68), new(312, 464, 36, 36), detailsCover, new(24, 288, 312, 88),
                new(136, 350, 88, 24), new(96, 434, 168, 56), new(56, 382, 248, 40)], [right, left]);
        bool bothAppearances = true, completeEyes = true, localMask = true, safeLifecycle = true;
        foreach (string appearance in CompanionAppearanceIds.All)
            foreach (var actual in new[] { peekLayout, detailsLayout })
            {
                var actor = new CompanionPlacementController(27); actor.UpdateLayout(actual, 0); actor.SetAppearance(appearance, 0);
                if (actual.Form == WidgetForm.Details)
                    bothAppearances &= !actor.TryHide(right, 0) && actor.Pose.BodyAnchor == actual.PreferredAnchor;
                bothAppearances &= actor.IsSafe(actor.Pose.BodyAnchor) && actor.TryHideAvailable(0);
                actor.Tick(1000);
                HideAnchor? hidden = actor.ActiveOccluder;
                bothAppearances &= actor.Pose.PlacementMode == CompanionPlacementMode.CoverPeek && hidden is not null;
                if (hidden is null) continue;
                CompanionCoverEdge edge = CompanionPlacementController.CoverEdge(hidden)!.Value;
                FloatRect eyes = CompanionContour.EyeBounds(actor.Pose.BodyAnchor, actual.BodySize, CompanionShapePose.Peek);
                completeEyes &= !eyes.Intersects(hidden.OccluderBounds) && actor.RevealHitRegion is { Width: >= 28, Height: >= 20 } hit &&
                    !hit.Intersects(hidden.OccluderBounds);
                var mask = CompanionContour.GetOcclusionClipPoints(hidden, edge);
                localMask &= mask.Count <= 64 && Simple(mask) && PointInPolygon(new(eyes.X + eyes.Width / 2, eyes.Y + eyes.Height / 2), mask) &&
                    !PointInPolygon(new(hidden.OccluderBounds.X + hidden.OccluderBounds.Width / 2, hidden.OccluderBounds.Y + hidden.OccluderBounds.Height / 2), mask);
                bool anyHidden = false, anyVisible = false;
                foreach (Vector2 point in CompanionContour.GetPoints(appearance, CompanionShapePose.Peek))
                {
                    Vector2 absolute = actor.Pose.BodyAnchor + (point - CompanionContour.BodyCenter) * (40f / 24);
                    bool visible = PointInPolygon(absolute, mask); anyVisible |= visible; anyHidden |= !visible;
                }
                localMask &= anyVisible && anyHidden;
                FloatRect reveal = actor.RevealHitRegion!.Value;
                Vector2 pointer = new(reveal.X + reveal.Width / 2, reveal.Y + reveal.Height / 2);
                safeLifecycle &= actor.TryCaptureReveal(pointer, actor.OpenSequence);
                actor.SetCapture(PointerCaptureOwner.CompanionReveal, 1001);
                safeLifecycle &= actor.CompleteReveal(actor.OpenSequence, 1002);
                actor.SetCapture(PointerCaptureOwner.None, 1002); actor.Tick(2002);
                safeLifecycle &= actor.Pose.PlacementMode == CompanionPlacementMode.Front && actor.IsSafe(actor.Pose.BodyAnchor) && actor.ActiveOccluder is null;

                for (int interruption = 0; interruption < 3; interruption++)
                {
                    actor = new(27); actor.UpdateLayout(actual, 0); actor.SetAppearance(appearance, 0); actor.TryHideAvailable(0); actor.Tick(300);
                    Vector2 interrupted = actor.Pose.BodyAnchor;
                    if (interruption == 0)
                    {
                        actor.SetCapture(PointerCaptureOwner.Timeline, 300);
                        safeLifecycle &= !actor.NeedsTick && actor.RevealHitRegion is null && actor.Pose.BodyAnchor == interrupted;
                        actor.SetCapture(PointerCaptureOwner.None, 301);
                    }
                    else if (interruption == 1)
                    {
                        actor.SetAvailability(true, false, false, 300);
                        safeLifecycle &= !actor.NeedsTick && actor.NextWakeTime is null && actor.RevealHitRegion is null;
                        actor.SetAvailability(true, true, false, 301);
                    }
                    else actor.SetAvailability(true, true, true, 300);
                    actor.Tick(1301);
                    safeLifecycle &= actor.Pose.PlacementMode == CompanionPlacementMode.Front && actor.IsSafe(actor.Pose.BodyAnchor) && actor.RevealHitRegion is null;
                }
                actor = new(27); actor.UpdateLayout(actual, 0); actor.TryHideAvailable(0); actor.Tick(1000);
                actor.SetAvailability(false, true, false, 1001);
                safeLifecycle &= actor.RevealHitRegion is null && !actor.NeedsTick && actor.NextWakeTime is null;
                actor.SetAvailability(true, true, true, 1002); actor.Tick(2002);
                safeLifecycle &= actor.IsSafe(actor.Pose.BodyAnchor) && actor.Pose.PlacementMode == CompanionPlacementMode.Front;
            }
        check(bothAppearances, "actual protected Peek and Details layouts reach bottom/left cover peeks in both appearances without crossing content");
        check(completeEyes, "actual side and bottom peeks retain complete expressive eyes and an exposed 28 by 20 reveal target");
        check(localMask && PointInPolygon(new(peekCover.X + 1, peekCover.Bottom - 1), CompanionContour.GetOcclusionClipPoints(bottom, CompanionCoverEdge.Bottom)),
            "rounded-cover masks hide only part of the body and preserve pixels outside real rounded corners");
        check(safeLifecycle, "actual cover peeks consume reveal once and safely end interrupted capture, hidden, disabled and reduced-motion states");

        var isolated = new CompanionPlacementController(27);
        isolated.UpdateLayout(peekLayout with { PreferredAnchor = new(292, 140) }, 0);
        check(isolated.IsSafe(isolated.Pose.BodyAnchor) && !isolated.TryHideAvailable(0),
            "Peek's disconnected right island cannot cross real transport and metadata protection to fake a left cover approach");
    }

    static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    static bool Simple(IReadOnlyList<Vector2> points)
    {
        if (points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y))) return false;
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 2; j < points.Count; j++)
            {
                if (i == 0 && j == points.Count - 1) continue;
                Vector2 a = points[i], b = points[(i + 1) % points.Count], c = points[j], d = points[(j + 1) % points.Count];
                static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (Cross(a, b, c) * Cross(a, b, d) < -.00001f && Cross(c, d, a) * Cross(c, d, b) < -.00001f) return false;
            }
        return true;
    }
}
