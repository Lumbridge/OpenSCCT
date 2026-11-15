using System;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class Ps2PickupReplicationTests
{
    [Theory]
    [InlineData("100", false)]
    [InlineData("300", false)]
    [InlineData("100", true)]
    [InlineData("300", true)]
    public void Owner_Use_resolves_replicated_vial_and_follows_source_pickup_state_handoff(string link, bool crouched)
    {
        MapCatalog maps = Ps2ObjectiveReplicationTests.SourceMap();
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 911, 1,
            new ServerSettings { Maps = maps, Map = "PS2ObjectiveNetwork", SpawnWalkers = false }, new ClientSettings { Maps = maps, Gameplay = true });
        Level level = rig.Server.World.Level;
        NetCombatBridge.Install(level, rig.Server.Driver);
        SPlayerController? authority = null;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<SPlayerController>()!;
            pc.bIsPlayer = true; pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
            var pawn = level.Spawn<SPawnAttaque>()!; pc.Possess(pawn); pawn.Physics = EPhysics.PHYS_None;
            InteractionMenuRules.EnsureManager(pc); authority = pc;
        };
        void Step(int count) { for (int i = 0; i < count; i++) rig.Step(); }
        Step(600);
        var client = rig.Clients[0];
        var owner = Assert.IsType<SPlayerController>(client.LocalPlayer);
        var flag = level.Actors.OfType<SFlag>().Single(f => f.bPS2Objective);
        flag.Reset();
        var entry = flag.PickupInteraction!;
        Assert.Equal((50f, 50f), (entry.CollisionRadius, entry.CollisionHeight));
        FVector start = entry.Location + new FVector(10, 0, 40);
        var pawn = authority!.SBPawn!;
        pawn.Location = start; pawn.Physics = EPhysics.PHYS_None; pawn.bIsCrouched = crouched;
        authority.GotoState("s_PlayerWalkingSt0H");
        pawn.BeginTouch(entry);
        Step(120);
        var copy = Assert.IsType<SPickupInteraction>(client.Driver.FindActorByNetGuid((uint)entry.ActorId));
        var vial = Assert.IsType<SFlag>(copy.Owner);
        Assert.True(vial.bPS2Objective);
        Assert.Equal((50f, 50f), (copy.CollisionRadius, copy.CollisionHeight));
        Assert.NotNull(owner.IManager);
        Assert.True(owner.IManager.IndexOf(copy) >= 0);
        var localPawn = owner.SBPawn!;
        localPawn.Location = start; localPawn.bIsCrouched = crouched; localPawn.Physics = EPhysics.PHYS_None;
        owner.GotoState("s_PlayerWalkingSt0H");
        owner.bUsing = 1; owner.bCanCheckUsingButton = true;
        Assert.True(InteractionMenuRules.DoInteraction(owner, true));
        owner.IManager.SelectedInteractions = owner.IManager.IndexOf(copy);
        owner.bUsing = 0;
        Assert.Equal(EMenuRelease.InteractionInitSent, InteractionMenuRules.MenuRelease(owner));
        Step(90);
        Assert.True(owner.IsInState("s_Pickup_GoTo"), $"ownerstate={owner.GetStateName()}");
        Assert.False(localPawn.bUseAdditionnalPhysicsForRepos);
        Assert.False(pawn.bUseAdditionnalPhysicsForRepos);
        Assert.Equal(vial.Location.X + 80f, localPawn.vVandammePos.X);
        Assert.Equal(start.Z + (crouched ? 27.5f : 0f), localPawn.vVandammePos.Z);
        owner.eventTopSynchro();
        Step(90);
        Assert.Same(flag, pawn.CarriedFlag);
        owner.GotoState("s_PlayerWalkingSt0H");
        Step(90);
        Assert.Same(localPawn, vial.Base);
        Assert.Equal(new Name("B R Thigh"), flag.AttachmentBone);
        Assert.Equal(new Name("B R Thigh"), vial.AttachmentBone);
        Assert.Equal(flag.Ps2SourceStaticMesh, vial.StaticMesh);
        Assert.Equal(flag.Ps2SourceStaticMesh, vial.InitialStaticMesh);
        Assert.Equal(SFlag.Ps2CarryOffset, vial.RelativeLocation);
        Assert.Equal(SFlag.Ps2CarryRotation, flag.RelativeRotation);
        // The existing actor attachment wire stores each rotator axis as a byte.
        Assert.Equal(new FRotator(15872, 0, 0), vial.RelativeRotation);
    }
}
