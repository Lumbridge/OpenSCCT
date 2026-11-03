using System;
using OpenScct.Game.Combat;
using OpenScctGame.Hud;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class HudActivityPresentationTests
{
    private static HudActivityEntry Entry(HudActivityKind kind, HudActivityParticipant? actor = null, string text = "") =>
        new(1, kind, actor, null, HudActivityCause.None, "", text, 1f, false);

    [Theory]
    [InlineData(HudActivityKind.Kill, "Eliminated")]
    [InlineData(HudActivityKind.Death, "Died")]
    [InlineData(HudActivityKind.Suicide, "Self-eliminated")]
    [InlineData(HudActivityKind.Resign, "Resigned")]
    [InlineData(HudActivityKind.Sleep, "Put to sleep")]
    [InlineData(HudActivityKind.Knockout, "Knocked out")]
    public void CombatActionsDoNotDependOnLegacyOrPreviewLabels(HudActivityKind kind, string expected)
    {
        var sample = Entry(kind);
        Assert.Empty(sample.Label);
        Assert.Equal(expected, HudActivityPresentation.Action(sample.Kind));
        Assert.Equal(expected, HudActivityPresentation.NoticeDetail(sample));
    }

    [Theory]
    [InlineData(HudActivityKind.Join, "Joined the match")]
    [InlineData(HudActivityKind.Leave, "Left the match")]
    [InlineData(HudActivityKind.LastLife, "Used their last life")]
    public void ParticipantNoticesSeparateNameFromAction(HudActivityKind kind, string expected)
    {
        var entry = Entry(kind, new(1, "Raven", 0), "Raven repeated legacy notice");
        Assert.Equal("Raven", HudActivityPresentation.NoticeTitle(entry));
        Assert.Equal(expected, HudActivityPresentation.NoticeDetail(entry));
        Assert.DoesNotContain("Raven", HudActivityPresentation.NoticeDetail(entry));
        Assert.Equal("Unknown player", HudActivityPresentation.NoticeTitle(Entry(kind)));
        Assert.Equal("Unknown player", HudActivityPresentation.NoticeTitle(Entry(kind, new(2, " \t\n", 1))));
    }

    [Fact]
    public void NamesPreserveUnicodeAndCaseWithoutLineBreaks()
    {
        Assert.Equal("Écho 猫", HudActivityPresentation.ParticipantName(new(1, " \tÉcho\n猫  ", 0)));
        Assert.Equal("Unknown player", HudActivityPresentation.ParticipantName(null));
    }

    [Fact]
    public void MatchNoticeRetainsOutcomeAndUsesFallbackOnlyWhenAbsent()
    {
        Assert.Equal("Match update", HudActivityPresentation.NoticeTitle(Entry(HudActivityKind.Match, text: "Victory")));
        Assert.Equal("Victory", HudActivityPresentation.NoticeDetail(Entry(HudActivityKind.Match, text: "Victory")));
        Assert.Equal("Score: Spies 2 · Mercs 1", HudActivityPresentation.NoticeDetail(Entry(HudActivityKind.Match, text: "Score: Spies 2 · Mercs 1")));
        Assert.Equal("Match status updated", HudActivityPresentation.NoticeDetail(Entry(HudActivityKind.Match)));
    }

    [Fact]
    public void EveryKnownKindAndCauseHasReadablePresentation()
    {
        foreach (HudActivityKind kind in Enum.GetValues<HudActivityKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(HudActivityPresentation.Action(kind)));
            Assert.NotEqual("Event", HudActivityPresentation.Action(kind));
            Assert.False(string.IsNullOrWhiteSpace(HudActivityPresentation.NoticeTitle(Entry(kind))));
            Assert.False(string.IsNullOrWhiteSpace(HudActivityPresentation.NoticeDetail(Entry(kind))));
        }
        foreach (HudActivityCause cause in Enum.GetValues<HudActivityCause>())
        {
            string label = HudActivityPresentation.Cause(cause);
            if (cause == HudActivityCause.None) Assert.Empty(label);
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(label));
                if (cause != HudActivityCause.Other) Assert.NotEqual("Other", label);
            }
        }
        Assert.Equal("Camera gas", HudActivityPresentation.Cause(HudActivityCause.StickyCamera));
        Assert.Equal("Neck break", HudActivityPresentation.Cause(HudActivityCause.NeckBreak));
    }

    [Fact]
    public void TriggerDetailsDistinguishShotsChainsAndDelayedFireWithoutInventingNames()
    {
        var shot = Entry(HudActivityKind.Kill) with { Cause = HudActivityCause.ProximityMine, Trigger = CombatDeathTrigger.Shot };
        Assert.Equal("Detonated by a shot", HudActivityPresentation.TriggerDetail(shot));
        shot = shot with { TriggeredBy = new(3, "Raven", 0) };
        Assert.Equal("Shot by Raven", HudActivityPresentation.TriggerDetail(shot));
        Assert.Equal("Ignited by Raven", HudActivityPresentation.TriggerDetail(shot with { Cause = HudActivityCause.MolotovFire }));
        Assert.Equal("Chain reaction · Raven", HudActivityPresentation.TriggerDetail(shot with { Trigger = CombatDeathTrigger.ChainReaction }));
        Assert.Equal("", HudActivityPresentation.TriggerDetail(Entry(HudActivityKind.Join)));
    }
}
