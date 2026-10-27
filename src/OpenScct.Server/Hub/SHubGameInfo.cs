using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScct.Server.Hub;

/// <summary>
/// The hub's game rules: an OpenSCCT addition (spec/additions/hub.md), not a port. It is the SBase game info with a
/// few rules replaced, so spawning, pawns, inventory and the moveset are the original's:
/// <list type="bullet">
/// <item>the match is always running and never ends: no time limit, no objectives, no end check on a leaver or a
/// death;</item>
/// <item>a player who joins plays at once (never the late joiner's spectator seat), on the side he asked for, with
/// no team cap below the server's player limit;</item>
/// <item>a player may change side at any time (<see cref="SwitchSide"/>) and respawn at the plaza
/// (<see cref="TakePawn"/> then <see cref="Spawn"/>); a pawn that is gone comes back after
/// <see cref="HubK.RespawnSeconds"/>;</item>
/// <item>no lives, kills or deaths are counted;</item>
/// <item>voice chat is Open (both sides hear each other), narrowed by distance on the server (docs/HUB.md).</item>
/// </list>
/// Damage and pawn-on-pawn attacks are off through the level's <see cref="OpenScct.Game.Modes.PlayRules"/>, which
/// <see cref="HubServer"/> installs.
/// </summary>
public sealed class SHubGameInfo : SGameInfo
{
    /// <summary>Players the hub takes (the SBase cap of 32 does not apply).</summary>
    public int HubMaxPlayers { get; set; } = HubK.DefaultMaxPlayers;

    public override void InitGame(string Options, ref string Error)
    {
        base.InitGame(Options, ref Error);
        MaxPlayers = Math.Max(1, GetIntOption(Options, "MaxPlayers", HubMaxPlayers));
        HubMaxPlayers = MaxPlayers;
        MaxTeamSize = MaxPlayers;
    }

    /// <summary>
    /// The SBase login, with the match seen as not yet begun so the player is not seated as a spectator, and as a
    /// delayed start so he waits for <see cref="HubServer"/> to spawn him (the late joiner's path).
    /// </summary>
    public override PlayerController? Login(string Portal, string Options, ref string Error)
    {
        GameReplicationInfo? gri = GameReplicationInfo;
        bool begun = gri?.bMatchHasBegun ?? false;
        bool delayed = bDelayedStart;
        if (gri is not null)
        {
            gri.bMatchHasBegun = false;
        }

        bDelayedStart = true;
        try
        {
            return base.Login(Portal, Options, ref Error);
        }
        finally
        {
            if (gri is not null)
            {
                gri.bMatchHasBegun = begun;
            }

            bDelayedStart = delayed;
        }
    }

    /// <summary>The asked side, whatever the team sizes (0 spies, anything else mercs).</summary>
    public override byte PickTeam(byte num, Controller C) => num == 0 ? (byte)0 : (byte)1;

    /// <summary>
    /// The hub's gear rule (spec/additions/gear-change.md): a living player's new kit goes on at once, at most once
    /// every <see cref="HubK.GearChangeCooldownSeconds"/>; without a pawn it goes on at the next spawn.
    /// </summary>
    public override GearChangeResult CanChangeGearNow(SPlayerController PC, out float WaitSeconds)
    {
        WaitSeconds = 0f;
        if (PC.Pawn is not SBasePawn { bDeleteMe: false, Health: > 0 })
        {
            return GearChangeResult.NextSpawn;
        }

        float since = Level.TimeSeconds - PC.GearChangedAt;
        if (since < HubK.GearChangeCooldownSeconds)
        {
            WaitSeconds = HubK.GearChangeCooldownSeconds - since;
            return GearChangeResult.Cooldown;
        }

        return GearChangeResult.Now;
    }

    /// <summary>Scores nothing and never ends the match.</summary>
    public override void Killed(Controller? Killer, Controller? Killed, Pawn? KilledPawn, SDamageType DamageType)
    {
    }

    /// <summary>A leaver never ends the match: the end check is skipped as for a spectator.</summary>
    public override void Logout(Controller Exiting)
    {
        if (Exiting.PlayerReplicationInfo is { } pri)
        {
            // Off his side first: as a spectator Controller.Destroyed no longer takes him off the team.
            if (!pri.bOnlySpectator && pri.Team is { } team)
            {
                team.RemoveFromTeam(Exiting);
            }

            pri.bOnlySpectator = true;
        }

        base.Logout(Exiting);
    }

    /// <summary>
    /// Starts the hub's never-ending match before anyone joined: the clock runs, every later player spawns through
    /// <see cref="Spawn"/>.
    /// </summary>
    public void StartHub()
    {
        bDelayedStart = false;

        // Voice is Open in the hub, so spies and mercs hear each other (PostLogin copies the mode to the GRI again).
        PersistentData.VoiceChatMode = 3;
        if (GameReplicationInfo is { } gri)
        {
            gri.bMatchHasBegun = true;
            gri.VoiceChatMode = EVoiceChatMode.VCM_Open;
            gri.allowVoiceChat = true;
        }

        StartMatch();
    }

    /// <summary>Spawns a player who has no pawn at a start of his side (the plaza), with the side's pawn and kit.</summary>
    public bool Spawn(PlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (pc.bDeleteMe || pc.Pawn is { bDeleteMe: false })
        {
            return false;
        }

        ApplySide(pc);
        if (pc.PlayerReplicationInfo is { } pri)
        {
            pri.NumLives = HubK.Lives;
            pri.bOnlySpectator = false;
            pri.bIsSpectator = false;
        }

        eventRestartPlayer(pc);
        Host.ClientCall(pc, new GameCall("resetConnectionPing"));
        return pc.Pawn is { bDeleteMe: false };
    }

    /// <summary>
    /// Takes the player's pawn away (destroyed, as SPlayerController.UnPossess does). The new one comes from
    /// <see cref="Spawn"/>, which <see cref="HubServer"/> calls once the player's client has let go of the old pawn:
    /// a restart that reaches a client still holding a live pawn restarts that pawn, and the new one is never taken
    /// (SPlayerController.ClientRestart only waits for a pawn when it has none).
    /// </summary>
    public void TakePawn(PlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        pc.UnPossess();
    }

    /// <summary>
    /// Moves the player to <paramref name="team"/> (0 spies, 1 mercs) and takes his pawn away (see
    /// <see cref="TakePawn"/>); <see cref="Spawn"/> then gives him the side's pawn. False when he is on that side already.
    /// </summary>
    public bool SwitchSide(PlayerController pc, int team)
    {
        ArgumentNullException.ThrowIfNull(pc);
        team = team == 0 ? 0 : 1;
        if (TeamIndexOf(pc) == team && pc.Pawn is { bDeleteMe: false })
        {
            return false;
        }

        TakePawn(pc);
        if (TeamIndexOf(pc) != team || pc.PlayerReplicationInfo?.Team is null)
        {
            eventChangeTeam(pc, team, true);
        }

        return true;
    }

    /// <summary>The side's pawn type, field of view and pawn class, as Login sets them.</summary>
    private void ApplySide(PlayerController pc)
    {
        PlayerReplicationInfo? pri = pc.PlayerReplicationInfo;
        if (pc is SPlayerController side)
        {
            // A spy's start state sets the crouch toggle and only a spy's sets it again: a player who was a spy
            // before would start his merc crouched (HubClient clears it on his own machine too).
            side.bCrouchWithRoll = false;
            if (TeamIndexOf(pc) == 0)
            {
                side.SPawnType = PawnType.SPAWNOFFENSE;
                side.DefaultFOV = side.SpyFOV;
                side.DesiredFOV = side.SpyFOV;
            }
            else
            {
                side.SPawnType = PawnType.SPAWNDEFENSE;
                side.DefaultFOV = side.DefenderFOV;
                side.DesiredFOV = side.DefenderFOV;
            }
        }

        int mesh = pri?.MyMeshNum ?? 0;
        Name pawnClass = pri?.Team is STeamInfo team && mesh < team.PawnList.Count ? team.PawnList[mesh] : Name.None;
        pc.SetPawnClass(pawnClass);
    }
}
