using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class DiskSwapSessionTests
{
    [Fact]
    public void SwappingStoryDisksDropsTheOldDiskAndKeepsTheNewDiskDeliverable()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("AquaE") is null, "AquaE content is required.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "AquaE", Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, Bots = 0,
        }, clock);
        long frame = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        Step(900);
        var pc = Assert.IsType<SPlayerController>(session.ServerController);
        var pawn = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        var found = new System.Collections.Generic.List<SFlag>();
        foreach (var flag in session.Host.World.Level.AllActors<SFlag>()) if (found.Count < 2) found.Add(flag);
        var flags = found.ToArray();
        Assert.Equal(2, flags.Length);
        var first = flags[0];
        var second = flags[1];
        foreach (var flag in flags)
        {
            DiskCarryRules.SetHandItemFromInteraction(pc, flag.PickupInteraction);
            DiskCarryRules.AnimNotifyPickup(pawn);
            DiskCarryRules.PickupEndState(pc);
            Step(30);
        }
        Step(60); // Allow the discarded disk to settle on the map floor.
        Assert.Same(second, pawn.CarriedFlag);
        Assert.NotSame(pawn, first.Base); // A settled disk may be based on the floor or a platform.
        Assert.True(first.AttachmentBone.IsNone, "Dropped disk still has its thigh attachment.");
        Assert.False(first.bHidden);
        Assert.Null(first.FlagCarrier);
        Assert.InRange(first.Location.Z, pawn.Location.Z - pawn.CollisionHeight - 20, pawn.Location.Z - 20);
        Assert.True(first.Physics == EPhysics.PHYS_None,
            $"Disk did not settle: at={first.Location}, carrier={pawn.Location}, velocity={first.Velocity}, state={first.GetStateName()}, meshColliders=" +
            ((OpenScct.Physics.Movement.PhysicsDriver)session.Host.World.Level.PhysicsDriver!).Collision.HasColliders(first));
        Assert.Equal(EClientState.Joined, session.Client.State);
        int firstIndex = session.Host.Driver.MapBinding.IndexOf(first);
        var droppedCopy = Assert.IsType<SFlag>(session.Client.Map!.MapActors[firstIndex]);
        Assert.NotSame(session.LocalPawn, droppedCopy.Base);
        Assert.False(droppedCopy.bHidden);
        Assert.InRange(FVector.Dist(first.Location, droppedCopy.Location), 0, 5);
        int secondIndex = session.Host.Driver.MapBinding.IndexOf(second);
        var clientFlag = Assert.IsType<SFlag>(session.Client.Map.MapActors[secondIndex]);
        Assert.Same(clientFlag, session.LocalPawn!.CarriedFlag);
        var zone = Assert.IsType<SFlagDropZone>(second.DropZone.First(z => z is not null));
        var drop = Assert.IsType<SFlagDropZoneInteraction>(zone.InteractionObj);
        pawn.Location = drop.Location + drop.Rotation.Vector() * 67;
        pawn.Rotation = new FRotator(0, drop.Rotation.Yaw + 32768, 0);
        session.LocalPawn.Location = pawn.Location;
        session.LocalPawn.Rotation = pawn.Rotation;
        pc.Rotation = session.LocalController!.Rotation = pawn.Rotation;
        session.Host.Movement.PeekEndpoint(pc)?.Sequenced?.ResetLifetime();
        Step(60);
        Assert.True(drop.CanUse(pc), "New disk's delivery entry is unusable after swapping.");
        int zoneIndex = session.Host.Driver.MapBinding.IndexOf(zone);
        var clientZone = Assert.IsType<SFlagDropZone>(session.Client.Map.MapActors[zoneIndex]);
        SFlagDropZoneInteraction? clientDrop = null;
        foreach (var entry in session.Client.World.Level.AllActors<SFlagDropZoneInteraction>())
            if (ReferenceEquals(entry.Owner ?? entry.MyOwner, clientZone)) clientDrop = entry;
        Assert.True(clientDrop is not null, $"No drop interaction for {clientZone}; authority channel=" +
            (session.Host.Driver.ClientConnections.First().FindActorChannel(drop) is not null));
        Assert.NotNull(clientDrop);
        Assert.True(clientDrop.CanUse(session.LocalController), "Owner lost the delivery entry after swapping.");
        Assert.True(second.CheckDropZone(zone));
        DiskCarryRules.PutFlagBeginState(pc);
        DiskCarryRules.PutFlagEndState(pc);
        Step(30);
        Assert.Null(pawn.CarriedFlag);
        Assert.True(second.Objective!.ObjectiveTaken);
    }
}
