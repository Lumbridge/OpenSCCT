using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class GasMaskObserverTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void ObserversAndLateJoinersReceiveWornStateAndTheSameHeadAttachment(NetProfile profile)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 731, 2,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection();
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = true;
        pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pawn.Physics = EPhysics.PHYS_None;
        pawn.Location = new FVector(100, 100, 1000);
        var gadget = level.SpawnActor(new SGadGasMask(), pawn.Location, pawn.Rotation, pawn, pawn)!;
        var mask = Assert.IsType<SGasMask>(gadget.DummyGasMask);
        mask.bAlwaysRelevant = true;

        Step(240);
        foreach (ClientSession viewer in rig.Clients) Check(viewer, false);

        gadget.Activate(true);
        Step(60);
        foreach (ClientSession viewer in rig.Clients) Check(viewer, true);

        ClientSession late = rig.AddClient("MaskedObserver");
        Step(240);
        Check(late, true);

        // The same existing actor is hidden/shown by the renderer, not recreated on each toggle.
        for (int cycle = 0; cycle < 3; cycle++)
        {
            gadget.Activate(false);
            Step(45);
            foreach (ClientSession viewer in rig.Clients.Append(late)) Check(viewer, false);
            gadget.Activate(true);
            Step(45);
            foreach (ClientSession viewer in rig.Clients.Append(late)) Check(viewer, true);
        }

        gadget.Unload();
        Step(45);
        foreach (ClientSession viewer in rig.Clients.Append(late)) Check(viewer, false);

        gadget.MaskDuration = 0.15f;
        gadget.Activate(true);
        Step(45);
        Assert.False(pawn.bGasMask);
        foreach (ClientSession viewer in rig.Clients.Append(late)) Check(viewer, false);

        void Step(int count)
        {
            for (int i = 0; i < count; i++) rig.Step();
        }

        void Check(ClientSession viewer, bool worn)
        {
            Assert.Equal(EClientState.Joined, viewer.State);
            var copy = Assert.IsAssignableFrom<SBasePawn>(viewer.Driver.FindActorByNetGuid((uint)pawn.ActorId));
            var copyMask = Assert.IsType<SGasMask>(viewer.Driver.FindActorByNetGuid((uint)mask.ActorId));
            Assert.Equal(worn, copy.bGasMask);
            Assert.Same(copy, copyMask.Instigator);
            Assert.Same(copy, copyMask.Base);
            Assert.Equal(OpenScct.Game.Equipment.K.GasMaskBone, copyMask.AttachmentBone);
            int attachedMasks = 0;
            foreach (SGasMask candidate in viewer.World.Level.AllActors<SGasMask>())
                if (ReferenceEquals(candidate.Instigator, copy)) attachedMasks++;
            Assert.Equal(1, attachedMasks);
        }
    }
}
