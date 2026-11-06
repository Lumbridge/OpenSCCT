using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Net.Discovery;
using OpenScct.Provenance;

namespace OpenScct.Server.Online;

// Practice server mode (spec/reloaded/FEATURES.md rl:net.practice): the chat commands !heal, !regen, !tank, !kill and !ammo,
// and the regeneration they turn on. Everything runs on the server; health is replicated to the players as usual.
public sealed partial class OnlineServer
{
    /// <summary>Health a pawn gets back each second with <c>!regen</c> on.</summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded's !regen; its exact rate is not decoded (FEATURES rl:net.practice Open)")]
    private const int PracticeRegenPerSecond = 10;

    /// <summary>The round's practice switches (the level service that drops a tank's damage).</summary>
    private readonly PracticeRules _practice = new();

    /// <summary>Players with <c>!regen</c> on in this round.</summary>
    private readonly HashSet<SessionPlayer> _regen = new();

    private readonly Dictionary<SessionPlayer, (Pawn? Old, long Since)> _teamRespawns = new();

    private long _nextRegen;

    /// <summary>Whether practice was on at any time during the running round (its results then do not count).</summary>
    private bool _practiceRound;

    /// <summary>A setup that starts from a map's or the rotation's values keeps the server's practice switch.</summary>
    private LobbySetup KeepPractice(LobbySetup next) => next with { Practice = Setup.Practice, Sidearms = Setup.Sidearms, ResponsiveSmoke = Setup.ResponsiveSmoke };

    /// <summary>A new round's map server: no switches carry over, and the practice rules go on its level.</summary>
    private void StartPracticeRound(ServerHost host)
    {
        _teamRespawns.Clear();
        _practice.Clear();
        _regen.Clear();
        _practiceRound = Setup.Practice;
        _practice.On = Setup.Practice;
        host.World.Level.SetService(_practice);
    }

    /// <summary>Practice turned on or off: turning it off clears every switch.</summary>
    private void PracticeChanged()
    {
        _practice.On = Setup.Practice;
        if (Setup.Practice)
        {
            _practiceRound |= Phase is SessionPhase.Loading or SessionPhase.Match;
            return;
        }

        _practice.Clear();
        _regen.Clear();
        _practiceBotsStopped = false;
        if (_bots is { } bots) bots.Stopped = false;
    }

    private const string PracticeHelp = "!heal, !regen, !tank, !kill, !ammo. Use !help bots for bot commands";

    /// <summary>The practice chat commands; false when <paramref name="verb"/> is not one.</summary>
    private bool OnPracticeCommand(SessionPlayer p, string verb)
    {
        if (verb is not ("heal" or "regen" or "tank" or "kill" or "ammo" or "practice"))
        {
            return false;
        }

        if (!Setup.Practice)
        {
            SendMessage(p, "Practice commands work on practice servers only.");
            return true;
        }

        if (verb == "practice")
        {
            SendMessage(p, "Practice server: no time limit, unlimited lives, ammo, gadgets and gas mask air, instant gear changes, quick respawns. Commands: " + PracticeHelp + ".");
            return true;
        }

        if (Phase != SessionPhase.Match || p.Slot?.Controller is not SPlayerController pc)
        {
            SendMessage(p, "Practice commands work during a round.");
            return true;
        }

        switch (verb)
        {
            case "heal":
                if (Heal(pc))
                {
                    SendMessage(p, "Healed.");
                }
                else
                {
                    SendMessage(p, "You are not alive.");
                }

                break;
            case "regen":
                if (_regen.Remove(p))
                {
                    SendMessage(p, "Regeneration off.");
                }
                else
                {
                    _regen.Add(p);
                    SendMessage(p, "Regeneration on.");
                }

                break;
            case "tank":
                bool tank = !_practice.IsTank(pc);
                _practice.SetTank(pc, tank);
                if (tank)
                {
                    Heal(pc);
                }

                SendMessage(p, tank ? "Tank on: you take no damage." : "Tank off.");
                break;
            case "ammo":
                // Ammunition is kept by the player's own machine, so the refill is the ammo crate's call to it.
                if (pc.Pawn is { Health: > 0 })
                {
                    pc.ReloadAmmoStock();
                    SendMessage(p, "Ammunition refilled.");
                }
                else
                {
                    SendMessage(p, "You are not alive.");
                }

                break;
            case "kill":
                if (pc.Pawn is { Health: > 0 } pawn)
                {
                    pc.PawnTakeDamage(pawn.Health + OpenScct.Game.GameFlow.K.ResignExtraDamage, pawn, pawn.Location, default, SDamageType.Suicided);
                }
                else
                {
                    SendMessage(p, "You are not alive.");
                }

                break;
        }

        return true;
    }

    /// <summary>A living pawn back to its class's full health and cured of poison (as the Heal Mate entry does).</summary>
    private static bool Heal(SPlayerController pc)
    {
        if (pc.Pawn is not { Health: > 0 } pawn)
        {
            return false;
        }

        pawn.Health = SHealMateInteraction.DefaultHealth(pawn);
        if (pawn is SBasePawn sbase)
        {
            sbase.bPoisonned = false;
        }

        return true;
    }

    private bool SwitchPracticeTeam(SessionPlayer player, byte team)
    {
        if (!Setup.Practice || team is not (0 or 1) || player.IsSpectator ||
            Host.Game is not { bGameEnded: false } game || player.Slot?.Controller is not SPlayerController pc ||
            !game.eventChangeTeam(pc, team, true)) return false;

        Pawn? old = pc.Pawn;
        pc.UnPossess();
        // Wait for the old pawn channel to close before ClientRestart can acquire its replacement.
        if (!_teamRespawns.ContainsKey(player)) _teamRespawns[player] = (old, _now);
        pc.bCrouchWithRoll = false;
        pc.SPawnType = team == 0 ? PawnType.SPAWNOFFENSE : PawnType.SPAWNDEFENSE;
        pc.DefaultFOV = pc.DesiredFOV = team == 0 ? pc.SpyFOV : pc.DefenderFOV;
        if (pc.PlayerReplicationInfo is { Team: STeamInfo side } pri)
        {
            int mesh = pri.MyMeshNum;
            pc.SetPawnClass(mesh >= 0 && mesh < side.PawnList.Count ? side.PawnList[mesh] : Name.None);
            var look = team == 0 ? player.SpyLook : player.MercLook;
            (pri.SvmLookLow, pri.SvmLookHigh) = look.Split();
            pri.SvmLookExtraLow = (int)look.Extra;
            pri.SvmLookExtraHigh = (int)(look.Extra >> 32);
            pri.NumLives = team == 0 ? game.MatchInfo?.NumLivesATT ?? 1 : game.MatchInfo?.NumLivesDEF ?? 1;
        }
        return true;
    }

    private void TickPracticeTeamRespawns()
    {
        foreach (var pair in new List<KeyValuePair<SessionPlayer, (Pawn? Old, long Since)>>(_teamRespawns))
        {
            var player = pair.Key;
            if (Phase != SessionPhase.Match || !Setup.Practice || !_players.Contains(player) ||
                player.Slot?.Controller is not SPlayerController pc || Host.Game is not { bGameEnded: false } game)
            {
                _teamRespawns.Remove(player);
                continue;
            }
            if (pair.Value.Old is { } old && player.Slot.Connection.FindActorChannel(old) is not null &&
                _now - pair.Value.Since < 3 * Second) continue;
            _teamRespawns.Remove(player);
            game.eventRestartPlayer(pc);
            game.UpdateTeamNumLives();
        }
    }

    /// <summary>Each second, <c>!regen</c> players get health back; tanks are kept at full health.</summary>
    private void TickPractice()
    {
        TickPracticeTeamRespawns();
        if (!Setup.Practice || _now < _nextRegen)
        {
            return;
        }

        _nextRegen = _now + Second;
        foreach (SessionPlayer p in _players)
        {
            if (p.Slot?.Controller is not SPlayerController pc || pc.Pawn is not { Health: > 0 } pawn)
            {
                continue;
            }

            int full = SHealMateInteraction.DefaultHealth(pawn);
            if (_practice.IsTank(pc))
            {
                pawn.Health = full;
            }
            else if (_regen.Contains(p) && pawn.Health < full)
            {
                pawn.Health = System.Math.Min(full, pawn.Health + PracticeRegenPerSecond);
            }
        }
    }
}
