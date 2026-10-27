using System;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

/// <summary>
/// What a bot needs to play by a player's rules: goggles on the bot's controller, so the goggle and torch keys work for
/// a server bot as for a player (the ability switches come from the host settings, HostSettingsTests).
/// </summary>
[Collection(AiMatchCollection.Name)]
public sealed class BotGadgetTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();

    [Fact]
    public void AMercBotsGoggleAndTorchKeysWork()
    {
        Assert.SkipWhen(Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        AiDifficulty quiet = AiDifficulty.For(AiSkill.Normal) with { GadgetUse = 0f };
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = MapCatalog.TestBox,
            Bots = ServerBotSettings.TwoVersusTwo(quiet, quiet),
            Seed = 5,
            Classes = NetClasses.Create(),
            Maps = Maps,
        });
        AiController? merc = null;
        while (match.Frame < 20 * Sim.Hz && merc is null)
        {
            match.Step();
            foreach (AiController b in match.Bots.Bots)
            {
                if (b.Pawn is SPAWNDEFENSE)
                {
                    merc = b;
                }
            }
        }

        Assert.NotNull(merc);
        SPlayerController pc = merc!.Controller;
        Assert.NotNull(pc.Goggle);
        Assert.True(pc.GameReplicationInfo?.bAvlb_MERC_Torche);

        // Motion tracking on, after the goggle clip; then off again.
        pc.eventSwitchHeadset(1);
        Step(match, 1f);
        Assert.Equal(SGoggle.REN_MotionTrackingVision, pc.RendMap);
        pc.eventSwitchHeadset(1);
        Step(match, 1f);
        Assert.Equal(SGoggle.REN_DynLight, pc.RendMap);

        // The torch toggles in normal view.
        var pawn = (SPAWNDEFENSE)merc.Pawn!;
        pc.eventGadTorcheSwitch();
        Step(match, 0.2f);
        Assert.True(pawn.Torche?.bInUse);
        pc.eventGadTorcheSwitch();
        Step(match, 0.2f);
        Assert.False(pawn.Torche?.bInUse);
    }

    private static void Step(AiBotMatch match, float seconds)
    {
        long until = match.Frame + (long)(seconds * Sim.Hz);
        while (match.Frame < until)
        {
            match.Step();
        }
    }
}
