using OpenScct.Core;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>
/// A class's native list adding a property its condition leaves out, per connection (<see cref="NetDriver.PropertyPush"/>),
/// as ASBasePawn::GetOptimizedRepList pushes a pawn's Rotation between position samples (spec/native/net-proxy.md rule 3)
/// while bCompressedPosition keeps it out of the Actor list.
/// </summary>
public sealed class PropertyPushTests
{
    [Fact]
    public void APushedPropertyGoesOnlyWhilePushedAndOnlyWhenItChanged()
    {
        using var rig = new NetRig(SimLinkProfile.A, 4, 1);
        rig.Step(60);
        TestSpawnProbe probe = rig.ServerWorld.Level.Spawn<TestSpawnProbe>(new FVector(1f, 2f, 3f))!;
        probe.bAlwaysRelevant = true;
        probe.bCompressedPosition = true;
        rig.Step(120);
        TestSpawnProbe copy = rig.Clients[0].Copy<TestSpawnProbe>(probe)!;
        Assert.NotNull(copy);

        // Not pushed: the condition keeps the Rotation off the wire.
        probe.Rotation = new FRotator(0, 8192, 0);
        rig.Step(60);
        Assert.Equal(0, copy.Rotation.Yaw);

        // Pushed: it goes.
        int sent = 0;
        rig.Server.PropertyPush = (actor, viewer, property) =>
        {
            bool push = ReferenceEquals(actor, probe) && property.Name == "Rotation";
            sent += push ? 1 : 0;
            return push;
        };
        rig.Step(60);
        Assert.Equal(8192, copy.Rotation.Yaw & 0xFFFF);
        Assert.True(sent > 0);

        // No longer pushed: a new rotation stays on the server.
        rig.Server.PropertyPush = null;
        probe.Rotation = new FRotator(0, 16384, 0);
        rig.Step(60);
        Assert.Equal(8192, copy.Rotation.Yaw & 0xFFFF);
    }
}
