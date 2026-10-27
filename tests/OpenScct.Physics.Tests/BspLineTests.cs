using System.IO;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// Zero-extent traces on the world BSP walk the tree (UModel::LineCheck, spec/native/ANSWERS-moveset.md W1): polygon
/// winding plays no part. Checked on the converted SteeE (local only).
/// </summary>
public sealed class BspLineTests
{
    private readonly ITestOutputHelper _out;

    public BspLineTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static CollisionWorld? SteeE()
    {
        string root = TestPaths.SteeE;
        return Directory.Exists(root) ? CollisionWorld.Build(SvMap.Load(root).Collision) : null;
    }

    [Fact]
    public void AMeshTriangleStopsALineFromEitherSide()
    {
        // A static mesh box x 100..110: from inside, the line hits the far face from behind and reports that face's
        // own (outward) normal (UStaticMesh::LineCheck, no facing test).
        var b = new CollisionDataBuilder();
        b.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags, [(V(0f, -50f, -50f), V(10f, 50f, 50f))],
            V(100f, 3f, 7f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(500f, 1f, 2f), V(105f, 1f, 2f), default, ETraceFlags.TRACE_World));
        Near(V(1f, 0f, 0f), hit.Normal);
        // Pulled back min(1 / |D|, 0.1) of the time (the mesh rule): 1 uu here.
        Assert.Equal(109f, hit.Location.X, 3);

        // From outside the near face as before.
        Assert.True(w.LineCheck(out hit, null, V(500f, 1f, 2f), V(0f, 1f, 2f), default, ETraceFlags.TRACE_World));
        Near(V(-1f, 0f, 0f), hit.Normal);
        Assert.Equal(99f, hit.Location.X, 3);
    }

    [Fact]
    public void ALineDownOntoTheSteeELaneFloorHitsIt()
    {
        CollisionWorld? w = SteeE();
        if (w is null)
        {
            Assert.Skip("SteeE is not built (local only).");
        }

        // The lane floor whose converted polygons face down in part (R4-sim-vs-game.md): box and line agree.
        foreach ((float x, float y) in new[] { (-492f, -2032f), (-480f, -2040f), (-500f, -2020f), (-470f, -2010f) })
        {
            Assert.True(w.LineCheck(out FCheckResult box, null, V(x, y, -1400f), V(x, y, -1100f), V(1f, 1f, 1f), ETraceFlags.TRACE_World));
            Assert.True(w.LineCheck(out FCheckResult line, null, V(x, y, -1400f), V(x, y, -1100f), default, ETraceFlags.TRACE_World),
                $"({x}, {y}) line misses the floor at {box.Location.Z}");
            Assert.True(line.Normal.Z > 0.99f, $"({x}, {y}) normal {line.Normal}");
            Assert.Equal(box.Location.Z - 1f, line.Location.Z, 1f);
        }
    }

    [Fact]
    public void LinesAndSmallBoxesAgreeAcrossSteeE()
    {
        CollisionWorld? w = SteeE();
        if (w is null)
        {
            Assert.Skip("SteeE is not built (local only).");
        }

        const int n = 60;
        FVector min = w.WorldMin;
        FVector max = w.WorldMax;
        int compared = 0;
        int agree = 0;
        int lineMiss = 0;
        int lineOnly = 0;
        int inSolid = 0;
        int shown = 0;
        var e = new FVector(0.5f, 0.5f, 0.5f);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float x = min.X + (max.X - min.X) * (i + 0.5f) / n;
                float y = min.Y + (max.Y - min.Y) * (j + 0.5f) / n;
                for (float z = min.Z + 100f; z < max.Z; z += 400f)
                {
                    FVector s = V(x, y, z);
                    if (w.PointCheck(out _, null, s, V(2f, 2f, 2f), ETraceFlags.TRACE_Level))
                    {
                        continue;
                    }

                    FVector end = V(x, y, z - 600f);
                    bool hb = w.LineCheck(out FCheckResult b, null, end, s, e, ETraceFlags.TRACE_Level);
                    bool hl = w.LineCheck(out FCheckResult l, null, end, s, default, ETraceFlags.TRACE_Level);
                    if (!hb && !hl)
                    {
                        continue;
                    }

                    if (hl && l.Time == 0f)
                    {
                        // The start lies inside solid (the point check only finds surfaces): the walk hits at once.
                        inSolid++;
                        continue;
                    }

                    compared++;
                    if (hb && hl && System.MathF.Abs(b.Location.Z - l.Location.Z) < 2f)
                    {
                        agree++;
                    }
                    else if (hb && hl && shown++ < 25)
                    {
                        _out.WriteLine($"diff at {s}: box {b.Location.Z:F2} n {b.Normal} line {l.Location.Z:F2} n {l.Normal} t {l.Time:F4}");
                    }
                    else if (hb && !hl)
                    {
                        lineMiss++;
                    }
                    else if (hl && !hb)
                    {
                        lineOnly++;
                    }
                }
            }
        }

        _out.WriteLine($"{compared} compared, {agree} agree, {lineMiss} line misses, {lineOnly} line only, {inSolid} in solid");
        Assert.True(compared > 1000);
        Assert.True(agree >= compared * 0.995, $"{agree} of {compared} agree ({lineMiss} line misses, {lineOnly} line only)");
    }
}
