using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/mover.md encroachment: what a mover does when its motion runs into an actor.</summary>
public sealed class MoverEncroachTests
{
    private const float Tick = 1f / 60f;

    /// <summary>A shutter 200 wide and 20 thick at height 200 that slides down 200 uu in 1 s onto a standing spot.</summary>
    private static (DeviceScene S, Mover M, Sender Switch) Shutter(EMoverEncroachType type, float stayOpen = 2f, int encroachDamage = 0)
    {
        DeviceScene s = DeviceScene.Create(ENetMode.NM_DedicatedServer);
        s.Floor();
        Sender sw = s.Add(new Sender());
        Mover m = s.Add(new Mover
        {
            InitialState = "TriggerOpenTimed",
            MoverEncroachType = type,
            StayOpenTime = stayOpen,
            EncroachDamage = encroachDamage,
            BasePos = V.At(0f, 0f, 300f),
        }, V.At(0f, 0f, 300f), tag: "shutter");
        m.KeyPos[1] = V.At(0f, 0f, -200f);
        s.Collider(m, V.At(-10f, -100f, -100f), V.At(10f, 100f, 100f));
        s.Begin();
        return (s, m, sw);
    }

    private static float Start(DeviceScene s, Sender sw, Pawn? instigator = null)
    {
        sw.FireNextTick("shutter", instigator);
        s.Step();
        return sw.FiredAt;
    }

    [Fact]
    public void WithNobodyInTheWayTheShutterRunsItsCycle()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_ReturnWhenEncroach);
        Start(s, sw);
        s.StepSeconds(1.2f);
        Assert.Equal(100f, m.Location.Z, 2);
        s.StepSeconds(3.2f);
        Assert.Equal(300f, m.Location.Z, 2);
    }

    [Fact]
    public void ReturnTypeGoesBackToItsStartAndStaysThereIdle()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_ReturnWhenEncroach);
        TestSpy spy = s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        Start(s, sw);
        s.StepSeconds(0.9f);

        // Blocked on the way down: back to key 0 (the leg reversed), then 1 s, then idle in its state with no code.
        Assert.True(m.IsInState("s_ReturnNoEncroach") || m.Location.Z > 299f);
        s.StepSeconds(3f);
        Assert.True(m.IsInState("TriggerOpenTimed"));
        Assert.Equal(300f, m.Location.Z, 2);
        Assert.False(m.bInterpolating);
        Assert.Equal(0, m.StateFrame.Pc);
        s.StepSeconds(5f);
        Assert.Equal(300f, m.Location.Z, 2);
        Assert.Empty(((TestController)spy.Controller!).PawnDamage);
    }

    [Fact]
    public void WaitAndRetryReopensWaitsASecondAndTriesAgain()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_WaitAndRetryWhenEncroach);
        TestSpy spy = s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        Start(s, sw);
        int retries = 0;
        bool waiting = false;
        for (int i = 0; i < 8 * 60; i++)
        {
            s.Step();
            bool now = m.IsInState("s_WaitAndRetry");
            if (now && !waiting)
            {
                retries++;
            }

            waiting = now;
        }

        // Each attempt is refused at the pawn, goes back up, waits 1 s and retries the Open label.
        Assert.True(retries >= 2, $"{retries} retries");
        Assert.True(m.Location.Z > 87.5f + 87.5f - 101f, $"shutter at {m.Location.Z}");

        // Once the way is clear it gets down.
        spy.SetLocation(V.At(1000f, 0f, 87.5f));
        s.Collision!.NotifyCollisionChanged();
        s.StepSeconds(4f);
        Assert.True(m.IsInState("TriggerOpenTimed"));
        Assert.True(m.Location.Z < 300f);
    }

    [Fact]
    public void StopTypeFreezesWhereItIs()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_StopWhenEncroach);
        s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        Start(s, sw);
        s.StepSeconds(2f);
        float z = m.Location.Z;
        Assert.False(m.bInterpolating);
        Assert.Equal(0, m.StateFrame.Pc);
        Assert.True(z > 175f && z < 300f, $"stopped at {z}");
        s.StepSeconds(5f);
        Assert.Equal(z, m.Location.Z);
    }

    [Fact]
    public void CrushTypeKillsThroughCrushedByWithTheMoversInstigatorAndKeepsGoing()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_CrushWhenEncroach);
        TestSpy victim = s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        TestMerc culprit = s.Player<TestMerc>(V.At(2000f, 0f, 87.5f));
        Start(s, sw, culprit);
        s.StepSeconds(1.2f);
        Assert.Equal(100f, m.Location.Z, 2);
        var pc = (TestController)victim.Controller!;
        (int damage, Pawn? by, SDamageType type) = pc.PawnDamage[0];
        Assert.Equal(200, damage);
        Assert.Same(culprit, by);
        Assert.Equal(SDamageType.SDTBase, type);
        Assert.True(victim.Health <= 0);
    }

    [Fact]
    public void AControllerlessPawnIsDestroyedByDamageAndDoesNotStopTheMover()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_StopWhenEncroach);
        TestSpy body = s.Spawn(new TestSpy { Physics = EPhysics.PHYS_None }, V.At(0f, 0f, 87.5f));
        s.Collision!.NotifyCollisionChanged();
        Start(s, sw);
        s.StepSeconds(1.2f);
        Assert.Equal(100f, m.Location.Z, 2);
        Assert.Contains((10000, (Pawn?)null, SDamageType.Crushed), body.Damage);
    }

    [Fact]
    public void EncroachDamageHurtsAPawnThroughItsController()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_ReturnWhenEncroach, encroachDamage: 1);
        TestSpy spy = s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        TestMerc culprit = s.Player<TestMerc>(V.At(2000f, 0f, 87.5f));
        Start(s, sw, culprit);
        s.StepSeconds(1f);
        Assert.Contains((1000, (Pawn?)culprit, SDamageType.Crushed), ((TestController)spy.Controller!).PawnDamage);
    }

    [Fact]
    public void IgnoreTypePassesThrough()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_IgnoreWhenEncroach);
        TestSpy spy = s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        Start(s, sw);
        s.StepSeconds(1.2f);
        Assert.Equal(100f, m.Location.Z, 2);
        Assert.Empty(((TestController)spy.Controller!).PawnDamage);
    }

    [Fact]
    public void DisabledShovingDoesNotMoveTheBlockingPawn()
    {
        (DeviceScene s, Mover m, _) = Shutter(EMoverEncroachType.ME_StopWhenEncroach);
        m.bTryToMoveEncroachedActor = false;
        TestSpy spy = s.Player<TestSpy>(V.At(0f, 0f, 187.5f));
        spy.Physics = EPhysics.PHYS_Walking;
        FVector before = spy.Location;

        Assert.False(s.Natives.MoveEncroacher(m, V.At(0, 0, -25), m.Rotation));
        Assert.Equal(before, spy.Location);
    }

    [Fact]
    public void RotatingDoorShovesPawnAlongItsArc()
    {
        DeviceScene s = DeviceScene.Create();
        Mover m = s.Add(new Mover { MoverEncroachType = EMoverEncroachType.ME_StopWhenEncroach });
        s.Collider(m, V.At(0, -10, 0), V.At(200, 10, 200));
        s.Begin();
        TestSpy spy = s.Player<TestSpy>(V.At(150, 55, 100));
        FVector before = spy.Location;

        Assert.True(s.Natives.MoveEncroacher(m, default, new FRotator(0, 4096, 0)));
        Assert.True(spy.Location.Y > before.Y + 30, $"Pawn did not follow door: {spy.Location}");
        Assert.False(s.Collision!.IsOverlapping(m, spy));
    }

    [Fact]
    public void FailedShoveRestoresPawnBeforeRefusingDoorStep()
    {
        DeviceScene s = DeviceScene.Create();
        Mover m = s.Add(new Mover { MoverEncroachType = EMoverEncroachType.ME_StopWhenEncroach });
        s.Collider(m, V.At(-10, -100, 0), V.At(10, 100, 200));
        StaticMeshActor wall = s.Add(new StaticMeshActor());
        s.Collider(wall, V.At(88, -100, 0), V.At(100, 100, 200));
        s.Begin();
        TestSpy spy = s.Player<TestSpy>(V.At(50, 0, 100));
        spy.Physics = EPhysics.PHYS_Walking;
        FVector before = spy.Location;

        Assert.False(s.Natives.MoveEncroacher(m, V.At(10, 0, 0), m.Rotation));
        Assert.Equal(default(FVector), m.Location);
        Assert.InRange((spy.Location - before).Size(), 0, 0.001f);
    }

    [Theory]
    [InlineData(EMoverEncroachType.ME_StopWhenEncroach)]
    [InlineData(EMoverEncroachType.ME_ReturnWhenEncroach)]
    [InlineData(EMoverEncroachType.ME_CrushWhenEncroach)]
    [InlineData(EMoverEncroachType.ME_IgnoreWhenEncroach)]
    [InlineData(EMoverEncroachType.ME_WaitAndRetryWhenEncroach)]
    public void ClientObstructionCannotChangeReplicatedDoorLeg(EMoverEncroachType type)
    {
        (DeviceScene s, Mover m, _) = Shutter(type);
        m.Role = ENetRole.ROLE_SimulatedProxy;
        m.bTryToMoveEncroachedActor = false;
        TestSpy spy = s.Player<TestSpy>(V.At(0f, 0f, 87.5f));
        m.SimOldPos = m.Location;
        m.SimInterpolate = new FVector(0, 100, 1);
        m.ClientStartInterpolation();

        for (int tick = 0; tick < 60; tick++)
        {
            m.physMovingBrush(Tick);
            Assert.True(m.IsInState("TriggerOpenTimed"), $"Client changed state to {m.GetStateName()}");
        }

        Assert.InRange(m.Location.Z, 99.99f, 100.01f);
        Assert.Empty(((TestController)spy.Controller!).PawnDamage);
    }

    [Fact]
    public void ClientPresenceEventDoesNotStartIndependentDoorCycle()
    {
        (DeviceScene s, Mover m, Sender sw) = Shutter(EMoverEncroachType.ME_WaitAndRetryWhenEncroach);
        m.Role = ENetRole.ROLE_SimulatedProxy;
        m.eventTrigger(sw, null);
        s.StepSeconds(0.5f);

        Assert.False(m.bInterpolating);
        Assert.Equal(300f, m.Location.Z);
        Assert.Equal(default(FVector), m.SimInterpolate);
    }
}
