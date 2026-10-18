using System.Numerics;
using GlassDesk.Core;

static class CompanionLiquidChecks
{
    public static void Run(Action<bool, string> check)
    {
        var controller = new CompanionPlacementController(42);
        var layout = new CompanionPlacementLayout(WidgetForm.Peek, new(0, 0, 600, 400), 24, 40,
            new(80, 200), [new(230, 100, 90, 200)]);
        controller.UpdateLayout(layout, 0); controller.TryMoveTo(new(500, 200), 0);
        bool safe = true, finite = true, tail = false, strain = false, rejoin = false, bounce = false, ribbonSafe = true;
        for (int time = 0; time <= 1500; time += 5)
        {
            controller.Tick(time); var frame = controller.LiquidFrame;
            finite &= float.IsFinite(frame.Scale.X) && float.IsFinite(frame.Scale.Y) && frame.Scale.X is >= .52f and <= 1 && frame.Scale.Y is >= .52f and <= 1;
            foreach (string appearance in CompanionAppearanceIds.All)
            {
                var reserved = CompanionContour.FullBounds(appearance, Vector2.Zero, 40);
                foreach (Vector2 corner in new[] { new Vector2(reserved.X, reserved.Y), new(reserved.Right, reserved.Y), new(reserved.X, reserved.Bottom), new(reserved.Right, reserved.Bottom) })
                {
                    Vector2 transformed = corner * frame.Scale + frame.HeadOffset;
                    safe &= transformed.X >= reserved.X - .001f && transformed.X <= reserved.Right + .001f && transformed.Y >= reserved.Y - .001f && transformed.Y <= reserved.Bottom + .001f;
                }
            }
            strain |= controller.NeedsTick && Math.Min(frame.Scale.X, frame.Scale.Y) < .92f;
            rejoin |= !controller.NeedsTick && controller.LiquidMotionActive && frame.Trail.Count > 0;
            bounce |= !controller.NeedsTick && frame.Trail.Count == 0 && Math.Min(frame.Scale.X, frame.Scale.Y) < .99f;
            tail |= frame.Trail.Count >= 3;
            var centers = new[] { controller.Pose.BodyAnchor }.Concat(frame.Trail.Select(x => x.Center)).ToArray();
            safe &= controller.IsPathSafe(centers);
            foreach (var drop in frame.Trail)
                safe &= drop.Radius > 0 && drop.Radius <= 12 && layout.OuterBounds.Contains(drop.Center.X - drop.Radius, drop.Center.Y - drop.Radius) &&
                    layout.OuterBounds.Contains(drop.Center.X + drop.Radius, drop.Center.Y + drop.Radius) &&
                    !layout.ProtectedRegions.Any(r => new FloatRect(drop.Center.X - drop.Radius, drop.Center.Y - drop.Radius, drop.Radius * 2, drop.Radius * 2).Intersects(r));
            Vector2[] contour = CompanionLiquidMotion.TrailContour(frame, controller.Pose.BodyAnchor, 40);
            ribbonSafe &= contour.Length <= 64 && contour.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y));
            for (int i = 0; i < contour.Length; i++)
                for (int step = 0; step <= 8; step++)
                {
                    Vector2 p = Vector2.Lerp(contour[i], contour[(i + 1) % contour.Length], step / 8f);
                    ribbonSafe &= layout.OuterBounds.Contains(p.X, p.Y) && !layout.ProtectedRegions.Any(r => r.Contains(p.X, p.Y));
                }
        }
        check(finite && safe && tail && strain, "liquid travel has connected safe residue and bounded body strain around protected content");
        check(ribbonSafe, "the continuous tapered liquid contour stays finite and outside protected content through turns and rejoining");
        check(rejoin && bounce && !controller.NeedsTick && !controller.LiquidMotionActive && controller.LiquidFrame == CompanionLiquidFrame.Rest,
            "arrival rejoins its tail before one finite squash/return and releases ticking");
        controller.TryMoveTo(new(80, 200), 1600); controller.Tick(1780);
        controller.SetAvailability(true, true, true, 1780);
        check(!controller.NeedsTick && !controller.LiquidMotionActive && controller.LiquidFrame == CompanionLiquidFrame.Rest,
            "reduced motion immediately removes liquid residue and settling");
        controller.SetAvailability(true, true, false, 1800); controller.TryMoveTo(new(500, 200), 1800); controller.Tick(1900);
        controller.SetCapture(PointerCaptureOwner.WidgetDrag, 1900);
        check(!controller.LiquidMotionActive && controller.LiquidFrame == CompanionLiquidFrame.Rest, "input capture clears old liquid geometry immediately");

        var peek = new CompanionPlacementController(42);
        peek.UpdateLayout(new(WidgetForm.Capsule, new(0, 0, 232, 52), 24, 32, new(40, 26), []), 0);
        peek.UpdateLayout(new(WidgetForm.Peek, new(0, 0, 320, 176), 24, 40, new(52, 150.4f),
            [new(24,18,272,32),new(96,56,164,50),new(86,124,148,44),new(266,66,40,24),new(24,56,56,56)]), 1);
        bool entered = peek.MoveOnOpen(1), compactTail = false, compactStrain = false;
        for (int time = 1; time <= 1101; time += 10)
        {
            peek.Tick(time); compactTail |= peek.LiquidFrame.Trail.Count > 0;
            compactStrain |= Math.Min(peek.LiquidFrame.Scale.X, peek.LiquidFrame.Scale.Y) < .7f;
        }
        check(entered && compactTail && compactStrain && !peek.LiquidMotionActive,
            "the actual compact music layout enters with visible liquid strain and residue, then settles");

        var clockPeek = new CompanionPlacementController(42);
        clockPeek.UpdateLayout(new(WidgetForm.Capsule, new(0, 0, 232, 52), 24, 32, new(40, 26), []), 0);
        var clockLayout = new CompanionPlacementLayout(WidgetForm.Peek, new(0, 0, 240, 144), 24, 30,
            new(21.5f, 80), [new(48,30,144,60), new(54,100,132,32)]);
        clockPeek.UpdateLayout(clockLayout, 1);
        bool clockSafe = clockPeek.IsSafe(clockPeek.Pose.BodyAnchor);
        foreach (string appearance in CompanionAppearanceIds.All)
        {
            clockPeek.SetAppearance(appearance, 2);
            var bounds = CompanionContour.FullBounds(appearance, clockPeek.Pose.BodyAnchor, clockLayout.BodySize);
            clockSafe &= clockPeek.IsSafe(clockPeek.Pose.BodyAnchor) && bounds.X >= 0 && bounds.Y >= 0 &&
                bounds.Right <= 240 && bounds.Bottom <= 144 &&
                !clockLayout.ProtectedRegions.Any(r => bounds.Intersects(r.Inflate(CompanionPlacementController.SafetyGap)));
        }
        clockPeek.SetAvailability(true, true, true, 3);
        clockSafe &= !clockPeek.NeedsTick && !clockPeek.LiquidMotionActive && clockPeek.IsSafe(clockPeek.Pose.BodyAnchor);
        check(clockSafe, "clock Peek keeps every skin and its safety gap inside the short shell without obscuring clock or action");
        var horizontal = new CompanionPathMotion([new(0, 0), new(100, 0)], 0, 600, Vector2.Zero);
        var horizontalFrame = CompanionLiquidMotion.Sample(horizontal, 300, 40, (_, _) => true);
        check(horizontalFrame.Scale.X > horizontalFrame.Scale.Y && horizontalFrame.Opacity == 1 &&
            horizontalFrame.Trail.Count > 3 && horizontalFrame.Trail[^1].Radius > horizontalFrame.Trail[2].Radius,
            "liquid has a narrow connecting neck and retained residue mass within one opaque silhouette");
    }
}
