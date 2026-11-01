using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Net.Moves;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>The reported MoonD low ledge, through the local player's real input and network session.</summary>
public sealed class MoonWaistLedgeNetworkTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrabStartupSettlesSmoothlyWithoutANetworkCorrection(bool moving)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("MoonD") is null, "MoonD content is not installed.");
        using var run = new PlacedPlayer("MoonD", PlaySide.Spy);
        if (!run.ClientController.bFastSpeed) run.Tap("ChangeSpeed");
        run.Place(new FVector(-4217.1587f, -3249.0166f, 889.005f), 4783633);
        run.ClientController.SetViewRotation(new FRotator(-3634, -495, 0));
        run.Ticks(10);
        ClientMoves moves = ((NetMovementBridge)run.Session.Client.World.Level.GetService<IMovementNet>()!)
            .EndpointOf(run.ClientController)!.Client!;
        int corrections = moves.CorrectionsApplied;
        long received = moves.AuthoritativeCorrectionsReceived;
        var trace = new List<string>();
        int detectTick = -1, climbTick = -1;
        bool grabPlayingBeforeClimb = true;
        float largestStartupDrop = 0f, largestViewDrop = 0f;
        FVector previous = run.Client.Location;
        FVector previousView = run.Session.View.Location;
        run.Session.Input.Set("BT_Up", moving);
        run.Session.Input.Commands.Add(new InputCommand("Jump"));
        run.Session.Input.Up = 280f;
        for (int tick = 0; tick < 90; tick++)
        {
            // A fixed half-second approach covers the grab and releases before walking off the far side.
            if (tick == 30) run.Session.Input.Set("BT_Up", false);
            bool grabPlaying = run.Client.PawnAnim[0].bPlaying;
            run.Tick();
            var client = run.Client;
            var server = run.Server;
            var channel = client.PawnAnim[0];
            string state = run.ClientController.GetStateName().ToString();
            if (state == "s_WaistLedgeDetect" && detectTick < 0) detectTick = tick;
            if (state == "s_WaistLedge" && climbTick < 0)
            {
                climbTick = tick;
                grabPlayingBeforeClimb = grabPlaying;
            }
            if (tick < 20)
            {
                largestStartupDrop = MathF.Max(largestStartupDrop, previous.Z - client.Location.Z);
                largestViewDrop = MathF.Max(largestViewDrop, previousView.Z - run.Session.View.Location.Z);
            }
            if (tick < 35 || tick % 10 == 0)
            {
                trace.Add($"{tick}: c={client.Location} d={client.Location - previous} {client.Physics}/{state} " +
                    $"s={server.Location} {server.Physics}/{run.Session.ServerController!.GetStateName()} " +
                    $"corr={moves.CorrectionsApplied - corrections}/{moves.AuthoritativeCorrectionsReceived - received} " +
                    $"offset={client.NetworkCorrectionOffset} clip={channel.Sequence}@{channel.Frame:F4}/{channel.ClipFrame:F4} " +
                    $"tween={channel.TweenWeight:F4} lock={client.NetworkRootMotion.Capture()} view={run.Session.View.Location} " +
                    $"sclip={server.PawnAnim[0].Sequence}@{server.PawnAnim[0].Frame:F4} slock={server.NetworkRootMotion.Capture()} " +
                    $"origins={client.vLocationBeforeRM}/{server.vLocationBeforeRM} ready={run.ClientController.bCanStop}/{run.Session.ServerController.bCanStop}");
            }
            previous = client.Location;
            previousView = run.Session.View.Location;
        }

        string details = $"moving={moving}; startup drop={largestStartupDrop:F3}; view drop={largestViewDrop:F3}; detect/climb={detectTick}/{climbTick}\n" + string.Join('\n', trace);
        output.WriteLine(details);
        Assert.True(detectTick >= 0 && climbTick > detectTick, details);
        // The first observed detect sample has already consumed one tick of its 0.08-second blend.
        Assert.True(climbTick - detectTick >= 4, details);
        // The grab completes before the physics tick which starts root motion. Starting inside AnimEnd
        // put the authoritative clip one tick behind and produced corrections throughout the climb.
        Assert.False(grabPlayingBeforeClimb, details);
        Assert.InRange(largestStartupDrop, 0f, 2.55f);
        Assert.InRange(largestViewDrop, 0f, 2.55f);
        Assert.Equal(corrections, moves.CorrectionsApplied);
        Assert.Equal(received, moves.AuthoritativeCorrectionsReceived);
        Assert.InRange(run.Client.NetworkCorrectionOffset.Size(), 0f, 0.01f);
        Assert.InRange(FVector.Dist(run.Client.Location, run.Server.Location), 0f, 8f);
        Assert.InRange(FVector.Dist(run.Client.Location, new FVector(-4161f, -3249f, 989.005f)), 0f, 0.5f);
    }
}
