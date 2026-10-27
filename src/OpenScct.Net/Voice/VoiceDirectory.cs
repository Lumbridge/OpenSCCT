using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net.Voice;

/// <summary>Where the server's voice relay learns who is playing and in which state.</summary>
public interface IVoiceDirectory
{
    /// <summary>Rebuilds <paramref name="roster"/> (cleared first): every player, their state, the mute lists and the rules.</summary>
    void Fill(VoiceRoster roster);

    /// <summary>
    /// A Join from an endpoint the roster does not know, claiming a talker id. Only test directories accept claims;
    /// the game's directory knows every endpoint from its connections and refuses.
    /// </summary>
    bool TryClaim(ushort talker, EndpointId endpoint) => false;
}

/// <summary>
/// The stock special cases of one player, read from gameplay that <c>OpenScct.Net</c> cannot see (SBase): grab talk, the
/// merc's binocular voice target, the Hack Comm key and spy bullet tags, downed chat partners and the lobby's
/// team-only switch. See spec/game/voice-chat.md rules 8 to 14.
/// </summary>
public interface IVoiceLinkSource
{
    void ReadLinks(PlayerController controller, ref VoiceMember member);
}

/// <summary>
/// The game's voice directory: the players of a server <see cref="NetDriver"/>'s connections (each connection's
/// actor is its PlayerController), their PlayerReplicationInfo (PlayerID, team, bOnlySpectator, bVoiceBanned) and
/// controller state, the stock mute lists (PlayerController.MutedPlayers) and the rules from the
/// GameReplicationInfo (VoiceChatMode, allowVoiceChat, allowVoiceChatInLobby). Reads the world: call it on the
/// server's thread.
/// </summary>
public sealed class NetDriverVoiceDirectory : IVoiceDirectory
{
    private static readonly Name NAME_s_LyingOnGround = "s_LyingOnGround";
    private static readonly Name NAME_s_Dead = "s_Dead";
    private static readonly Name NAME_s_MultiSpectating_WhenKilled = "s_MultiSpectating_WhenKilled";

    private readonly NetDriver _driver;
    private readonly List<PlayerController> _controllers = new();

    public NetDriverVoiceDirectory(NetDriver driver, IVoiceLinkSource? links = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (!driver.IsServer)
        {
            throw new ArgumentException("Voice routing runs on the server.", nameof(driver));
        }

        _driver = driver;
        Links = links;
    }

    /// <summary>The SBase special cases, once installed by the game.</summary>
    public IVoiceLinkSource? Links { get; set; }

    public void Fill(VoiceRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        roster.Clear();
        _controllers.Clear();
        Level? level = _driver.World?.Level;
        if (level is null)
        {
            return;
        }

        GameReplicationInfo? gri = level.Info.GRI ?? level.Info.Game?.GameReplicationInfo;
        bool inLobby = string.Equals(level.GetService<IGameHost>()?.GetURLMap(), "Lobby", StringComparison.OrdinalIgnoreCase);
        bool enabled = gri is null || (gri.allowVoiceChat && (!inLobby || gri.allowVoiceChatInLobby));
        roster.Rules = new VoiceRules(gri?.VoiceChatMode ?? EVoiceChatMode.VCM_Modern, inLobby, enabled);

        IReadOnlyList<NetConnection> connections = _driver.ClientConnections;
        for (int i = 0; i < connections.Count; i++)
        {
            NetConnection connection = connections[i];
            if (connection.State == EConnectionState.USOCK_Closed || connection.IsRecorder || connection.Actor is not PlayerController pc ||
                pc.bDeleteMe || pc.PlayerReplicationInfo is not { } pri || VoiceK.TalkerOf(pri.PlayerID) == 0)
            {
                continue;
            }

            var member = VoiceMember.Create(VoiceK.TalkerOf(pri.PlayerID), pri.Team?.TeamIndex ?? -1, LifeOf(pc, pri));
            member.Endpoint = connection.RemoteEndpoint;
            member.VoiceBanned = pri.bVoiceBanned;
            ref VoiceMember added = ref roster.Add(member);
            Links?.ReadLinks(pc, ref added);
            _controllers.Add(pc);
        }

        foreach (PlayerController pc in _controllers)
        {
            ushort talker = VoiceK.TalkerOf(pc.PlayerReplicationInfo!.PlayerID);
            foreach (Actor? muted in pc.MutedPlayers)
            {
                if (muted is PlayerController { PlayerReplicationInfo: { } other })
                {
                    roster.AddMutualMute(talker, VoiceK.TalkerOf(other.PlayerID));
                }
            }
        }

        _controllers.Clear();
        roster.Commit();
    }

    /// <summary>Voice life state from the controller's state (spec/game/voice-chat.md "Inputs").</summary>
    public static EVoiceLife LifeOf(PlayerController pc, PlayerReplicationInfo pri)
    {
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentNullException.ThrowIfNull(pri);
        if (pri.bOnlySpectator)
        {
            return EVoiceLife.Visitor;
        }

        if (pc.IsInState(NAME_s_LyingOnGround))
        {
            return EVoiceLife.LyingOnGround;
        }

        return pc.IsInState(NAME_s_Dead) || pc.IsInState(NAME_s_MultiSpectating_WhenKilled) ? EVoiceLife.Dead : EVoiceLife.Alive;
    }
}

/// <summary>
/// A fixed list of players for tests and the loopback smoke: members are given up front, and a client claims its
/// talker id with its first Join, which binds its endpoint.
/// </summary>
public sealed class StaticVoiceDirectory : IVoiceDirectory
{
    private readonly List<VoiceMember> _members = new();
    private readonly List<(ushort A, ushort B)> _mutes = new();

    public VoiceRules Rules { get; set; } = VoiceRules.Default;

    /// <summary>Adds or replaces a player.</summary>
    public void Set(VoiceMember member)
    {
        ushort talker = member.Talker;
        int i = _members.FindIndex(m => m.Talker == talker);
        if (i >= 0)
        {
            VoiceMember updated = member;
            if (!updated.Endpoint.IsValid)
            {
                updated.Endpoint = _members[i].Endpoint;
            }

            _members[i] = updated;
        }
        else
        {
            _members.Add(member);
        }
    }

    /// <summary>Changes one player in place (team, life state, links).</summary>
    public void Update(ushort talker, Func<VoiceMember, VoiceMember> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        int i = _members.FindIndex(m => m.Talker == talker);
        if (i < 0)
        {
            throw new ArgumentException($"No player {talker}.", nameof(talker));
        }

        _members[i] = change(_members[i]);
    }

    public void AddMutualMute(ushort a, ushort b) => _mutes.Add((a, b));

    public void Fill(VoiceRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        roster.Clear();
        roster.Rules = Rules;
        foreach (VoiceMember m in _members)
        {
            roster.Add(m);
        }

        foreach ((ushort a, ushort b) in _mutes)
        {
            roster.AddMutualMute(a, b);
        }

        roster.Commit();
    }

    public bool TryClaim(ushort talker, EndpointId endpoint)
    {
        int i = _members.FindIndex(m => m.Talker == talker);
        if (i < 0 || !endpoint.IsValid)
        {
            return false;
        }

        VoiceMember m = _members[i];
        m.Endpoint = endpoint;
        _members[i] = m;
        return true;
    }
}
