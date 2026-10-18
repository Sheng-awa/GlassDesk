using System.Numerics;

namespace GlassDesk.Core;

/// <summary>
/// Shared, immutable contour samples for the native character and the settings sample.
/// Coordinates are nominal 24 DIP units; (12,12) is the body anchor, independent of ears.
/// Every appearance/pose has 32 clockwise points starting at the top. Cat ears extend
/// beyond the nominal box, so consumers must fit the contour rather than clip to 24×24.
/// </summary>
public static class CompanionContour
{
    public const int PointCount = 32;
    public const float NominalSize = 24;
    public static Vector2 BodyCenter => new(12, 12);
    static readonly IReadOnlyList<Vector2>[,] Points = Build();
    static readonly FloatRect[] Envelopes = [BuildEnvelope(0), BuildEnvelope(1), BuildEnvelope(2)];
    static readonly FloatRect PlacementEnvelope = Union(Union(Envelopes[0], Envelopes[1]), Envelopes[2]);
    static readonly FloatRect[] EyeEnvelopes = Enum.GetValues<CompanionShapePose>().Select(BuildEyeEnvelope).ToArray();

    public static IReadOnlyList<Vector2> GetPoints(string appearanceId, CompanionShapePose pose)
        => Points[AppearanceIndex(appearanceId), ValidPose(pose)];

    static int AppearanceIndex(string id) => id == CompanionAppearanceIds.CatEar ? 1 : id == CompanionAppearanceIds.Devil ? 2 : 0;

    /// <summary>Maps facial landmarks through the same deformation as the body.</summary>
    public static Vector2 TransformPoint(Vector2 point, CompanionShapePose pose)
    {
        Vector2 v = point - BodyCenter;
        return BodyCenter + (pose switch
        {
            CompanionShapePose.Stretch => new Vector2(v.X * 1.09f + .055f * v.Y, v.Y * .91f + .012f * v.X * v.X),
            CompanionShapePose.Squash => new Vector2(v.X * 1.09f, v.Y * .87f + .014f * v.X * v.X + .45f),
            CompanionShapePose.Droplet => new Vector2(v.X * (1 - .014f * v.Y) + .11f * v.Y, v.Y * 1.025f + .012f * v.X * v.X),
            CompanionShapePose.Peek => new Vector2(v.X * (1.02f - .006f * v.Y), v.Y * .94f - .45f + .009f * v.X * v.X),
            _ => v
        });
    }

    /// <summary>
    /// Conservative bounds include all cached poses, expression scale/tilt and the
    /// visible shadow. Placement reserves this envelope throughout each morph.
    /// </summary>
    public static FloatRect FullBounds(string appearanceId, Vector2 anchor, float bodySize)
    {
        FloatRect local = Envelopes[AppearanceIndex(appearanceId)];
        return ScaleBounds(local, anchor, bodySize);
    }

    /// <summary>Reserves both appearances, so changing ears never invalidates a safe body anchor or path.</summary>
    public static FloatRect PlacementBounds(Vector2 anchor, float bodySize) => ScaleBounds(PlacementEnvelope, anchor, bodySize);

    /// <summary>Both eyes, including expression, pointer gaze, posture tilt and scale. Coordinates are widget-local DIPs.</summary>
    public static FloatRect EyeBounds(Vector2 anchor, float bodySize, CompanionShapePose pose)
        => ScaleBounds(EyeEnvelopes[ValidPose(pose)], anchor, bodySize);

    static FloatRect ScaleBounds(FloatRect local, Vector2 anchor, float bodySize)
    {
        float scale = bodySize / NominalSize;
        return new(anchor.X + local.X * scale, anchor.Y + local.Y * scale, local.Width * scale, local.Height * scale);
    }
    internal static FloatRect Union(FloatRect a, FloatRect b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y));

    static int ValidPose(CompanionShapePose pose) => (int)pose is >= 0 and < 5 ? (int)pose : 0;

    static IReadOnlyList<Vector2>[,] Build()
    {
        var result = new IReadOnlyList<Vector2>[3, 5];
        for (int appearance = 0; appearance < 3; appearance++)
            for (int pose = 0; pose < 5; pose++)
            {
                var points = new Vector2[PointCount];
                for (int i = 0; i < points.Length; i++)
                {
                    float angle = -MathF.PI / 2 + i * MathF.Tau / PointCount;
                    var point = BodyCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 10.8f;
                    // Broad triangular pinnae have a softly rounded tip and wide root.
                    // Horns instead sweep outward, then taper inward like the devil emoji.
                    // Identical winding/topology keeps every appearance morph continuous.
                    if (appearance > 0 && (i is >= 1 and <= 7 || i is >= 25 and <= 31))
                    {
                        int j = i > 16 ? 32 - i : i;
                        Vector2[] ear = appearance == 1
                            ? [new(14,1.5f),new(15.6f,1.1f),new(20,-1.7f),new(20.7f,.2f),new(21.1f,3.4f),new(21.7f,6.8f),new(22.4f,9.6f)]
                            : [new(14,1.5f),new(18,3),new(20,1.2f),new(18,-3.1f),new(21.5f,-1.1f),new(22.5f,3.4f),new(22.4f,7.7f)];
                        point = ear[j - 1];
                        if (i > 16) point.X = 24 - point.X;
                    }
                    points[i] = TransformPoint(point, (CompanionShapePose)pose);
                }
                result[appearance, pose] = Array.AsReadOnly(points);
            }
        return result;
    }

    static FloatRect BuildEnvelope(int appearance)
    {
        var points = new List<Vector2>();
        for (int pose = 0; pose < 5; pose++)
            foreach (Vector2 point in Points[appearance, pose])
            {
                points.Add(point - BodyCenter);
                points.Add(point - BodyCenter + new Vector2(0, 1.3f));
            }
        // RoundedPolygon's Bezier control points remain in the original
        // polygon's convex hull. This margin covers raster antialiasing; the
        // complete translated shadow was already included above.
        return PostureEnvelope(points, .8f);
    }

    static FloatRect BuildEyeEnvelope(CompanionShapePose pose)
    {
        float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        Vector2[] radii = [new(1.55f, 2.25f), new(2.1f, 3.45f), new(2.55f, 2.3f), new(2.05f, 1.65f), new(2.75f, 3.4f), new(2.2f, .25f), new(1.7f, 2.8f)];
        foreach (float eyeX in new[] { 7.9f, 16.1f })
            foreach (Vector2 radius in radii)
            {
                Vector2 center = TransformPoint(new(eyeX, 11.7f), pose) - BodyCenter;
                Vector2 r = TransformPoint(BodyCenter + radius, pose) - TransformPoint(BodyCenter, pose);
                foreach (int angle in new[] { -12, -8, 0, 7, 12 })
                {
                    float c = MathF.Cos(angle * MathF.PI / 180), s = MathF.Sin(angle * MathF.PI / 180);
                    foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
                        foreach (float sx in new[] { .92f, 1.06f }) foreach (float sy in new[] { .92f, 1.06f })
                            for (int halfDegree = -28; halfDegree <= 28; halfDegree++)
                            {
                                float radians = halfDegree * MathF.PI / 360, ct = MathF.Cos(radians), st = MathF.Sin(radians);
                                Vector2 gazeCenter = (center + new Vector2(x * 1.9f, y * 1.6f)) * new Vector2(sx, sy);
                                float cx = gazeCenter.X * ct - gazeCenter.Y * st, cy = gazeCenter.X * st + gazeCenter.Y * ct;
                                float xx = r.X * (sx * ct * c - sy * st * s), xy = r.Y * (-sx * ct * s - sy * st * c);
                                float yx = r.X * (sx * st * c + sy * ct * s), yy = r.Y * (-sx * st * s + sy * ct * c);
                                float rx = MathF.Sqrt(xx * xx + xy * xy), ry = MathF.Sqrt(yx * yx + yy * yy);
                                left = Math.Min(left, cx - rx); right = Math.Max(right, cx + rx);
                                top = Math.Min(top, cy - ry); bottom = Math.Max(bottom, cy + ry);
                            }
                }
            }
        // Exact ellipse support at each angle, rather than rotated bounding-box
        // corners. A quarter DIP covers sub-half-degree angular extrema (<.1
        // local unit here) plus raster antialiasing, without forcing side peeks
        // so far outside the cover that the body ceases to overlap it.
        return new(left - .25f, top - .25f, right - left + .5f, bottom - top + .5f);
    }

    /// <summary>Character-local occlusion mask in widget DIPs, following the real rounded cover edge.</summary>
    public static IReadOnlyList<Vector2> GetOcclusionClipPoints(HideAnchor anchor, CompanionCoverEdge edge)
    {
        FloatRect cover = anchor.OccluderBounds;
        bool vertical = edge is CompanionCoverEdge.Left or CompanionCoverEdge.Right;
        float half = (vertical ? cover.Height : cover.Width) / 2, depth = vertical ? cover.Width : cover.Height;
        float radius = Math.Clamp(anchor.OccluderRadius, 0, Math.Min(cover.Width, cover.Height) / 2);
        Vector2 normal = CompanionPlacementController.CoverNormal(edge), tangent = vertical ? Vector2.UnitY : Vector2.UnitX;
        Vector2 origin = edge switch
        {
            CompanionCoverEdge.Top => new(cover.X + cover.Width / 2, cover.Y),
            CompanionCoverEdge.Right => new(cover.Right, cover.Y + cover.Height / 2),
            CompanionCoverEdge.Bottom => new(cover.X + cover.Width / 2, cover.Bottom),
            _ => new(cover.X, cover.Y + cover.Height / 2)
        };
        var points = new List<Vector2>();
        void Add(float u, float v) => points.Add(origin + tangent * u + normal * v);
        Add(-half - 512, -depth - 512); Add(-half, -depth - 512); Add(-half, -radius);
        for (int i = 1; i <= 12; i++)
        {
            float a = MathF.PI - i * MathF.PI / 24;
            Add(-half + radius + radius * MathF.Cos(a), -radius + radius * MathF.Sin(a));
        }
        Add(half - radius, 0);
        for (int i = 1; i <= 12; i++)
        {
            float a = MathF.PI / 2 - i * MathF.PI / 24;
            Add(half - radius + radius * MathF.Cos(a), -radius + radius * MathF.Sin(a));
        }
        Add(half, -depth - 512); Add(half + 512, -depth - 512); Add(half + 512, 512); Add(-half - 512, 512);
        return points.AsReadOnly();
    }

    static FloatRect PostureEnvelope(IEnumerable<Vector2> points, float padding)
    {
        float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        const float limit = 14 * MathF.PI / 180;
        foreach (Vector2 point in points)
            foreach (float scaleX in new[] { .92f, 1.06f }) foreach (float scaleY in new[] { .92f, 1.06f })
            {
                Vector2 v = point * new Vector2(scaleX, scaleY);
                float phase = MathF.Atan2(v.Y, v.X);
                // Check endpoints and analytic extrema, including independent
                // expression scale on both axes and the rotated shadow offset.
                foreach (float candidate in new[] { -limit, limit, -phase, MathF.PI - phase, -MathF.PI - phase, MathF.PI / 2 - phase, -MathF.PI / 2 - phase })
                {
                    if (candidate < -limit || candidate > limit) continue;
                    float c = MathF.Cos(candidate), s = MathF.Sin(candidate);
                    Vector2 p = new(v.X * c - v.Y * s, v.X * s + v.Y * c);
                    left = Math.Min(left, p.X); top = Math.Min(top, p.Y); right = Math.Max(right, p.X); bottom = Math.Max(bottom, p.Y);
                }
            }
        return new(left - padding, top - padding, right - left + padding * 2, bottom - top + padding * 2);
    }
}
