using System;
using OpenScct.Core.Engine;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

/// <summary>The routing rules of spec/game/voice-chat.md: who hears whom by mode, team and life state.</summary>
public sealed class VoiceRoutingTests
{
    private const int Spies = 0;
    private const int Mercs = 1;

    // Two spies (1, 2) and two mercs (3, 4).
    private static VoiceRoster Roster(EVoiceChatMode mode, bool lobby = false, Action<VoiceRoster>? change = null)
    {
        var roster = new VoiceRoster();
        roster.Rules = new VoiceRules(mode, lobby, Enabled: true);
        roster.Add(VoiceMember.Create(1, Spies));
        roster.Add(VoiceMember.Create(2, Spies));
        roster.Add(VoiceMember.Create(3, Mercs));
        roster.Add(VoiceMember.Create(4, Mercs));
        change?.Invoke(roster);
        roster.Commit();
        return roster;
    }

    private static ref VoiceMember M(VoiceRoster r, ushort talker) => ref r[r.IndexOf(talker)];

    private static bool Hears(VoiceRoster r, ushort listener, ushort speaker, EVoiceChannel channel = EVoiceChannel.All) =>
        VoiceRouting.CanHear(r, in M(r, listener), in M(r, speaker), channel);

    [Theory]
    [InlineData(EVoiceChatMode.VCM_Modern)]
    [InlineData(EVoiceChatMode.VCM_Classic)]
    [InlineData(EVoiceChatMode.VCM_Team)]
    public void InAMatchOnlyTheTeamHearsYou(EVoiceChatMode mode)
    {
        VoiceRoster r = Roster(mode);
        Assert.True(Hears(r, 2, 1));
        Assert.True(Hears(r, 4, 3));
        Assert.False(Hears(r, 3, 1));
        Assert.False(Hears(r, 1, 4));
        Assert.False(Hears(r, 1, 1));
        Assert.True(Hears(r, 2, 1, EVoiceChannel.Team));
    }

    [Fact]
    public void OpenModeLetsEveryoneHearEveryoneExceptOnTheTeamChannel()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Open);
        Assert.True(Hears(r, 3, 1));
        Assert.True(Hears(r, 1, 4));
        Assert.True(Hears(r, 2, 1, EVoiceChannel.Team));
        Assert.False(Hears(r, 3, 1, EVoiceChannel.Team));
    }

    [Fact]
    public void OpenModeStillLetsTheDeadAndTheSpectatorsTalk()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Open, change: roster =>
        {
            M(roster, 1).Life = EVoiceLife.Dead;
            roster.Add(VoiceMember.Create(9, -1, EVoiceLife.Visitor));
        });
        Assert.True(Hears(r, 3, 1));
        Assert.True(Hears(r, 9, 3));
        Assert.True(Hears(r, 3, 9));
    }

    [Theory]
    [InlineData(EVoiceChatMode.VCM_Modern, true)]
    [InlineData(EVoiceChatMode.VCM_Team, true)]
    [InlineData(EVoiceChatMode.VCM_Classic, false)]
    public void DeadPlayersTalkToTheirTeamUnlessTheModeIsClassic(EVoiceChatMode mode, bool matesHearTheDead)
    {
        VoiceRoster r = Roster(mode, change: roster => M(roster, 1).Life = EVoiceLife.Dead);
        Assert.Equal(matesHearTheDead, Hears(r, 2, 1));

        // The dead always hear their living team, and the enemy never hears them.
        Assert.True(Hears(r, 1, 2));
        Assert.False(Hears(r, 3, 1));
    }

    [Theory]
    [InlineData(EVoiceChatMode.VCM_Modern, true)]
    [InlineData(EVoiceChatMode.VCM_Classic, false)]
    public void KnockedOutPlayersFollowTheSameRule(EVoiceChatMode mode, bool matesHearTheDowned)
    {
        VoiceRoster r = Roster(mode, change: roster => M(roster, 3).Life = EVoiceLife.LyingOnGround);
        Assert.Equal(matesHearTheDowned, Hears(r, 4, 3));
        Assert.True(Hears(r, 3, 4));
        Assert.False(Hears(r, 1, 3));
    }

    [Fact]
    public void TwoDeadTeammatesHearEachOtherOnlyInModern()
    {
        VoiceRoster modern = Roster(EVoiceChatMode.VCM_Modern, change: r => { M(r, 1).Life = EVoiceLife.Dead; M(r, 2).Life = EVoiceLife.Dead; });
        VoiceRoster classic = Roster(EVoiceChatMode.VCM_Classic, change: r => { M(r, 1).Life = EVoiceLife.Dead; M(r, 2).Life = EVoiceLife.Dead; });
        Assert.True(Hears(modern, 2, 1));
        Assert.False(Hears(classic, 2, 1));
    }

    [Fact]
    public void SpectatorsWhoAreNotPlayingHearNoOneAndNoOneHearsThem()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Modern, change: roster =>
        {
            roster.Add(VoiceMember.Create(9, -1, EVoiceLife.Visitor));
            roster.Add(VoiceMember.Create(10, -1, EVoiceLife.Visitor));
        });
        Assert.False(Hears(r, 9, 1));
        Assert.False(Hears(r, 1, 9));
        Assert.False(Hears(r, 10, 9));
    }

    [Fact]
    public void TheLobbyLetsEveryoneTalkUnlessATalkerRestrictedHisMic()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Team, lobby: true, change: roster => M(roster, 1).TeamVoiceOnly = true);
        Assert.True(Hears(r, 1, 3));
        Assert.True(Hears(r, 2, 1));
        Assert.False(Hears(r, 3, 1));
        Assert.True(Hears(r, 4, 3, EVoiceChannel.Team));
        Assert.False(Hears(r, 1, 3, EVoiceChannel.Team));
    }

    [Fact]
    public void GrabTalkGoesBothWaysAndCutsTheSpyFromHisTeam()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Modern, change: roster => M(roster, 1).GrabTalkWith = 3);
        Assert.True(Hears(r, 3, 1));
        Assert.True(Hears(r, 1, 3));
        Assert.False(Hears(r, 2, 1));

        // The held merc's team still hears him; the other merc does not hear the spy.
        Assert.True(Hears(r, 4, 3));
        Assert.False(Hears(r, 4, 1));

        // Team talk is not grab talk.
        Assert.False(Hears(r, 3, 1, EVoiceChannel.Team));
    }

    [Fact]
    public void TeamModeRefusesEveryLinkAcrossTeams()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Team, change: roster =>
        {
            M(roster, 1).GrabTalkWith = 3;
            M(roster, 4).VoiceTarget = 2;
            M(roster, 2).SpiedByTeam = Mercs;
            M(roster, 3).HackCommKey = true;
        });
        Assert.False(Hears(r, 3, 1));
        Assert.False(Hears(r, 4, 2));
        Assert.False(Hears(r, 3, 2));

        // As in the original, the spy who holds the voice key while grabbing is still cut from his team.
        Assert.False(Hears(r, 2, 1));
    }

    [Fact]
    public void TheBinocularTraceAndHackedCommsEavesdropEvenOnTeamTalk()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Modern, change: roster =>
        {
            M(roster, 4).VoiceTarget = 2;       // merc 4's binoculars found spy 2
            M(roster, 3).SpiedByTeam = Spies;   // spies tagged merc 3
            M(roster, 1).HackCommKey = true;    // spy 1 holds Hack Comm
        });
        Assert.True(Hears(r, 4, 2, EVoiceChannel.Team));
        Assert.False(Hears(r, 2, 4));
        Assert.True(Hears(r, 1, 3, EVoiceChannel.Team));
        Assert.False(Hears(r, 2, 3));
        Assert.False(Hears(r, 1, 4));
    }

    [Fact]
    public void EavesdroppingStopsWhenEitherSideIsDownOrDead()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Modern, change: roster =>
        {
            M(roster, 3).SpiedByTeam = Spies;
            M(roster, 1).HackCommKey = true;
            M(roster, 3).Life = EVoiceLife.LyingOnGround;
        });
        Assert.False(Hears(r, 1, 3));

        VoiceRoster dead = Roster(EVoiceChatMode.VCM_Modern, change: roster =>
        {
            M(roster, 3).SpiedByTeam = Spies;
            M(roster, 1).HackCommKey = true;
            M(roster, 1).Life = EVoiceLife.Dead;
        });
        Assert.False(Hears(dead, 1, 3));
    }

    [Fact]
    public void ADownedPlayerHearsTheEnemyChattingWithHimWhoseTeamStopsHearingHim()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Modern, change: roster =>
        {
            M(roster, 1).Life = EVoiceLife.LyingOnGround;
            M(roster, 1).DownedChat0 = 3;
        });
        Assert.True(Hears(r, 1, 3));
        Assert.False(Hears(r, 4, 3));
        Assert.False(Hears(r, 3, 1));
        Assert.True(M(r, 3).TalksToDowned);
    }

    [Fact]
    public void MutesAndVoiceBansBeatEveryMode()
    {
        foreach (EVoiceChatMode mode in Enum.GetValues<EVoiceChatMode>())
        {
            VoiceRoster muted = Roster(mode, change: roster => roster.AddMutualMute(1, 2));
            Assert.False(Hears(muted, 2, 1));
            Assert.False(Hears(muted, 1, 2));

            VoiceRoster banned = Roster(mode, change: roster => M(roster, 3).VoiceBanned = true);
            Assert.False(Hears(banned, 4, 3));
            Assert.False(Hears(banned, 3, 4));
        }
    }

    [Fact]
    public void VoiceOffMeansNobodyHearsAnybody()
    {
        VoiceRoster r = Roster(EVoiceChatMode.VCM_Open);
        r.Rules = r.Rules with { Enabled = false };
        Assert.False(Hears(r, 2, 1));
    }
}
