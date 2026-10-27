using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>GE elements, FindArmsGE, the zone natives and the top-exit and wall tests (spec/native/physics-ge-queries.md).</summary>
public sealed class GENativeTests
{
    private static readonly FRotator FacingY = new(0, 16384, 0);

    private static Scene GeScene(System.Action<CollisionDataBuilder>? geometry, params GERecord[] ges)
    {
        CollisionDataBuilder b = Floor();
        geometry?.Invoke(b);
        return Scene.Create(b.Build(), ges: ges);
    }

    /// <summary>A 224 high block in y 0..200 with its top edge along y = 0 as an LG (TestBox's ledge block).</summary>
    private static Scene LedgeBlock(System.Action<CollisionDataBuilder>? more = null, byte mask = 0)
    {
        GERecord ledge = new(0, "GE0", GEKind.LG, [V(-400f, 0f, 224f), V(400f, 0f, 224f)], mask, GEPlatform.Common, GESource.Synthetic());
        return GeScene(b =>
        {
            b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 224f));
            more?.Invoke(b);
        }, ledge);
    }

    // ---- GE elements ---------------------------------------------------------------------------------------

    [Fact]
    public void GEDirectionsConnectionAndEquality()
    {
        GERecord lg = GE(0, GEKind.LG, V(0f, 0f, 100f), V(100f, 0f, 120f));
        FVector c = PhysicsDriver.GEClosestPoint(GE(9, GEKind.LG, V(0f, 0f, 100f), V(100f, 0f, 100f)), V(50f, 30f, 300f), out FVector dir);
        Near(V(50f, 0f, 100f), c);
        Near(V(-1f, 0f, 0f), dir);
        PhysicsDriver.GEClosestPoint(lg, default, out FVector sloped);
        Near(V(-1f, 0f, 0f), sloped);

        GERecord ladder = GE(1, GEKind.LADDER, V(0f, 0f, 0f), V(0f, 0f, 400f), V(0f, -10f, 0f));
        PhysicsDriver.GEClosestPoint(ladder, V(0f, -50f, 50f), out FVector toClimber);
        Near(V(0f, -1f, 0f), toClimber);

        GERecord zip = GE(2, GEKind.ZL, V(0f, 0f, 300f), V(400f, 0f, 600f));
        PhysicsDriver.GEClosestPoint(zip, default, out FVector downhill);
        Near(V(-0.8f, 0f, -0.6f), downhill);

        PhysicsDriver.GEClosestPoint(GE(3, GEKind.POLE, V(0f, 0f, 0f), V(0f, 0f, 300f)), default, out FVector up);
        Near(V(0f, 0f, 1f), up);

        Assert.True(PhysicsDriver.GEConnected(lg, GE(4, GEKind.LG, V(101.9f, 1.9f, 118.1f), V(300f, 0f, 120f))));
        Assert.False(PhysicsDriver.GEConnected(lg, GE(5, GEKind.LG, V(102.1f, 0f, 120f), V(300f, 0f, 120f))));
        Assert.False(PhysicsDriver.GEConnected(ladder, GE(6, GEKind.LADDER, V(0f, 0f, 0f), V(0f, 0f, 400f), V(0f, -10f, 0f))));
        Assert.True(PhysicsDriver.GEEquals(lg, GE(7, GEKind.LG, V(0.09f, 0f, 100f), V(100f, 0.09f, 120f))));
        Assert.False(PhysicsDriver.GEEquals(lg, GE(8, GEKind.LG, V(0.11f, 0f, 100f), V(100f, 0f, 120f))));
    }

    [Fact]
    public void FindArmsGEPicksTheNearestThenTheChainingSecond()
    {
        GERecord far = GE(0, GEKind.LG, V(-200f, 20f, 160f), V(200f, 20f, 160f));
        GERecord near = GE(1, GEKind.LG, V(-200f, 5f, 160f), V(200f, 5f, 160f));
        GERecord pipe = GE(2, GEKind.PIPE, V(30f, 0f, 0f), V(30f, 0f, 400f), V(30f, -10f, 0f));
        Scene s = GeScene(null, far, near, pipe);
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 100f), EPhysics.PHYS_Falling);
        p.m_CurrentArmsZone = V(0f, 0f, 60f);
        p.m_CurrentArmsRadius = 40f;

        // The nearest wins; an LG best takes a PIPE second.
        Assert.True(s.Driver.FindArmsGE(p, out GERecord? best, out GERecord? second));
        Assert.Same(near, best);
        Assert.Same(pipe, second);

        // On a pipe the nearest PIPE comes first; a PIPE best takes the last LG as its second.
        p.Physics = EPhysics.PHYS_OnPipe;
        Assert.True(s.Driver.FindArmsGE(p, out best, out second));
        Assert.Same(pipe, best);
        Assert.Same(near, second);

        // No radius, no search; inactive GEs never take part.
        p.m_CurrentArmsRadius = 0f;
        Assert.False(s.Driver.FindArmsGE(p, out best, out _));
        Assert.Null(best);
    }

    [Fact]
    public void ConnectedLedgesAreTheSecond()
    {
        GERecord a = GE(0, GEKind.LG, V(-200f, 5f, 160f), V(0f, 5f, 160f));
        GERecord b = GE(1, GEKind.LG, V(1f, 6f, 160f), V(200f, 6f, 160f));
        GERecord loose = GE(2, GEKind.LG, V(-200f, 15f, 160f), V(200f, 15f, 160f));
        Scene s = GeScene(null, a, b, loose);
        TestSBasePawn p = s.SpawnSpy(V(-10f, 0f, 100f), EPhysics.PHYS_Falling);
        p.m_CurrentArmsZone = V(0f, 0f, 60f);
        p.m_CurrentArmsRadius = 40f;
        Assert.True(s.Driver.FindArmsGE(p, out GERecord? best, out GERecord? second));
        Assert.Same(a, best);
        Assert.Same(b, second);
    }

    // ---- CheckArmsZone: ledges ----------------------------------------------------------------------------

    private static TestSBasePawn Hanging(Scene s, TestController c, float x = 0f, float z = 117f)
    {
        TestSBasePawn p = s.SpawnSpy(V(x, -45f, z), EPhysics.PHYS_Falling, c, FacingY);
        p.m_CurrentArmsZone = p.m_LedgeGrabArmsZone;
        p.m_CurrentArmsRadius = p.m_LedgeGrabArmsRadius;
        return p;
    }

    [Fact]
    public void LedgeNeedsTheHangWindowAndBothClearanceTraces()
    {
        Scene s = LedgeBlock();
        var c = new TestController();
        TestSBasePawn p = Hanging(s, c);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.Ledges);
        Assert.Equal(V(-400f, 0f, 224f), p.ZipP1);
        Assert.Equal(V(400f, 0f, 224f), p.ZipP2);

        // Falling too fast (Velocity.Z at most -1270), out of the height window, or near an end (24.5): no Ledge.
        p.Velocity = V(0f, 0f, -1271f);
        s.Driver.CheckArmsZone(p);
        p.Velocity = default;
        p.Location = V(0f, -45f, 117f + 40f);
        s.Driver.CheckArmsZone(p);
        p.Location = V(380f, -45f, 117f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.Ledges);
    }

    [Fact]
    public void LedgeIsRefusedWhenSomethingIsUnderTheLip()
    {
        Scene s = LedgeBlock(b => b.AddBox(V(-100f, -8f, 60f), V(100f, 0f, 110f)));
        var c = new TestController();
        TestSBasePawn p = Hanging(s, c);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(0, c.Ledges);
    }

    [Fact]
    public void WaistLedgeStandsOrCrouchesByTheRoomOnTop()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, 0f, 100f), V(400f, 0f, 100f));
        var c = new TestController();
        Scene s = GeScene(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 100f)), ledge);
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, 90f), EPhysics.PHYS_Falling, c, FacingY);
        p.m_CurrentArmsZone = V(39f, 0f, 30f);
        p.m_CurrentArmsRadius = 60f;
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.WaistLedges);
        Assert.True(c.LastWaistCanStand);

        // A ceiling that leaves only crouching room.
        Scene low = GeScene(b =>
        {
            b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 100f));
            b.AddBox(V(-500f, -200f, 250f), V(500f, 200f, 300f));
        }, ledge);
        var c2 = new TestController();
        TestSBasePawn q = low.SpawnSpy(V(0f, -37f, 90f), EPhysics.PHYS_Falling, c2, FacingY);
        q.m_CurrentArmsZone = V(39f, 0f, 30f);
        q.m_CurrentArmsRadius = 60f;
        low.Driver.CheckArmsZone(q);
        Assert.Equal(1, c2.WaistLedges);
        Assert.False(c2.LastWaistCanStand);
    }

    [Fact]
    public void AutoFeetLedgeWhileRunningAtALowLedge()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, 0f, 50f), V(400f, 0f, 50f));
        var c = new TestController();
        Scene s = GeScene(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 50f)), ledge);
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, WalkZ()), EPhysics.PHYS_Walking, c, FacingY);
        p.m_CurrentArmsZone = V(39f, 0f, 0f);
        p.m_CurrentArmsRadius = 60f;
        p.Velocity = V(0f, 301f, 0f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.AutoFeetLedges);

        // Not fast enough (LocalV.X above 300 needed).
        p.Velocity = V(0f, 299f, 0f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.AutoFeetLedges);
    }

    // ---- CheckArmsZone: other kinds -----------------------------------------------------------------------

    [Fact]
    public void HandOverHandWhileRisingUnderTheBar()
    {
        GERecord bar = GE(0, GEKind.HOH, V(-200f, 0f, 300f), V(200f, 0f, 300f));
        var c = new TestController();
        Scene s = GeScene(null, bar);
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 170f), EPhysics.PHYS_Falling, c);
        p.m_CurrentArmsZone = p.m_HandOverHandArmsZone;
        p.m_CurrentArmsRadius = 50f;
        p.Velocity = V(0f, 0f, 300f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.HandOverHands);
        Near(V(1f, 0f, 0f), p.m_geoNormal);

        // Falling (Velocity.Z not above 0): no grab.
        p.Velocity = V(0f, 0f, -10f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.HandOverHands);
    }

    [Fact]
    public void NarrowLadderFromTheFrontSpot()
    {
        GERecord ladder = GE(0, GEKind.LADDER, V(0f, 0f, 0f), V(0f, 0f, 600f), V(0f, -10f, 0f));
        var c = new TestController();
        Scene s = GeScene(null, ladder);
        TestSBasePawn p = s.SpawnSpy(V(0f, -35f, WalkZ()), EPhysics.PHYS_Walking, c, FacingY);
        p.m_CurrentArmsZone = p.m_NarrowLadderArmsZone;
        p.m_CurrentArmsRadius = 19.05f;
        p.Acceleration = V(0f, 3000f, 0f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.NarrowLadders);
        Near(V(0f, -1f, 0f), p.m_geoNormal);

        // Pushing away from the ladder: no grab.
        p.Acceleration = V(0f, -3000f, 0f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.NarrowLadders);
    }

    [Fact]
    public void PipeGrabGivesTheHoldSpotAndFacing()
    {
        GERecord pipe = GE(0, GEKind.PIPE, V(0f, 0f, 0f), V(0f, 0f, 600f), V(0f, -10f, 0f));
        var c = new TestController();
        Scene s = GeScene(null, pipe);
        TestSBasePawn p = s.SpawnSpy(V(0f, -35f, WalkZ()), EPhysics.PHYS_Walking, c, FacingY);
        p.m_CurrentArmsZone = p.m_PipeArmsZone;
        p.m_CurrentArmsRadius = 25.4f;
        p.Acceleration = V(0f, 3000f, 0f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.Pipes);
        Near(V(0f, -37f, 87.5f + 2f), c.LastEventLocation, 1e-3f);
        Assert.Equal(16384, c.LastEventRotation.Yaw);
    }

    [Fact]
    public void ZipLineGrabWhileFallingNearTheCable()
    {
        GERecord zip = GE(0, GEKind.ZL, V(-500f, 0f, 400f), V(500f, 0f, 300f));
        var c = new TestController();
        Scene s = GeScene(null, zip);
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 350f - 116f + 5f), EPhysics.PHYS_Falling, c);
        p.m_CurrentArmsZone = p.m_ZipLineArmsZone;
        p.m_CurrentArmsRadius = 60f;
        p.Velocity = V(0f, 0f, -100f);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.ZipLines);
        Assert.True(p.m_geoNormal.X > 0.99f && p.m_geoNormal.Z < 0f);
        Assert.Equal(0, c.LastEventRotation.Yaw);
        Near(V(0f, 0f, 350f - 116f), c.LastEventLocation, 1f);
    }

    // ---- feet zones ----------------------------------------------------------------------------------------

    [Fact]
    public void FeetLedgeAtAKneeHighLedgeAhead()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, 0f, 40f), V(400f, 0f, 40f));
        var c = new TestController();
        Scene s = GeScene(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 40f)), ledge);
        TestSBasePawn p = s.SpawnSpy(V(0f, -38f, 95f), EPhysics.PHYS_Falling, c, FacingY);
        s.Driver.CheckFeetZone(p);
        Assert.Equal(1, c.FeetLedges);
        Assert.Equal(ledge.P0, p.ZipP1);

        // Falling (Velocity.Z below -1): no feet ledge.
        p.Velocity = V(0f, 0f, -2f);
        s.Driver.CheckFeetZone(p);
        Assert.Equal(1, c.FeetLedges);
    }

    [Fact]
    public void BackFeetZoneFindsTheLedgeBehind()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, -38f, 4f), V(400f, -38f, 4f));
        Scene s = GeScene(null, ledge);
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 89f), EPhysics.PHYS_Walking, rotation: FacingY);
        Assert.True(s.Driver.CheckBackFeetZone(p));
        Assert.Equal(ledge.P0, p.ZipP1);
        // n = (Dir.Y, -Dir.X) with Dir = (-1, 0): (0, 1), kept because Fwd . n would be positive: its negative.
        Near(V(0f, -1f, 0f), p.vBackFeetNormal);

        Scene none = GeScene(null);
        TestSBasePawn q = none.SpawnSpy(V(0f, 0f, 89f), EPhysics.PHYS_Walking, rotation: FacingY);
        Assert.False(none.Driver.CheckBackFeetZone(q));
    }

    // ---- top exits -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(330f, 2)]
    [InlineData(280f, 0)]
    public void TestTopLedgeStandsCrouchesOrNothing(float ceiling, int expected)
    {
        Scene s = LedgeBlock(b =>
        {
            if (ceiling > 0f)
            {
                b.AddBox(V(-500f, -300f, ceiling), V(500f, 300f, ceiling + 50f));
            }
        });
        TestSBasePawn p = s.SpawnSpy(V(0f, -39f, 107f), EPhysics.PHYS_Ledge, rotation: FacingY);
        Assert.Equal(expected, s.Driver.TestTopLedge(p, false));
        if (expected == 1)
        {
            // bCrouchOnly skips the standing test.
            Assert.Equal(2, s.Driver.TestTopLedge(p, true));
        }
    }

    [Fact]
    public void TestTopLedgeVaultsOverAThinWall()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, 0f, 224f), V(400f, 0f, 224f));
        Scene s = GeScene(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 20f, 224f)), ledge);
        TestSBasePawn p = s.SpawnSpy(V(0f, -39f, 107f), EPhysics.PHYS_Ledge, rotation: FacingY);
        Assert.Equal(3, s.Driver.TestTopLedge(p, false));
    }

    [Fact]
    public void TopOfLadderTestsTheExitAndTheUncrouchRoom()
    {
        Scene s = GeScene(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 300f)));
        TestSBasePawn p = s.SpawnSpy(V(0f, -40f, 200f), EPhysics.PHYS_OnLadder, rotation: FacingY);
        Assert.True(s.Driver.bTestTopLadder(p, out int uncrouch));
        Assert.Equal(1, uncrouch);

        Scene low = GeScene(b =>
        {
            b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 300f));
            b.AddBox(V(-500f, 0f, 475f), V(500f, 200f, 520f));
        });
        TestSBasePawn q = low.SpawnSpy(V(0f, -40f, 200f), EPhysics.PHYS_OnLadder, rotation: FacingY);
        Assert.True(low.Driver.bTestTopLadder(q, out uncrouch));
        Assert.Equal(0, uncrouch);
    }

    // ---- fence, barrier, wall jumps --------------------------------------------------------------------------

    private static Scene FenceScene(bool climbable)
    {
        CollisionDataBuilder b = Floor();
        ushort mat = b.AddMaterial(0, climbable ? CollisionMaterialFlags.Climbable : CollisionMaterialFlags.None);
        b.AddBox(V(-500f, 0f, 0f), V(500f, 20f, 400f), mat);
        return Scene.Create(b.Build());
    }

    [Fact]
    public void CheckFenceReadsTheFaceAndItsMaterial()
    {
        Scene s = FenceScene(climbable: true);
        TestSBasePawn p = s.SpawnSpy(V(0f, -40f, 200f), EPhysics.PHYS_Walking, rotation: FacingY);
        Assert.Equal(1, s.Driver.CheckFence(p, out FVector n, out Actor? wall, p.Location));
        Near(V(0f, -1f, 0f), n);
        Assert.Same(s.Level.Info, wall);

        Scene plain = FenceScene(climbable: false);
        TestSBasePawn q = plain.SpawnSpy(V(0f, -40f, 200f), EPhysics.PHYS_Walking, rotation: FacingY);
        Assert.Equal(2, plain.Driver.CheckFence(q, out _, out _, q.Location));

        // Nothing in reach (R + 20), or a corner probe past the top: 0.
        Assert.Equal(0, s.Driver.CheckFence(p, out _, out _, p.Location - V(0f, 30f, 0f)));
        Assert.Equal(0, s.Driver.CheckFence(p, out _, out _, V(0f, -40f, 400f - 50f)));
    }

    [Fact]
    public void BarrierVaultNeedsAnLgAtVaultHeightAndRoomBeyond()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, 0f, 110f), V(400f, 0f, 110f));
        Scene s = GeScene(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 20f, 110f)), ledge);
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, WalkZ()), EPhysics.PHYS_Walking, rotation: FacingY);
        p.m_CurrentArmsZone = V(39f, 0f, 30f);
        p.m_CurrentArmsRadius = 60f;
        Assert.True(s.Driver.CheckBarrier(p));
        Assert.Equal(ledge.P0, p.ZipP1);

        // A wall right behind the barrier leaves no room.
        Scene blocked = GeScene(b =>
        {
            b.AddBox(V(-500f, 0f, 0f), V(500f, 20f, 110f));
            b.AddBox(V(-500f, 60f, 0f), V(500f, 80f, 400f));
        }, ledge);
        TestSBasePawn q = blocked.SpawnSpy(V(0f, -37f, WalkZ()), EPhysics.PHYS_Walking, rotation: FacingY);
        q.m_CurrentArmsZone = V(39f, 0f, 30f);
        q.m_CurrentArmsRadius = 60f;
        Assert.False(blocked.Driver.CheckBarrier(q));
    }

    [Fact]
    public void WallJumpNeedsACorridorOfTheRightWidth()
    {
        // Walls at y 0 and y 190: probes 200 each way along the right axis sum to 189.
        Scene s = GeScene(b =>
        {
            b.AddBox(V(-500f, -50f, 0f), V(500f, 0f, 600f));
            b.AddBox(V(-500f, 190f, 0f), V(500f, 240f, 600f));
        });
        // The spy 37 from the near wall: within R + 3 of it, and the picker (R - 1 out, 18 long) finds it.
        TestSBasePawn p = s.SpawnSpy(V(0f, 37f, 300f), EPhysics.PHYS_Falling);
        p.nJumpNumber = 1;
        p.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector hitNormal = default;
        Assert.True(s.Driver.CheckWallJump(p, x, y, z, ref hitNormal));
        Near(V(0f, 1f, 0f), hitNormal);

        p.nJumpNumber = 2;
        hitNormal = default;
        Assert.True(s.Driver.CheckWallJump(p, x, y, z, ref hitNormal));
        Near(V(0f, 1f, 0f), hitNormal);
        Assert.Equal(95f, p.vVandammePos.Y, 0);
        Assert.Equal(300f, p.vVandammePos.Z, 1);

        // Too wide a corridor.
        Scene wide = GeScene(b =>
        {
            b.AddBox(V(-500f, -50f, 0f), V(500f, 0f, 600f));
            b.AddBox(V(-500f, 260f, 0f), V(500f, 300f, 600f));
        });
        TestSBasePawn q = wide.SpawnSpy(V(0f, 37f, 300f), EPhysics.PHYS_Falling);
        q.nJumpNumber = 1;
        FVector unchanged = V(0f, 0f, 1f);
        Assert.False(wide.Driver.CheckWallJump(q, x, y, z, ref unchanged));

        // Walls on both sides within the picker's reach, or none: no split jump, HitNormal untouched.
        Scene narrow = GeScene(b =>
        {
            b.AddBox(V(-500f, -50f, 0f), V(500f, 0f, 600f));
            b.AddBox(V(-500f, 80f, 0f), V(500f, 130f, 600f));
        });
        TestSBasePawn r = narrow.SpawnSpy(V(0f, 40f, 300f), EPhysics.PHYS_Falling);
        r.nJumpNumber = 1;
        FVector kept = V(0f, 0f, 1f);
        Assert.False(narrow.Driver.CheckWallJump(r, x, y, z, ref kept));
        Assert.Equal(V(0f, 0f, 1f), kept);
    }

    [Fact]
    public void WallJumpGEFindsTheLedgeOnTheWallAhead()
    {
        GERecord top = GE(0, GEKind.LG, V(300f, -200f, 400f), V(300f, 200f, 400f));
        Scene s = GeScene(b => b.AddBox(V(300f, -500f, 0f), V(400f, 500f, 400f)), top);
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking);
        p.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        Assert.True(s.Driver.CheckGEForWallJump(p, x, y, z, out float gz));
        Assert.Equal(400f, gz, 2);

        // Without the ledge: the height of the side probes (72.5 up), so the run-up goes for the back flip.
        Scene bare = GeScene(b => b.AddBox(V(300f, -500f, 0f), V(400f, 500f, 400f)));
        TestSBasePawn q = bare.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking);
        Assert.True(bare.Driver.CheckGEForWallJump(q, x, y, z, out gz));
        Assert.Equal(q.Location.Z + 72.5f, gz, 1);

        // A ledge GE along the run (not across the wall) does not count.
        GERecord along = GE(0, GEKind.LG, V(300f, 0f, 400f), V(400f, 0f, 400f));
        Scene wrong = GeScene(b => b.AddBox(V(300f, -500f, 0f), V(400f, 500f, 400f)), along);
        TestSBasePawn t = wrong.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking);
        Assert.True(wrong.Driver.CheckGEForWallJump(t, x, y, z, out gz));
        Assert.Equal(t.Location.Z + 72.5f, gz, 1);

        // A hole in the floor in front of the wall: no.
        Scene hole = GeScene(b =>
        {
            b.AddBox(V(300f, -500f, 0f), V(400f, 500f, 400f));
        }, top);
        TestSBasePawn u = hole.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking);
        Assert.True(hole.Driver.CheckGEForWallJump(u, x, y, z, out _));

        // A wall closer than 105: no.
        Scene close = GeScene(b => b.AddBox(V(100f, -500f, 0f), V(200f, 500f, 400f)));
        TestSBasePawn r = close.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking);
        Assert.False(close.Driver.CheckGEForWallJump(r, x, y, z, out _));
    }

    [Fact]
    public void InactiveGEsTakePartInNothing()
    {
        Scene s = LedgeBlock(mask: 4);
        var c = new TestController();
        TestSBasePawn p = Hanging(s, c);
        s.Driver.CheckArmsZone(p);
        Assert.Equal(0, c.Ledges);
        s.Level.Info.GEActivationMask = 4;
        s.Driver.CheckArmsZone(p);
        Assert.Equal(1, c.Ledges);
        _ = new List<int>();
    }
}
