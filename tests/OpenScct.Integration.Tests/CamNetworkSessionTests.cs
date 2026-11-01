using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CamNetworkSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoteMercEntersOnOnePressAndKeepsTheCameraWithoutTurningInput(bool latency)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("AquaE") is null, "Aquarius content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "AquaE", Side = PlaySide.Merc, Rules = PlayRules.MapSettings,
            HostMode = PlayHostMode.Inline, MatchSeed = 3, SimLink = latency ? SimLinkProfile.B : null,
        }, clock);
        var trace = new List<string>();
        void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1800 && (session.LocalPawn?.MyWeapon is null ||
            session.ServerController?.SBPawn?.MyWeapon is null || session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        Tick(120);
        SPlayerController pc = Assert.IsType<SPlayerController>(session.LocalController);
        SPlayerController authority = Assert.IsType<SPlayerController>(session.ServerController);
        NetConnection connection = session.Host!.Driver.OwningConnection(authority)!;
        Assert.Same(authority.SBPawn, session.Host.Driver.ViewerFor(connection).Viewer);
        Assert.Equal(authority.SBPawn!.Location, session.Host.Driver.ViewerFor(connection).Source);
        Assert.IsType<SGadCamNetwork>(PawnInventory.FindGadgetType(session.ServerController!.SBPawn!, new ClassRef("SBase.SGadCamNetwork")));
        var previous = session.Host!.Driver.DeferReceivedFunction;
        session.Host.Driver.DeferReceivedFunction = (actor, function, arguments, connection, handler) =>
        {
            NetFunctionHandler traced = (self, f, values, from) =>
            {
                string before = session.ServerController.GetStateName().ToString();
                handler(self, f, values, from);
                if (before != session.ServerController.GetStateName().ToString() || f.Name == "PCChangeServerState")
                    trace.Add($"RPC {f.Name}: {before} -> {session.ServerController.GetStateName()} used={session.ServerController.SBPawn?.UsedObject?.ActorId}");
            };
            if (previous?.Invoke(actor, function, arguments, connection, traced) == true) return true;
            traced(actor, function, arguments, connection);
            return true;
        };
        SGadCamNetwork gadget = Assert.IsType<SGadCamNetwork>(PawnInventory.FindGadgetType(pc.SBPawn!, new ClassRef("SBase.SGadCamNetwork")));
        pc.SBPawn!.SelectedGadget = gadget;
        session.Input.Set("bAltFire", true);
        session.Input.Commands.Add(new InputCommand("AltFire"));
        Tick(8);
        session.Input.Set("bAltFire", false);
        for (int i = 0; i < 240; i++)
        {
            Tick();
            if (i < 8 || i % 60 == 0) trace.Add($"{i}: owner={pc.GetStateName()} used={pc.SBPawn?.UsedObject?.ActorId} server={session.ServerController?.GetStateName()} used={session.ServerController?.SBPawn?.UsedObject?.ActorId}");
        }
        string detail = string.Join("\n", trace);
        Assert.True(pc.IsInState(GadgetStates.s_UseObject_CamNetwork), detail);
        Assert.True(session.ServerController!.IsInState(GadgetStates.s_UseObject_CamNetwork), detail);
        SCamNetwork cam = Assert.IsType<SCamNetwork>(pc.SBPawn!.UsedObject);
        Assert.Same(pc.SBPawn, cam.aUsedByPawn);
        Assert.Same(authority.SBPawn!.UsedObject, session.Host.Driver.ViewerFor(connection).Viewer);
        PlayerCalcView.StickyCamView(authority, false, out _, out FVector cameraPosition, out _);
        Assert.Equal(cameraPosition, session.Host.Driver.ViewerFor(connection).Source);
        int yaw = cam.iYaw, pitch = cam.iPitch;
        Tick(240);
        Assert.True(pc.IsInState(GadgetStates.s_UseObject_CamNetwork), pc.GetStateName().ToString());
        Assert.Equal(yaw, cam.iYaw);
        Assert.Equal(pitch, cam.iPitch);
        Assert.Equal((int)cam.YawConstraint.X & 65535, cam.iYaw & 65535);
        Assert.Equal((int)cam.PitchConstraint.X & 65535, cam.iPitch & 65535);
        // A positional correction must not restore a pre-camera controller state or discard the used object.
        pc.SBPawn.Location += new FVector(80, 0, 0);
        Tick(180);
        Assert.True(pc.IsInState(GadgetStates.s_UseObject_CamNetwork), pc.GetStateName().ToString());
        Assert.Same(cam, pc.SBPawn.UsedObject);
        uint beforeTurning = session.Client.Movement.PeekEndpoint(pc)!.Sequenced!.LastAcknowledged;
        // Rendered camera rotation reports must not force reliable traffic past the budget and starve movement
        // fences. More than 256 reports reproduces the real UDP session's former control-queue disconnect.
        for (int i = 0; i < 360; i++)
        {
            Assert.Equal(EClientState.Joined, session.Client.State);
            session.Client.Driver.ServerConnection!.CurrentNetSpeed = 5120;
            pc.UpdateOrientableRotation(cam, pitch, yaw + i % 64);
            Tick();
        }
        Tick(120);
        Assert.Equal(EClientState.Joined, session.Client.State);
        Assert.True(session.Client.Movement.PeekEndpoint(pc)!.Sequenced!.LastAcknowledged > beforeTurning + 300);
        Assert.True(pc.IsInState(GadgetStates.s_UseObject_CamNetwork), pc.GetStateName().ToString());
        SCamNetwork originalAuthorityCamera = Assert.IsType<SCamNetwork>(authority.SBPawn.UsedObject);
        Assert.Equal(cam.iYaw, originalAuthorityCamera.iReplicatedYaw);
        // Hold a final old-camera report behind an exhausted budget while the reliable switch is requested.
        typeof(NetDriver).GetProperty(nameof(NetDriver.QueuedBytes))!.SetValue(session.Client.Driver, 20_000L);
        pc.ServerUpdateOrientableRotation(cam, pitch, yaw + 300);
        session.Input.Set("bUsing", true);
        Tick(1);
        session.Input.Set("bUsing", false);
        typeof(NetDriver).GetProperty(nameof(NetDriver.QueuedBytes))!.SetValue(session.Client.Driver, 0L);
        Tick(120);
        Assert.Same(cam.NextCam, pc.SBPawn.UsedObject);
        Assert.NotEqual(yaw + 300, originalAuthorityCamera.iReplicatedYaw);
        Assert.True(ReferenceEquals(pc.SBPawn, ((SCamNetwork)pc.SBPawn.UsedObject!).aUsedByPawn),
            $"owner {pc.GetStateName()} used={pc.SBPawn.UsedObject?.ActorId} controller={((SCamNetwork)pc.SBPawn.UsedObject!).aUsedByPawn?.ActorId}; " +
            $"server {session.ServerController.GetStateName()} used={session.ServerController.SBPawn?.UsedObject?.ActorId} " +
            $"controller={(session.ServerController.SBPawn?.UsedObject as SCamNetwork)?.aUsedByPawn?.ActorId} " +
            $"client channel={session.Client.Driver.ServerConnection!.FindActorChannel(pc.SBPawn.UsedObject!)?.ChIndex} " +
            $"source role={session.ServerController.SBPawn?.UsedObject?.Role} remote={session.ServerController.SBPawn?.UsedObject?.RemoteRole} " +
            $"always={session.ServerController.SBPawn?.UsedObject?.bAlwaysRelevant}\n" + string.Join("\n", trace));
        pc.eventAltFire(0);
        Tick(120);
        Assert.False(pc.IsInState(GadgetStates.s_UseObject_CamNetwork));
        Assert.Null(session.ServerController.SBPawn!.UsedObject);
        Assert.Same(authority.SBPawn, session.Host.Driver.ViewerFor(connection).Viewer);
        // A dead spectator must see their surviving teammate's area, even while their own corpse still exists.
        SBasePawn teammate = Assert.IsType<SPawnDefense_1Mesh>(session.Host.World.Level.Spawn<SPawnDefense_1Mesh>());
        teammate.Location = authority.SBPawn.Location + new FVector(4000, 0, 0);
        authority.GotoState(new Name("s_MultiSpectating"));
        authority.SpecTarget = teammate;
        Assert.Same(teammate, session.Host.Driver.ViewerFor(connection).Viewer);
        Assert.Equal(teammate.Location, session.Host.Driver.ViewerFor(connection).Source);
        authority.SpecTarget = null;
        authority.ViewTarget = teammate;
        Assert.Same(teammate, session.Host.Driver.ViewerFor(connection).Viewer);
        Assert.Equal(teammate.Location, session.Host.Driver.ViewerFor(connection).Source);
    }
}
