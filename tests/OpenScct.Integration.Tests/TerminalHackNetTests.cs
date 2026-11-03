using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Integration.Tests;

/// <summary>
/// spec/objectives/terminal-hack.md rules 31 to 35 over the wire: a SteeE terminal hacked well past 10 s of level time
/// shows the server's progress on a connected client (it used to read 99 % from the first frame, the client rebuilding
/// the progress from its own level time and an unreplicated start time). Skipped without the converted SteeE.
/// </summary>
public sealed class TerminalHackNetTests
{
    private const string MapName = "SteeE";

    [Fact]
    public void A_walking_correction_during_alignment_returns_control_without_a_terminal_snap()
    {
        var geometry = new CollisionDataBuilder();
        geometry.AddBox(new FVector(-1000, -1000, -100), new FVector(1000, 1000, 0));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, geometry.Build());
        rig.Place(new FVector(0, 0, 89.5f), default, default, false);
        rig.WaitStill(120);
        var terminal = rig.Level.SpawnActor(new SComputerObjectiveTrigger { bStatic = false, bNoDelete = false, CaptureTime = 30 },
            rig.Pawn.Location + new FVector(180, 0, 0), new FRotator(0, 32768, 0));
        Assert.NotNull(terminal);
        rig.Controller.Role = rig.Pawn.Role = ENetRole.ROLE_AutonomousProxy;
        rig.Controller.RemoteRole = ENetRole.ROLE_Authority;
        rig.Controller.GotoState(K.s_Computer_GoTo);
        Assert.True(rig.Pawn.Physics == EPhysics.PHYS_Repositionnal,
            $"Initial {rig.Controller.GetStateName()}/{rig.Pawn.Physics}, pawn {rig.Pawn.Location}, terminal {terminal.Location}.");
        rig.Step(default);
        Assert.True(rig.Pawn.Physics == EPhysics.PHYS_Repositionnal,
            $"After step {rig.Controller.GetStateName()}/{rig.Pawn.Physics}, pawn {rig.Pawn.Location}, terminal {terminal.Location}.");

        var moves = new SBasePawnMoves(rig.Controller) { ModernCommands = true };
        FVector corrected = rig.Pawn.Location;
        moves.CapturePrediction(0);
        moves.BeginPredictionReplay();
        try
        {
            moves.RestorePrediction(0, corrected, default, EPhysics.PHYS_Walking, rig.Level.Info, rig.Pawn.ZoneConfig);
        }
        finally { moves.EndPredictionReplay(corrected); }
        Assert.True(rig.Controller.IsInState(K.s_Computer_GoTo));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);

        for (int i = 0; i < 5 * Sim.Hz; i++) rig.Step(default);

        Assert.Equal(MovementStateNames.s_PlayerWalkingSt0H, rig.Controller.GetStateName());
        Assert.InRange((rig.Pawn.Location - corrected).Size2D(), 0f, 1f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_owner_aligns_and_hacks_over_the_movement_protocol(bool delayed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapName, Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, SimLink = delayed ? SimLinkProfile.B : null,
            TerminalsBlockPlayers = false,
        }, clock);
        void Tick()
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
        }
        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        SPlayerController owner = Assert.IsType<SPlayerController>(session.LocalController);
        SPlayerController server = Assert.IsType<SPlayerController>(session.ServerController);
        var serverPawn = Assert.IsAssignableFrom<SPawnAttaque>(server.Pawn);
        for (int i = 0; i < 120; i++) Tick();

        SComputerObjectiveTrigger? terminal = null;
        foreach (SComputerObjectiveTrigger candidate in session.Host.World.Level.AllActors<SComputerObjectiveTrigger>())
        {
            terminal = candidate;
            break;
        }
        Assert.NotNull(terminal);
        terminal.bActive = true;
        terminal.bLocked = false;
        terminal.CaptureTime = 30;
        if (terminal.Objective is { } objective) objective.ObjectiveTaken = false;
        FVector front = terminal.Rotation.Vector();
        FVector approach = terminal.Location + front * 100f;
        var driver = MovementNatives.Driver(serverPawn)!;
        Assert.True(driver.Collision.LineCheck(out FCheckResult floor, serverPawn,
            approach - new FVector(0, 0, 500), approach + new FVector(0, 0, 100), new FVector(4, 4, 2),
            OpenScct.Physics.K.Flags.Floor), $"No floor at {approach} (terminal {terminal.Location}, front {front}).");
        FVector at = floor.Location + new FVector(0, 0, serverPawn.CollisionHeight + 2f);
        MovementNatives.FarMoveActor(serverPawn, at);
        serverPawn.Velocity = serverPawn.Acceleration = default;
        serverPawn.Rotation = (-front).Rotation();
        var serverMoves = ((NetMovementBridge)session.Host.World.Level.GetService<IMovementNet>()!).EndpointOf(server)!.Server!;
        serverMoves.ReinitPawn();
        owner.Rotation = serverPawn.Rotation;
        for (int i = 0; i < RubberBandRun.SettleTicks; i++) Tick();
        var pawn = Assert.IsAssignableFrom<SPawnAttaque>(owner.Pawn);
        int index = session.Host.Driver.MapBinding.IndexOf(terminal);
        var copy = Assert.IsType<SComputerObjectiveTrigger>(session.Client.Map!.MapActors[index]);
        var entry = Assert.IsType<SComputerInteraction>(copy.ComputerInteraction);
        Assert.True(entry.CanUse(owner));
        long reinits = serverMoves.Sent.Reinits;
        owner.InteractionInit(entry, 0);
        bool aligned = false;
        for (int i = 0; i < 5 * Sim.Hz && !owner.IsInState(K.s_Computer_Use); i++)
        {
            Tick();
            aligned |= owner.IsInState(K.s_Computer_GoTo) && pawn.Physics == EPhysics.PHYS_Repositionnal;
        }
        Assert.True(aligned, "The owner must actually run laptop positioning through movement commands.");
        Assert.True(owner.IsInState(K.s_Computer_Use), $"Owner remained in {owner.GetStateName()}/{pawn.Physics} at {pawn.Location}.");
        for (int i = 0; i < Sim.Hz; i++) Tick();
        Assert.True(server.IsInState(K.s_Computer_Use));
        Assert.True(terminal.bTriggering);
        Assert.InRange((serverPawn.Location - pawn.Location).Size(), 0f, 2f);
        Assert.Equal(reinits, serverMoves.Sent.Reinits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_late_client_can_resume_a_stopped_hack_as_soon_as_the_terminal_is_released(bool delayed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(delayed ? SimLinkProfile.B : null, 42UL, 0,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer });
        for (int n = 0; n < 30 * Sim.Hz; n++) rig.Step();
        ClientSession client = rig.AddClient();
        for (int n = 0; n < 3 * Sim.Hz; n++) rig.Step();
        Assert.Equal(EClientState.Joined, client.State);

        SComputerObjectiveTrigger? terminal = null;
        foreach (SComputerObjectiveTrigger candidate in rig.Server.World.Level.AllActors<SComputerObjectiveTrigger>())
        {
            terminal = candidate;
            break;
        }
        Assert.NotNull(terminal);
        var entry = Assert.IsType<SComputerInteraction>(terminal.InteractionObj);
        terminal.bActive = true;
        terminal.bLocked = false;
        terminal.CaptureTime = 10;
        if (terminal.Objective is { } objective) objective.ObjectiveTaken = false;
        for (int n = 0; n < Sim.Hz; n++) rig.Step();
        var copy = Assert.IsType<SComputerInteraction>(client.Driver.FindActorByNetGuid((uint)entry.ActorId));
        Assert.Equal(0, copy.iReusableTime);

        // Drive the authoritative use and cancel; both the use timestamp and availability reach the late
        // client through normal replication, while its own level clock is still thirty seconds younger.
        var spy = rig.Server.World.Level.Spawn<SPlayerController>()!;
        FVector useSpot = entry.Location + entry.Rotation.Vector() * 30f;
        spy.Pawn = rig.Server.World.Level.Spawn<SPawnAttaque>(useSpot, (entry.Location - useSpot).Rotation())!;
        spy.SBPawn = (SBasePawn)spy.Pawn;
        spy.Pawn.Controller = spy;
        spy.GotoState("s_PlayerWalkingSt0H");
        InteractionMenuRules.InteractionInit(spy, entry, 0);
        terminal.eventTriggering(true);
        for (int n = 0; n < Sim.Hz; n++) rig.Step();
        Assert.False(copy.bUsable);
        Assert.Equal(entry.iLastUsedTime, copy.iLastUsedTime);
        terminal.eventTriggering(false);
        Assert.True(entry.bUsable);
        for (int n = 0; n < Sim.Hz && !copy.bUsable; n++) rig.Step();
        Assert.True(copy.bUsable);
        Assert.True(copy.iLastUsedTime - copy.Level.TimeSeconds > 20f,
            "The replicated server use time must still be ahead of the late client's clock.");

        var localSpy = client.World.Level.Spawn<SPlayerController>()!;
        FVector at = copy.Location + copy.Rotation.Vector() * 30f;
        localSpy.Pawn = client.World.Level.Spawn<SPawnAttaque>(at, (copy.Location - at).Rotation())!;
        Assert.True(copy.CanUse(localSpy),
            "A released laptop with no reuse delay must be usable immediately, without waiting for client uptime to reach the server's last-use timestamp.");
    }

    [Fact]
    public void A_client_shows_the_server_hack_progress_after_thirty_seconds_of_level_time()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.Get("B"), 41UL, 1,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer });
        SComputerObjectiveTrigger? terminal = null;
        foreach (Actor? actor in rig.Server.Map.MapActors)
        {
            if (actor is SComputerObjectiveTrigger { bDeleteMe: false } t)
            {
                terminal = t;
                break;
            }
        }

        Assert.NotNull(terminal);
        ClientSession client = rig.Clients[0];
        rig.WaitForAllWalkers(1, input: WalkPatterns.For);
        int index = rig.Server.Driver.MapBinding.IndexOf(terminal);
        var copy = (SComputerObjectiveTrigger)client.Map!.MapActors[index]!;
        for (int n = 0; n < 1800 && (client.Connection!.FindActorChannel(copy) is null || terminal.Level.TimeSeconds < 30f); n++)
        {
            rig.Step(WalkPatterns.For);
        }

        Assert.NotNull(client.Connection!.FindActorChannel(copy));
        Assert.True(terminal.Level.TimeSeconds >= 30f);
        Assert.Equal(ENetRole.ROLE_SimulatedProxy, copy.Role);

        terminal.bActive = true;
        terminal.bLocked = false;
        terminal.CaptureTime = terminal.DominationCaptureTime;
        if (terminal.Objective is { } objective)
        {
            objective.ObjectiveTaken = false;
        }

        terminal.eventTriggering(true);
        var samples = new List<(int Server, int Client)>();
        for (int n = 0; n < 5 * Sim.Hz; n++)
        {
            rig.Step(WalkPatterns.For);
            if (n % Sim.Hz == Sim.Hz - 1)
            {
                samples.Add((terminal.GetHackPercent(), copy.GetHackPercent()));
            }
        }

        // The server counts DeltaCaptureTime from the hack start; the client only shows what was sent.
        Assert.InRange(terminal.GetHackPercent(), 49, 51);
        Assert.InRange(copy.GetHackPercent(), 40, 55);
        foreach ((int server, int clientPercent) in samples)
        {
            // Within a net update or two of the server (5 Hz objective triggers, plus link latency).
            Assert.InRange(server - clientPercent, -1, 6);
        }

        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].Client > samples[i - 1].Client, $"client percent did not rise: {samples[i - 1].Client} then {samples[i].Client}");
        }
    }
}
