using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Integration.Tests;

public sealed class BombPresentationNetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plant_remove_replant_and_explode_update_sound_skins_and_visibility_for_existing_and_late_clients(bool delayed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("CineS") is null, "CineS content is required.");
        using WalkerRig rig = WalkerRig.Create(delayed ? SimLinkProfile.B : null, 9713, 1,
            new ServerSettings { Map = "CineS", GameOptions = "?GameMode=5?TimeLimit=0", PlayersToStart = 4, Profile = NetProfile.Enhanced },
            new ClientSettings { Gameplay = true });
        Steps(rig, 180);
        Level level = rig.Server.World.Level;
        var objective = rig.Server.Map.MapActors.OfType<SObjective>().First(o =>
            o.Triggers.OfType<SBombTargetObjectiveTrigger>().Count() >= 2);
        SBombTargetObjectiveTrigger[] spots = objective.Triggers.OfType<SBombTargetObjectiveTrigger>().ToArray();
        SBombTargetObjectiveTrigger bomb = spots[0];
        SBombTargetObjectiveTrigger other = spots[1];
        objective.ActivationTime = 0;
        objective.RandomTriggers = 0;
        bomb.CaptureTime = 30;
        objective.Reset();
        bomb.ControllerWhoTakesIt = Assert.IsAssignableFrom<PlayerController>(rig.Server.Players[0].Controller);
        Steps(rig, 180);
        SBombTargetObjectiveTrigger copy = Copy(rig.Clients[0], bomb);
        AssertGhost(copy);
        AssertGhost(Copy(rig.Clients[0], other));

        bomb.eventTriggering(true);
        Steps(rig, 120);
        AssertArmed(copy);
        Assert.True(Copy(rig.Clients[0], other).bHidden);

        ClientSession late = rig.AddClient();
        Steps(rig, 240);
        SBombTargetObjectiveTrigger lateCopy = Copy(late, bomb);
        AssertArmed(lateCopy);
        Assert.True(Copy(late, other).bHidden);

        var merc = level.SpawnActor(new SPlayerController(), default, default)!;
        merc.Pawn = level.SpawnActor(new SPAWNDEFENSE(), new FVector(10000, 0, 0), default)!;
        bomb.InteractionObj!.eventInitInteract(merc, 0);
        Steps(rig, 120);
        foreach (ClientSession client in new[] { rig.Clients[0], late })
        {
            AssertGhost(Copy(client, bomb));
            AssertGhost(Copy(client, other));
        }

        // A fresh fuse must start its loop again, then disappear permanently when the objective is taken.
        bomb.CaptureTime = 5;
        bomb.eventTriggering(true);
        Steps(rig, 120);
        AssertArmed(copy);
        AssertArmed(lateCopy);
        Steps(rig, 240);
        Assert.True(objective.ObjectiveTaken);
        foreach (ClientSession client in new[] { rig.Clients[0], late })
        {
            foreach (SBombTargetObjectiveTrigger source in new[] { bomb, other })
            {
                SBombTargetObjectiveTrigger seen = Copy(client, source);
                Assert.False(seen.bActive);
                Assert.False(seen.bTriggering);
                Assert.True(seen.bHidden);
                Assert.True(seen.AmbientSound.IsNone);
            }
        }
    }

    private static SBombTargetObjectiveTrigger Copy(ClientSession client, SBombTargetObjectiveTrigger source) =>
        Assert.Single(client.ActorChannels.Select(c => c.Actor).OfType<SBombTargetObjectiveTrigger>(), b =>
            !b.bDeleteMe && FVector.DistSquared(b.Location, source.Location) < 1f);

    private static void AssertGhost(SBombTargetObjectiveTrigger bomb)
    {
        Assert.False(bomb.bHidden);
        Assert.False(bomb.bSkinState);
        Assert.All(bomb.Skins.Take(3), skin => Assert.Equal(bomb.AbsentSkin, skin));
        Assert.True(bomb.AmbientSound.IsNone);
    }

    private static void AssertArmed(SBombTargetObjectiveTrigger bomb)
    {
        Assert.False(bomb.bHidden);
        Assert.True(bomb.bSkinState);
        Assert.True(bomb.bLocalSkinState);
        Assert.All(bomb.Skins.Take(3), skin => Assert.True(skin.IsNone));
        Assert.Equal(MK.BombArmedSound, bomb.AmbientSound);
        Assert.True(bomb.SoundRadius > 0);
        Assert.True(bomb.SoundVolume > 0);
    }

    private static void Steps(WalkerRig rig, int frames)
    {
        for (int i = 0; i < frames; i++) rig.Step();
    }
}
