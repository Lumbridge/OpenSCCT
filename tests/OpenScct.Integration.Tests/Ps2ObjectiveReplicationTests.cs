using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class Ps2ObjectiveReplicationTests
{
    internal static MapCatalog SourceMap()
    {
        const string map = "PS2ObjectiveNetwork";
        var maps = new MapCatalog();
        maps.Register(map, (world, client) =>
        {
            Level level = world.Level;
            level.BeginLoad();
            var terminal = level.SpawnActor(new SComputerObjectiveTrigger
            {
                bNoDelete = false,
                bPS2Objective = true,
                StaticMesh = AssetRef.None,
                bAlwaysRelevant = true,
                CaptureTime = 40,
                DominationCaptureTime = 40,
                TriggerName = "Original console A",
                Ps2SkinActive = new AssetRef("PS2.Active"),
                Ps2SkinInactive = new AssetRef("PS2.Inactive"),
                Ps2SkinKeyboard = new AssetRef("PS2.Keyboard"),
            }, new FVector(40, 0, 0), default)!;
            var vial = level.SpawnActor(new SFlag
            {
                bNoDelete = false,
                bPS2Objective = true,
                bAlwaysRelevant = true,
                StaticMesh = new AssetRef("PS2.OriginalVial"),
                Ps2Computer = terminal,
                TriggerName = "Objective A - 1",
            }, new FVector(40, 0, 25), default)!;
            Actor?[] actors = [terminal, vial];
            if (client) NetMapBinding.PrepareClientMap(level, actors);
            level.EndLoad();
            if (!client)
            {
                terminal.Ps2UseAsProxy();
                terminal.Ps2SetVisualState(5, 1f);
                vial.bHidden = false;
            }
            return new MapInfo(map, map, 0, map, [], [new MapStart(default, default, 0)], actors);
        });
        return maps;
    }

    private static void Players(WalkerRig rig)
    {
        rig.Server.PlayerJoined += slot =>
        {
            var pc = rig.Server.World.Level.Spawn<PlayerController>()!;
            pc.bIsPlayer = true;
            pc.Player = new SimNetConnection();
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
    }

    private static T Single<T>(Level level) where T : Actor
    {
        var actors = new List<T>();
        foreach (T actor in level.AllActors<T>())
            if (actor is not SObjectiveTrigger objective || objective.bPS2Objective) actors.Add(actor);
        return Assert.Single(actors);
    }

    private static void Step(WalkerRig rig, int count) { for (int i = 0; i < count; i++) rig.Step(); }

    [Theory]
    [InlineData("100")]
    [InlineData("300")]
    public void Original_map_replacements_override_PC_nonspawnable_defaults_and_keep_source_profile(string link)
    {
        Assert.True(new SFlag().bNoDelete); // The PC factory differs from the authored PS2 instance.
        MapCatalog maps = SourceMap();
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 906, 1,
            new ServerSettings { Map = "PS2ObjectiveNetwork", Maps = maps, SpawnWalkers = false },
            new ClientSettings { Maps = maps, Gameplay = true });
        Players(rig);
        Step(rig, 600);
        var client = rig.Clients[0];
        Assert.All(client.Map!.MapActors, actor => Assert.True(actor!.bDeleteMe));
        SFlag vial = Single<SFlag>(client.World.Level);
        SComputerObjectiveTrigger terminal = Single<SComputerObjectiveTrigger>(client.World.Level);
        Assert.True(vial.bPS2Objective);
        Assert.False(vial.bNoDelete);
        Assert.Null(vial.Server);
        Assert.Equal(new AssetRef("PS2.OriginalVial"), vial.StaticMesh);
        Assert.Equal(new AssetRef("PS2.OriginalVial"), vial.InitialStaticMesh);
        Assert.Equal(new AssetRef("PS2.OriginalVial"), vial.Ps2SourceStaticMesh);
        // A return/reset must not restore the PC factory's ARGUS rack.
        vial.Reset();
        Assert.Equal(new AssetRef("PS2.OriginalVial"), vial.StaticMesh);
        Assert.Equal(new AssetRef("PS2.OriginalVial"), vial.InitialStaticMesh);
        Assert.Same(terminal, vial.Ps2Computer);
        Assert.Equal("Objective A - 1", vial.TriggerName);
        Assert.Equal("Original console A", terminal.TriggerName);
        Assert.True(terminal.bPS2Objective);
        Assert.Equal(new AssetRef("PS2.Active"), terminal.Ps2SkinActive);
        Assert.Equal((byte)5, terminal.Ps2VisualState);
        Assert.Equal(0, client.Driver.Stats.FailedSpawns);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("300")]
    public void Pause_resume_repeated_state_and_late_join_keep_authoritative_source_animation(string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 907, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Players(rig);
        Step(rig, 300);
        Level level = rig.Server.World.Level;
        level.Info.Game = new SGameInfo { MatchInfo = new USMatchInfo { GameMode = EGameMode.GM_Neutralization } };
        var terminal = level.SpawnActor(new SComputerObjectiveTrigger
        {
            bNoDelete = false,
            bPS2Objective = true,
            bAlwaysRelevant = true,
            CaptureTime = 40,
            DominationCaptureTime = 40,
            StaticMesh = AssetRef.None,
        }, new FVector(10, 0, 0), default)!;
        terminal.Reset();
        Step(rig, 150);
        terminal.eventTriggering(true);
        Step(rig, 120);
        var copy = Single<SComputerObjectiveTrigger>(rig.Clients[0].World.Level);
        Assert.True(copy.bPS2Objective);
        Assert.Equal(terminal.Ps2AnimationSerial, copy.Ps2AnimationSerial);
        terminal.eventTriggering(false); // State remains0, but the animation must stop.
        int paused = terminal.Ps2AnimationSerial;
        Step(rig, 90);
        Assert.Equal(paused, copy.Ps2AnimationSerial);
        Assert.Equal(0f, copy.Ps2AnimationRate);
        float frozen = copy.Ps2AnimationFrame;
        Step(rig, 60);
        Assert.Equal(frozen, copy.Ps2AnimationFrame);
        terminal.eventTriggering(true); // Same visualstate0, new serial and sourceframe.
        Step(rig, 90);
        Assert.Equal(terminal.Ps2AnimationSerial, copy.Ps2AnimationSerial);
        Assert.Equal(.25f, copy.Ps2AnimationRate);
        Assert.InRange(MathF.Abs(terminal.Ps2AnimationFrame - copy.Ps2AnimationFrame), 0f, .04f);
        terminal.Ps2SetVisualState(2, 4f);
        Step(rig, 60);
        terminal.Ps2SetVisualState(2, 4f); // Repeated reverse state still restarts the command serial.
        Step(rig, 90);
        Assert.Equal(terminal.Ps2AnimationSerial, copy.Ps2AnimationSerial);
        Assert.True(copy.Ps2AnimationBackward);
        terminal.Ps2SetVisualState(0, .25f);
        Step(rig, 240);
        var late = rig.AddClient();
        Step(rig, 300);
        var lateCopy = Single<SComputerObjectiveTrigger>(late.World.Level);
        Assert.Equal(terminal.Ps2AnimationSerial, lateCopy.Ps2AnimationSerial);
        Assert.True(lateCopy.Ps2AnimationStartFrame > .08f); // Initial snapshot is not the old command's frame0.
        Assert.InRange(MathF.Abs(terminal.Ps2AnimationFrame - lateCopy.Ps2AnimationFrame), 0f, .04f);
    }
}
