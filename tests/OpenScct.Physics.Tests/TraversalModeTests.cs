using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>The GE traversal modes and the zip speed pose (spec/native/physics-traversal.md).</summary>
public sealed class TraversalModeTests
{
    private static readonly FRotator FacingY = new(0, 16384, 0);

    private static Scene Make(System.Action<CollisionDataBuilder>? geometry = null, params GERecord[] ges)
    {
        CollisionDataBuilder b = Floor();
        geometry?.Invoke(b);
        return Scene.Create(b.Build(), ges: ges);
    }

    private static void SetGE(TestSBasePawn p, FVector a, FVector b)
    {
        p.ZipP1 = a;
        p.ZipP2 = b;
        p.NewZipP1 = a;
        p.NewZipP2 = b;
    }

    // ---- vertical pipe -------------------------------------------------------------------------------------

    private static (Scene, TestSBasePawn, TestController) OnPipe(float z, float bottom = 0f, float top = 3000f,
        System.Action<CollisionDataBuilder>? geometry = null)
    {
        Scene s = Make(geometry);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, z), EPhysics.PHYS_OnPipe, c, FacingY);
        SetGE(p, V(0f, 0f, bottom), V(0f, 0f, top));
        p.m_geoNormal = V(0f, -1f, 0f);
        p.m_CurrentArmsRadius = 25.4f;
        return (s, p, c);
    }

    [Fact]
    public void PipeClimbsAtAccelerationZ()
    {
        (Scene s, TestSBasePawn p, _) = OnPipe(300f);
        p.Acceleration = V(0f, 0f, 145f);
        s.Step(60);
        Assert.Equal(300f + 145f, p.Location.Z, 1);
        Assert.Equal(145f, p.Velocity.Z, 1);
        Assert.False(p.bSimulateGravity);
        Near(V(0f, -37f, p.Location.Z), p.Location, 1e-3f);
    }

    [Fact]
    public void PipeHoldSnapsAt500AndTurnsAt16384PerSecond()
    {
        (Scene s, TestSBasePawn p, _) = OnPipe(300f);
        p.Location = V(0f, -137f, 300f);
        p.Rotation = default;
        s.Step(1);
        Assert.Equal(-137f + 500f * Sim.DeltaTime, p.Location.Y, 2);
        Assert.Equal(SvmMath.Trunc(16384f * Sim.DeltaTime), p.Rotation.Yaw);
    }

    [Fact]
    public void PipeSlideIsClampedAndTheBottomRaisesTopSynchro()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnPipe(1500f, bottom: 20f);
        p.Acceleration = V(-524f, 0f, 0f);
        s.Step(70);
        Assert.Equal(-450f, p.Velocity.Z, 1);
        for (int i = 0; i < 300 && c.TopSynchros == 0; i++)
        {
            s.Step(1);
        }

        // The bottom: (HOH zone Z + Location.Z) - pipe bottom below 210.
        Assert.True(c.TopSynchros > 0);
        Assert.True(128f + p.Location.Z - 20f < 210f + 450f * Sim.DeltaTime);
    }

    [Fact]
    public void PipeTopExitsStandingOrCrouched()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnPipe(300f, top: 500f);
        p.Acceleration = V(0f, 0f, 145f);
        s.Step(120);
        Assert.True(c.TopPipes > 0);
        Assert.Equal(500f, p.Location.Z + 90.3f, 1);

        (Scene low, TestSBasePawn q, TestController c2) = OnPipe(300f, top: 500f,
            geometry: b => b.AddBox(V(-500f, -500f, 650f), V(500f, 500f, 700f)));
        q.Acceleration = V(0f, 0f, 145f);
        low.Step(120);
        Assert.Equal(0, c2.TopPipes);
        Assert.True(c2.TopPipeCrouches > 0);
    }

    [Fact]
    public void TestTopPipeFollowsItsFourSegments()
    {
        Scene s = Make();
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, 400f), EPhysics.PHYS_OnPipe, rotation: FacingY);
        Assert.True(s.Driver.TestTopPipe(p, 0f, false));
        Assert.True(s.Driver.TestTopPipe(p, 0f, true));

        // Something to the pawn's right (-X when facing +Y) blocks the first segment.
        Scene side = Make(b => b.AddBox(V(-80f, -100f, 0f), V(-70f, 100f, 1000f)));
        TestSBasePawn q = side.SpawnSpy(V(0f, -37f, 400f), EPhysics.PHYS_OnPipe, rotation: FacingY);
        Assert.False(side.Driver.TestTopPipe(q, 0f, false));
    }

    // ---- hand over hand ------------------------------------------------------------------------------------

    private static (Scene, TestSBasePawn, TestController) OnBar(System.Action<CollisionDataBuilder>? geometry = null, float y = 0f)
    {
        Scene s = Make(geometry);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, y, 172f), EPhysics.PHYS_OnPipeH, c);
        SetGE(p, V(-300f, 0f, 300f), V(300f, 0f, 300f));
        p.m_CurrentArmsRadius = 50f;
        return (s, p, c);
    }

    [Fact]
    public void HandOverHandMovesAlongTheBarAndStops27FromTheEnd()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnBar();
        s.Step(1);
        Assert.True(c.TopSynchros > 0);
        p.Acceleration = V(126f, 0f, 0f);
        s.Step(1);
        Assert.Equal(126f * Sim.DeltaTime, p.Location.X, 2);
        Assert.Equal(126f, p.Velocity.X, 1);
        p.Acceleration = V(126f, 0f, 0f);
        for (int i = 0; i < 400; i++)
        {
            p.Acceleration = V(126f, 0f, 0f);
            s.Step(1);
        }

        Assert.Equal(300f - 27f, p.Location.X, 1);
    }

    [Fact]
    public void LegsOverStops95FromTheEnd()
    {
        (Scene s, TestSBasePawn p, _) = OnBar();
        for (int i = 0; i < 400; i++)
        {
            p.Acceleration = V(113f, 0f, 0f);
            s.Step(1);
        }

        Assert.Equal(300f - 95f, p.Location.X, 1);
    }

    [Fact]
    public void ABlockedHoldRaisesNoGE()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnBar(b => b.AddBox(V(-500f, -40f, 0f), V(500f, -30f, 400f)), y: -80f);
        s.Step(1);
        Assert.Equal(1, c.NoGEs);
    }

    // ---- ladder --------------------------------------------------------------------------------------------

    private static (Scene, TestSBasePawn, TestController) OnLadder(float z, float top = 1000f)
    {
        Scene s = Make();
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, -37.5f, z), EPhysics.PHYS_OnLadder, c, FacingY);
        SetGE(p, V(0f, 0f, 0f), V(0f, 0f, top));
        p.m_geoNormal = V(0f, -1f, 0f);
        return (s, p, c);
    }

    [Fact]
    public void LadderClimbsAndStopsOnARung()
    {
        (Scene s, TestSBasePawn p, _) = OnLadder(300f);
        p.Acceleration = V(0f, 0f, 126f);
        s.Step(30);
        Assert.Equal(300f + 63f, p.Location.Z, 1);

        // Stop on the nearest rung: 64 apart, 28 above the bottom, at 100 uu/s.
        p.Location = V(0f, -37.5f, 300f);
        p.Acceleration = V(0f, 20f, 0f);
        s.Step(1);
        Assert.Equal(300f - 100f * Sim.DeltaTime, p.Location.Z, 2);
        s.Step(30);
        Assert.Equal(28f + 64f * 4f, p.Location.Z, 1);
    }

    [Fact]
    public void LadderSlideHasItsFloorsAndTheTopRaisesTopLadder()
    {
        (Scene s, TestSBasePawn p, _) = OnLadder(900f, top: 2000f);
        p.Acceleration = V(-600f, 0f, 0f);
        s.Step(1);
        Assert.Equal(-200f, p.Velocity.Z, 1);
        s.Step(120);
        Assert.True(p.Velocity.Z >= -700f - 0.1f);

        (Scene t, TestSBasePawn q, TestController c) = OnLadder(250f, top: 400f);
        q.Acceleration = V(0f, 0f, 126f);
        t.Step(60);
        Assert.True(c.TopLadders > 0);
        Assert.Equal(400f - 100.5f, q.Location.Z, 1);
    }

    // ---- zip line ------------------------------------------------------------------------------------------

    private static (Scene, TestSBasePawn, TestController) OnZip(System.Action<CollisionDataBuilder>? geometry = null)
    {
        Scene s = Make(geometry);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(100f, 0f, 990f - 116f), EPhysics.PHYS_Linear, c);
        SetGE(p, V(0f, 0f, 1000f), V(4000f, 0f, 600f));
        p.WalkSpeed = 400f;
        return (s, p, c);
    }

    [Fact]
    public void ZipSpeedRampsToGroundSpeedAndEasesDownFromAbove()
    {
        (Scene s, TestSBasePawn p, _) = OnZip();
        // The script sets Acceleration every frame (the native overwrites it while easing).
        FVector down = V(4000f, 0f, -400f).SafeNormal();
        for (int i = 0; i < 60; i++)
        {
            p.Acceleration = down * 3000f;
            s.Step(1);
        }

        Assert.Equal(400f, p.Velocity.Size(), 0);
        Assert.True((p.Velocity | down) > 399f);

        // Above GroundSpeed: Velocity += 0.03 (GroundSpeed - s) Velocity dt.
        p.Velocity = down * 600f;
        s.Driver.physLinear(p, Sim.DeltaTime);
        Assert.Equal(600f * (1f - 0.03f * 200f * Sim.DeltaTime), p.Velocity.Size(), 0);

        // Moving uphill is stopped.
        p.Velocity = -down * 100f;
        p.Acceleration = default;
        p.WalkSpeed = 0f;
        p.GroundSpeed = 400f;
        s.Driver.physLinear(p, Sim.DeltaTime);
        Assert.True((p.Velocity | down) >= -0.01f);
    }

    [Fact]
    public void TheZipEndWallRaisesNotifyHitWall()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnZip(b => b.AddBox(V(500f, -500f, 0f), V(600f, 500f, 2000f)));
        for (int i = 0; i < 120; i++)
        {
            p.Acceleration = V(4000f, 0f, -400f).SafeNormal() * 3000f;
            s.Step(1);
        }

        Assert.True(c.HitWalls > 0);
        // physLinear sets bSimulateGravity on the hit; performPhysics then rewrites it from the mode (false on a zip).
        Assert.False(p.bSimulateGravity);
    }

    [Fact]
    public void ZipSpeedPoseKeepsTheTopOfTheCylinder()
    {
        Scene s = Make();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), EPhysics.PHYS_Linear);
        s.Driver.SetZipSpeed(p);
        Assert.Equal(45f, p.CollisionHeight);
        Assert.Equal(500f + 42.5f, p.Location.Z, 3);
        Assert.Equal(V(0f, 0f, -42.5f), p.PrePivot);
        Assert.True(p.bIsZipSpeed);
        Assert.Equal(2, p.ZoneConfig);
        Assert.Equal(2, p.ZoneConfigForClient);

        Assert.True(s.Driver.CanUnZipSpeed(p));
        Assert.Equal(45f, p.CollisionHeight);
        Assert.Equal(500f + 42.5f, p.Location.Z, 3);

        Assert.False(s.Driver.UnZipSpeed(p, bTest: false));
        Assert.Equal(87.5f, p.CollisionHeight);
        Assert.Equal(500f, p.Location.Z, 3);
        Assert.Equal(default, p.PrePivot);
        Assert.False(p.bIsZipSpeed);
        Assert.Equal(0, p.ZoneConfig);
    }

    [Fact]
    public void CannotUnZipInATightSpot()
    {
        Scene s = Make(b => b.AddBox(V(-500f, -500f, 92f), V(500f, 500f, 150f)));
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 46f - 42.5f), EPhysics.PHYS_Linear);
        p.CollisionHeight = 87.5f;
        p.Location = V(0f, 0f, 3.5f);
        s.Driver.SetZipSpeed(p);
        Assert.Equal(46f, p.Location.Z, 3);
        Assert.False(s.Driver.CanUnZipSpeed(p));
        Assert.True(s.Driver.UnZipSpeed(p, bTest: false));
        Assert.Equal(45f, p.CollisionHeight);
        Assert.Equal(46f, p.Location.Z, 3);
    }

    // ---- fence ---------------------------------------------------------------------------------------------

    private static Scene FenceScene(float thickness, params GERecord[] ges)
    {
        CollisionDataBuilder b = Floor();
        ushort mat = b.AddMaterial(0, CollisionMaterialFlags.Climbable);
        b.AddBox(V(-500f, 0f, 0f), V(500f, thickness, 600f), mat);
        return Scene.Create(b.Build(), ges: ges);
    }

    [Fact]
    public void FenceClimbStopsWhereTheFaceEnds()
    {
        Scene s = FenceScene(20f);
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, 200f), EPhysics.PHYS_OnVerticalPlane, rotation: FacingY);
        for (int i = 0; i < 400; i++)
        {
            p.Acceleration = V(0f, 0f, 120f);
            s.Step(1);
        }

        // The corner probes (H - 15 up) must stay on the face: the climb ends 72.5 below its top.
        Assert.InRange(p.Location.Z, 600f - 72.5f - 2.5f, 600f - 72.5f);
        Assert.Equal(default, p.Acceleration);
        Assert.InRange(p.Location.Y, -37.6f, -37.4f);
    }

    [Fact]
    public void FenceTopEndsTheClimb()
    {
        GERecord fence = GE(0, GEKind.FENCE, V(-400f, 0f, 600f), V(400f, 0f, 600f));
        Scene s = FenceScene(2f, fence);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, 300f), EPhysics.PHYS_OnVerticalPlane, c, FacingY);
        p.m_CurrentArmsZone = p.m_FenceArmsZone;
        p.m_CurrentArmsRadius = 25f;
        for (int i = 0; i < 400 && c.FenceTops == 0; i++)
        {
            p.Acceleration = V(0f, 0f, 120f);
            s.Step(1);
        }

        Assert.Equal(1, c.FenceTops);
        Assert.Equal(16384, c.LastEventRotation.Yaw);
    }

    // ---- ledge ---------------------------------------------------------------------------------------------

    private static (Scene, TestSBasePawn, TestController) OnLedge(byte mask = 0, params GERecord[] more)
    {
        var ges = new GERecord[more.Length + 1];
        ges[0] = new GERecord(0, "GE0", GEKind.LG, [V(-400f, 0f, 224f), V(400f, 0f, 224f)], mask, GEPlatform.Common, GESource.Synthetic());
        more.CopyTo(ges, 1);
        Scene s = Make(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 224f)), ges);
        s.Level.Info.GEActivationMask = 4;
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, -39f, 107f), EPhysics.PHYS_Ledge, c, FacingY);
        SetGE(p, V(-400f, 0f, 224f), V(400f, 0f, 224f));
        p.m_CurrentArmsZone = p.m_LedgeGrabArmsZone;
        p.m_CurrentArmsRadius = p.m_LedgeGrabArmsRadius;
        return (s, p, c);
    }

    [Fact]
    public void LedgeHoldsAndShimmiesToThe36Stop()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnLedge();
        s.Step(1);
        Assert.True(c.TopSynchros > 0);
        Near(V(0f, -39f, 107f), p.Location, 1e-3f);

        // Acceleration 80 along the right axis (-X when facing +Y).
        for (int i = 0; i < 600; i++)
        {
            p.Acceleration = V(-80f, 0f, 0f);
            s.Step(1);
        }

        // Hands stop at the 36 uu end stop (within one step, as the stop is crossed at exactly 36).
        Assert.InRange(p.Location.X, -400f + 36f - 80f * Sim.DeltaTime - 0.01f, -400f + 36f + 0.01f);
        Assert.Equal(0, c.NoGEs);
    }

    [Fact]
    public void LedgeHoldTargetsByType()
    {
        (Scene s, TestSBasePawn p, _) = OnLedge();
        p.TypeLedge = 1; // WAIST: 20 below the ledge
        p.Location = V(0f, -60f, 150f);
        for (int i = 0; i < 30; i++)
        {
            s.Step(1);
        }

        Assert.Equal(224f - 20f, p.Location.Z, 1);
        Assert.Equal(-60f, p.Location.Y, 1);
    }

    [Fact]
    public void LedgeRaisesNoGEWhenItsGEGoesAway()
    {
        (Scene s, TestSBasePawn p, TestController c) = OnLedge(mask: 4);
        s.Step(2);
        Assert.Equal(0, c.NoGEs);
        s.Level.Info.GEActivationMask = 0;
        s.Step(1);
        Assert.Equal(1, c.NoGEs);
    }

    [Fact]
    public void AnInsideCornerRaisesChangeLedge()
    {
        GERecord other = GE(1, GEKind.LG, V(400f, 0f, 224f), V(400f, 400f, 224f));
        (Scene s, TestSBasePawn p, TestController c) = OnLedge(0, other);
        p.Location = V(330f, -39f, 107f);
        for (int i = 0; i < 120 && c.ChangeLedges == 0; i++)
        {
            p.Acceleration = V(80f, 0f, 0f);
            s.Step(1);
        }

        Assert.Equal(1, c.ChangeLedges);
        Assert.Equal(1, c.LastChangeLedgeType);
    }
}
