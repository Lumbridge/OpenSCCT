using System;
using OpenScct.Core;
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
        Assert.NotNull(MapCatalog.CreateDefault().FindFolder("TestRange"));
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
