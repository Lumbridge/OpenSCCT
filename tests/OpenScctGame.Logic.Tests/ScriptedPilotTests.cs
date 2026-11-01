using System;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ScriptedPilotTests
{
    [Theory]
    [InlineData(PlaySide.Spy)]
    [InlineData(PlaySide.Merc)]
    public void PositioningFinishesWithoutAUsableInteractionOrPressingUse(PlaySide side)
    {
        Run(side, faceOnly: true);
    }

    [Fact]
    public void DefaultArrivalStillRequiresAUsableInteraction()
    {
        Run(PlaySide.Spy, faceOnly: false);
    }

    private static void Run(PlaySide side, bool faceOnly)
    {
        Assert.NotNull(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox));
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Side = side, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0,
        }, clock);
        (FVector Spot, FVector Face)? target = null;
        (FVector Spot, FVector Face)? Target(SPlayerController pc) => target ??=
            pc.Pawn is { } pawn ? (pawn.Location, pawn.Location + pawn.Rotation.Vector() * 500f) : null;
        var pilot = faceOnly ? new ScriptedPilot(Target, PilotArrival.FaceOnly) : new ScriptedPilot(Target);
        bool sawFace = false, soughtPrompt = false;
        FVector faceLocation = default;
        session.BeforeClientTick = (s, _) =>
        {
            PilotPhase before = pilot.Phase;
            pilot.Tick(s);
            Assert.Equal(0, s.Input.Get("bUsing"));
            if (before == PilotPhase.Face)
            {
                if (!sawFace) faceLocation = s.LocalPawn!.Location;
                sawFace = true;
                Assert.False(s.LocalController!.IManager?.HasUsableInteractions() == true,
                    "The positioning fixture unexpectedly reached an interactable object.");
                soughtPrompt |= s.Input.Get("BT_Up") != 0;
            }
        };
        for (int tick = 1; tick <= 20 * Sim.Hz && pilot.Phase is not (PilotPhase.Done or PilotPhase.Failed); tick++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
            session.Step(clock.NowMicroseconds);
            Assert.Null(session.ServerFailure);
        }

        Assert.True(sawFace, $"Pilot did not reach Face: {pilot.Phase}, {pilot.Failure}");
        if (faceOnly)
        {
            Assert.Equal(PilotPhase.Done, pilot.Phase);
            Assert.Null(pilot.Failure);
            Assert.False(soughtPrompt, "Position-only arrival continued walking in search of an interaction.");
            Assert.InRange((session.LocalPawn!.Location - faceLocation).Size2D(), 0f, 3f);
        }
        else
        {
            Assert.True(soughtPrompt, "Interaction arrival did not approach its required prompt.");
            Assert.Equal(PilotPhase.Failed, pilot.Phase);
            Assert.StartsWith("no usable interaction", pilot.Failure, StringComparison.Ordinal);
        }
    }
}
