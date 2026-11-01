using System;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>More decoded rules: ledge stop, simulated walking, falling slides and landings, feet tests, chains.</summary>
public sealed class MoreRuleTests
{
    private static readonly FRotator FacingY = new(0, 16384, 0);

    private static Scene Make(Action<CollisionDataBuilder>? geometry = null, params GERecord[] ges)
    {
        CollisionDataBuilder b = Floor();
        geometry?.Invoke(b);
        return Scene.Create(b.Build(), ges: ges);
    }

    // ---- walking -------------------------------------------------------------------------------------------

    [Fact]
    public void WithAnAttackTargetTheLedgeStopKeepsThePawnOnTheEdge()
    {
        Scene s = Make(b => b.AddBox(V(-1000f, -500f, 0f), V(0f, 500f, 200f)));
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(-200f, 0f, StandZ(200f) + 1f), EPhysics.PHYS_Falling, c);
        s.Settle(p);
        TestSBasePawn target = s.SpawnSpy(V(-600f, 300f, StandZ(200f) + 1f), EPhysics.PHYS_Falling);
        p.AttackTarget = target;
        p.bCanWalkOffLedges = false;
        p.Acceleration = V(3000f, 0f, 0f);
        for (int i = 0; i < 90; i++)
        {
            p.Acceleration = V(3000f, 0f, 0f);
            s.Step(1);
        }

        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        Assert.True(p.Location.X <= 35f, $"walked off to {p.Location}");
        Assert.Equal(0, c.MyFallings);

        // Without an attack target the native turns bCanWalkOffLedges back on.
        p.AttackTarget = null;
        s.Step(1);
        Assert.True(p.bCanWalkOffLedges);
    }

    [Fact]
    public void SimulatedWalkingFollowsTheRootDeltaWithoutAController()
    {
        Scene s = Make(b => b.AddBox(V(100f, -500f, 0f), V(2000f, 500f, 20f)));
        TestSBasePawn p = s.Spawn(new TestSBasePawn(), V(0f, 0f, WalkZ()), EPhysics.PHYS_RootMotion, noController: true);
        p.bJustTeleported = false;
        p.RootMesh = new TestRootMotionMesh { DeltaPerCall = V(5f, 0f, 0f) };
        p.bUseRootMotionAdditionnalPhysics = true;
        p.RootMotionAdditionnalPhysics = EPhysics.PHYS_Walking;
        s.Step(60);
        // Up the 20 uu step on the way and on along it.
        Assert.Equal(300f, p.Location.X, 0);
        Assert.InRange(p.Location.Z, StandZ(20f), StandZ(20f) + 1.6f);
    }

    // ---- falling -------------------------------------------------------------------------------------------

    [Fact]
    public void AFallAlongAWallSlidesAndKeepsFalling()
    {
        Scene s = Make(b => b.AddBox(V(100f, -500f, 0f), V(200f, 500f, 2000f)));
        var c = new TestController();
        TestPawn p = s.SpawnPawn(V(0f, 0f, 1000f), EPhysics.PHYS_Falling, c);
        p.Velocity = V(400f, 300f, 0f);
        s.Step(30);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        // The falling pre-probe slides off the wall within one move of it.
        Assert.InRange(p.Location.X, 100f - 35f - 7f, 100f - 35f);
        Assert.True(p.Location.Y > 100f, $"no slide: {p.Location}");
        Assert.True(p.Velocity.Z < -400f);
    }

    [Fact]
    public void ANarrowTopPushesThePawnOff()
    {
        // A post 20 wide: the pawn lands on it, the landing probe (0.9 R box, 0.2 H + 8 down) finds it, so it lands.
        Scene wide = Make(b => b.AddBox(V(-10f, -10f, 0f), V(10f, 10f, 300f)));
        var c = new TestController();
        TestPawn p = wide.SpawnPawn(V(0f, 0f, 600f), EPhysics.PHYS_Falling, c);
        wide.Step(60);
        Assert.Equal(1, c.MyLandeds);

        // A post 2 wide caught by the edge of the box only: the landing probe misses, the side probes slide it off.
        Scene thin = Make(b => b.AddBox(V(32f, -1f, 0f), V(34f, 1f, 300f)));
        var c2 = new TestController();
        TestPawn q = thin.SpawnPawn(V(0f, 0f, 600f), EPhysics.PHYS_Falling, c2);
        thin.Step(120);
        Assert.True(q.Location.Z < 300f, $"stuck on the post at {q.Location}");
    }

    [Fact]
    public void ARootMotionFallDropAttacksTheFirstTargetInTheWindow()
    {
        Scene s = Make();
        var targetController = new TestController { CanBeDropAttacked = true };
        TestSBasePawn merc = s.Spawn(new TestSBasePawn { Team = PawnType.SPAWNDEFENSE }, V(100f, 0f, StandZ() + 1f), EPhysics.PHYS_Falling,
            targetController);
        s.Settle(merc);
        var c = new TestController();
        TestSBasePawn spy = s.SpawnSpy(V(0f, 0f, merc.Location.Z + 100f), EPhysics.PHYS_RootMotion, c);
        spy.RootMesh = new TestRootMotionMesh { DeltaPerCall = V(0f, 0f, -20f) };
        spy.bUseRootMotionAdditionnalPhysics = true;
        spy.RootMotionAdditionnalPhysics = EPhysics.PHYS_Falling;
        spy.Velocity = V(0f, 0f, -900f);
        s.Driver.physFallingInRootMotion(spy, Sim.DeltaTime, 0, default);
        Assert.Equal(1, c.DropAttacks);
        Assert.Same(merc, c.LastDropAttack);
    }

    // ---- feet tests from the top ---------------------------------------------------------------------------

    [Fact]
    public void LadderFeetFromTheTopGivesTheSpot()
    {
        // A ladder up the -y face of a 300 high block; the pawn stands on top facing out over it (-y).
        GERecord ladder = GE(0, GEKind.LADDER, V(0f, -5f, 0f), V(0f, -5f, 300f), V(0f, -15f, 0f));
        Scene s = Make(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 400f, 300f)), ladder);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, 60f, WalkZ(300f)), EPhysics.PHYS_Walking, c, new FRotator(0, -16384, 0));
        p.m_CurrentFeetZone = V(39f, 2f, -85f);
        p.Acceleration = V(0f, -3000f, 0f);
        s.Driver.CheckFeetZone(p);
        Assert.Equal(1, c.TopLadders);
        Near(V(0f, -1f, 0f), p.m_geoNormal);
        Assert.Equal(-5f + (72f - 37.5f), p.vVandammePos.Y, 2);
    }

    [Fact]
    public void PipeFeetFromTheTopGivesTheSpotAndSide()
    {
        GERecord pipe = GE(0, GEKind.PIPE, V(0f, -10f, 0f), V(0f, -10f, 300f), V(0f, -20f, 0f));
        Scene s = Make(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 400f, 300f)), pipe);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, 40f, WalkZ(300f)), EPhysics.PHYS_Walking, c, new FRotator(0, -16384, 0));
        p.Acceleration = V(0f, -3000f, 0f);
        s.Driver.CheckFeetZone(p);
        Assert.Equal(1, c.TopPipes);
        Assert.Equal(-10f + 35f + 15f, p.vVandammePos.Y, 2);
        Assert.Equal(p.Location.Z + 2f, p.vVandammePos.Z, 2);
    }

    // ---- chains between GE modes ---------------------------------------------------------------------------

    [Fact]
    public void ClimbingUpAPipeIntoAZipLineRaisesZipLine()
    {
        GERecord zip = GE(1, GEKind.ZL, V(0f, -37f, 520f), V(1000f, -37f, 400f));
        Scene s = Make(null, GE(0, GEKind.PIPE, V(0f, 0f, 0f), V(0f, 0f, 3000f), V(0f, -10f, 0f)), zip);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, 300f), EPhysics.PHYS_OnPipe, c, FacingY);
        p.ZipP1 = V(0f, 0f, 0f);
        p.ZipP2 = V(0f, 0f, 3000f);
        p.NewZipP1 = p.ZipP1;
        p.NewZipP2 = p.ZipP2;
        p.m_geoNormal = V(0f, -1f, 0f);
        p.m_CurrentArmsRadius = 60f;
        for (int i = 0; i < 300 && c.ZipLines == 0; i++)
        {
            p.Acceleration = V(0f, 0f, 145f);
            s.Step(1);
        }

        Assert.Equal(1, c.ZipLines);
        Assert.Equal(zip.P0, p.ZipP1);
    }

    [Fact]
    public void HandOverHandToAPerpendicularBarRaisesOrthoPipeH()
    {
        GERecord cross = GE(1, GEKind.HOH, V(300f, 0f, 300f), V(300f, 300f, 300f));
        Scene s = Make(null, GE(0, GEKind.HOH, V(-300f, 0f, 300f), V(300f, 0f, 300f)), cross);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 172f), EPhysics.PHYS_OnPipeH, c);
        p.ZipP1 = V(-300f, 0f, 300f);
        p.ZipP2 = V(300f, 0f, 300f);
        p.NewZipP1 = p.ZipP1;
        p.NewZipP2 = p.ZipP2;
        p.m_CurrentArmsRadius = 50f;
        for (int i = 0; i < 300 && c.OrthoPipeHs == 0; i++)
        {
            p.Acceleration = V(126f, 0f, 0f);
            s.Step(1);
        }

        Assert.Equal(1, c.OrthoPipeHs);
        Assert.Equal(cross.P0, p.NewZipP1);
    }

    [Fact]
    public void SlowingDownALadderSnapsToEachRungWithTopSynchro()
    {
        Scene s = Make();
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, -37.5f, 600f), EPhysics.PHYS_OnLadder, c, FacingY);
        p.ZipP1 = V(0f, 0f, 0f);
        p.ZipP2 = V(0f, 0f, 1000f);
        p.m_geoNormal = V(0f, -1f, 0f);
        p.Velocity = V(0f, 0f, -300f);
        for (int i = 0; i < 120 && c.TopSynchros == 0; i++)
        {
            p.Acceleration = V(100f, 0f, 0f);
            s.Step(1);
        }

        Assert.True(c.TopSynchros > 0);
        float h = p.Location.Z - 28f;
        Assert.Equal(0f, h - 64f * SvmMath.Round(h / 64f), 1);
    }

    [Fact]
    public void AnOutsideCornerNeedsRoomAroundIt()
    {
        // The current ledge along x ends at x 400; the other runs along y on the far side of the block's corner.
        GERecord current = GE(0, GEKind.LG, V(-400f, 0f, 224f), V(400f, 0f, 224f));
        GERecord other = GE(1, GEKind.LG, V(400f, 0f, 224f), V(400f, 400f, 224f));
        foreach (bool blocked in new[] { false, true })
        {
            Scene s = Make(b =>
            {
                b.AddBox(V(-500f, 0f, 0f), V(400f, 200f, 224f));
                if (blocked)
                {
                    b.AddBox(V(420f, -60f, 0f), V(460f, -20f, 400f));
                }
            }, current, other);
            var c = new TestController();
            TestSBasePawn p = s.SpawnSpy(V(330f, -39f, 107f), EPhysics.PHYS_Ledge, c, FacingY);
            p.ZipP1 = current.P0;
            p.ZipP2 = current.P1;
            p.m_CurrentArmsZone = p.m_LedgeGrabArmsZone;
            p.m_CurrentArmsRadius = p.m_LedgeGrabArmsRadius;
            for (int i = 0; i < 120 && c.ChangeLedges == 0; i++)
            {
                p.Acceleration = V(80f, 0f, 0f);
                s.Step(1);
            }

            if (blocked)
            {
                Assert.Equal(0, c.ChangeLedges);
            }
            else
            {
                Assert.Equal(1, c.ChangeLedges);
                Assert.Equal(3, c.LastChangeLedgeType);
            }
        }
    }

    [Fact]
    public void TraversalTicksDoNotAllocate()
    {
        GERecord ledge = GE(0, GEKind.LG, V(-400f, 0f, 224f), V(400f, 0f, 224f));
        GERecord pipe = GE(1, GEKind.PIPE, V(1000f, 0f, 0f), V(1000f, 0f, 3000f), V(1000f, -10f, 0f));
        Scene s = Make(b => b.AddBox(V(-500f, 0f, 0f), V(500f, 200f, 224f)), ledge, pipe);
        TestSBasePawn hanger = s.SpawnSpy(V(0f, -39f, 107f), EPhysics.PHYS_Ledge, rotation: FacingY);
        hanger.ZipP1 = ledge.P0;
        hanger.ZipP2 = ledge.P1;
        hanger.m_CurrentArmsZone = hanger.m_LedgeGrabArmsZone;
        hanger.m_CurrentArmsRadius = hanger.m_LedgeGrabArmsRadius;
        TestSBasePawn climber = s.SpawnSpy(V(1000f, -37f, 300f), EPhysics.PHYS_OnPipe, rotation: FacingY);
        climber.ZipP1 = pipe.P0;
        climber.ZipP2 = pipe.P1;
        climber.NewZipP1 = pipe.P0;
        climber.NewZipP2 = pipe.P1;
        climber.m_geoNormal = V(0f, -1f, 0f);
        climber.m_CurrentArmsRadius = 25.4f;
        TestSBasePawn jumper = s.SpawnSpy(V(-1500f, 0f, StandZ() + 1f), EPhysics.PHYS_Falling);
        s.Settle(jumper);

        void Drive(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                int phase = (int)(s.Level.TickCount % 120);
                float dir = phase < 60 ? 1f : -1f;
                hanger.Acceleration = V(80f * dir, 0f, 0f);
                climber.Acceleration = V(0f, 0f, 145f * dir);
                if (phase == 0 && jumper.Physics == EPhysics.PHYS_Walking)
                {
                    s.Driver.MyDoJump(jumper);
                }

                s.Driver.CheckArmsZone(jumper);
                s.Driver.CheckFeetZone(jumper);
                s.Step(1);
            }
        }

        Drive(240);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Drive(480);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
