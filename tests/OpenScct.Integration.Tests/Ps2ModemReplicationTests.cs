using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Game.Equipment;
using OpenScct.Content.Collision;
using OpenScct.Physics.Collision;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class Ps2ModemReplicationTests
{
    [Theory]
    [InlineData("100")]
    [InlineData("300")]
    public void Owning_client_cycles_to_fifth_modem_and_alternate_fire_reaches_validated_authority_placement(string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 903, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        NetCombatBridge.Install(level, rig.Server.Driver);
        level.PhysicsDriver = null;
        var console = level.SpawnActor(new SComputerObjectiveTrigger { bNoDelete = false, bPS2Objective = true, bAlwaysRelevant = true, StaticMesh = AssetRef.None }, new FVector(40, 60, 0), default)!;
        var target = level.SpawnActor(new SBombTargetObjectiveTrigger
        {
            bNoDelete = false, bPS2Objective = true, Ps2Computer = console,
            CaptureTime = 30, CollisionRadius = 200, CollisionHeight = 200,
            Ps2ModemMesh = new AssetRef("PS2_Objectives.Modem"),
        }, new FVector(70, 0, 0), default)!;
        target.Reset();
        var boxes = new CollisionDataBuilder();
        boxes.AddBox(new FVector(70, -500, -500), new FVector(90, 500, 500));
        level.SetService<ICollisionWorld>(CollisionWorld.Build(boxes.Build()));
        SPlayerController? authority = null;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<SPlayerController>()!;
            pc.bIsPlayer = true;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
            var pawn = level.Spawn<SPawnAttaque>()!;
            pc.Possess(pawn);
            pawn.Physics = EPhysics.PHYS_None;
            foreach (SGadget item in new SGadget[] { new SGadSmokeGrenade(), new SGadChaffeGrenade(), new SGadStickyCam(), new SGadHeartBeatSensor(), new SGadBomb() })
            {
                SGadget gadget = level.SpawnActor(item, default, default)!;
                gadget.GiveTo(pawn);
            }
            authority = pc;
        };
        for (int i = 0; i < 600; i++) rig.Step();
        var client = rig.Clients[0];
        var owner = Assert.IsType<SPlayerController>(client.LocalPlayer);
        Assert.NotNull(authority);
        var ownerPawn = Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        client.World.Level.PhysicsDriver = null;
        client.World.Level.SetService<ICollisionWorld>(CollisionWorld.Build(boxes.Build()));
        SGadBomb? selected = null;
        for (int i = 0; i < 6; i++)
        {
            PawnInventory.NextGadget(ownerPawn);
            if (ownerPawn.SelectedGadget is SGadBomb bomb) { selected = bomb; break; }
        }
        Assert.NotNull(selected);
        for (int i = 0; i < 90; i++) rig.Step();
        Assert.IsType<SGadBomb>(authority.SBPawn!.SelectedGadget);
        Assert.Equal("Modem", selected.ItemName);
        Assert.True(selected.Ps2ModemProfile);
        ownerPawn.Location = authority.SBPawn.Location = default;
        owner.Rotation = authority.Rotation = default;
        ownerPawn.Physics = authority.SBPawn.Physics = EPhysics.PHYS_Walking;
        owner.GotoState("s_PlayerWalkingSt0H");
        authority.GotoState("s_PlayerWalkingSt0H");
        Assert.True(selected.CanUseGadget(), $"owner placement rejected: {selected.WhyCantUse}");
        var serverGadget = (SGadBomb)authority.SBPawn.SelectedGadget!;
        Assert.True(serverGadget.CanUseGadget(), $"server placement rejected: {serverGadget.WhyCantUse}");
        owner.eventAltFire(0f);
        for (int i = 0; i < 90; i++) rig.Step();
        Ps2Modem? placed = null;
        foreach (Ps2Modem modem in level.AllActors<Ps2Modem>()) placed = modem;
        Assert.NotNull(placed);
        Assert.True(target.bTriggering);
        Assert.Equal(0, ((SGadBomb)authority.SBPawn.SelectedGadget!).Charge);
        Assert.Equal(0, selected.Charge);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("300")]
    public void Source_modem_uses_real_client_factories_and_replicates_visuals_sound_shot_destruction_and_effect(string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 902, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.bIsPlayer = true;
            pc.Player = new SimNetConnection();
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        for (int i = 0; i < 300; i++) rig.Step();
        var mesh = new AssetRef("PS2_Objectives.Modem");
        var sound = new AssetRef("PS2_Objectives.ModemLoop");
        var smoke = new AssetRef("PS2_Objectives.OriginalSmoke");
        var fire = new AssetRef("PS2_Objectives.OriginalFlash");
        var source = level.SpawnActor(new Ps2Modem { StaticMesh = mesh, bAlwaysRelevant = true }, new FVector(10f, 20f, 30f), default)!;
        source.AmbientSound = sound;
        source.SmokeTexture = smoke;
        source.FireTexture = fire;
        for (int i = 0; i < 120; i++) rig.Step();
        SGamePlayObject? copy = null;
        foreach (SGamePlayObject actor in rig.Clients[0].World.Level.AllActors<SGamePlayObject>())
            if (actor.StaticMesh == mesh) copy = actor;
        Assert.NotNull(copy);
        Assert.IsType<Ps2Modem>(copy); // The actual registered runtime client factory.
        Assert.Equal(source.Location, copy.Location);
        Assert.Equal(sound, copy.AmbientSound);
        Assert.Equal(source.SoundVolume, copy.SoundVolume);
        Assert.Equal(source.HeatIntensity, copy.HeatIntensity);
        Assert.False(copy.bHidden);
        Assert.True(copy.bCollideActors);
        source.eventTakeDamage(1, null, source.Location, FVector.Zero, SDamageType.SDTBase);
        Assert.True(source.bDeleteMe);
        for (int i = 0; i < 120; i++) rig.Step();
        Assert.True(copy.bDeleteMe);
        Ps2ModemEffect? effect = null;
        foreach (Ps2ModemEffect actor in rig.Clients[0].World.Level.AllActors<Ps2ModemEffect>()) effect = actor;
        Assert.NotNull(effect);
        Assert.Equal(smoke, effect.SmokeTexture);
        Assert.Equal(fire, effect.FireTexture);
        Assert.Equal(smoke, effect.SourceEmitters[0].Texture);
        Assert.Equal(smoke, effect.SourceEmitters[1].Texture);
        Assert.Equal(fire, effect.SourceEmitters[2].Texture);
        Assert.Equal(3, effect.SourceEmitters.Count);
    }
}
