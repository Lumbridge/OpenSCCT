using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>A headless match of server bots only.</summary>
public sealed record AiBotMatchSettings
{
    public string Map { get; init; } = "SteeE";

    /// <summary>The game options (Story by default: the map's 10 minutes, 4 spy and 3 merc lives, 10 s respawn).</summary>
    public string GameOptions { get; init; } = BotMatchSettings.StoryOptions;

    public IReadOnlyList<ServerBotSpec> Bots { get; init; } = ServerBotSettings.TwoVersusTwo(AiDifficulty.For(AiSkill.Normal), AiDifficulty.For(AiSkill.Normal));

    public ulong Seed { get; init; } = 1;

    public NetClassRegistry? Classes { get; init; }

    public MapCatalog? Maps { get; init; }

    public AiNavCache? Nav { get; init; }

    /// <summary>See <see cref="ServerBotSettings.TerminalsBlockPlayers"/>.</summary>
    public bool TerminalsBlockPlayers { get; init; } = true;
}

/// <summary>What one bot did over a match.</summary>
public sealed record AiBotReport(
    string Name,
    int Team,
    AiSkill Skill,
    int Kills,
    int Deaths,
    float Score,
    long ThinkTicks,
    int LinksDone,
    int LinksFailed,
    int Hacked,
    int UseRequests,
    int Bursts,
    int Frags,
    int Smokes,
    int Shocks,
    string Goals,
    string Status)
{
    public int SearchInspections { get; init; }

    public int SearchPlans { get; init; }
}

/// <summary>The outcome of a server bot match.</summary>
public sealed record AiBotMatchReport(
    string Map,
    bool Started,
    bool Ended,
    int WinningTeam,
    int WinningReason,
    float MatchSeconds,
    int ObjectivesTaken,
    int ObjectivesNeeded,
    int SpyKills,
    int MercKills,
    int Noises,
    IReadOnlyList<AiBotReport> Bots)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"{Map}: started {Started} ended {Ended} winner {WinningTeam} reason {(EEndReason)Math.Max(0, WinningReason)} after {MatchSeconds:F1} s, objectives {ObjectivesTaken}/{ObjectivesNeeded}, kills spies {SpyKills} mercs {MercKills}, noises {Noises}"));
        foreach (AiBotReport b in Bots)
        {
            sb.AppendLine();
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"  {b.Name} ({(b.Team == 0 ? "spy" : "merc")}, {b.Skill}): kills {b.Kills} deaths {b.Deaths} score {b.Score:F0}, links {b.LinksDone}/{b.LinksFailed} failed, hacked {b.Hacked} (uses {b.UseRequests}), bursts {b.Bursts}, frags {b.Frags}, smokes {b.Smokes}, shocks {b.Shocks}, searches {b.SearchPlans} inspected {b.SearchInspections}; goals {b.Goals}; {b.Status}"));
        }

        return sb.ToString();
    }
}

/// <summary>
/// A dedicated server with the game rules and only server bots (no connections), stepped on the caller's thread at
/// 60 Hz with a virtual clock: a bot-versus-bot match that reports the objectives taken, the kills, the round time and
/// what each bot did.
/// </summary>
public sealed class AiBotMatch : IDisposable
{
    private readonly LoopbackNetwork _network = new();
    private readonly ITransport _transport;
    private long _frame;
    private bool _disposed;

    public AiBotMatch(AiBotMatchSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = _network.Bind("server");
        Host = new ServerHost(_transport, new ServerSettings
        {
            Map = settings.Map,
            GameOptions = settings.GameOptions,
            PlayersToStart = Math.Max(1, settings.Bots.Count),
            MaxPlayers = Math.Max(4, settings.Bots.Count),
            MatchSeed = settings.Seed,
            NetMode = ENetMode.NM_DedicatedServer,
            Classes = settings.Classes,
            Maps = settings.Maps,
            SpawnWalkers = false,
        });
        Bots = new ServerBots(Host, new ServerBotSettings
        {
            Bots = settings.Bots,
            Nav = settings.Nav,
            Maps = settings.Maps,
            TerminalsBlockPlayers = settings.TerminalsBlockPlayers,
        });
    }

    public AiBotMatchSettings Settings { get; }

    public ServerHost Host { get; }

    public ServerBots Bots { get; }

    public long Frame => _frame;

    public bool Ended => Host.Game?.GameReplicationInfo is { WinningTeam: not 255 } && Host.Game is { bGameEnded: true };

    /// <summary>One server tick.</summary>
    public void Step()
    {
        _frame++;
        Host.Tick(VirtualClock.TickToMicroseconds(_frame, Sim.Hz));
    }

    /// <summary>Steps until the round ends (plus <paramref name="afterEndSeconds"/>) or <paramref name="maxSeconds"/> pass.</summary>
    public AiBotMatchReport Run(float maxSeconds, float afterEndSeconds = 1f, Action<AiBotMatch>? everySecond = null)
    {
        long maxFrames = (long)(maxSeconds * Sim.Hz);
        long endedAt = -1;
        while (_frame < maxFrames)
        {
            Step();
            if (everySecond is not null && _frame % Sim.Hz == 0)
            {
                everySecond(this);
            }

            if (Ended && endedAt < 0)
            {
                endedAt = _frame;
            }

            if (endedAt >= 0 && _frame - endedAt > afterEndSeconds * Sim.Hz)
            {
                break;
            }
        }

        return Report();
    }

    public AiBotMatchReport Report()
    {
        int taken = 0;
        int needed = 0;
        var seen = new HashSet<SObjective>(ReferenceEqualityComparer.Instance);
        EGameMode mode = Host.Game?.MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
        foreach (SMission m in Host.World.Level.AllActors<SMission>())
        {
            if (m.GameMode != mode)
            {
                continue;
            }

            needed = m.MinimumObjectives;
            foreach (SObjective? o in m.Objectives)
            {
                if (o is { ObjectiveTaken: true } && seen.Add(o))
                {
                    taken++;
                }
            }
        }

        int spyDeaths = 0;
        int mercDeaths = 0;
        var bots = new List<AiBotReport>();
        foreach (AiController bot in Bots.Bots)
        {
            PlayerReplicationInfo? pri = bot.Controller.PlayerReplicationInfo;
            int team = pri?.Team is TeamInfo t ? t.TeamIndex : -1;
            int deaths = pri?.Deaths ?? 0;
            if (team == 0)
            {
                spyDeaths += deaths;
            }
            else if (team == 1)
            {
                mercDeaths += deaths;
            }

            AiBrain? brain = bot.Brain;
            var spy = brain as SpyAi;
            var merc = brain as MercAi;
            bots.Add(new AiBotReport(pri?.PlayerName ?? "?", team, bot.Difficulty.Skill, pri?.Kills ?? 0, deaths, pri?.Score ?? 0f,
                bot.ThinkTicks, brain?.Follower.LinksDone ?? 0, brain?.Follower.LinksFailed ?? 0, spy?.Hacked ?? 0, spy?.Requests ?? 0,
                merc?.Bursts ?? 0, merc?.FragsThrown ?? 0, spy?.SmokesThrown ?? 0, spy?.ShocksFired ?? 0, Goals(brain), bot.Status)
            {
                SearchInspections = merc?.Search.Inspections ?? 0,
                SearchPlans = merc?.Search.Plans ?? 0,
            });
        }

        GameReplicationInfo? gri = Host.Game?.GameReplicationInfo;
        float seconds = Host.StartGate is { StartedAt: >= 0f } gate ? Host.World.TimeSeconds - gate.StartedAt : 0f;
        return new AiBotMatchReport(Settings.Map, Host.StartGate?.Started ?? false, Ended, gri?.WinningTeam ?? -1, gri?.WinningReason ?? -1,
            seconds, taken, needed, mercDeaths, spyDeaths, Bots.World?.Noise.MakeNoiseCount ?? 0, bots);
    }

    private static string Goals(AiBrain? brain)
    {
        if (brain is null)
        {
            return "-";
        }

        var sb = new StringBuilder();
        for (int g = 0; g < brain.GoalCounts.Length; g++)
        {
            if (brain.GoalCounts[g] == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(brain is SpyAi ? SpyAi.GoalName(g) : MercAi.GoalName(g)).Append('=').Append(brain.GoalCounts[g].ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Bots.Dispose();
        Host.Dispose();
        _transport.Dispose();
    }
}
