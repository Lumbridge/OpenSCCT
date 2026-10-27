using OpenScct.Core;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>Interest management (an OpenSCCT addition for the hub): culling by distance and update spacing by band.</summary>
public sealed class InterestTests
{
    private static NetDriverSettings WithInterest(DistanceInterest interest) => new() { UpdateGate = NetUpdateGate.Always, Interest = interest };

    [Fact]
    public void FarActorsAreCulledAndNearOnesOpen()
    {
        var interest = new DistanceInterest();
        using var rig = new NetRig(SimLinkProfile.A, 21, 1, server: WithInterest(interest));
        TestMover far = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(9000f, 0f, 0f))!;
        TestMover near = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(1000f, 0f, 0f))!;
        rig.Step(120);
        Assert.Null(rig.Clients[0].Copy<TestMover>(far));
        Assert.NotNull(rig.Clients[0].Copy<TestMover>(near));
        Assert.True(rig.Server.Stats.InterestCulled > 0);

        // Coming within the cull distance opens it; moving out past the keep distance closes it after the timeout.
        far.Location = new FVector(6000f, 0f, 0f);
        rig.Step(60);
        TestMover copy = rig.Clients[0].Copy<TestMover>(far)!;
        Assert.NotNull(copy);
        far.Location = new FVector(7500f, 0f, 0f);
        rig.Step(60 * 6);
        Assert.False(copy.bDeleteMe);
        far.Location = new FVector(9000f, 0f, 0f);
        rig.Step(60 * 7);
        Assert.True(copy.bDeleteMe);
    }

    [Fact]
    public void WithoutInterestTheSameActorReplicates()
    {
        using var rig = new NetRig(SimLinkProfile.A, 22, 1, server: new NetDriverSettings { UpdateGate = NetUpdateGate.Always });
        rig.Server.Relevancy.LineOfSight = null;
        TestMover far = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(9000f, 0f, 0f))!;
        rig.Step(120);
        Assert.NotNull(rig.Clients[0].Copy<TestMover>(far));
        Assert.Equal(0, rig.Server.Stats.InterestCulled);
        Assert.Equal(0, rig.Server.Stats.InterestDeferred);
    }

    [Theory]
    [InlineData(1000f, 60)]
    [InlineData(3000f, 13)]
    [InlineData(5000f, 7)]
    [InlineData(6800f, 4)]
    public void DistantActorsUpdateLessOften(float distance, int maxBunches)
    {
        var interest = new DistanceInterest();
        using var rig = new NetRig(SimLinkProfile.A, 23, 1, server: WithInterest(interest));
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(distance, 0f, 0f))!;
        rig.Step(60);
        ActorChannel channel = rig.ServerSide(0).FindActorChannel(mover)!;
        int before = channel.PropertyBunchesSent;

        // Three seconds of a mover that changes every frame: 20 passes a second at most.
        for (int i = 0; i < 180; i++)
        {
            mover.Location = new FVector(distance, i * 0.5f, 0f);
            rig.Step(1);
        }

        int sent = channel.PropertyBunchesSent - before;
        Assert.InRange(sent, 1, maxBunches + 1);
    }

    [Fact]
    public void BandsMustBeOrderedAndKeepAtLeastCull()
    {
        Assert.Throws<System.ArgumentException>(() => new DistanceInterest(new DistanceInterestSettings
        {
            Bands = [new InterestBand(4000f, 0.5f, 2f), new InterestBand(2000f, 0.2f, 2f)],
        }));
        Assert.Throws<System.ArgumentException>(() => new DistanceInterest(new DistanceInterestSettings { CullDistance = 9000f, KeepDistance = 8000f }));
    }
}
