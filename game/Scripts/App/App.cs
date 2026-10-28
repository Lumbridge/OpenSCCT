using Godot;
using OpenScctGame.Smoke;

namespace OpenScctGame;

/// <summary>
/// Autoload at /root/App. Reads the launch options and, when a smoke test is requested,
/// starts the smoke runner instead of the normal flow.
/// </summary>
public partial class App : Node
{
    public const string NodePath = "/root/App";

    public LaunchOptions Options { get; private set; } = LaunchOptions.Empty;

    public bool IsSmokeRun => Options.SmokeTest is not null;

    public static App From(Node node) => node.GetNode<App>(NodePath);

    public override void _ExitTree()
    {
        // The skeletal models kept for reuse are not in the tree: free them while the servers still run.
        Render.Characters.SkeletalModel.ReleaseTemplates();
    }

    public override void _Ready()
    {
        Options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());

        // Crash reports next to the settings (rl:ac.crash), then the process options of the command line and the
        // power throttling opt-out (rl:gfx.system).
        CrashLog.Install(OS.GetUserDataDir());
        ProcessTuning.Apply(Options.Priority, Options.Affinity);

        // Where an installed build finds the content the launcher built and the maps shipped beside the exe.
        OpenScct.Server.ContentPaths.Configure(Options.ContentRoot, System.IO.Path.GetDirectoryName(OS.GetExecutablePath()));
        // Maps downloaded from SCCT-Maps: where they are, so lobbies can tell and fetch them.
        OpenScctGame.Online.MapDownloads.Configure(Options);
        foreach (string arg in Options.Unknown)
        {
            if (arg != BackgroundWindow.Flag)
            {
                GD.PushWarning($"Unknown argument '{arg}'.");
            }
        }

        if (BackgroundWindow.Wanted(Options.SmokeTest is not null, OS.GetCmdlineUserArgs()))
        {
            // Test runs leave the person at the machine their focus and their mouse.
            OpenScctGame.Controls.Pointer.Detached = true;
            AddChild(new BackgroundWindow { Name = "BackgroundWindow" });
        }

        // Controller use of the menus (focus, B and Start back out).
        AddChild(new OpenScctGame.Options.PadMenus());

        // The player's saved gear and the gear-change HUD line on every level the game loads (spec/additions/gear-change.md).
        OpenScctGame.Play.Session.PlayMapLoader.AddLevelServices = OpenScctGame.Play.GearServices.Install;

        if (Options.SmokeTest is { } smokeName)
        {
            // Smoke runs use the default settings and never write the player's files.
            OpenScctGame.Options.SettingsStore.Isolated = true;
            var runner = new SmokeRunner(smokeName, Options.SmokeTimeoutSeconds) { Name = "SmokeRunner" };
            AddChild(runner);
        }
        else
        {
            // Settings saved before the rename to OpenSCCT, then the player's saved window, audio and frame-cap
            // settings (user://settings.json).
            OpenScctGame.Options.UserDataMigration.Run(OS.GetUserDataDir());
            OpenScctGame.Options.SettingsApplier.ApplyAtStart(GetViewport());
            if (OpenScctGame.Options.SettingsStore.Current.ThrottleStop)
            {
                ProcessTuning.StopPowerThrottling();
            }

            // The developer console on the key left of 1 (rl:qol.console); it keeps itself last in the tree.
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, new OpenScctGame.UI.DevConsole());

            // The player's Discord status and a friend's Join (docs/DISCORD.md).
            if (OpenScctGame.Discord.DiscordPresence.Wanted(this))
            {
                AddChild(new OpenScctGame.Discord.DiscordPresence { Name = OpenScctGame.Discord.DiscordPresence.NodeName });
            }
        }
    }
}
