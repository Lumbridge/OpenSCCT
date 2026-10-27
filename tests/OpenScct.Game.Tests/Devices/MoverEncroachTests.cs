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
}
