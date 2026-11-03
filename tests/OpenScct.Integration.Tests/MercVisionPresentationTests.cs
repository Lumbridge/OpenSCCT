using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercVisionPresentationTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void SnapshotObserversAndLateJoinersReceiveTheActiveVisorColour(NetProfile profile)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 739, 1,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection();
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        var pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = true;
        pawn.Physics = EPhysics.PHYS_None;
        pawn.Location = new FVector(100, 100, 1000);
        Assert.Null(pawn.GoggleBeam); // A dedicated server has no renderer-side beam or goggle actor.
        Step(240);
        Check(rig.Clients[0], "SGoggleTextures.beam_goggle.TRD_goggle_orange");

        pawn.ExecuteAction(SBasePawn.SA_VISION_MOTION);
        pawn.bGogglesUp = true;
        PawnVision.AnimEvent_GoggleSwitchDown(pawn);
        pawn.bGogglesUp = false;
        Step(60);
        Check(rig.Clients[0], "SGoggleTextures.beam_goggle.TRD_goggle_motion");

        pawn.ExecuteAction(SBasePawn.SA_VISION_EMF);
        pawn.ExecuteAction(SBasePawn.SA_GOG_SWITCH_WITHOUTANIM);
        Step(60);
        Check(rig.Clients[0], "SGoggleTextures.beam_goggle.TRD_goggle_EMF");

        ClientSession late = rig.AddClient("VisorObserver");
        Step(240);
        Check(late, "SGoggleTextures.beam_goggle.TRD_goggle_EMF");

        pawn.ExecuteAction(SBasePawn.SA_GOG_HEADMVT_OFF);
        PawnVision.AnimEvent_GoggleSwitchUp(pawn);
        pawn.bGogglesUp = true;
        Step(60);
        Check(rig.Clients[0], "SGoggleTextures.beam_goggle.TRD_goggle_orange");
        Check(late, "SGoggleTextures.beam_goggle.TRD_goggle_orange");

        void Step(int count) { for (int i = 0; i < count; i++) rig.Step(); }
        void Check(ClientSession viewer, string skin)
        {
            Assert.Equal(PawnProxyMode.Snapshots, viewer.PawnProxies);
            var observed = Assert.IsAssignableFrom<SBasePawn>(viewer.Driver.FindActorByNetGuid((uint)pawn.ActorId));
            Assert.True(observed.NetworkPoseReady);
            Assert.Equal(skin, Assert.IsType<SGoggleBeam_DEF>(observed.GoggleBeam).Skin.Path);
        }
    }
}
