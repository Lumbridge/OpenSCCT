using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-diskhunt</c>: SteeE in Disk Hunt with the untouched menu settings (20 of 30 disks in play,
/// 10 to take). The spy walks to the nearest disk the client shows and picks it up through the Use menu; the server
/// must count it as carried by him.
/// </summary>
public sealed class PlayDiskHuntSmoke : PlaySmoke
{
    public const string TestName = "play-diskhunt";

    public const int ExpectedActive = 20;

    public const int ExpectedToTake = 10;

    public override string Name => TestName;

    protected override string Mode => "DiskHunt";

    protected override int MaxTicks => 6000;

    protected override (FVector Spot, FVector Face)? Target(SPlayerController pc)
    {
        if (pc.Pawn is not { } pawn)
        {
            return null;
        }

        SDisk? best = null;
        float bestDistance = float.MaxValue;
        foreach (SDisk d in pc.XLevel.AllActors<SDisk>())
        {
            float distance = (d.Location - pawn.Location).SizeSquared();
            if (d.bActive && !d.bHidden && d.Base is null && distance < bestDistance)
            {
                best = d;
                bestDistance = distance;
            }
        }

        return best is null ? null : (best.Location, best.Location);
    }

    protected override SmokeResult? Check(PlayRoot root, ScriptedPilot pilot)
    {
        PlaySession session = root.Session!;
        (bool live, int numDisks, int toWin, int active, int carried, int shown, string state) = session.ServerSnapshot(host =>
        {
            GameReplicationInfo? gri = host.World.Level.Info.GRI;
            bool missionLive = false;
            foreach (SMission m in host.World.Level.AllActors<SMission>())
            {
                missionLive |= m.GameMode == EGameMode.GM_DiskHunt && !m.ObjectiveTaken;
            }

            int activeDisks = 0;
            foreach (SDisk d in host.World.Level.AllActors<SDisk>())
            {
                if (d.bActive)
                {
                    activeDisks++;
                }
            }

            SPlayerController? pc = session.ServerController;
            var pawn = pc?.Pawn as SBasePawn;

            // Carried once the pickup is over: the hand free again and the disk on the spy's thigh.
            int disks = pawn is { HandItem: null } && pawn.Disks.Count > 0 && ReferenceEquals(pawn.Disks[0]?.Base, pawn) &&
                (pc!.IsInState("s_PlayerWalkingSt0H") || pc.IsInState("s_PlayerWalkingCr0H"))
                ? pawn.Disks.Count
                : 0;
            int taken = pc?.PlayerReplicationInfo?.iNbObjectivesTaken ?? 0;
            return (missionLive, gri?.NumDisks ?? -1, gri?.NumDisksToWin ?? -1, activeDisks, disks, taken,
                $"server state={pc?.GetStateName()} mode={gri?.GameMode}");
        });
        if (!live)
        {
            // The disk mission is put in play when the host starts the match.
            return root.Stats.Ticks > 900 ? SmokeResult.Fail("the Disk Hunt mission never started: " + state) : null;
        }

        if (numDisks != ExpectedActive || toWin != ExpectedToTake)
        {
            return SmokeResult.Fail($"the host settings did not reach the game: NumDisks={numDisks} NumDisksToWin={toWin}");
        }

        if (active > ExpectedActive || (carried > 0 && active + carried != ExpectedActive))
        {
            return SmokeResult.Fail($"{active} disks in play and {carried} carried, expected {ExpectedActive} put in play");
        }

        if (carried < 1 || shown != carried)
        {
            return null;
        }

        return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
            $"disk picked up: carried={carried} iNbObjectivesTaken={shown} active={active} NumDisks={numDisks} toTake={toWin} {state} at tick {root.Stats.Ticks}"));
    }
}

/// <summary>
/// <c>--svm-smoke=play-deathmatch</c>: SteeE in Deathmatch with the untouched menu settings (8 spy and 4 merc lives,
/// 5 s respawn). Once the spy plays, the server's Deathmatch mission is live and the Story mission is not; the spy
/// is then killed on the server and must come back with one life less.
/// </summary>
public sealed class PlayDeathmatchSmoke : PlaySmoke
{
    public const string TestName = "play-deathmatch";

    public const int ExpectedSpyLives = 8;

    public const int ExpectedRespawn = 5;

    private long _killTick = -1;

    private Pawn? _killedPawn;

    public override string Name => TestName;

    protected override string Mode => "Deathmatch";

    protected override int MaxTicks => 6000;

    protected override (FVector Spot, FVector Face)? Target(SPlayerController pc) =>
        pc.Pawn is { } pawn ? (pawn.Location, pawn.Location + pawn.Rotation.Vector() * 100f) : null;

    protected override SmokeResult? Check(PlayRoot root, ScriptedPilot pilot)
    {
        PlaySession session = root.Session!;
        bool pilotDone = pilot.Phase == PilotPhase.Done;
        (string? error, int lives, int deaths, bool alive, string state) = session.ServerSnapshot(host =>
        {
            GameReplicationInfo? gri = host.World.Level.Info.GRI;
            if (gri is null || gri.GameMode != EGameMode.GM_DeathMatch || gri.TeamLivesATT != ExpectedSpyLives ||
                gri.TimesRespawn != ExpectedRespawn)
            {
                return ((string?)$"rules: mode={gri?.GameMode} spyLives={gri?.TeamLivesATT} respawn={gri?.TimesRespawn}", 0, 0, false, "");
            }

            foreach (SMission m in host.World.Level.AllActors<SMission>())
            {
                bool live = !m.ObjectiveTaken;
                if (m.GameMode != EGameMode.GM_Undefined && live != (m.GameMode == EGameMode.GM_DeathMatch))
                {
                    return ($"mission {m.GameMode} live={live}", 0, 0, false, "");
                }
            }

            SPlayerController? pc = session.ServerController;
            PlayerReplicationInfo? pri = pc?.PlayerReplicationInfo;
            if (_killTick < 0 && pilotDone && pc?.Pawn is { Health: > 0 } pawn && pri?.NumLives == ExpectedSpyLives)
            {
                _killedPawn = pawn;
                pc.PawnTakeDamage(1000, null, pawn.Location, FVector.Zero, SDamageType.SDTBase);
                _killTick = root.Stats.Ticks;
            }

            // Respawned: a new living pawn, back in a walking state.
            bool respawned = _killedPawn is not null && pc?.Pawn is { Health: > 0 } now && !ReferenceEquals(now, _killedPawn) &&
                (pc.IsInState("s_PlayerWalkingSt0H") || pc.IsInState("s_PlayerWalkingCr0H") || pc.IsInState("s_PlayerWalkingSt2H"));
            return (null, pri?.NumLives ?? 0, pri?.Deaths ?? 0, respawned, $"server state={pc?.GetStateName()}");
        });
        if (error is not null)
        {
            return root.Stats.Ticks > 600 ? SmokeResult.Fail(error) : null;
        }

        if (_killTick < 0 || deaths < 1 || !alive)
        {
            return null;
        }

        // The death timeline: 5 s body, 2.5 s blink, then the TimesRespawn spectating (spec/game/death-lives-respawn.md).
        if (root.Stats.Ticks - _killTick < (5 + ExpectedRespawn) * 60)
        {
            return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture, $"respawned only {root.Stats.Ticks - _killTick} ticks after the kill"));
        }

        if (lives != ExpectedSpyLives - 1 || deaths != 1)
        {
            return SmokeResult.Fail($"after one death: lives={lives} deaths={deaths}");
        }

        return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
            $"spy respawned with {lives} lives, {root.Stats.Ticks - _killTick} ticks after the kill; {state}"));
    }
}
