using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>Real carousel collision and owner prediction, including input and delayed server acknowledgements.</summary>
public sealed class MovingPlatformOwnerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void OwnerCanRideAndWalkOnMoonCarousel(int latency)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("MoonD") is null, "MoonD content is not installed.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "MoonD", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            SimLink = latency == 0 ? null : SimLinkProfile.Get(latency.ToString()), MatchSeed = 119,
        }, clock);
        void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int tick = 0; tick < 600 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); tick++) Tick();
        Tick(60);
        var pc = Assert.IsAssignableFrom<SPlayerController>(session.LocalController);
        var authority = Assert.IsAssignableFrom<SPlayerController>(session.ServerController);
        var local = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        var server = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        Mover carousel = session.Host.Map.MapActors.OfType<Mover>().Single(m => m.Tag.ToString().EndsWith("RideCarouselSpin", StringComparison.Ordinal));
        Mover copy = session.Client.Map!.MapActors.OfType<Mover>().Single(m => m.Tag == carousel.Tag);
        var owner = ((NetMovementBridge)pc.XLevel.GetService<IMovementNet>()!).EndpointOf(pc)!.Client!;
        var endpoint = ((NetMovementBridge)authority.XLevel.GetService<IMovementNet>()!).EndpointOf(authority)!;
        // Land naturally on the rotating deck, using the same authoritative reset as a respawn.
        MovementNatives.FarMoveActor(server, new FVector(300, -1000, 620));
        server.Velocity = default;
        server.Acceleration = default;
        server.Rotation = default;
        authority.Rotation = default;
        endpoint.Server!.ReinitPawn();
        Tick(60);
        pc.SetViewRotation(default);
        output.WriteLine($"setup local={local.Location} base={local.Base?.Tag}, server={server.Location} base={server.Base?.Tag}, mover={carousel.Location}/{carousel.Rotation} copy={copy.Location}/{copy.Rotation}");
        Assert.Same(carousel, server.Base);
        Assert.Same(copy, local.Base);
        var trace = new List<string>();
        static FVector Relative(Actor pawn, Actor basis)
        {
            FVector delta = pawn.Location - basis.Location;
            FCoords axes = FCoords.FromRotator(basis.Rotation);
            return new FVector(delta | axes.XAxis, delta | axes.YAxis, delta | axes.ZAxis);
        }
        FVector previous = local.Location;
        FVector idleLocal = Relative(local, copy), idleServer = Relative(server, carousel);
        FVector settledLocal = default, settledServer = default;
        float largestStep = 0, largestError = 0, largestIdleDrift = 0, largestSettledDrift = 0;
        int corrections = owner.CorrectionsApplied;
        int settledCorrections = corrections;
        for (int tick = 0; tick < 300; tick++)
        {
            // This half-second walk reaches the outer deck, where a missed rotation step exceeds the
            // correction threshold. At the initial radius that same error can remain below tolerance.
            session.Input.Set("BT_Up", tick is >= 90 and < 120);
            Tick();
            largestStep = MathF.Max(largestStep, (local.Location - previous).Size());
            largestError = MathF.Max(largestError, (Relative(local, copy) - Relative(server, carousel)).Size());
            if (tick < 90)
            {
                largestIdleDrift = MathF.Max(largestIdleDrift, (Relative(local, copy) - idleLocal).Size());
                largestIdleDrift = MathF.Max(largestIdleDrift, (Relative(server, carousel) - idleServer).Size());
            }
            if (tick == 180)
            {
                settledCorrections = owner.CorrectionsApplied;
                settledLocal = Relative(local, copy);
                settledServer = Relative(server, carousel);
            }
            else if (tick > 180)
            {
                largestSettledDrift = MathF.Max(largestSettledDrift, (Relative(local, copy) - settledLocal).Size());
                largestSettledDrift = MathF.Max(largestSettledDrift, (Relative(server, carousel) - settledServer).Size());
            }
            if (tick % 30 == 0) trace.Add(
                $"{tick}: c={local.Location}/{local.Base?.Tag}/{local.Physics}/{local.ZoneConfig} v={local.Velocity}, " +
                $"s={server.Location}/{server.Base?.Tag}/{server.Physics}/{server.ZoneConfig} v={server.Velocity}, " +
                $"corrections={owner.CorrectionsApplied - corrections}, lastDistance={owner.LastCorrectionDistance}, " +
                $"savedError={(owner.UpdatingSMove is { } updated ? (updated.SavedLocation - owner.vUpdateLocation).Size() : 0)}, updateBase={owner.aUpdateBase?.Tag}");
            previous = local.Location;
        }
        string details = $"latency={latency}; largest step={largestStep}, relative error={largestError}, " +
            $"idle drift={largestIdleDrift}, settled drift={largestSettledDrift};\n" + string.Join('\n', trace);
        output.WriteLine(details);
        Assert.True(largestStep < 25f, details);
        Assert.True(largestError < 50f, details);
        Assert.True(largestIdleDrift < 1f, details);
        Assert.True(largestSettledDrift < 1f, details);
        Assert.True((Relative(server, carousel) - idleServer).Size() > 25f, "Forward input must move the player across the carousel.\n" + details);
        Assert.True((server.Location - carousel.Location).Size2D() > 350f, "The player must reach the outer rotating deck.\n" + details);
        Assert.True((Relative(local, copy) - Relative(server, carousel)).Size() < 5f, details);
        Assert.True(owner.CorrectionsApplied - settledCorrections <= 1, "Standing on the carousel must not cause repeated prediction corrections.\n" + details);
        Assert.Same(carousel, server.Base);
        Assert.Same(copy, local.Base);
    }
}
