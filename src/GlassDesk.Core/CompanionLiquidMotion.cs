using System.Numerics;

namespace GlassDesk.Core;

public readonly record struct CompanionLiquidDrop(Vector2 Center, float Radius);
public sealed record CompanionLiquidFrame(Vector2 Scale, Vector2 HeadOffset, IReadOnlyList<CompanionLiquidDrop> Trail, float Opacity, bool Active)
{
    public static CompanionLiquidFrame Rest { get; } = new(Vector2.One, Vector2.Zero, Array.Empty<CompanionLiquidDrop>(), 0, false);
}

/// <summary>Finite liquid travel, tail rejoining, then one damped squash/return. Never enlarges the reserved body envelope.</summary>
public static class CompanionLiquidMotion
{
    public const double RejoinMilliseconds = 210, SettleMilliseconds = 300;
    public static CompanionLiquidFrame Sample(CompanionPathMotion motion, double now, float bodySize,
        Func<Vector2, Vector2, bool> segmentSafe)
    {
        double age = Math.Max(0, now - motion.EndTime);
        if (now >= motion.EndTime + RejoinMilliseconds + SettleMilliseconds) return CompanionLiquidFrame.Rest;
        var head = motion.Sample(now);
        Vector2 lagVelocity = motion.Sample(Math.Max(motion.StartTime, Math.Min(now, motion.EndTime) - 48)).Velocity;
        float speed = lagVelocity.Length();
        Vector2 direction = speed > .001f ? lagVelocity / speed : Vector2.UnitX;
        float onset = Smooth((float)((Math.Min(now, motion.EndTime) - motion.StartTime) / 80));
        float strain = Math.Max(Math.Clamp(speed / (bodySize * 4), 0, 1), .9f * onset);
        float rejoin = 1 - Smooth((float)((age - 35) / (RejoinMilliseconds - 35)));
        // Thin across travel, retain length along travel. The old inverse ratio
        // looked like a smaller bead rather than a body stretching into liquid.
        Vector2 scale = Vector2.One - new Vector2(.26f * Math.Abs(direction.X) + .44f * Math.Abs(direction.Y),
            .26f * Math.Abs(direction.Y) + .44f * Math.Abs(direction.X)) * (strain * rejoin);
        scale = Vector2.Clamp(scale, new(.54f), Vector2.One);
        Vector2 headOffset = direction * (bodySize * .08f * strain * rejoin);
        if (age >= RejoinMilliseconds)
        {
            float t = (float)((age - RejoinMilliseconds) / SettleMilliseconds);
            float pulse = MathF.Sin(t * MathF.Tau) * MathF.Exp(-2.5f * t) * .18f;
            scale = pulse >= 0 ? new(1, 1 - pulse) : new(1 + pulse, 1);
        }
        var drops = new List<CompanionLiquidDrop>(6);
        Vector2 previous = head.Position;
        if (rejoin > .001f)
            for (int i = 1; i <= 6; i++)
            {
                Vector2 center = motion.Sample(Math.Max(motion.StartTime, now - i * 52)).Position;
                if (Vector2.DistanceSquared(head.Position, center) > bodySize * bodySize * 2.25f ||
                    !segmentSafe(previous, center) || !segmentSafe(head.Position, center)) break;
                if (drops.Count > 0)
                {
                    Vector2 first = drops[0].Center - head.Position, next = center - previous;
                    if(first.LengthSquared()>.01f && next.LengthSquared()>.01f &&
                        Vector2.Dot(Vector2.Normalize(first),Vector2.Normalize(next))<.97f)break;
                }
                if (Vector2.DistanceSquared(previous, center) < .02f) continue;
                // Rejoining pulls the residue toward the head; convex travel is
                // checked again so a tail cannot cut a protected polyline corner.
                Vector2 joined = Vector2.Lerp(head.Position, center, rejoin);
                if (!segmentSafe(previous, joined)) break;
                // Keep a little mass behind the neck, including on a short safe
                // move. A purely tapering tail disappeared underneath the head
                // in the real compact layout's narrow travel corridor.
                float radius = i switch { 1 => .25f, 2 => .21f, 3 => .17f, 4 => .19f, 5 => .25f, _ => .29f };
                drops.Add(new(joined, bodySize * radius * rejoin));
                previous = joined;
            }
        // A single opaque silhouette rejoins by geometry, not by fading a chain
        // of ghost balls. There remains exactly one face throughout the move.
        return new(scale, headOffset, drops, drops.Count > 0 ? 1 : 0, true);
    }

    /// <summary>One tapered ribbon behind the head; points stay inside the safe spine's reserved corridor.</summary>
    public static Vector2[] TrailContour(CompanionLiquidFrame frame, Vector2 anchor, float bodySize)
    {
        if (!frame.Active || frame.Trail.Count == 0) return [];
        var centers = new[] { anchor + frame.HeadOffset }.Concat(frame.Trail.Select(drop => drop.Center)).ToArray();
        var radii = new[] { bodySize * .30f * Math.Min(frame.Scale.X, frame.Scale.Y) }
            .Concat(frame.Trail.Select(drop => drop.Radius)).ToArray();
        // An envelope of overlapping liquid masses remains smooth even when
        // short travel makes adjacent radii larger than their separation. A
        // normal-offset ribbon folds over itself in that case and leaves cuts.
        var points = new List<Vector2>(centers.Length * 12);
        for (int i = 0; i < centers.Length; i++)
            for(int j=0;j<12;j++)
            {
                float angle=j*MathF.Tau/12;
                points.Add(centers[i]+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*radii[i]);
            }
        var sorted=points.Distinct().OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray();
        var hull=new List<Vector2>();
        static float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        foreach(var point in sorted)
        {
            while(hull.Count>=2&&Cross(hull[^2],hull[^1],point)<=0)hull.RemoveAt(hull.Count-1);
            hull.Add(point);
        }
        int lower=hull.Count;
        for(int i=sorted.Length-2;i>=0;i--)
        {
            while(hull.Count>lower&&Cross(hull[^2],hull[^1],sorted[i])<=0)hull.RemoveAt(hull.Count-1);
            hull.Add(sorted[i]);
        }
        if(hull.Count>1)hull.RemoveAt(hull.Count-1);
        return hull.Count<=64?hull.ToArray():Enumerable.Range(0,64).Select(i=>hull[i*hull.Count/64]).ToArray();
    }
    static float Smooth(float value) { float t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
}
