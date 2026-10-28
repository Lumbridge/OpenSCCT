using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>Something drawn over the HUD in its font and atlas (the chat line).</summary>
public interface IHudOverlay
{
    void Draw(MagmaDrawList into, Vector2 screen, WhiteRegion white);
}

/// <summary>
/// The in-game HUD: a CanvasLayer that draws the Magma pages of the local player each frame. The play client adds it
/// with <see cref="Attach"/>, gives it a projector for the spy's objective markers and connects it to the client
/// session so the server's messages and chat reach it:
/// <code>
/// HudLayer? hud = HudLayer.Attach(this, () => localController, out string? error);
/// if (hud is not null) { hud.Projector = WorldToScreen; hud.Connect(session.Client); }
/// </code>
/// The spy page (P_HudATT), the merc page (P_HudDEF) or the spectator page (P_SpectatorMode) is chosen each frame (a
/// registered <see cref="IHudPageOverride"/> can replace it); the spy's interaction list (P_InterATT) and canvas
/// markers draw over it, then the console lines and the screen-effect washes.
/// </summary>
public partial class HudLayer : CanvasLayer
{
    public const string NodeName = "MagmaHud";
    public const int DefaultLayer = 10;

    private readonly List<MagmaDrawItem> _items = new();
    private readonly List<IHudPageOverride> _overrides = new();
    private readonly List<IHudAreaDriver> _drivers = new();
    private readonly List<IHudOverlay> _overlays = new();
    private readonly MagmaDrawList _overlayList = new();
    private readonly MagmaDrawList _canvasList = new();
    private readonly MagmaDrawList _sfxList = new();
    private readonly HudSnapshot _noPlayer = new();
    private MagmaCanvas? _canvas;
    private Func<SPlayerController?>? _controller;
    private WhiteRegion? _white;
    private SPlayerController? _lastController;
    private SHud? _lastHud;
    private bool _mercAimRan;

    public HudLayer()
    {
        Layer = DefaultLayer;
        Receiver = new HudReceiver(this);
    }

    public HudAssets? Assets { get; private set; }

    public HudOptions Options { get; } = new();

    public IHudSource? Source { get; set; }

    public HudSnapshot Snapshot { get; } = new();

    public SpyHudPage? Spy { get; private set; }

    public MercHudPage? Merc { get; private set; }

    public SpectatorHudPage? Spectator { get; private set; }

    /// <summary>The end banner (P_EndGame) opened when the local controller entered GameEnded, else null.</summary>
    public EndGamePage? EndGame { get; private set; }

    public InteractionPage? Interaction { get; private set; }

    /// <summary>The binocular pages that replace the player pages in the binocular views.</summary>
    public HudBinocularPages? Binoculars { get; private set; }

    /// <summary>The gadget views: sticky cam and camera network pages, gadget menus, gas mask, heart sensor.</summary>
    public OpenScctGame.Hud.Gadgets.GadgetHudViews? GadgetViews { get; private set; }

    public HudMarkers Markers { get; } = new();

    /// <summary>The merc's motion tracking brackets (HudMotionTracking.cs), drawn after the markers.</summary>
    public HudMotionTracking Motion { get; } = new();

    /// <summary>The HUD's own effect state (screen-effect slots, damage feedback, foam, flash, shutdown).</summary>
    public HudEffects Effects { get; } = new();

    /// <summary>The console lines (chat and TeamMessage texts).</summary>
    public HudConsole Console { get; } = new();

    /// <summary>The server calls and chat lines for the HUD (<see cref="Connect"/>).</summary>
    public HudReceiver Receiver { get; }

    /// <summary>
    /// The merc's scope aim point at the drawn moment between ticks (<see cref="HudBinocularAim"/>), or null to project
    /// the last tick's aim point.
    /// </summary>
    public Func<FVector?>? DrawnAimLocation { get; set; }

    /// <summary>
    /// The level time at the drawn moment between ticks (the play scene's <c>DrawnTime</c>), or null to use the last
    /// tick's: the screen effects' fades and the pages' time move every frame, as the original's per-frame HUD.
    /// </summary>
    public Func<float>? DrawnTime { get; set; }

    /// <summary>The time the screen effects were last updated to (the frame-rate smoke reads it).</summary>
    public float EffectsTime { get; private set; }

    /// <summary>World point (uu) to screen pixels for the objective markers; null when behind the camera.</summary>
    public Func<FVector, Vector2?>? Projector
    {
        get => Markers.Projector;
        set => Markers.Projector = value;
    }

    /// <summary>The local controller the HUD shows (also what the effects and the message driver follow).</summary>
    public Func<SPlayerController?>? Controller
    {
        get => _controller;
        set => _controller = value;
    }

    /// <summary>The page drawn last frame, or null when there was no local player.</summary>
    public HudPage? ActivePage { get; private set; }

    /// <summary>Draws for this screen size instead of the viewport's (headless checks).</summary>
    public Vector2? ScreenSize { get; set; }

    /// <summary>The screen size the last frame was laid out for (pixels).</summary>
    public Vector2 DrawnScreen { get; private set; }

    /// <summary>Every item drawn last frame: the page, the interaction list, the markers, the console, the washes.</summary>
    public IReadOnlyList<MagmaDrawItem> Items => _items;

    /// <summary>The screen-effect washes drawn last frame.</summary>
    public MagmaDrawList SfxDrawList => _sfxList;

    /// <summary>Raised when the connected players list is toggled (Reloaded remembers its state).</summary>
    public event Action<bool>? ConnectedPlayersToggled;

    /// <summary>
    /// Adds a HUD under <paramref name="parent"/> reading the controller <paramref name="controller"/> returns.
    /// Returns null with the reason when the UI content is missing (the game runs without a HUD then).
    /// </summary>
    public static HudLayer? Attach(Node parent, Func<SPlayerController?> controller, out string? error, string? contentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(controller);
        HudLayer? layer = Create(out error, contentRoot);
        if (layer is null)
        {
            return null;
        }

        HudAssets assets = layer.Assets!;
        layer.Controller = controller;
        layer.Source = new HudSimReader(controller, (section, key, fallback) => assets.Localize("MagmaDynamic", section, key, fallback), layer.Effects);
        parent.AddChild(layer);
        return layer;
    }

    /// <summary>A HUD with no source yet, or null with the reason when the UI content is missing.</summary>
    public static HudLayer? Create(out string? error, string? contentRoot = null)
    {
        HudAssets? assets = HudAssets.TryLoad(contentRoot, out error);
        if (assets is null)
        {
            return null;
        }

        var layer = new HudLayer { Name = NodeName };
        layer.Init(assets);
        return layer;
    }

    private void Init(HudAssets assets)
    {
        Assets = assets;
        Spy = new SpyHudPage(assets, Options);
        Merc = new MercHudPage(assets, Options);
        Spectator = assets.Hud.Pages.ContainsKey(SpectatorHudPage.PageName) ? new SpectatorHudPage(assets, Options) : null;
        Interaction = new InteractionPage(assets);
        Binoculars = new HudBinocularPages(assets, Options);
        _overrides.Insert(0, Binoculars);
        GadgetViews = OpenScctGame.Hud.Gadgets.GadgetHudViews.Attach(this);
        _white = WhiteRegion.Find(assets);
        if (assets.TextureSize(HudMarkers.Package, HudMarkers.Atlas) is { } size)
        {
            Markers.AtlasSize = new Vector2(size.X, size.Y);
        }

        _canvas = new MagmaCanvas { Name = "Canvas", Assets = assets };
        AddChild(_canvas);
    }

    /// <summary>Connects the HUD to the client session: the server's HUD calls and the chat lines.</summary>
    public void Connect(ClientSession client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Receiver.Connect(client);
    }

    /// <summary>Adds a driver for areas another module owns (gadget overlays) to the player pages.</summary>
    public void AddDriver(IHudAreaDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _drivers.Add(driver);
        foreach (HudPage? page in new HudPage?[] { Spy, Merc })
        {
            page?.Drivers.Add(driver);
        }
    }

    /// <summary>Adds something drawn over the HUD (the chat line).</summary>
    public void AddOverlay(IHudOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlays.Add(overlay);
    }

    /// <summary>Adds a page that replaces the player page while it is active (binoculars, sticky cam, camera network).</summary>
    public void AddPageOverride(IHudPageOverride page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _overrides.Add(page);
    }

    /// <summary>
    /// <c>SPlayerController.ShowConnectedPlayers</c> (exec, client side): flips the local SHud's
    /// bShowConnectedPlayers.
    /// </summary>
    [OpenScct.Provenance.Uc("SBase.SPlayerController.ShowConnectedPlayers", Sha1 = "4e08738fb29658bcbf96fa284cad1e0800abb54c")]
    [OpenScct.Provenance.PortOf("uc:SBase.SPlayerController:ShowConnectedPlayers", Spec = "spec/hud/scoreboard.md#rules")]
    public bool ShowConnectedPlayers()
    {
        if (_controller?.Invoke()?.myHUD is not SHud hud)
        {
            return false;
        }

        hud.bShowConnectedPlayers = !hud.bShowConnectedPlayers;
        ConnectedPlayersToggled?.Invoke(hud.bShowConnectedPlayers);
        return hud.bShowConnectedPlayers;
    }

    /// <summary>Reloaded's frame-rate readout in the top right corner.</summary>
    public bool ShowFps { get; set; }

    /// <summary>The frame-rate readout.</summary>
    public HudFps Fps { get; } = new();

    /// <summary>Reloaded's network readout under the frame-rate readout (rl:net.stats).</summary>
    public bool ShowNet { get; set; }

    /// <summary>The network readout; its <see cref="HudNet.Source"/> is set by the play scene.</summary>
    public HudNet Net { get; } = new();

    /// <summary>Sets the connected players list of a new HUD (Reloaded keeps it on and remembers it).</summary>
    public bool InitialConnectedPlayers { get; set; }

    public override void _Process(double delta) => Step((float)delta);

    /// <summary>One HUD frame: effects and message driver, snapshot, pages, then the draw.</summary>
    public void Step(float dt)
    {
        if (Spy is null || Merc is null || Interaction is null)
        {
            return;
        }

        SPlayerController? pc = _controller?.Invoke() is { bDeleteMe: false } c ? c : null;
        if (!ReferenceEquals(pc, _lastController))
        {
            _lastController = pc;
            Effects.Reset();
            Console.Clear();
            Spectator?.Reset();
            EndGame = null;
        }

        if (pc?.myHUD is SHud fresh && !ReferenceEquals(fresh, _lastHud))
        {
            _lastHud = fresh;
            HudBegin(fresh);
        }

        if (pc is not null)
        {
            HudEventTap.Ensure(pc.XLevel.World, Effects);
            EffectsTime = DrawnTime?.Invoke() ?? pc.Level.TimeSeconds;
            Effects.Update(pc, EffectsTime);
            if (pc.myHUD is SHud shud)
            {
                HudMessages.Tick(shud, shud.sTutoMap.Length > 4);
            }

            // The merc's aim point (SHud.SpotReticule) before the snapshot reads it (spec/hud/merc-reticle.md rule 3).
            _mercAimRan = HudMercAim.Update(pc, ScreenSize ?? GetViewport()?.GetVisibleRect().Size ?? new Vector2(MagmaRenderer.PageWidth, MagmaRenderer.PageHeight));
        }
        else
        {
            _mercAimRan = false;
        }

        Snapshot.Clear();
        Source?.Fill(Snapshot);
        Snapshot.EnhancedReality |= Options.EnhancedReality;
        foreach (IHudAreaDriver d in _drivers)
        {
            d.Read(pc, Snapshot);
        }

        // SpawnMagmaHud opens the side's page once the player is ready (PlayerAreReady): its PRI has a team, or it only
        // spectates. Before that the client shows no HUD (the first frames of an online round showed the spy page with
        // 0 lives over the view before the pawn was possessed).
        if (pc is not null && !MagmaHudOpen(pc))
        {
            Snapshot.Valid = false;
        }

        if (pc is not null && DrawnTime is { } drawnTime)
        {
            Snapshot.TimeSeconds = drawnTime();
        }

        Vector2 screen = ScreenSize ?? GetViewport()?.GetVisibleRect().Size ?? new Vector2(MagmaRenderer.PageWidth, MagmaRenderer.PageHeight);
        DrawnScreen = screen;
        if (Snapshot.Binoculars.Mode == HudBinocularMode.Merc &&
            HudBinocularPages.AimPoint(DrawnAimLocation?.Invoke() ?? Snapshot.Binoculars.AimLocation, Projector, screen) is { } spot)
        {
            Snapshot.SpotReticule = spot;
        }

        if (Binoculars?.Spy is { } binoculars)
        {
            binoculars.Projector = Projector;
        }

        HudPage page = ChoosePage(pc);
        foreach (HudPage? other in new HudPage?[] { Spy, Merc, Spectator })
        {
            if (other is not null && !ReferenceEquals(other, page))
            {
                other.Root.SetVisible(false, children: false);
            }
        }

        page.Update(Snapshot, dt);
        bool playerPage = ReferenceEquals(page, Spy) || ReferenceEquals(page, Merc);
        if (playerPage)
        {
            Interaction.Update(Snapshot, dt);
        }
        else
        {
            Interaction.Update(_noPlayer, dt);
        }

        Markers.Update(Snapshot, dt);
        ActivePage = Snapshot.Valid ? page : null;

        page.Draw(screen, Snapshot);
        Interaction.Draw(screen);
        if (playerPage)
        {
            Markers.Draw(Snapshot, screen);
        }
        else
        {
            Markers.DrawList.Clear();
        }

        // The laser's target name: the merc HUD's aim point helper past its gates, or the camera network's HUD state.
        bool mercPage = playerPage && Snapshot.Side == HudSide.Merc;
        bool camNetwork = IsCamNetworkPage(page);
        bool targetName = camNetwork || (mercPage && _mercAimRan && pc?.myHUD is SHud aimHud && aimHud.TargetNameInAimHelper());
        Motion.Update(mercPage || camNetwork ? pc : null, screen, Projector, Options.StretchHud, Assets, targetName);
        _canvasList.Clear();
        if (Snapshot.RespawnWeapon is { } weapon && !Snapshot.HideHud && Assets?.TextureSize(HudRespawnWeaponDrawer.Package, HudRespawnWeaponDrawer.Atlas) is { } atlas)
        {
            // BeginRespawnWeaponPick names the key bound to Use: here the bUsing button's key.
            HudRespawnWeapon shown = Options.KeyName?.Invoke("bUsing") is { Length: > 0 } key ? weapon with { Key = key } : weapon;
            HudRespawnWeaponDrawer.Draw(_canvasList, screen, shown, new Vector2(atlas.X, atlas.Y));
        }

        Console.Draw(screen, Snapshot.TimeSeconds, !Snapshot.Valid);
        _sfxList.Clear();
        if (_white is { } white && Snapshot.Valid)
        {
            Effects.Sfx.Draw(_sfxList, screen, white, Snapshot.SleepingText, Snapshot.SleepSeconds);
        }

        // GameEnded.BeginState opens P_EndGame on top of the HUD at once (spec/game/end-of-round.md rule 17).
        if (pc is { bGameEnded: true } && Assets is { } endAssets && endAssets.Hud.Pages.ContainsKey(EndGamePage.PageName))
        {
            EndGame ??= new EndGamePage(endAssets);
            EndGame.Show(EndGamePage.AreaFor(pc, pc.myHUD is SHud endHud && endHud.sTutoMap.Length > 4));
            EndGame.Update(dt, screen);
        }
        else if (pc is not { bGameEnded: true })
        {
            EndGame = null;
        }

        _overlayList.Clear();
        if (_white is { } overlayWhite)
        {
            foreach (IHudOverlay o in _overlays)
            {
                o.Draw(_overlayList, screen, overlayWhite);
            }
        }

        _items.Clear();
        if (Snapshot.Valid)
        {
            _items.AddRange(page.DrawList.Items);
            _items.AddRange(Interaction.DrawList.Items);
            _items.AddRange(Markers.DrawList.Items);
            _items.AddRange(Motion.DrawList.Items);
            _items.AddRange(_canvasList.Items);
            _items.AddRange(Console.DrawList.Items);
            _items.AddRange(_sfxList.Items);
        }

        if (EndGame is not null)
        {
            _items.AddRange(EndGame.DrawList.Items);
        }

        _items.AddRange(_overlayList.Items);
        Fps.Add(dt);
        Fps.Draw(screen, ShowFps);
        _items.AddRange(Fps.DrawList.Items);
        Net.Draw(screen, dt, ShowNet, ShowFps);
        _items.AddRange(Net.DrawList.Items);

        _canvas?.Submit(_items);
    }

    /// <summary>
    /// The HUD side of <c>SHud.PostNetBeginPlay</c> for a new SHud: Enhanced Reality on (level 1), metric distances
    /// (HudUnit), and the connected players list as the settings keep it (the original starts it off; Reloaded
    /// remembers it).
    /// </summary>
    [OpenScct.Provenance.Uc("SBase.SHud.PostNetBeginPlay", Sha1 = "8a024f36ed1c77b91eca0c49848710adb3540a12")]
    [OpenScct.Provenance.Uc("SBase.SHud.HudUnit", Sha1 = "76f5dfcd8fea6f48c121b9e20b3158b70fd629d4")]
    [OpenScct.Provenance.PortOf("uc:SBase.SHud:HudUnit", Spec = "spec/hud/counters-and-prompts.md#rules")]
    private void HudBegin(SHud hud)
    {
        hud.EnhancedRealityLevel = 1;
        if (hud.StrDistanceUnit.Length == 0)
        {
            hud.StrDistanceUnit = HudK.DefaultDistanceUnit;
            hud.fDistanceUnit = HudK.DefaultDistanceScale;
        }

        hud.bShowConnectedPlayers = InitialConnectedPlayers;
    }

    /// <summary>
    /// SPlayerController.SpawnMagmaHud's condition on the player's machine: called from PlayerAreReady, it opens the
    /// spectator page for a pure spectator, else the spy or merc page by PRI.Team.TeamIndex, so a PRI without a team
    /// yet has no page.
    /// </summary>
    [OpenScct.Provenance.Uc("SBase.SPlayerController.SpawnMagmaHud", Sha1 = "c9df8bea76411484296edf8ca5f1ca80cc48b894")]
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Decoded, "SPlayerController.PlayerAreReady calls SpawnMagmaHud; it reads PRI.bOnlySpectator and PRI.Team.TeamIndex")]
    public static bool MagmaHudOpen(SPlayerController pc) =>
        pc?.PlayerReplicationInfo is { } pri && (pri.bOnlySpectator || pri.Team is not null);

    /// <summary>The camera network's page (P_CamNetWork), whose HUD state draws the motion tracking too.</summary>
    private bool IsCamNetworkPage(HudPage page) => ReferenceEquals(page, GadgetViews?.CamNetwork);

    /// <summary>The page manager: an active override, else the spectator page while spectating, else the side's page.</summary>
    private HudPage ChoosePage(SPlayerController? pc)
    {
        foreach (IHudPageOverride o in _overrides)
        {
            if (o.ActivePage(pc, Snapshot) is { } p)
            {
                return p;
            }
        }

        if (Snapshot.Spectator.Active && Spectator is not null)
        {
            return Spectator;
        }

        return Snapshot.Side == HudSide.Merc ? Merc! : Spy!;
    }
}
