using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class SoundNativeTests
{
    private sealed class Source : Actor;
    private sealed class SoundPawn : Pawn;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmittedSlotUsesMiscAndOnlySelfOrOwnerInstigatorDampens(bool owned)
    {
        var world = new World(1);
        var pawn = world.Level.Spawn<SoundPawn>()!;
        var source = world.Level.Spawn<Source>()!;
        pawn.Instigator = pawn;
        pawn.SoundDampening = 0.25f;
        source.Instigator = pawn;
        source.SetOwner(pawn);
        var sound = new AssetRef("Sound");
        if (owned) { pawn.PlayOwnedSound(sound, Volume: 0.8f); source.PlayOwnedSound(sound, Volume: 0.8f); }
        else { pawn.PlaySound(sound, Volume: 0.8f); source.PlaySound(sound, Volume: 0.8f); }
        source.SetOwner(null);
        if (owned) source.PlayOwnedSound(sound, ESoundSlot.SLOT_None, 0.8f);
        else source.PlaySound(sound, ESoundSlot.SLOT_None, 0.8f);
        SimEvent[] events = world.Events.Late.ToArray();
        Assert.Equal(3, events.Length);
        Assert.Equal((int)ESoundSlot.SLOT_Misc, events[0].Slot);
        Assert.Equal((int)ESoundSlot.SLOT_Misc, events[1].Slot);
        Assert.Equal((int)ESoundSlot.SLOT_None, events[2].Slot);
        Assert.Equal(0.2f, events[0].Volume);
        Assert.Equal(0.2f, events[1].Volume);
        Assert.Equal(0.8f, events[2].Volume);
    }

    [Fact]
    public void WildcardStopsRetainActorScopeAndReplayProducesNothing()
    {
        var world = new World(1);
        var source = world.Level.Spawn<Source>()!;
        source.StopSound(AssetRef.None);
        world.Events.StopSound(null);
        world.IsReplaying = true;
        source.StopSound(AssetRef.None);
        SimEvent[] events = world.Events.Late.ToArray();
        Assert.Equal(2, events.Length);
        Assert.All(events, e => { Assert.Equal(SimEventKind.StopSound, e.Kind); Assert.True(e.What.IsNone); });
        Assert.Equal(source.ActorId, events[0].ActorId);
        Assert.Equal(0, events[1].ActorId);
    }
}
