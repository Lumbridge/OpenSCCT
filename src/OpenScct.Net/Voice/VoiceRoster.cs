using System;
using OpenScct.Core.Engine;

namespace OpenScct.Net.Voice;

/// <summary>Where a player is in the life cycle, as far as voice routing cares (spec/game/voice-chat.md).</summary>
public enum EVoiceLife : byte
{
    /// <summary>Playing (or waiting to spawn or respawn).</summary>
    Alive = 0,

    /// <summary>Knocked out on the ground (s_LyingOnGround).</summary>
    LyingOnGround = 1,

    /// <summary>Dead: the death state and spectating teammates after it (s_Dead, s_MultiSpectating_WhenKilled).</summary>
    Dead = 2,

    /// <summary>A spectator who is not playing (bOnlySpectator: late joiners, the non-playing host).</summary>
    Visitor = 3,
}

/// <summary>The match settings voice routing depends on.</summary>
/// <param name="Mode">GameReplicationInfo.VoiceChatMode (Enhanced 4.0 voice modes).</param>
/// <param name="InLobby">The current map is the lobby.</param>
/// <param name="Enabled">The host allows voice chat here (allowVoiceChat, and allowVoiceChatInLobby in the lobby).</param>
public readonly record struct VoiceRules(EVoiceChatMode Mode, bool InLobby, bool Enabled)
{
    public static VoiceRules Default => new(EVoiceChatMode.VCM_Modern, false, true);
}

/// <summary>
/// One player as voice routing sees it. The first block comes from the player's controller and replication info; the
/// special cases of the stock rules (grab talk, the binocular voice trace, hacked comms, downed chat) come from an
/// <see cref="IVoiceLinkSource"/> once the gameplay that drives them is ported.
/// </summary>
public struct VoiceMember
{
    /// <summary>The talker id on the wire (<see cref="VoiceK.TalkerOf"/>: PlayerReplicationInfo.PlayerID plus one). 0 is nobody.</summary>
    public ushort Talker;

    /// <summary>The player's connection (driver endpoint id); none for a listen host's own player.</summary>
    public EndpointId Endpoint;

    /// <summary>TeamInfo.TeamIndex, or -1 without a team.</summary>
    public int Team;

    public EVoiceLife Life;

    /// <summary>SPlayerController.bTeamVoiceOnly: the lobby's "Restrict Mic to Team".</summary>
    public bool TeamVoiceOnly;

    /// <summary>PlayerReplicationInfo.bVoiceBanned: the host banned this player from voice.</summary>
    public bool VoiceBanned;

    /// <summary>On a spy holding a merc with the voice key down (ServerSetVoiceComWhenGrabbing): the merc's talker id.</summary>
    public ushort GrabTalkWith;

    /// <summary>On a merc whose binocular voice trace found a spy (ServerSetVoiceChatTarget): that spy's talker id.</summary>
    public ushort VoiceTarget;

    /// <summary>Holding the Hack Comm key (bSpecialsChat) while not grabbing: hears the enemies his team tagged.</summary>
    public bool HackCommKey;

    /// <summary>Tagged by a spy bullet or the merc laser (bSpyBulletOn): the tagging team (iTeamSpying), else -1.</summary>
    public int SpiedByTeam;

    /// <summary>On a downed player: up to three enemies chatting with him (PCChattingWith), 0 for none.</summary>
    public ushort DownedChat0;

    public ushort DownedChat1;

    public ushort DownedChat2;

    /// <summary>Set by <see cref="VoiceRoster.Commit"/>: this player is in some downed player's chat list.</summary>
    public bool TalksToDowned { get; internal set; }

    /// <summary>A member with the plain defaults: no team, alive, no special cases.</summary>
    public static VoiceMember Create(ushort talker, int team, EVoiceLife life = EVoiceLife.Alive) =>
        new() { Talker = talker, Team = team, Life = life, SpiedByTeam = -1 };

    public readonly bool HasDownedChatWith(ushort talker) =>
        talker != 0 && (DownedChat0 == talker || DownedChat1 == talker || DownedChat2 == talker);
}

/// <summary>
/// The players and settings the server routes voice by, rebuilt from the game a few times a second
/// (<see cref="IVoiceDirectory.Fill"/>). Reused between rebuilds, so it does not allocate in steady state.
/// </summary>
public sealed class VoiceRoster
{
    private VoiceMember[] _members = new VoiceMember[16];
    private uint[] _mutes = new uint[16];
    private int _muteCount;

    public VoiceRules Rules { get; set; } = VoiceRules.Default;

    public int Count { get; private set; }

    public ref VoiceMember this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
            return ref _members[index];
        }
    }

    public void Clear()
    {
        Count = 0;
        _muteCount = 0;
        Rules = VoiceRules.Default;
    }

    /// <summary>Adds a player and returns its entry for filling in. A talker id already present is replaced.</summary>
    public ref VoiceMember Add(in VoiceMember member)
    {
        int existing = IndexOf(member.Talker);
        if (existing >= 0)
        {
            _members[existing] = member;
            return ref _members[existing];
        }

        if (Count == _members.Length)
        {
            Array.Resize(ref _members, Count * 2);
        }

        _members[Count] = member;
        return ref _members[Count++];
    }

    public int IndexOf(ushort talker)
    {
        if (talker == 0)
        {
            return -1;
        }

        for (int i = 0; i < Count; i++)
        {
            if (_members[i].Talker == talker)
            {
                return i;
            }
        }

        return -1;
    }

    public int IndexOfEndpoint(EndpointId endpoint)
    {
        if (!endpoint.IsValid)
        {
            return -1;
        }

        for (int i = 0; i < Count; i++)
        {
            if (_members[i].Endpoint == endpoint)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The stock mute lists (PlayerController.MutedPlayers, voice bans): neither hears the other.</summary>
    public void AddMutualMute(ushort a, ushort b)
    {
        if (a == 0 || b == 0 || a == b)
        {
            return;
        }

        uint key = MuteKey(a, b);
        if (MutuallyMuted(a, b))
        {
            return;
        }

        if (_muteCount == _mutes.Length)
        {
            Array.Resize(ref _mutes, _muteCount * 2);
        }

        _mutes[_muteCount++] = key;
    }

    public bool MutuallyMuted(ushort a, ushort b)
    {
        uint key = MuteKey(a, b);
        for (int i = 0; i < _muteCount; i++)
        {
            if (_mutes[i] == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finishes a rebuild: works out who is chatting with a downed enemy.</summary>
    public void Commit()
    {
        for (int i = 0; i < Count; i++)
        {
            ref VoiceMember m = ref _members[i];
            m.TalksToDowned = false;
            for (int j = 0; j < Count; j++)
            {
                ref VoiceMember downed = ref _members[j];
                if (j != i && downed.Life != EVoiceLife.Dead && downed.HasDownedChatWith(m.Talker) && downed.Team != m.Team)
                {
                    m.TalksToDowned = true;
                    break;
                }
            }
        }
    }

    private static uint MuteKey(ushort a, ushort b) => a < b ? ((uint)a << 16) | b : ((uint)b << 16) | a;
}
