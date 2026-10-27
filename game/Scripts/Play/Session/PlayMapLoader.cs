using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.GameFlow;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics;
using OpenScct.Server;

namespace OpenScctGame.Play.Session;

/// <summary>
/// Loads a converted map into a server or client world the way a playing machine needs it: the map's actors through
/// <see cref="MapCatalog.CreateMapClasses"/>, the client map preparation, physics, and the gameplay layers the
/// headless server leaves out (movement with the converted pawn animations, the devices with the map's mesh
/// animations, combat and equipment, the camera). The server also spawns the game rules (SGameInfo) inside the
/// load with the match URL, as the engine's map load does. Registered on a <see cref="MapCatalog"/>, so the
/// <see cref="ServerHost"/> and the <see cref="ClientSession"/> load it through their normal path.
/// </summary>
public sealed class PlayMapLoader
{
    private readonly SvMap _map;
    private readonly string _gameOptions;
    private readonly IPawnAnimationSource _pawnAnimations;
    private readonly ContentMeshAnimations _meshAnimations;
    private readonly USPersistentData? _hostSettings;

    /// <param name="hostSettings">The host's settings object the rules read (SPersistentData), or null for an empty one.</param>
    public PlayMapLoader(SvMap map, string gameOptions, string? charsDir, USPersistentData? hostSettings = null)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _gameOptions = gameOptions ?? throw new ArgumentNullException(nameof(gameOptions));
        _hostSettings = hostSettings;
        _pawnAnimations = charsDir is not null && Directory.Exists(charsDir)
            ? PawnAnimationSource.FromContent(charsDir)
            : PawnAnimationSource.Default;
        _meshAnimations = new ContentMeshAnimations(map.Gameplay);
    }

    public SvMap Map => _map;

    public string GameOptions => _gameOptions;

    public ContentMeshAnimations MeshAnimations => _meshAnimations;

    /// <summary>The game rules of the server load.</summary>
    public SGameInfo? Game { get; private set; }

    /// <summary>The <c>shared/chars</c> folder of a map folder (<see cref="SharedContent.FindRoot"/>), or null.</summary>
    public static string? FindCharsDir(string mapFolder) =>
        SharedContent.FindRoot(mapFolder) is { } shared && Path.Combine(shared, "chars") is { } chars && Directory.Exists(chars)
            ? chars
            : null;

    /// <summary>Registers this map on <paramref name="catalog"/> under its id.</summary>
    public void Register(MapCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Register(_map.Id, Load);
    }

    /// <summary>The catalog builder: loads the map into <paramref name="world"/>'s level.</summary>
    public MapInfo Load(World world, bool isClient)
    {
        ArgumentNullException.ThrowIfNull(world);
        Level level = world.Level;
        InstallBeforeLoad(level);

        level.BeginLoad();
        MapLoadResult loaded = new MapLoader(MapCatalog.CreateMapClasses()).Load(world, _map);
        if (isClient)
        {
            NetMapBinding.PrepareClientMap(level, loaded.Actors);
        }
        else
        {
            string error = string.Empty;
            SGameInfo game = GameInfo.SpawnGameInfo<SGameInfo>(level, _gameOptions, ref error);
            game.LevelSettings = MapSettings(_map);
            Game = game;
        }

        level.EndLoad();
        loaded.AttachStartupBases();
        PhysicsWorld physics = PhysicsWorld.Attach(_map, loaded);
        DeviceNatives.Install(level, physics.Driver, _meshAnimations);
        CombatEquipment.Install(level);

        var starts = new List<MapStart>();
        foreach (Actor? actor in loaded.Actors)
        {
            if (actor is PlayerStart start)
            {
                starts.Add(new MapStart(start.Location, start.Rotation, start.TeamNumber));
            }
        }

        if (starts.Count == 0)
        {
            starts.Add(new MapStart(default, default, 0));
        }

        return new MapInfo(_map.Id, _map.Id + "/" + SvMapHash.ManifestFile, MapCatalog.ContentHashOf(_map.Manifest.SimHash),
            _map.Manifest.SimHash, _map.SimFiles, starts.ToArray(), loaded.Actors, loaded, physics);
    }

    /// <summary>
    /// The level services a playing machine runs with, before the begin-play passes: the movement layer (with the
    /// camera answering its camera calls), the movement part of the start state, and the mesh animations.
    /// Equipment and combat wrap these after the load (<see cref="CombatEquipment.Install"/>).
    /// </summary>
    private void InstallBeforeLoad(Level level)
    {
        level.SetService<IMeshAnimations>(_meshAnimations);
        if (_hostSettings is not null)
        {
            level.SetService(_hostSettings);
        }

        level.SetService(new MovementService(_pawnAnimations, new CameraMovementHooks()));
        level.SetService<IGameFlowPorts>(new MovementGameFlowPorts(level.GetService<IGameFlowPorts>()));

        AddLevelServices?.Invoke(level);
    }

    /// <summary>
    /// Services the Godot game adds to every level it loads, such as the player's saved gear
    /// (spec/additions/gear-change.md); null where this file is built without the game (tests, tools).
    /// </summary>
    public static Action<Level>? AddLevelServices { get; set; }

    /// <summary>The map's SMapSettings as the rules read it (name and per-mode entries).</summary>
    private static USMapSettings MapSettings(SvMap map) => MapSettingsOf(map.Id, map.Gameplay);

    /// <summary>A map's SMapSettings from its gameplay document (name and per-mode entries).</summary>
    public static USMapSettings MapSettingsOf(string mapId, GameplayDoc gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        var settings = new USMapSettings { MapName = mapId };
        foreach (KeyValuePair<string, PropBag> mode in gameplay.MapSettings)
        {
            PropBag p = mode.Value;
            settings.Modes.Add(new FGameModeInfo
            {
                TimeLimit = p.GetInt("TimeLimit"),
                ScoreLimit = p.GetInt("ScoreLimit"),
                SpiesLives = p.GetInt("SpiesLives"),
                RespawnTime = p.GetInt("RespawnTime"),
                SentriesLives = p.GetInt("SentriesLives"),
                Handicap = p.GetInt("Handicap"),
                NumDisksMax = p.GetInt("NumDisksMax"),
                GameModeId = (EGameMode)(byte)p.GetInt("GameModeId"),
            });
        }

        return settings;
    }
}
