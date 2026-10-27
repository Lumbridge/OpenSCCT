using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Server;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Net;

/// <summary>
/// The client's simulated tick of another player's pawn (spec/native/net-proxy.md rules 10 to 15, ASBasePawn::TickSimulated
/// decoded at editor 0x1121dc30): the proxy is moved only by the samples' velocity and error, never by a replicated
/// Location (the server keeps it off the wire, rule 4). Playtest: standing bot mercs seen from another player turned
/// 3 times as far as on the server, because the Rotation property and the samples fought; keeping the position and
/// rotation to the samples had stopped the proxies, since a walking proxy was never moved by its velocity.
/// </summary>
public sealed class ProxyTickTests
{
    private static (PawnRig Rig, SBasePawn Proxy) Scene(FVector at, int yaw = 0)
    {
        PawnRig rig = Rig(PawnTeam.Spy, at: V(-2000f, -2000f, 0f));
        PawnProxyReceiver.InstallTick(rig.Level);
        SBasePawn proxy = rig.Level.SpawnActor(new SPawnDefense_1Mesh(), V(at.X, at.Y, 90f), new FRotator(0, yaw, 0))!;
        proxy.Role = ENetRole.ROLE_SimulatedProxy;
        proxy.RemoteRole = ENetRole.ROLE_Authority;
        proxy.SetPhysics(EPhysics.PHYS_Walking);
        proxy.OldPhysics = EPhysics.PHYS_Walking;
        rig.Collision?.NotifyCollisionChanged();
        rig.Hold(default, 30);
        return (rig, proxy);
    }

    [Fact]
    public void AWalkingProxyWithNoErrorWalksAtTheReceivedVelocityOnTheFloor()
    {
        (PawnRig rig, SBasePawn proxy) = Scene(V(0f, 0f, 0f));
        PawnProxyReceiver.ApplySample(proxy, proxy.Location, proxy.Rotation, V(200f, 0f, 0f));
        FVector start = proxy.Location;
        rig.Hold(default, Sim.Hz);
        float moved = proxy.Location.X - start.X;
        Assert.InRange(moved, 190f, 210f);
        // On the floor (top at 0): the simulated walk keeps the cylinder in the 1.39 to 1.42 floor band.
        Assert.InRange(proxy.Location.Z - proxy.CollisionHeight, 1f, 2f);
        Assert.Equal(200f, proxy.Velocity.X, 0);
    }

    [Fact]
    public void AWalkingProxysErrorIsBlendedInAtARampUpTo200UnitsASecond()
    {
        (PawnRig rig, SBasePawn proxy) = Scene(V(0f, 0f, 0f));
        FVector start = proxy.Location;

        // A still proxy 100 uu behind its sample (under 6 collision radii): no snap.
        PawnProxyReceiver.ApplySample(proxy, start + V(100f, 0f, 0f), proxy.Rotation, default);
        rig.Hold(default, Sim.Hz / 2);

        // The correction speed ramps from 0 to 200 uu/s over the first second: 25 uu after half a second.
        Assert.InRange(proxy.Location.X - start.X, 20f, 30f);
        rig.Hold(default, Sim.Hz);
        Assert.Equal(start.X + 100f, proxy.Location.X, 0);
        Assert.True(proxy.ErrorDistance.IsZero());
    }

    [Fact]
    public void AnErrorBeyondSixCollisionRadiiSnaps()
    {
        (PawnRig rig, SBasePawn proxy) = Scene(V(0f, 0f, 0f));
        FVector start = proxy.Location;
        float far = (PawnProxyReceiver.SnapRadii * proxy.CollisionRadius) + 50f;
        PawnProxyReceiver.ApplySample(proxy, start + V(far, 0f, 0f), proxy.Rotation, default);
        rig.Hold(default, 1);
        Assert.Equal(start.X + far, proxy.Location.X, 0);
    }

    [Fact]
    public void AStillProxyTurnsTowardTheSamplesYawAtTheFastRate()
    {
        (PawnRig rig, SBasePawn proxy) = Scene(V(0f, 0f, 0f), yaw: 0);
        PawnProxyReceiver.ApplySample(proxy, proxy.Location, new FRotator(0, 16384, 0), default);
        rig.Hold(default, 1);

        // 131071 units a second under 10 uu/s: one tick turns 2184 units.
        Assert.InRange(proxy.Rotation.Yaw, 2100, 2250);
        rig.Hold(default, 10);
        Assert.Equal(16384, proxy.Rotation.Yaw & 0xFFFF);
    }

    [Fact]
    public void AReplicatedRotationBetweenSamplesIsTurnedBackToTheSamples()
    {
        // Rule 3 pushes Rotation as a property between samples; the next simulated tick turns the proxy back toward
        // the sample's yaw (rule 15), so a standing proxy follows its samples, not each push.
        (PawnRig rig, SBasePawn proxy) = Scene(V(0f, 0f, 0f), yaw: 0);
        PawnProxyReceiver.ApplySample(proxy, proxy.Location, new FRotator(0, 0, 0), default);
        rig.Hold(default, 2);
        proxy.Rotation = new FRotator(0, 1000, 0);
        rig.Hold(default, 1);
        Assert.Equal(0, proxy.Rotation.Yaw);
    }
}
