using System;
using System.Text.Json;
using Godot;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame;

/// <summary>Local TestRange browser experiment; reports the actual renderer and running simulation.</summary>
public partial class BrowserPrototype : Node
{
    private PlayRoot? _play;
    private double _elapsed;
    private bool _capturedFrame;

    public override void _Ready()
    {
        GD.Print("OPENSCCT_BROWSER_START " + RenderingServer.GetCurrentRenderingDriverName());
        if (OperatingSystem.IsBrowser())
        {
            Callable.From(() => BrowserPlatform.StartRound?.Invoke(BrowserPlatform.Lesson)).CallDeferred();
            return;
        }
        Callable.From(() =>
        {
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = "TestRange",
                    MapRoots = ["/content/svmap"],
                    Side = PlaySide.Spy,
                    Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Inline,
                    Bots = 0,
                    PlayerName = BrowserPlatform.AccountName ?? OpenScctGame.Options.SettingsStore.Current.PlayerName,
                },
                Audio = true,
            };
            _play = PlayRoot.Create(config);
            AddChild(_play);
            BrowserPlatform.CurrentPlay = _play;
        }).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (!_capturedFrame && !OperatingSystem.IsBrowser() && _play?.IsLoaded == true && _play.Session?.TicksRun > 180)
        {
            string? capture = System.Linq.Enumerable.FirstOrDefault(OS.GetCmdlineUserArgs(), value => value.StartsWith("--svm-browser-capture=", StringComparison.Ordinal));
            if (capture is not null)
            {
                _capturedFrame = true;
                using Image image = GetViewport().GetTexture().GetImage();
                image.SavePng(capture[22..]);
                GD.Print("OPENSCCT_NATIVE_PARITY_CAPTURE " + capture[22..]);
                GetTree().Quit();
            }
        }
        _elapsed += delta;
        if (_elapsed < 2) return;
        _elapsed = 0;
        BrowserPlatform.FlushSettings?.Invoke();
        var pawn = _play?.Session?.LocalController?.Pawn;
        GD.Print("OPENSCCT_BROWSER_PROBE " + JsonSerializer.Serialize(new
        {
            driver = RenderingServer.GetCurrentRenderingDriverName(),
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            loaded = _play?.IsLoaded ?? false,
            error = _play?.LoadError,
            ticks = _play?.Session?.TicksRun ?? 0,
            pawn = pawn is not null,
            x = pawn?.Location.X,
            y = pawn?.Location.Y,
            z = pawn?.Location.Z,
            postError = _play?.View?.Effect.Error,
            compositor = _play?.View?.Camera.Compositor is not null,
            controls = _play?.Controls?.Enabled,
            captured = OpenScctGame.Controls.Pointer.IsCaptured,
            focused = GetWindow().HasFocus(),
            menu = _play?.Menu?.IsOpen,
            inputActive = BrowserPlatform.InputActivated,
            playerName = OpenScctGame.Options.SettingsStore.Current.PlayerName,
            invertY = OpenScctGame.Options.SettingsStore.Current.InvertY,
            fps = Engine.GetFramesPerSecond(),
        }));
    }
}
