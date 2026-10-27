using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.AI;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The motion tracker's sight rule on SteeE's own collision (converted locally): a line through one window pane is
/// clear, a pane with a wall behind it is not (spec/equipment/motion-tracking.md, rule "GetActors").
/// </summary>
public sealed class MotionTrackingSteeETests
{
    private static PawnRig? SteeE(PawnTeam team = PawnTeam.Merc)
    {
        string? dir = AiTestKit.SteeEFolder;
        return dir is null ? null : PawnRig.ForMap(team, dir);
    }

    /// <summary>The floor under <paramref name="p"/> (a world trace down 400 uu), or null.</summary>
    private static float? FloorZ(CollisionWorld world, FVector p)
    {
        if (world.LineCheck(out FCheckResult hit, null, p - new FVector(0f, 0f, 400f), p, FVector.Zero, ETraceFlags.TRACE_World) && hit.bHit &&
            hit.Normal.Z > 0.7f)
        {
            return hit.Location.Z;
        }

        return null;
    }

    /// <summary>True when a standing pawn's cylinder (radius 35, half height 87.5) is free at <paramref name="p"/>.</summary>
    private static bool Fits(CollisionWorld world, FVector p) =>
        !world.PointCheck(out _, null, p, new FVector(35f, 35f, 87.5f), ETraceFlags.TRACE_World);

    private sealed record Spot(string Name, FVector Merc, FVector Spy, string Kind, string Hits);

    /// <summary>Lines across each SVitre pane at pawn height, sorted into "glass only" and "glass then wall".</summary>
    private static List<Spot> GlassSpots(PawnRig rig)
    {
        CollisionWorld world = rig.Collision!;
        var spots = new List<Spot>();
        var hits = new List<FCheckResult>();
        var colliders = new List<int>();
        foreach (SVitre glass in rig.Level.AllActors<SVitre>())
        {
            colliders.Clear();
            if (world.GetActorColliders(glass, colliders) == 0)
            {
                continue;
            }

            ColliderInfo c = world.GetCollider(colliders[0]);
            FVector size = c.Max - c.Min;
            FVector centre = (c.Max + c.Min) * 0.5f;
            FVector normal = size.X < size.Y ? new FVector(1f, 0f, 0f) : new FVector(0f, 1f, 0f);
            FVector along = new FVector(normal.Y, normal.X, 0f);
            foreach (float side in new[] { 1f, -1f })
            {
                foreach (float near in new[] { 150f, 250f, 400f, 600f, 900f })
                {
                    foreach (float far in new[] { 120f, 250f, 400f, 600f, 800f, 1000f, 1300f, 1600f })
                    {
                      foreach (float lateral in new[] { 0f, -60f, 60f })
                      {
                        FVector mercXY = centre + normal * (side * near) + along * lateral;
                        FVector spyXY = centre - normal * (side * far) + along * lateral;
                        float? mf = FloorZ(world, new FVector(mercXY.X, mercXY.Y, centre.Z + 40f));
                        float? sf = FloorZ(world, new FVector(spyXY.X, spyXY.Y, centre.Z + 40f));
                        if (mf is not { } mz || sf is not { } sz)
                        {
                            continue;
                        }

                        var merc = new FVector(mercXY.X, mercXY.Y, mz + 87.5f + 1f);
                        var spy = new FVector(spyXY.X, spyXY.Y, sz + 87.5f + 1f);
                        FVector away = (spy - merc).SafeNormal();
                        away = new FVector(away.X, away.Y, 0f).SafeNormal();
                        if (!Fits(world, merc) || !Fits(world, spy) || !Fits(world, spy + away * 120f) || !Fits(world, spy + away * 240f))
                        {
                            continue;
                        }
                        SMotionTrackingVision.WorldLineHits(world, null, merc, spy, SMotionTrackingVisionK.SightFlags, hits);
                        if (hits.Count == 0 || !ReferenceEquals(hits[0].Actor, glass))
                        {
                            continue;
                        }

                        string list = string.Join(", ", hits.Select(h => (h.Actor?.GetType().Name ?? "none") + "@" + h.Time.ToString("F2", CultureInfo.InvariantCulture)));
                        string kind = hits.Count == 1 ? "glass" : hits.Skip(1).All(h => h.Actor is { bSeeThroughInMotionVision: true }) ? "glasses" : "glass+wall";
                        spots.Add(new Spot($"{world.GetCollider(colliders[0]).ActorId ?? glass.ToString()} {(side > 0 ? "+" : "-")} {near:F0}/{far:F0}/{lateral:F0}", merc, spy, kind, list));
                      }
                    }
                }
            }
        }

        return spots;
    }

    [Fact]
    public void ARunningSpySeenThroughOneWindowPaneIsInSight()
    {
        PawnRig? rig = SteeE();
        Assert.SkipWhen(rig is null, "SteeE has not been converted into .content/svmap.");
        List<Spot> spots = GlassSpots(rig!);
        var report = new StringBuilder();
        foreach (Spot s in spots)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{s.Kind,-10} {s.Name,-28} merc {s.Merc} spy {s.Spy} hits {s.Hits}");
        }

        AiTestKit.SaveReport("motion-glass-spots", report.ToString());
        Spot clear = Assert.Single(spots, s => s.Name == "SVitre1935 + 250/120/0");
        RigMate spy = rig!.AddMate(PawnTeam.Spy, clear.Spy - new FVector(0f, 0f, 88.5f), 0);
        Assert.True(spy.Pawn.SetLocation(clear.Spy), "the spy could not be put at " + clear.Spy);
        var tracker = rig.Level.SpawnActor(new SMotionTrackingVision(), rig.Pawn.Location, default)!;
        Assert.True(tracker.InSight(clear.Merc, spy.Pawn), "the pane hides the spy: " + clear);

        // Without the pane's see-through flag the same line is blocked by it.
        SVitre? pane = null;
        foreach (SVitre g in rig.Level.AllActors<SVitre>())
        {
            if (pane is null || (g.Location - (clear.Merc + clear.Spy) * 0.5f).Size() < (pane.Location - (clear.Merc + clear.Spy) * 0.5f).Size())
            {
                pane = g;
            }
        }

        Assert.NotNull(pane);
        pane!.bSeeThroughInMotionVision = false;
        Assert.False(tracker.InSight(clear.Merc, spy.Pawn), "an opaque pane did not block the line");
    }
}
