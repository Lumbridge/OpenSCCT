using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;

namespace OpenScct.Game.Tests.Modes;

/// <summary>SteeE's own mode settings (SteeE-i.utc, spec/game/match-options.md data table).</summary>
internal static class SteeE
{
    public const string Map = "SteeE";

    public static readonly FGameModeInfo Story = new()
    {
        GameModeId = EGameMode.GM_Multi, TimeLimit = 10, ScoreLimit = 0, SpiesLives = 4, RespawnTime = 10, SentriesLives = 3,
    };

    public static readonly FGameModeInfo DiskHunt = new()
    {
        GameModeId = EGameMode.GM_DiskHunt, TimeLimit = 10, ScoreLimit = 10, SpiesLives = 4, RespawnTime = 10, SentriesLives = 3,
        Handicap = 20, NumDisksMax = 30,
    };

    public static readonly FGameModeInfo Deathmatch = new()
    {
        GameModeId = EGameMode.GM_DeathMatch, TimeLimit = 10, ScoreLimit = 0, SpiesLives = 8, RespawnTime = 5, SentriesLives = 4,
    };

    /// <summary>SteeE's SMapSettings: Story, Disk Hunt and Deathmatch entries in package order.</summary>
    public static USMapSettings Settings()
    {
        var settings = new USMapSettings { MapName = "ENHANCED STEEL SQUAT" };
        settings.Modes.Add(Story);
        settings.Modes.Add(DiskHunt);
        settings.Modes.Add(Deathmatch);
        return settings;
    }

    /// <summary>The untouched match creation page for <paramref name="mode"/> on SteeE.</summary>
    public static MatchSetup Setup(EGameMode mode, bool revenge = false, int gamesInARow = 1) =>
        MatchSetup.Defaults(Settings(), mode, Map, revenge, gamesInARow);
}

/// <summary>
/// A synthetic level laid out like SteeE's missions: the Story mission (one terminal-less objective per entry, kept
/// open), the Disk Hunt mission with its disks, the Deathmatch mission without objectives, spy and merc starts; the
/// host settings installed from the untouched menu for the mode before the map loads.
/// </summary>
internal sealed class ModeWorld
{
    public ModeWorld(EGameMode mode, int disks = 30, Func<int, byte>? group = null, ENetMode netMode = ENetMode.NM_ListenServer,
        MatchSetup? setup = null, string extraOptions = "")
    {
        Setup = setup ?? SteeE.Setup(mode);
        W = new GameWorld(netMode);
        W.Level.SetService(Setup.ToPersistentData());
        W.Load(Setup.GameOptions(2) + extraOptions, x =>
        {
            MatchLifecycleTests.TwoSides(x);
            StoryObjective = x.Level.SpawnActor(new SObjective(), new FVector(0f, 3000f, 0f), default)!;
            Story = x.Level.SpawnActor(new SMission { GameMode = EGameMode.GM_Multi, bChained = false, MinimumObjectives = 1 },
                default, default)!;
            Story.Objectives.Add(StoryObjective);

            DiskHunt = x.Level.SpawnActor(new SMission { GameMode = EGameMode.GM_DiskHunt, bChained = false, MinimumObjectives = 12 },
                default, default)!;
            for (int i = 0; i < disks; i++)
            {
                var disk = new SDisk { Group = group?.Invoke(i) ?? 0 };
                Disks.Add(x.Level.SpawnActor(disk, new FVector(1000f + 100f * i, 2000f, 0f), default)!);
            }

            foreach (SDisk d in Disks)
            {
                DiskHunt.Triggers.Add(d);
            }

            Deathmatch = x.Level.SpawnActor(new SMission { GameMode = EGameMode.GM_DeathMatch, bChained = false }, default, default)!;
        });
    }

    public MatchSetup Setup { get; }

    public GameWorld W { get; }

    public SMission Story { get; private set; } = null!;

    public SObjective StoryObjective { get; private set; } = null!;

    public SMission DiskHunt { get; private set; } = null!;

    public SMission Deathmatch { get; private set; } = null!;

    public List<SDisk> Disks { get; } = new();

    public SGameInfo Game => W.Game;

    public GameReplicationInfo Gri => W.Gri;

    /// <summary>Players join, the host starts the network match, and the round counts as begun.</summary>
    public SPlayerController[] Start(params string[] joins)
    {
        var players = new SPlayerController[joins.Length];
        for (int i = 0; i < joins.Length; i++)
        {
            players[i] = W.Join(joins[i]);
        }

        W.StartNetworkMatch(players[0]);
        Gri.bMatchHasBegun = true;
        return players;
    }

    /// <summary>The disks in play.</summary>
    public List<SDisk> Active() => Disks.FindAll(d => d.bActive);
}
