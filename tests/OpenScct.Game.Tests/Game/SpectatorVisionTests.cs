using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

public sealed class SpectatorVisionTests
{
    private static (GameWorld World, SPlayerController Viewer, SBasePawn Target) Spectator(string state, bool merc)
    {
        GameWorld world = new GameWorld().Load(MatchLifecycleTests.SteeEStory);
        world.Gri.bAvlb_SPY_NightVision = world.Gri.bAvlb_SPY_ThermalVision = true;
        world.Gri.bAvlb_MERC_MotiontrackingVision = world.Gri.bAvlb_MERC_EMFVision = true;
        var viewer = world.Level.Spawn<SPlayerController>()!;
        viewer.GameReplicationInfo = world.Gri;
        viewer.GotoState(state);
        // Visitors may watch the opposite side and have never possessed a pawn or spawned goggles.
        viewer.SPawnType = merc ? PawnType.SPAWNOFFENSE : PawnType.SPAWNDEFENSE;
        SBasePawn target = world.SpawnPawn(!merc, default);
        viewer.SpecTarget = target;
        return (world, viewer, target);
    }

    private static void Mode(SPlayerController viewer, int expected)
    {
        Assert.Equal(expected, Assert.IsType<SGoggle>(viewer.Goggle).CurrentMode);
        Assert.Equal(expected, viewer.RendMap);
    }

    [Theory]
    [InlineData("s_BaseSpectating", false)]
    [InlineData("s_BaseSpectating", true)]
    [InlineData("s_MultiSpectating", false)]
    [InlineData("s_MultiSpectating", true)]
    [InlineData("s_MultiSpectating_WhenKilled", false)]
    [InlineData("s_MultiSpectating_WhenKilled", true)]
    [InlineData("s_Spectating_Visitor", false)]
    [InlineData("s_Spectating_Visitor", true)]
    public void VisionKeysImmediatelyChangeOnlyTheSpectatorsView(string state, bool merc)
    {
        (_, SPlayerController viewer, SBasePawn target) = Spectator(state, merc);
        int first = merc ? SGoggle.REN_MotionTrackingVision : SGoggle.REN_NightVision;
        int second = merc ? SGoggle.REN_EMFVision : SGoggle.REN_ThermalVision;
        bool emfBefore = target.bEMFIsOn;
        Assert.Null(viewer.Pawn);
        Assert.Null(viewer.Goggle);

        foreach (int expected in new[] { first, second, SGoggle.REN_DynLight })
        {
            viewer.eventSwitchHeadset(3);
            Mode(viewer, expected);
        }
        foreach ((int key, int expected) in new[] { (1, first), (1, SGoggle.REN_DynLight),
            (2, second), (2, SGoggle.REN_DynLight) })
        {
            viewer.eventSwitchHeadset(key);
            Mode(viewer, expected);
        }

        Assert.True(target.bGogglesUp);
        Assert.False(target.PawnAnim.IsAnimating(SBasePawn.GOGGLECHANNEL));
        Assert.Equal(emfBefore, target.bEMFIsOn);
        Assert.Null(viewer.Pawn);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void DisabledModesAreRefusedAndSkipped(bool merc, bool firstAvailable, bool secondAvailable)
    {
        (GameWorld world, SPlayerController viewer, _) = Spectator("s_Spectating_Visitor", merc);
        world.Gri.bAvlb_SPY_NightVision = world.Gri.bAvlb_MERC_MotiontrackingVision = firstAvailable;
        world.Gri.bAvlb_SPY_ThermalVision = world.Gri.bAvlb_MERC_EMFVision = secondAvailable;
        int first = merc ? SGoggle.REN_MotionTrackingVision : SGoggle.REN_NightVision;
        int second = merc ? SGoggle.REN_EMFVision : SGoggle.REN_ThermalVision;
        viewer.EnsureGoggle()!.Activate();

        if (!firstAvailable)
        {
            viewer.eventSwitchHeadset(1);
            Mode(viewer, SGoggle.REN_DynLight);
        }
        if (!secondAvailable)
        {
            viewer.eventSwitchHeadset(2);
            Mode(viewer, SGoggle.REN_DynLight);
        }
        viewer.eventSwitchHeadset(3);
        Mode(viewer, firstAvailable ? first : secondAvailable ? second : SGoggle.REN_DynLight);
        viewer.eventSwitchHeadset(3);
        Mode(viewer, SGoggle.REN_DynLight);
    }

    [Theory]
    [InlineData(PawnType.SPAWNOFFENSE, SGoggle.REN_NightVision)]
    [InlineData(PawnType.SPAWNDEFENSE, SGoggle.REN_MotionTrackingVision)]
    public void WithNoTargetTheSpectatorsOwnSideChoosesTheModes(PawnType side, int expected)
    {
        (_, SPlayerController viewer, _) = Spectator("s_MultiSpectating", false);
        viewer.SpecTarget = null;
        viewer.SPawnType = side;
        viewer.eventSwitchHeadset(1);
        Mode(viewer, expected);
    }
}
