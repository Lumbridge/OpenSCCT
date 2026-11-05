using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class InteractionUsabilityNetTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void TeamVariantsAndLaterUsabilityChangesReachClients(NetProfile profile)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 946, 1,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection();
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        var spy = level.SpawnActor(new STriggerInteraction { bAlwaysRelevant = true, UsableBy = Usable.U_SPAWNOFFENSE },
            new FVector(100, 0, -118), default)!;
        var merc = level.SpawnActor(new STriggerInteraction { bAlwaysRelevant = true, UsableBy = Usable.U_SPAWNDEFENSE },
            new FVector(100, 0, 0), default)!;
        var panel = level.SpawnActor(new SBasicGamePlayObject { bAlwaysRelevant = true },
            merc.Location, default)!;
        merc.SetOwner(panel);
        Step(240);
        var viewer = rig.Clients[0];
        Assert.Equal(EClientState.Joined, viewer.State);
        var spyCopy = Assert.IsType<STriggerInteraction>(viewer.Driver.FindActorByNetGuid((uint)spy.ActorId));
        var mercCopy = Assert.IsType<STriggerInteraction>(viewer.Driver.FindActorByNetGuid((uint)merc.ActorId));
        Assert.Same(viewer.Driver.FindActorByNetGuid((uint)panel.ActorId), mercCopy.Owner);
        Assert.NotNull(mercCopy.Owner);
        var pawn = viewer.World.Level.Spawn<SPAWNDEFENSE>()!;
        var player = viewer.World.Level.Spawn<SPlayerController>()!;
        player.Pawn = player.SBPawn = pawn;
        player.SPawnType = PawnType.SPAWNDEFENSE;
        pawn.Health = 100;
        Assert.False(spyCopy.CanUse(player));
        Assert.True(mercCopy.CanUse(player));
        Assert.Equal(-118f, spyCopy.Location.Z);
        Assert.Equal(0f, mercCopy.Location.Z);

        // A map manager can hide a switch until an earlier event, then enable it for the merc.
        merc.UsableBy = Usable.U_NO_ONE;
        Step(60);
        Assert.False(mercCopy.CanUse(player));
        merc.UsableBy = Usable.U_SPAWNDEFENSE;
        merc.bUsable = false;
        Step(60);
        Assert.False(mercCopy.CanUse(player));
        merc.bUsable = true;
        Step(60);
        Assert.True(mercCopy.CanUse(player));

        void Step(int count)
        {
            for (int i = 0; i < count; i++) rig.Step();
        }
    }
}
