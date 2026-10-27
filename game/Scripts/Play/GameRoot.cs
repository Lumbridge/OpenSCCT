using System;
using System.Globalization;
using System.IO;
using Godot;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Server;
using OpenScctGame.Maps;
using OpenScctGame.Match;

namespace OpenScctGame.Play;

/// <summary>Who drives the local player.</summary>
public enum PilotMode
{
    Keyboard,

    /// <summary>A fixed pattern from <see cref="BotPilot"/>, for smoke runs.</summary>
    Scripted,
}

/// <summary>What <see cref="GameRoot"/> loads.</summary>
public sealed record GameConfig
{
    public string Map { get; init; } = MapCatalog.TestBox;

    public int Bots { get; init; } = LaunchOptions.DefaultBots;

    public PilotMode Pilot { get; init; } = PilotMode.Keyboard;
}

/// <summary>
/// The game scene: loads a map through OpenScct.Content, builds its view, hosts a listen server match with the local
/// player and bots, and draws the walkers the local client has replicated.
/// </summary>
public partial class GameRoot : Node3D
{
    public const string ScenePath = "res://Scenes/Game.tscn";

    private KeyboardPilot? _keyboard;
    private Camera3D? _camera;
    private Label? _status;
    private Aabb _mapBounds;

    public GameConfig Config { get; private set; } = new();

    public SvMap? Map { get; private set; }

    /// <summary>
    /// An offline world holding the map's actors as OpenScct.Content loads them. The view is built from it until the
    /// net worlds load maps through OpenScct.Content themselves.
    /// </summary>
    public World? MapWorld { get; private set; }

    public MapLoadResult? MapLoad { get; private set; }

    public MapView? View { get; private set; }

    public LocalMatch? Match { get; private set; }

    public WalkerViews? Walkers { get; private set; }

    /// <summary>True once the map and the match are up.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Why loading failed, or null.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Replaces the current scene with a game scene for <paramref name="config"/>.</summary>
    public static GameRoot Enter(SceneTree tree, GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(config);
        var game = GD.Load<PackedScene>(ScenePath).Instantiate<GameRoot>();
        game.Config = config;
        Node? previous = tree.CurrentScene;
        tree.Root.AddChild(game);
        tree.CurrentScene = game;
        previous?.QueueFree();
        return game;
    }

    public override void _Ready()
    {
        _status = new Label { Name = "Status", Position = new Vector2(16f, 12f) };
        var hud = new CanvasLayer { Name = "Hud" };
        hud.AddChild(_status);
        AddChild(hud);
        try
        {
            Load();
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is SvMapFormatException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            LoadError = ex.Message;
            GD.PushError($"Could not load map '{Config.Map}': {ex.Message}");
            _status.Text = $"Could not load map '{Config.Map}': {ex.Message}";
        }
    }

    private void Load()
    {
        MapLocator locator = MapLocator.CreateDefault(App.From(this).Options);
        string folder = locator.Find(Config.Map)
            ?? throw new FileNotFoundException($"map '{Config.Map}' not found in: {string.Join("; ", locator.Roots)}");
        Map = SvMap.Load(folder);
        MapWorld = new World(0);
        MapLoad = new MapLoader().Load(MapWorld, Map);
        View = MapBuilder.Build(Map, MapLoad);
        AddChild(View.Root);
        foreach (string warning in View.Warnings)
        {
            GD.PushWarning($"{Map.Id}: {warning}");
        }

        _mapBounds = View.World?.GetAabb() ?? new Aabb(Vector3.Zero, Vector3.One);
        AddChild(DefaultLighting());
        Walkers = new WalkerViews { Name = "Walkers" };
        AddChild(Walkers);
        _camera = new Camera3D { Name = "Camera", Fov = 70f };
        AddChild(_camera);
        _camera.MakeCurrent();
        PlaceOverviewCamera();

        Match = new LocalMatch(new LocalMatchSettings { Map = Map.Id, Bots = Config.Bots });
        if (Config.Pilot == PilotMode.Scripted)
        {
            Match.LocalPilot = tick => BotPilot.For(0, tick);
        }
        else
        {
            _keyboard = new KeyboardPilot();
            Match.LocalPilot = _ => _keyboard.Read();
        }

        Walkers.Session = Match.LocalPlayer;
    }

    public override void _UnhandledInput(InputEvent @event) => _keyboard?.HandleInput(@event);

    public override void _Process(double delta)
    {
        if (Match is null || Walkers is null)
        {
            return;
        }

        Match.Update();
        Walkers.Sync();
        UpdateCamera();
        if (_status is not null)
        {
            ClientSession local = Match.LocalPlayer;
            _status.Text = string.Create(CultureInfo.InvariantCulture,
                $"{Map?.Id} - {local.State} - walkers {Walkers.Count} - tick {local.LocalTick}");
        }
    }

    public override void _ExitTree()
    {
        OpenScctGame.Controls.Pointer.Release();
        Match?.Dispose();
        Match = null;
    }

    private void UpdateCamera()
    {
        if (_camera is null || Walkers is null || Match?.LocalPlayer is not { LocalWalker: { } walker, LocalChannel: { } channel })
        {
            return;
        }

        MeshInstance3D? view = Walkers.ViewOf(channel.NetGuid);
        if (view is null)
        {
            return;
        }

        // Behind and above the walker, looking along its facing.
        Vector3 target = view.Position + new Vector3(0f, 0.6f, 0f);
        Vector3 forward = UeSpace.DirectionToGodot(new FRotator(0, walker.Rotation.Yaw, 0).Vector());
        _camera.Position = target - forward * 3.2f + new Vector3(0f, 1.4f, 0f);
        _camera.LookAt(target, Vector3.Up);
    }

    private void PlaceOverviewCamera()
    {
        if (_camera is null)
        {
            return;
        }

        Vector3 centre = _mapBounds.GetCenter();
        float reach = Math.Max(_mapBounds.Size.X, _mapBounds.Size.Z);
        _camera.Position = centre + new Vector3(0f, reach * 0.6f, reach * 0.6f);
        _camera.LookAt(centre, Vector3.Up);
    }

    /// <summary>A plain sun and ambient light until map lighting is converted.</summary>
    private static Node3D DefaultLighting()
    {
        var lighting = new Node3D { Name = "Lighting" };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.08f, 0.09f, 0.11f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.57f, 0.6f),
            AmbientLightEnergy = 0.7f,
        };
        lighting.AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });
        var sun = new DirectionalLight3D { Name = "Sun", LightEnergy = 0.9f };
        sun.RotationDegrees = new Vector3(-55f, 35f, 0f);
        lighting.AddChild(sun);
        return lighting;
    }
}
