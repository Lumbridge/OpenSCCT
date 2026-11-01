using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class OwnerLatencyTests
{
    [Theory]
    [InlineData(PlaySide.Spy)]
    [InlineData(PlaySide.Merc)]
    public void PossessionClearsTheOldBodysPendingCorrectionBeforeClientReplay(PlaySide side)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestBox", Side = side, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            SimLink = SimLinkProfile.Get("100"), MatchSeed = 119,
        }, clock);
        for (int i = 0; i < 600; i++)
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.EndTick();
        }
        var pc = Assert.IsAssignableFrom<SPlayerController>(session.LocalController);
        var bridge = (NetMovementBridge)pc.XLevel.GetService<IMovementNet>()!;
        var moves = bridge.EndpointOf(pc)!.Client!;
        var old = pc.Pawn;
        moves.SavedMoves.Add(123, old!.Location, EPhysics.PHYS_Falling, 0);
        moves.bUpdatePosition = true;
        // Possession can be resolved before the server's reliable new-epoch message. No epoch is
        // supplied here: the pawn identity itself must invalidate the old replay and correction.
        pc.Pawn = side == PlaySide.Spy
            ? pc.XLevel.Spawn<SPawnAttaque_1Mesh>(new FVector(2000, 2000, 2000))
            : pc.XLevel.Spawn<SPawnDefense_1Mesh>(new FVector(2000, 2000, 2000));
        Assert.NotNull(pc.Pawn);
        Assert.Null(pc.Pawn.Controller);
        pc.Pawn.Role = ENetRole.ROLE_AutonomousProxy;
        pc.SetViewTarget(pc);
        bridge.BeforeClientTick();
        Assert.Null(moves.SavedMoves.FirstSMove);
        Assert.False(moves.bUpdatePosition);
        Assert.Null(moves.UpdatingSMove);
        Assert.Same(pc.Pawn, pc.ViewTarget);
        Assert.Same(pc, pc.Pawn.Controller);
        pc.Pawn = old;
    }

    [Theory]
    [InlineData(PlaySide.Spy)]
    [InlineData(PlaySide.Merc)]
    public void CorrectedLandingDoesNotChargeASecondMovementStepToTheInputTick(PlaySide side)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestBox", Side = side, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            SimLink = SimLinkProfile.Get("100"), MatchSeed = 119,
        }, clock);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        Tick(600);
        var pc = Assert.IsAssignableFrom<SPlayerController>(session.LocalController);
        var pawn = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        var connection = session.Host.Driver.OwningConnection(session.ServerController!)!;
        FVector grounded = pawn.Location;
        pc.GotoState(new Name("PlayerFalling"));
        pawn.Physics = EPhysics.PHYS_Falling;
        var correction = new SBasePawnMoves(pc) { ModernCommands = true };
        correction.CapturePrediction(0);
        correction.BeginPredictionReplay();
        try { correction.RestorePrediction(0, grounded, default, EPhysics.PHYS_Walking, pawn.Base, pawn.ZoneConfig); }
        finally { correction.EndPredictionReplay(grounded); }
        Assert.False(pc.StateNode is SPlayerController.s_PlayerJump);
        var sequenced = ((NetMovementBridge)pc.XLevel.GetService<IMovementNet>()!).EndpointOf(pc)!.Sequenced!;
        sequenced.SealPredictionTick();
        Tick(180);
        Assert.True(connection.State == EConnectionState.USOCK_Open, connection.CloseReason);
        Assert.Equal(EClientState.Joined, session.Client.State);
        Assert.Equal(0, sequenced.ResynchronizationRequests);
        Assert.Equal(EPhysics.PHYS_Walking, session.LocalPawn!.Physics);
    }

    [Fact]
    public void ExtraFullMovementStepsInOneInputTickAreRejected()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestBox", Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            SimLink = SimLinkProfile.A, MatchSeed = 119,
        }, clock);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        Tick(600);
        var pc = Assert.IsAssignableFrom<SPlayerController>(session.LocalController);
        var source = session.Host.Driver.OwningConnection(session.ServerController!)!;
        var moves = ((NetMovementBridge)pc.XLevel.GetService<IMovementNet>()!).EndpointOf(pc)!.Sequenced!;
        // The normal tick already submitted its full physics step. A second full step with the
        // same tick must not spend saved outage credit to accelerate this client's simulation.
        moves.MyShortServerMove(pc.Level.TimeSeconds, pc.Pawn!.Location, 0, 0x10, 0, 0);
        moves.SealPredictionTick();
        Tick(60);
        Assert.Equal(EConnectionState.USOCK_Closed, source.State);
        Assert.Equal("Movement input exceeds one simulation step per tick", source.CloseReason);
    }

    [Fact]
    public void RecoveredInputDoesNotRemainASecondBehindAfterAnOutage()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestBox", Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            SimLink = SimLinkProfile.Get("100"), MatchSeed = 119,
        }, clock);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        Tick(600);
        var pc = Assert.IsAssignableFrom<SPlayerController>(session.LocalController);
        var authority = Assert.IsAssignableFrom<SPlayerController>(session.ServerController);
        var owner = ((NetMovementBridge)pc.XLevel.GetService<IMovementNet>()!).EndpointOf(pc)!.Sequenced!;
        var source = ((NetMovementBridge)authority.XLevel.GetService<IMovementNet>()!).EndpointOf(authority)!.Sequenced!;
        for (int outage = 0; outage < 3; outage++)
        {
            session.SimLink!.PauseLink("local-player-1", PlaySession.HostAddress, clock.NowMicroseconds + 1_000_000);
            Tick(240);
            Assert.True(owner.NextSequence - owner.LastAcknowledged < 25,
                $"Outage {outage}: input still queued. Owner [{owner.DescribePending()}]; authority [{source.DescribePending()}]");
            Assert.Equal(0, owner.ResynchronizationRequests);
        }
    }

    [Theory]
    [InlineData(NetProfile.Stock, "100", false)]
    [InlineData(NetProfile.Stock, "150", false)]
    [InlineData(NetProfile.Stock, "200", false)]
    [InlineData(NetProfile.Enhanced, "100", false)]
    [InlineData(NetProfile.Enhanced, "150", false)]
    [InlineData(NetProfile.Enhanced, "200", false)]
    [InlineData(NetProfile.Stock, "200", true)]
    [InlineData(NetProfile.Enhanced, "200", true)]
    [InlineData(NetProfile.Stock, "300", false)]
    [InlineData(NetProfile.Enhanced, "300", false)]
    [InlineData(NetProfile.Stock, "300", true)]
    [InlineData(NetProfile.Enhanced, "300", true)]
    public void OwnerReplaysCorrectionsAndConvergesWithinHalfASecondAfterAQuarterSecondOutage(NetProfile profile, string latency, bool changingLink)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null,
            "The TestRange map is not in this checkout (python tools/testmap/generate.py --no-classes).");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            NetworkProfile = profile, SimLink = SimLinkProfile.Get(latency), MatchSeed = 27,
        }, clock);
        void Tick()
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.EndTick();
        }
        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        SPlayerController pc = Assert.IsAssignableFrom<SPlayerController>(session.LocalController);
        SPlayerController authority = Assert.IsAssignableFrom<SPlayerController>(session.ServerController);
        for (int i = 0; i < 120; i++) Tick();
        SBasePawn local = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        SBasePawn server = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var clientMoves = ((NetMovementBridge)pc.XLevel.GetService<IMovementNet>()!).EndpointOf(pc)!.Client!;
        var serverEndpoint = ((NetMovementBridge)authority.XLevel.GetService<IMovementNet>()!).EndpointOf(authority)!;
        long reinits = serverEndpoint.Server!.Sent.Reinits;
        uint epoch = serverEndpoint.Sequenced!.CommandEpoch;
        pc.Rotation = default;
        MovementNatives.FarMoveActor(local, local.Location + new FVector(12f, 0, 0));
        int singleStepRun = 0, longestSingleStepRun = 0;
        for (int i = 0; i < 60; i++)
        {
            // Routine authoritative state acknowledgements refresh independently of a movement lifetime.
            // Their reliable delivery can lag commands; neither side may reset its sequence because of them.
            if (i == 10) authority.eventMirrorState(authority.GetStateName());
            if (changingLink && i == 30)
            {
                SimLinkProfile link = SimLinkProfile.Get(latency);
                session.SimLink!.SetLinkProfile("local-player-1", PlaySession.HostAddress, link with { OneWayLatencyMs = 150 });
                session.SimLink.SetLinkProfile(PlaySession.HostAddress, "local-player-1", link with { OneWayLatencyMs = 50 });
            }
            if (changingLink && i == 45)
            {
                session.SimLink!.SetLinkProfile("local-player-1", PlaySession.HostAddress, null);
                session.SimLink.SetLinkProfile(PlaySession.HostAddress, "local-player-1", null);
            }
            session.Input.Set("BT_Up", true);
            uint applied = serverEndpoint.Sequenced.LastApplied;
            Tick();
            singleStepRun = serverEndpoint.Sequenced.LastApplied == applied + 1 ? singleStepRun + 1 : 0;
            longestSingleStepRun = Math.Max(longestSingleStepRun, singleStepRun);
        }
        if (!changingLink)
            Assert.True(longestSingleStepRun >= 12, $"{profile}/{latency}: movement never sustained twelve single-step host ticks ({longestSingleStepRun})");
        session.Input.Set("BT_Up", false);
        long deliveryResumes = clock.NowMicroseconds + 250_000;
        session.SimLink!.PauseLink("local-player-1", PlaySession.HostAddress, deliveryResumes);
        // The acceptance deadline starts when delivery recovers, after the injected outage.
        while (clock.NowMicroseconds < deliveryResumes + 500_000) Tick();
        Assert.True((local.Location - server.Location).Size() <= 2f, $"{profile}/{latency}: client {local.Location}, server {server.Location}; " +
            $"authority [{serverEndpoint.Sequenced.DescribePending()}], owner [{((OpenScct.Net.Moves.SequencedMoveTransport)clientMoves.Transport).DescribePending()}]");
        Assert.Equal(reinits, serverEndpoint.Server.Sent.Reinits);
        Assert.Equal(epoch, serverEndpoint.Sequenced.CommandEpoch);
        Assert.Equal(0, ((OpenScct.Net.Moves.SequencedMoveTransport)clientMoves.Transport).ResynchronizationRequests);
        Assert.True(clientMoves.MovesReplayed > 0);
        Assert.True(serverEndpoint.Sequenced!.DuplicateCommands > 0);
        for (int i = 0; i < 6; i++) Tick();
        Assert.InRange(local.NetworkCorrectionOffset.Size(), 0f, 0.01f);
    }
}
