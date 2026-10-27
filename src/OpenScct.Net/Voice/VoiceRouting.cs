using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice;

/// <summary>
/// Who hears whom (spec/game/voice-chat.md). The stock game keeps a mute matrix that its scripts update on events
/// (team change, death, knock-out, respawn, grab, hacked comms, spectating); these rules compute the same answer from
/// the players' current state, so the server can rebuild routing at any time and a modified client cannot listen to
/// the enemy: audio only ever goes where these rules send it.
/// </summary>
[PortOf("rl:voice.routing", Spec = "spec/game/voice-chat.md#rules")]
public static class VoiceRouting
{
    /// <summary>Whether <paramref name="listener"/> hears <paramref name="speaker"/> talking on <paramref name="channel"/>.</summary>
    public static bool CanHear(VoiceRoster roster, in VoiceMember listener, in VoiceMember speaker, EVoiceChannel channel)
    {
        System.ArgumentNullException.ThrowIfNull(roster);
        VoiceRules rules = roster.Rules;
        if (!rules.Enabled || listener.Talker == 0 || speaker.Talker == 0 || listener.Talker == speaker.Talker)
        {
            return false;
        }

        // Rules 2 and 3: voice bans and the stock mute lists beat every mode.
        if (listener.VoiceBanned || speaker.VoiceBanned || roster.MutuallyMuted(listener.Talker, speaker.Talker))
        {
            return false;
        }

        bool sameTeam = listener.Team >= 0 && listener.Team == speaker.Team;

        // Rules 4 and 5: Open mode and the lobby let everyone hear everyone; the team channel, and in the lobby a talker
        // who restricted his mic to his team, reach only his team.
        if (rules.Mode == EVoiceChatMode.VCM_Open || rules.InLobby)
        {
            return sameTeam || (channel == EVoiceChannel.All && !(rules.InLobby && speaker.TeamVoiceOnly));
        }

        // Rule 6: spectators who are not playing hear no one and no one hears them.
        if (listener.Life == EVoiceLife.Visitor || speaker.Life == EVoiceLife.Visitor)
        {
            return false;
        }

        if (sameTeam)
        {
            return HearsMate(rules, speaker);
        }

        // Rule 10: Team mode refuses every link across teams.
        return rules.Mode != EVoiceChatMode.VCM_Team && HearsEnemy(listener, speaker, channel);
    }

    /// <summary>Rules 7 to 9.</summary>
    private static bool HearsMate(VoiceRules rules, in VoiceMember speaker)
    {
        // A spy talking to the merc he holds, or a player chatting with a downed enemy, is cut from his own team.
        if (speaker.GrabTalkWith != 0 || speaker.TalksToDowned)
        {
            return false;
        }

        // Classic: the dead and the knocked out are silent to their team (Modern and Team let them talk to mates).
        return rules.Mode != EVoiceChatMode.VCM_Classic || speaker.Life is not (EVoiceLife.Dead or EVoiceLife.LyingOnGround);
    }

    /// <summary>Rules 11 to 14.</summary>
    private static bool HearsEnemy(in VoiceMember listener, in VoiceMember speaker, EVoiceChannel channel)
    {
        // The other team never hears the dead or the knocked out.
        if (speaker.Life is EVoiceLife.Dead or EVoiceLife.LyingOnGround)
        {
            return false;
        }

        // Eavesdropping works on team talk too: the merc's binocular voice trace and hacked comms.
        if (listener.Life == EVoiceLife.Alive)
        {
            if (listener.VoiceTarget == speaker.Talker)
            {
                return true;
            }

            if (listener.HackCommKey && listener.Team >= 0 && speaker.SpiedByTeam == listener.Team)
            {
                return true;
            }
        }

        if (channel == EVoiceChannel.Team)
        {
            return false;
        }

        // Grab talk goes both ways between the spy and the merc he holds.
        if (speaker.GrabTalkWith == listener.Talker || listener.GrabTalkWith == speaker.Talker)
        {
            return listener.Life != EVoiceLife.Dead;
        }

        // A downed player hears the enemies chatting with him.
        return listener.Life == EVoiceLife.LyingOnGround && listener.HasDownedChatWith(speaker.Talker);
    }
}
