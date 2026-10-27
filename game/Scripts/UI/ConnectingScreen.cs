using System;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// Joining a server and changing maps. Joining: what the visit is trying (direct, NAT punch, relay) and Cancel. Going
/// to a round: the original's loading page (GUI.loading.InitBackground): "Loading...", the map's loading picture for
/// the player's side (SMapSettings' loading textures, <see cref="MapImages.Loading"/>), the mode's picture and name
/// (common L_Story, L_diskhunt, L_Death), and the map's explanation of the mode for the player's side
/// (SMapSettings.ModesStrings ExplicationModeSpy / ExplicationModeSentry). The online service moves on to the lobby,
/// the round or the main menu by itself.
/// </summary>
public partial class ConnectingScreen : MenuPage
{
    private Label? _heading;
    private Label? _mode;
    private Label? _explain;
    private TextureRect? _modeImage;
    private MapThumb? _mapPicture;
    private string? _pictureKey;
    private Label? _log;

    public OnlineService? Service { get; set; }

    public Button? CancelButton { get; private set; }

    public override void _Ready()
    {
        Name = "Connecting";
        BuildFrame("Loading...", 760f);
        _heading = new Label { Name = "Heading", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus };
        Body.AddChild(_heading);
        _mapPicture = new MapThumb(new Vector2(360f, 360f)) { Name = "MapPicture", SizeFlagsHorizontal = SizeFlags.ShrinkCenter, Visible = false };
        Body.AddChild(_mapPicture);
        var modeRow = new HBoxContainer { Name = "ModeRow", Alignment = BoxContainer.AlignmentMode.Center };
        _modeImage = new TextureRect
        {
            Name = "ModeImage", CustomMinimumSize = new Vector2(96f, 96f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _mode = new Label { Name = "Mode", Modulate = UiStyle.MenuTitle, VerticalAlignment = VerticalAlignment.Center };
        modeRow.AddChild(_modeImage);
        modeRow.AddChild(_mode);
        Body.AddChild(modeRow);
        _explain = new Label { Name = "Explanation", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(740f, 0f) };
        Body.AddChild(_explain);
        _log = new Label { Name = "Log", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(740f, 120f) };
        Body.AddChild(_log);
        CancelButton = PageButton("Cancel", () => Service?.Leave(), "Cancel");
        CancelButton.GrabFocus();
        Refresh();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        Refresh();
    }

    private void Refresh()
    {
        if (_heading is null || _log is null || Service?.Client is not { } client)
        {
            return;
        }

        string map = client.TravellingTo ?? client.CurrentMap ?? client.Lobby.Setup.Map ?? string.Empty;
        bool round = client.State is OnlineClientState.Loading or OnlineClientState.Match ||
            (client.State == OnlineClientState.Travelling && !OpenScct.Server.MapCatalog.IsMenuMap(map));
        _heading.Text = client.State switch
        {
            OnlineClientState.Travelling or OnlineClientState.Loading => "Loading " + MapTitles.Of(client.Maps, map),
            OnlineClientState.Match => "Playing " + MapTitles.Of(client.Maps, map),
            _ => "Connecting to " + (client.Lobby.ServerName.Length > 0 ? client.Lobby.ServerName : "the server"),
        };
        EGameMode mode = client.Lobby.Setup.Mode;
        _mode!.Visible = _modeImage!.Visible = _explain!.Visible = _mapPicture!.Visible = round;
        if (round)
        {
            bool merc = client.Lobby.Local?.Team == 1;
            string key = map + (merc ? "/merc" : "/spy");
            if (key != _pictureKey)
            {
                _pictureKey = key;
                _mapPicture.SetMap(MapImages.Loading(client.Maps, map, merc), MapTitles.Of(client.Maps, map));
            }

            _mode.Text = LobbySetup.ModeName(mode);
            _modeImage.Texture = UiStyle.Assets?.Texture("common", mode switch
            {
                EGameMode.GM_DiskHunt => "L_diskhunt",
                EGameMode.GM_DeathMatch or EGameMode.GM_MercDeathMatch => "L_Death",
                _ => "L_Story",
            });
            _explain.Text = Explanation(client, mode);
        }

        // The join's steps (direct, NAT punch, relay) belong to joining; the loading page of a round shows none.
        var sb = new StringBuilder();
        if (!round)
        {
            foreach (string line in client.ConnectLog)
            {
                sb.AppendLine(line);
            }
        }

        if (client.State is OnlineClientState.Loading or OnlineClientState.Match && client.CurrentMap is { } current && !Service.Catalog.CanRender(current))
        {
            sb.AppendLine(current + " has no render content in this install: the round runs without a view.");
        }

        _log.Text = sb.ToString();
    }

    /// <summary>The map's words for the mode, for the local player's side (loading.InitBackground).</summary>
    private static string Explanation(OnlineClient client, EGameMode mode)
    {
        if (client.Maps.GetMapSettings(client.Lobby.Setup.Map) is not { } settings)
        {
            return string.Empty;
        }

        int index = settings.SearchForGameMode(USMatchInfo.RulesModeFor(mode));
        if (index < 0 || index >= settings.ModesStrings.Count)
        {
            return string.Empty;
        }

        FGameModeInfoStrings words = settings.ModesStrings[index];
        return client.Lobby.Local?.Team == 1 ? words.ExplicationModeSentry : words.ExplicationModeSpy;
    }
}
