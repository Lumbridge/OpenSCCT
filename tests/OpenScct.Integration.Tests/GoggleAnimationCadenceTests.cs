using System;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class GoggleAnimationCadenceTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(NetProfile.Stock, 0)]
    [InlineData(NetProfile.Stock, 2)]
    [InlineData(NetProfile.Enhanced, 0)]
    [InlineData(NetProfile.Enhanced, 2)]
    public void OwnerGoggleActionsAdvanceEveryAuthorityTickThroughDelayedCommandBatches(NetProfile profile, int ownerIndex)
    {
        string? folder = WalkerRig.Maps.FindFolder("SteeE");
        string? chars = folder is null ? null : PlayMapLoader.FindCharsDir(folder);
        Assert.SkipWhen(chars is null, "The converted character clips are not in .content/shared/chars.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 530, SimLinkProfile.Ping200);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
            Profile = profile,
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
            match.Step(clock.NowMicroseconds);
        }
        for (int i = 0; i < 900; i++) Tick();
        BotClient owner = match.Bots[ownerIndex];
        SPlayerController authority = match.ServerController(owner)!;
        var source = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var local = Assert.IsAssignableFrom<SBasePawn>(owner.Controller!.Pawn);
        SGoggle goggles = Assert.IsType<SGoggle>(owner.Controller.Goggle);
        PawnAnimationSource animations = PawnAnimationSource.FromContent(chars!);
        void UseClips(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
        UseClips(match.Server.World.Level);
        foreach (BotClient client in match.Bots) UseClips(client.Session.World.Level);
        Name clip = new("GoglStAlDn2");
        Assert.True(animations.TryGetClip(source, clip, out PawnClip authored) && authored.Frames > 2);
        var commands = match.Server.Movement.PeekEndpoint(authority)!.Sequenced!;
        Assert.True(match.Server.Movement.DrivesPawn(source));

        int quietTicks = 0, burstTicks = 0;
        foreach (bool lower in new[] { true, false })
        {
            goggles.SwitchMode(1);
            int authoritySamples = 0, ownerSamples = 0;
            bool paused = false;
            for (int i = 0; i < 180; i++)
            {
                float before = source.PawnAnim[SBasePawn.GOGGLECHANNEL].Frame;
                float localBefore = local.PawnAnim[SBasePawn.GOGGLECHANNEL].Frame;
                uint applied = commands.LastApplied;
                Tick();
                CheckStep(source, before, ref authoritySamples);
                CheckStep(local, localBefore, ref ownerSamples);
                PawnAnimChannel ch = source.PawnAnim[SBasePawn.GOGGLECHANNEL];
                if (!paused && ch.Sequence == clip && ch.Frame is > .05f and < .3f)
                {
                    // A short upstream outage deliberately exhausts the input playout cushion while
                    // the independent overlay is active, then redundant commands catch up in batches.
                    network.PauseLink(owner.Name, "server", clock.NowMicroseconds + 250_000);
                    paused = true;
                }
                if (ch.Sequence == clip && before > .05f && ch.Frame < .85f)
                {
                    quietTicks += commands.LastApplied == applied ? 1 : 0;
                    burstTicks += commands.LastApplied > applied + 1 ? 1 : 0;
                }
            }
            Assert.True(authoritySamples > 40 && ownerSamples > 40,
                $"{profile}/{ownerIndex}: authority {authoritySamples}, owner {ownerSamples} smooth goggle ticks");
            Assert.Equal(!lower, source.bGogglesUp);
            Assert.Equal(!lower, local.bGogglesUp);
            Assert.Equal(0f, source.PawnAnim[SBasePawn.GOGGLECHANNEL].BlendAlpha);
            Assert.Equal(0f, local.PawnAnim[SBasePawn.GOGGLECHANNEL].BlendAlpha);
            if (source is SPAWNDEFENSE) Assert.Equal(lower ? 1 : 0, PawnVision.MercGlowMode(source));
        }
        Assert.True(quietTicks > 0 && burstTicks > 0, $"Expected packet pacing: {quietTicks} quiet and {burstTicks} batch ticks.");
        Assert.Equal(0, commands.ResynchronizationRequests);
        Assert.Equal(0, owner.Session.Movement.PeekEndpoint(owner.Controller)!.Sequenced!.ResynchronizationRequests);

        void CheckStep(SBasePawn pawn, float before, ref int samples)
        {
            PawnAnimChannel ch = pawn.PawnAnim[SBasePawn.GOGGLECHANNEL];
            if (ch.Sequence != clip || before <= .05f || ch.Frame >= .85f || ch.Frame < before) return;
            Assert.InRange(MathF.Abs(ch.Frame - before - Sim.DeltaTime * ch.Rate), 0f, .00001f);
            samples++;
        }
    }
}
