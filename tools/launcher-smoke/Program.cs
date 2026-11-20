using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Unreal;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    // A preserved DLL can prove that a regression fails against a previous build, without replacing
    // the current source or the launcher installed on this computer.
    private static readonly Type MainType = (Environment.GetEnvironmentVariable("OPENSCCT_SMOKE_LAUNCHER_DLL") is { Length: > 0 } prior
        ? Assembly.LoadFrom(Path.GetFullPath(prior)) : Assembly.Load("OpenSCCT.Launcher")).GetType("OpenScct.Launcher.App.MainForm", true)!;
    private static readonly SemVersion InstalledVersion = SemVersion.Parse("0.8.166");
    private static readonly SemVersion LatestVersion = SemVersion.Parse("0.8.167");
    private static readonly SemVersion EditorGameVersion = SemVersion.Parse("0.8.166");
    private static readonly SemVersion EditorVersion = SemVersion.Parse("0.2.54");
    private static readonly SemVersion UnrealVersion = SemVersion.Parse("0.1.10");
    private static readonly SemVersion UnrealLatestVersion = SemVersion.Parse("0.2.0");
    private const string SmokeUnrealRepository = "smoke-owner/smoke-unreal";
    // Published v0.1.1 highlights, captured from its release on 2026-11-05. These are deliberately
    // longer than the old generic game fixture: this is the real Editor-tab scrollbar regression.
    private const string PublicEditorNotes = """
        OpenSCCT Editor 0.1.1 adds support for the Editor tab in the unified Windows OpenSCCT launcher. The editor remains available as a standalone download and keeps its own updater.

        ## Changes

        - Support optional launcher installation, guided game/content setup, and updates from either the launcher or editor.
        - Resolve game builds, downloaded content, and content tools from the launcher's installation root, including after the game updates while the editor remains open.
        - Coordinate editor update staging, installation, and recovery so simultaneous updater operations do not replace each other's files or instructions.
        - Add structured update commands and package compatibility metadata for the launcher, with cancellable staging and verified installation.
        - Include the placeables palette, drag-and-drop placement and wiring, actor/device icons, and imported-map mesh preview improvements already merged into the editor.
        """;
    private const string PublicGameNotes = """
        ## Gameplay
        - Rescuing a grabbed merc by killing the spy no longer takes 51 health from the merc. A merc on low health survives the release with the health they had before the rescue.
        - Added regression coverage for gunfire, grenade and mine interruptions during grabs and ledge pulls.

        Includes all gameplay, menu, rendering, demo and networking fixes shipped in 0.8.92 and 0.8.93. Launcher remains 0.8.44.

        ### Launcher 0.8.45

        - **Game and Editor in one Windows launcher.** Switch to the Editor tab for the blue editor theme, its own release notes, and Install, Update, and Open actions.
        - **Optional editor setup.** Install OpenSCCT Editor and any missing game files together. Existing game content is shared, and game-only users do not download the editor.
        - **Updates from either app.** Keep the editor's self-updater or update through the launcher, with verified packages, safe cancellation, recovery, and protection for your projects and shared files.
        """;
    private static int _checks;
    private static int _failures;
    private static int _networkAttempts;
    private static bool _reportedDpi;
    private static int _captures;

    [STAThread]
    private static int Main(string[] args)
    {
        // Synthetic captures start at logical 96 DPI regardless of the interactive monitor's scale.
        // The production launcher retains PerMonitorV2; higher scales below change geometry and fonts.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "local/launcher-smoke/artifacts");
        Directory.CreateDirectory(output);
        if (args.Contains("--library", StringComparer.Ordinal))
        {
            LibraryChecks(output);
            Console.WriteLine($"Library smoke: {_checks} checks passed; {_failures} failed cases.");
            return _failures == 0 ? 0 : 1;
        }
        if (args.Contains("--sizing", StringComparer.Ordinal))
        {
            EditorSizingChecks(output);
            Console.WriteLine($"Launcher sizing smoke: {_checks} checks passed; {_failures} failed cases.");
            return _failures == 0 ? 0 : 1;
        }
        if (args.Contains("--lifecycle", StringComparer.Ordinal))
        {
            LifecycleChecks();
            Console.WriteLine($"Launcher lifecycle smoke: {_checks} checks passed; {_failures} failed cases.");
            return _failures == 0 ? 0 : 1;
        }
        if (args.Contains("--settings", StringComparer.Ordinal))
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using var fixture = new Fixture(new Scenario("ready", true, true, "PLAY"));
                CaptureDialogs(fixture, scale, output, settingsOnly: true);
            }
            SettingsScopeChecks();
            Inspect(() => Check(_networkAttempts == 0, "Settings captures made no HTTP requests."));
            Console.WriteLine($"Launcher settings smoke: {_checks} checks passed; {_failures} failed cases; {_captures} offscreen captures in {output}.");
            return _failures == 0 ? 0 : 1;
        }
        var scenarios = new Scenario[]
        {
            new("fresh-no-feed", false, false, "Install"),
            new("fresh", false, false, "Install"),
            new("steam-missing", false, false, "Choose Steam folder"),
            new("incomplete", true, false, "Build game files"),
            new("ready", true, true, "PLAY"),
            new("remove-blocked", true, true, "PLAY"),
            new("game-update", true, true, "Update"),
            new("legacy-packs", true, true, "Build game files"),
            new("old-client", true, true, "Update"),
            new("building", true, false, "Cancel"),
            new("game-downloading", true, true, "Cancel"),
            new("stopped", true, false, "Build game files"),
            new("error", true, false, "Build game files"),
            new("offline", true, true, "PLAY"),
            new("join", true, true, "JOIN SESSION"),
            new("launcher-restart", true, true, "PLAY"),
            new("pending-game", true, true, "PLAY"),
            new("editor-absent", false, false, "Set up Editor"),
            new("editor-incomplete", true, false, "Set up Editor"),
            new("editor-ready", true, true, "Open Editor"),
            new("editor-update", true, true, "Update Editor"),
            new("editor-busy", true, true, "Cancel"),
            new("editor-busy-game", true, true, "Cancel"),
            new("editor-applying", true, true, "Please wait"),
            new("editor-recovery", true, true, "Recover Editor"),
            new("editor-offline", true, true, "Open Editor"),
            new("editor-error", true, true, "Open Editor"),
            new("editor-launcher-restart", true, true, "Open Editor"),
            new("unreal-absent", true, true, "Install Unreal"),
            new("unreal-prerequisite", false, false, "Build game files"),
            new("unreal-update", true, true, "Update Unreal"),
            new("unreal-steam-update", true, true, "Update Steam files"),
            new("unreal-ready", true, true, "Play"),
            new("unreal-pending", true, true, "Waiting for game to close"),
            new("unreal-busy", true, true, "Cancel"),
            new("unreal-unreleased", true, true, "Check again"),
        };

        foreach (Scenario scenario in scenarios)
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                foreach (bool minimum in new[] { false, true })
                {
                    string label = $"{scenario.Name}-{(minimum ? "minimum" : "default")}-{scale * 100:0}";
                    using var fixture = new Fixture(scenario);
                    using Form form = Create(fixture, scenario);
                    Size previewSize = minimum ? new Size(600, 420) : form.ClientSize;
                    if (scale != 1f) ScaleForPreview(form, fixture, scale);
                    // Client size is logical at 96 DPI. MinimumSize includes the native window frame.
                    form.MinimumSize = Size.Empty;
                    form.ClientSize = new Size((int)(previewSize.Width * scale), (int)(previewSize.Height * scale));
                    Inspect(() => Capture(form, fixture, scenario, output, label));
                }
            }
        }

        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            var scenario = new Scenario("ready", true, true, "PLAY");
            using var fixture = new Fixture(scenario);
            using Form form = Create(fixture, scenario);
            if (scale != 1f) ScaleForPreview(form, fixture, scale);
            form.MinimumSize = Size.Empty;
            form.Size = new Size((int)(600 * scale), (int)(420 * scale));
            Inspect(() => Capture(form, fixture, scenario, output, $"ready-outer-minimum-{scale * 100:0}"));
            CaptureDialogs(fixture, scale, output);
        }

        LibraryChecks(output);
        EditorSizingChecks(output);
        LifecycleChecks();
        SettingsScopeChecks();
        Inspect(() => Check(_networkAttempts == 0, "No dialog HTTP requests were attempted."));
        Console.WriteLine($"Launcher smoke: {_checks} checks passed; {_failures} failed cases; {_captures} offscreen captures in {output}.");
        Console.WriteLine("Fixtures used temporary installs and offline startup; live update checks, downloads, game/editor launch, browser, clipboard and protocol registration were not invoked.");
        return _failures == 0 ? 0 : 1;
    }

    private static Form Create(Fixture fixture, Scenario scenario)
    {
        if (!File.Exists(fixture.Layout.Settings)) new LauncherSettings { SelectedProduct = "Game", CheckOnStart = false }.Save(fixture.Layout.Settings);
        var form = (Form)Activator.CreateInstance(MainType, Hidden, null,
            new object?[] { fixture.Layout, scenario.Name == "join" ? "smoke-session" : null, null, fixture.LauncherPath }, null)!;
        Set(form, "_steamSource", new Lazy<string?>(() => scenario.Name == "steam-missing" ? null : fixture.Layout.Root));
        if (!_reportedDpi) Console.WriteLine($"Before handle: DeviceDpi={form.DeviceDpi}, AutoScaleDimensions={form.AutoScaleDimensions}, CurrentAutoScaleDimensions={form.CurrentAutoScaleDimensions}, Font={form.Font.SizeInPoints}pt.");
        _ = form.Handle;
        if (!_reportedDpi)
        {
            Console.WriteLine($"After handle: DeviceDpi={form.DeviceDpi}, AutoScaleDimensions={form.AutoScaleDimensions}, CurrentAutoScaleDimensions={form.CurrentAutoScaleDimensions}, Font={form.Font.SizeInPoints}pt.");
            _reportedDpi = true;
        }
        ReleaseFeedSource source = scenario.Name == "offline" ? ReleaseFeedSource.Cache : ReleaseFeedSource.Network;
        SemVersion version = scenario.Name is "fresh" or "game-update" or "game-downloading" or "old-client" ? LatestVersion : InstalledVersion;
        var release = new ReleaseInfo
        {
            TagName = "v" + version,
            Name = "OpenSCCT " + version,
            PublishedAt = new DateTimeOffset(2026, 11, 3, 19, 30, 0, TimeSpan.Zero),
            Body = """
                ## Gameplay fixes

                - Fixed a movement-action queue issue after quick action sequences, keeping Spy stance transitions and interaction responsive.
                - Improved focus recovery after switching windows, with safer mouse capture when returning to play.
                - Build the original Versus maps from the installed Steam copy of Chaos Theory and download additional maps under Mods.
                - Additional changes are available in the full changelog.
                """,
        };
        var check = new UpdateCheck(new ReleaseFeed([release], source, null, scenario.Name == "offline" ? "Offline. Showing cached release information." : null),
            new VersionedRelease(version, release), scenario.Game ? InstalledVersion : null,
            scenario.Name is "fresh" or "game-update" or "game-downloading" or "old-client", false);
        if (scenario.Name != "fresh-no-feed") Call(form, "ShowReleaseCheck", check);
        if (scenario.Editor)
        {
            SemVersion editorLatest = scenario.Name == "editor-update" ? SemVersion.Parse("0.2.55") : EditorVersion;
            var editorRelease = new ReleaseInfo
            {
                TagName = "v" + editorLatest, Name = "OpenSCCT Editor " + editorLatest,
                PublishedAt = release.PublishedAt, Body = release.Body,
                HtmlUrl = EditorService.ReleasesPage + "/tag/v" + editorLatest,
            };
            string archive = $"OpenSCCT-Editor-{editorLatest}-win-x64.zip";
            editorRelease.Assets = [new ReleaseAsset { Name = archive }, new ReleaseAsset { Name = archive + ".sha256" },
                new ReleaseAsset { Name = archive[..^4] + ".launcher.json" }];
            var editorFeed = new ReleaseFeed([editorRelease], scenario.Name == "editor-offline" ? ReleaseFeedSource.Cache : ReleaseFeedSource.Network, null, null);
            Set(form, "_editorCheck", new EditorCheck(editorFeed, EditorService.Offer(editorRelease),
                scenario.Name.StartsWith("editor-absent", StringComparison.Ordinal) ? null : new EditorInstallation(EditorVersion, EditorGameVersion, 1), scenario.Name == "editor-update"));
            Call(form, "SelectProduct", true);
        }
        if (scenario.Unreal)
        {
            // The fixture service rejects HTTP and never sees a running Unreal game.
            var unreal = new UnrealService(fixture.Layout, fixture.Http);
            typeof(UnrealService).GetProperty("RunningOverride", Hidden)!.SetValue(unreal, (Func<bool>)(() => scenario.Name == "unreal-pending"));
            Set(form, "_unreal", unreal);
            bool unreleased = scenario.Name == "unreal-unreleased";
            SemVersion unrealLatest = scenario.Name is "unreal-update" or "unreal-pending" or "unreal-busy" ? UnrealLatestVersion : UnrealVersion;
            var unrealRelease = new ReleaseInfo
            {
                TagName = "v" + unrealLatest, Name = "OpenSCCT Unreal " + unrealLatest,
                PublishedAt = release.PublishedAt, Body = release.Body,
                HtmlUrl = unreal.ReleasesPage + "/tag/v" + unrealLatest,
            };
            VersionedRelease? latest = unreleased ? null : new VersionedRelease(unrealLatest, unrealRelease);
            Set(form, "_unrealCheck", new UnrealCheck(new ReleaseFeed(unreleased ? [] : [unrealRelease], ReleaseFeedSource.Network, null, null),
                latest, unreal.Installed(), scenario.Name == "unreal-update"));
            Call(form, "SelectProduct", Product("Unreal"));
        }
        if (scenario.Name is "building" or "game-downloading" or "editor-busy" or "editor-busy-game" or "editor-applying" or "unreal-busy")
        {
            fixture.Work = new CancellationTokenSource();
            Set(form, "_work", fixture.Work);
            Set(form, "_operationProduct", Product(scenario.Unreal ? "Unreal" : scenario.Editor ? "Editor" : "Game"));
            Set(form, "_operationCanCancel", scenario.Name != "editor-applying");
        }
        Call(form, "RefreshLocal");
        switch (scenario.Name)
        {
            case "remove-blocked":
                EditorService editor = Field<EditorService>(form, "_editor");
                typeof(EditorService).GetProperty("RunningOverride", Hidden)!.SetValue(editor, (Func<string, bool>)(_ => true));
                Call(form, "RemoveGameFiles", false);
                break;
            case "building":
            case "game-downloading":
                Call(form, "SetOperation", scenario.Name == "building" ? "Building Steam game files" : "Downloading OpenSCCT");
                SetStatus(form, scenario.Name == "building" ? "Reading the Steam installation." : $"Downloading OpenSCCT {LatestVersion}…", Color.FromArgb(127, 149, 133));
                Field<Control>(form, "_progress").Visible = true;
                Field<Control>(form, "_progress").GetType().GetProperty("Fraction")!.SetValue(Field<Control>(form, "_progress"), 0.42);
                Field<Label>(form, "_progressText").Text = scenario.Name == "building" ? "4 of 21  ·  Building map geometry" : "128.0 MB of 304.0 MB  12.0 MB/s";
                Field<Label>(form, "_progressText").Visible = true;
                break;
            case "editor-busy":
            case "editor-busy-game":
                Call(form, "SetOperation", "Downloading editor");
                SetStatus(form, "Downloading OpenSCCT Editor…", Color.FromArgb(148, 170, 155));
                Field<Control>(form, "_progress").Visible = true;
                Field<Control>(form, "_progress").GetType().GetProperty("Fraction")!.SetValue(Field<Control>(form, "_progress"), 0.42);
                Field<Label>(form, "_progressText").Text = "128.0 MB of 304.0 MB";
                Field<Label>(form, "_progressText").Visible = true;
                if (scenario.Name == "editor-busy-game") Call(form, "SelectProduct", false);
                break;
            case "unreal-busy":
                Call(form, "SetOperation", "Installing OpenSCCT Unreal");
                SetStatus(form, "Downloading OpenSCCT Unreal 0.2.0...", Color.FromArgb(184, 161, 143));
                Field<Control>(form, "_progress").Visible = true;
                Field<Control>(form, "_progress").GetType().GetProperty("Fraction")!.SetValue(Field<Control>(form, "_progress"), 0.42);
                Field<Label>(form, "_progressText").Text = "512.0 MB of 1210.0 MB  24.0 MB/s";
                Field<Label>(form, "_progressText").Visible = true;
                break;
            case "editor-applying":
                Call(form, "SetOperation", "Finishing editor update");
                SetStatus(form, "Checking that the new editor starts before completing the update.", Color.FromArgb(148, 170, 155));
                break;
            case "editor-offline":
                SetStatus(form, "Offline. You can still open your installed editor.", Color.FromArgb(255, 196, 92));
                break;
            case "editor-error":
                SetStatus(form, "Editor download was interrupted. The installed editor and your projects are unchanged.", Color.FromArgb(255, 110, 100));
                break;
            case "stopped":
                SetStatus(form, "Stopped. Press the button to carry on.", Color.FromArgb(127, 149, 133));
                break;
            case "error":
                SetStatus(form, "Could not read a required Steam file. Verify the Steam installation, then build the game files again.", Color.FromArgb(255, 110, 100));
                break;
            case "offline":
                SetStatus(form, "Offline. Showing cached release information. You can still play your installed version.", Color.FromArgb(255, 196, 92));
                break;
            case "join":
                SetStatus(form, "Your Join request is waiting. Continue in the game when it starts.", Color.FromArgb(127, 149, 133));
                break;
        }
        return form;
    }

    private static void Capture(Form form, Fixture fixture, Scenario scenario, string output, string label)
    {
        // Reparent controls into a neutral host: production OnShown starts network work.
        Control[] controls = form.Controls.Cast<Control>().ToArray();
        using var host = new PreviewForm
        {
            Text = form.Text, Icon = form.Icon, ClientSize = form.ClientSize, Font = form.Font,
            BackColor = form.BackColor, ForeColor = form.ForeColor, Padding = form.Padding,
            AutoScaleMode = AutoScaleMode.None, StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000), ShowInTaskbar = false,
        };
        foreach (Control control in controls) host.Controls.Add(control);
        host.Show();
        Settle(host);
        Save(host, output, label);
        Button main = Field<Button>(form, "_main");
        Button help = Field<Button>(form, "_help");
        Check(main.Text == scenario.Button, label + " action is " + scenario.Button);
        if (scenario.Name == "ready")
            Check(main.BackColor == Color.FromArgb(120, 255, 140), label + " Game play button uses the green accent");
        // While an installed Unreal update waits for the running game, there is nothing to start.
        bool disabledAction = scenario.Name is "editor-applying" or "unreal-pending";
        Check(disabledAction ? !main.Enabled : main.Enabled && main.CanSelect,
            label + (scenario.Name == "editor-applying" ? " apply cannot be cancelled"
                : scenario.Name == "unreal-pending" ? " waiting Unreal update offers no start" : " primary action is keyboard selectable"));
        Check(help.Enabled && help.CanSelect, label + " Help stays keyboard selectable");
        Button forceClose = Field<Button>(form, "_forceClose");
        if (scenario.Name == "remove-blocked")
        {
            Check(forceClose.Visible && forceClose.CanSelect && IsInClient(host, forceClose), label + " blocked removal offers force close");
            Check(File.Exists(fixture.Layout.Manifest) && Directory.Exists(fixture.Layout.Packs), label + " blocked removal keeps the installed files");
        }
        else Check(!forceClose.Visible, label + " force close is hidden outside blocked removal");
        Check(Field<Label>(form, "_heading").Text.Length > 0, label + " status heading is populated");
        if (scenario.Unreal)
            Check(Field<Label>(form, "_installedInfo").Text.Contains(scenario.UnrealInstalled ? "OpenSCCT Unreal " + UnrealVersion : "not installed", StringComparison.Ordinal),
                label + " Unreal installed version is shown");
        else if (scenario.Game && scenario.Name != "editor-recovery")
            Check(Field<Label>(form, "_installedInfo").Text.Contains(scenario.Editor && scenario.Name != "editor-busy-game"
                ? EditorVersion.ToString() : GameVersionFor(scenario).ToString(), StringComparison.Ordinal), label + " installed version remains visible");
        if (scenario.Editor)
        {
            bool editorSelected = scenario.Name != "editor-busy-game";
            Check(host.BackColor == (editorSelected ? Color.FromArgb(19, 21, 27) : Color.FromArgb(16, 22, 18)), label + " whole window uses selected product palette");
            Check(main.BackColor == (fixture.Work is not null
                ? editorSelected ? Color.FromArgb(26, 31, 42) : Color.FromArgb(22, 31, 25)
                : Color.FromArgb(84, 148, 245)), label + " primary action uses selected product palette");
            Check(LauncherSettings.Load(fixture.Layout.Settings).SelectedProduct == (editorSelected ? "Editor" : "Game"), label + " selected tab is persisted");
            Check(Field<ComboBox>(form, "_compactPicker").CanSelect || Field<Button>(form, "_gameTab").CanSelect && Field<Button>(form, "_editorTab").CanSelect, label + " both product tabs remain keyboard selectable");
        }
        if (scenario.Unreal)
        {
            Check(host.BackColor == Color.FromArgb(26, 20, 16), label + " whole window uses the Unreal palette");
            Check(Field<Button>(form, "_unrealTab").BackColor == Color.FromArgb(255, 150, 70), label + " selected Unreal tab uses the orange accent");
            // Busy work and the disabled "Waiting for game to close" are not emphasized.
            bool plain = fixture.Work is not null || scenario.Name == "unreal-pending";
            Check(main.BackColor == (plain ? Color.FromArgb(36, 27, 21) : Color.FromArgb(255, 150, 70)), label + " primary action uses the Unreal palette");
            Check(LauncherSettings.Load(fixture.Layout.Settings).SelectedProduct == "Unreal", label + " Unreal tab is persisted");
            Check(Field<ComboBox>(form, "_compactPicker").CanSelect || Field<Button>(form, "_gameTab").CanSelect && Field<Button>(form, "_editorTab").CanSelect && Field<Button>(form, "_unrealTab").CanSelect,
                label + " all three product tabs remain keyboard selectable");
            Check(Field<Button>(form, "_website").Text == "Unreal releases", label + " website action opens Unreal releases");
            string expected = scenario.Name switch
            {
                "unreal-absent" => "Set up OpenSCCT Unreal",
                "unreal-prerequisite" => "Steam files needed",
                "unreal-update" => "Unreal update available",
                "unreal-ready" => "Ready to play",
                "unreal-steam-update" => "Steam content update",
                "unreal-pending" => "Update ready",
                "unreal-busy" => "Installing OpenSCCT Unreal",
                _ => "Not released yet",
            };
            Check(Field<Label>(form, "_heading").Text == expected, label + " Unreal heading is " + expected);
            if (scenario.Name == "unreal-busy")
                Check(Field<Control>(form, "_progress").Visible && Field<Label>(form, "_progressText").Visible, label + " Unreal transfer progress is visible");
            else
                Check(Field<Label>(form, "_launchDetail").Visible && Field<Label>(form, "_launchDetail").Text.Length > 0, label + " Unreal detail explains the state");
        }
        if (scenario.Name is "building" or "game-downloading")
        {
            Check(Field<Label>(form, "_heading").Text == (scenario.Name == "building" ? "Building Steam game files" : "Downloading OpenSCCT"), label + " operation phase is shown");
            Check(Field<Control>(form, "_progress").Visible && Field<Label>(form, "_progressText").Visible, label + " progress and transfer details are visible");
            Check(!Field<Button>(form, "_playCurrent").Visible, label + " update deferral stays hidden while busy");
        }
        if (scenario.Name is "editor-busy" or "editor-busy-game")
        {
            Check(Field<Label>(form, "_heading").Text == (scenario.Name == "editor-busy" ? "Downloading editor" : "Editor · Downloading editor"), label + " operation owner survives tab changes");
            Check(Field<Control>(form, "_progress").Visible && Field<Label>(form, "_progressText").Visible, label + " editor transfer progress remains visible");
        }
        Control wordmark = Walk(host).Single(c => c.GetType().Name == "Wordmark");
        using (Graphics graphics = host.CreateGraphics())
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int width = TextRenderer.MeasureText(graphics, "Open", wordmark.Font, Size.Empty, flags).Width
                + TextRenderer.MeasureText(graphics, "SCCT", wordmark.Font, Size.Empty, flags).Width;
            Check(wordmark.Width + 2 >= width, label + " complete wordmark fits its painted text");
        }
        if (scenario.Name is "fresh-no-feed" or "unreal-unreleased")
            Check(Field<Label[]>(form, "_previewItems").Count(c => c.Visible && c.Text.Length > 0) == 1, label + " notes placeholder shown before first check");
        else
        {
            Check(Field<Label>(form, "_previewTitle").Text.Contains(VersionFor(scenario), StringComparison.Ordinal), label + " preview identifies its release");
            Check(Field<Label[]>(form, "_previewItems").Count(c => c.Visible && c.Text.Length > 0) == 3, label + " three preview entries shown");
        }
        Check(IsInClient(host, main), label + " primary action is visible inside client area");
        Check(IsInClient(host, help), label + " Help is visible inside client area");
        Check(host.SelectNextControl(null, true, true, true, false), label + " keyboard traversal enters launcher controls");
        if (scenario.Name == "old-client")
            Check(!Field<Button>(form, "_playCurrent").Visible, label + " old clients cannot launch even when Steam-built files exist");
        if (scenario.Name is "game-update" or "editor-update" or "unreal-update" or "unreal-steam-update")
        {
            Button defer = Field<Button>(form, "_playCurrent");
            Check(defer.Visible && defer.Enabled && IsInClient(host, defer), label + " update deferral remains available");
        }
        if (scenario.Name is "launcher-restart" or "editor-launcher-restart")
        {
            Button restart = Field<Button>(form, "_launcherButton");
            Check(restart.Visible && restart.Text == "Restart to finish" && IsInClient(host, restart), label + " staged launcher restart is visible");
        }
        if (scenario.Name is "stopped" or "error" or "offline" or "editor-error" or "editor-offline")
        {
            Label status = Field<Label>(form, "_status");
            string before = status.Text;
            Call(form, "RefreshLocal");
            Check(status.Text == before, label + " refresh preserves operation message");
            Check(IsInClient(host, status), label + " operation message remains visible");
        }
        foreach (Button button in Walk(host).OfType<Button>().Where(c => c.Visible))
        {
            Check(button.Width + 2 >= button.GetPreferredSize(Size.Empty).Width, label + " button label fits: " + button.Text);
            Check(IsInClient(host, button), label + " button is inside client area: " + button.Text);
        }
        foreach (FlowLayoutPanel flow in Walk(host).OfType<FlowLayoutPanel>().Where(c => c.Visible))
        {
            foreach (Control child in flow.Controls)
            {
                if (!child.Visible) continue;
                Check(child.Left >= 0 && child.Right <= flow.ClientSize.Width + 2 && child.Top >= 0 && child.Bottom <= flow.ClientSize.Height + 2,
                    label + " wrapping flow contains " + child.Text);
            }
        }
        if (fixture.Work is { } work)
        {
            if (scenario.Editor || scenario.Unreal)
            {
                string owner = scenario.Unreal ? "Unreal" : "Editor";
                Call(form, "QueueWebJoin", "smoke-session");
                Check(Selected(form) == "Game" && OperationProduct(form) == owner, label + " Join selects Game without changing " + owner + " operation owner");
                Check(Field<string>(form, "_pendingWebJoin") == "smoke-session" && !work.IsCancellationRequested, label + " Join queues without cancelling " + owner + " operation");
                if (scenario.Unreal)
                    Check(Field<Label>(form, "_heading").Text == "Unreal · Installing OpenSCCT Unreal", label + " Unreal operation owner survives tab changes");
            }
            if (scenario.Name != "editor-applying")
            {
                // Only the cancellation branch is invoked; this cannot start network or install work.
                typeof(Button).GetMethod("OnClick", Hidden)!.Invoke(main, [EventArgs.Empty]);
                Check(work.IsCancellationRequested, label + " Stop cancels the active operation");
            }
            else Check(!work.IsCancellationRequested, label + " apply is allowed to complete after Join queues");
            Set(form, "_work", null);
        }
        foreach (Control control in controls) form.Controls.Add(control);
        host.Hide();
    }

    private static SemVersion GameVersionFor(Scenario scenario) => scenario.Name == "old-client" ? SemVersion.Parse("0.8.165") : scenario.Editor ? EditorGameVersion : InstalledVersion;
    private static string VersionFor(Scenario scenario) => scenario.Unreal
        ? (scenario.Name is "unreal-update" or "unreal-pending" or "unreal-busy" ? UnrealLatestVersion : UnrealVersion).ToString()
        : scenario.Editor && scenario.Name != "editor-busy-game"
        ? scenario.Name == "editor-update" ? "0.2.55" : EditorVersion.ToString()
        : scenario.Name is "fresh" or "game-update" or "game-downloading" or "old-client" ? LatestVersion.ToString() : InstalledVersion.ToString();
    private static void CaptureDialogs(Fixture fixture, float scale, string output, bool settingsOnly = false)
    {
        Assembly assembly = MainType.Assembly;
        using var http = new HttpClient(new NoNetworkHandler());
        Type contextType = assembly.GetType("OpenScct.Project.ReportContext", true)!;
        object context = Activator.CreateInstance(contextType)!;
        contextType.GetProperty("Source")!.SetValue(context,
            Enum.Parse(assembly.GetType("OpenScct.Project.ReportSource", true)!, "Launcher"));
        contextType.GetProperty("LauncherVersion")!.SetValue(context, "0.8.37");
        contextType.GetProperty("InstalledGameVersion")!.SetValue(context, InstalledVersion.ToString());
        Type paletteType = assembly.GetType("OpenScct.Launcher.App.ThemePalette", true)!;
        object editorPalette = paletteType.GetProperty("Editor")!.GetValue(null)!;
        object unrealPalette = paletteType.GetProperty("Unreal")!.GetValue(null)!;
        using var editorFixture = new Fixture(new Scenario("editor-ready", true, true, "Open Editor"));
        using var unrealFixture = new Fixture(new Scenario("unreal-ready", true, true, "Play"));
        var unrealSettings = new LauncherSettings { SelectedProduct = "Unreal", UnrealRepository = SmokeUnrealRepository };
        var dialogs = new (string Type, object?[] Args, Size Minimum, bool Editor, bool Unreal)[]
        {
            ("SettingsForm", [new LauncherSettings(), fixture.Layout, null, "Vulkan", false, 1, null], new Size(460, 360), false, false),
            ("MapsForm", [http, fixture.Layout.Packs, null, null], new Size(620, 460), false, false),
            ("HelpForm", [context, null, null, null, null], new Size(480, 420), false, false),
            ("SettingsForm", [new LauncherSettings { SelectedProduct = "Editor" }, editorFixture.Layout, editorPalette, "Vulkan", false, 1, null], new Size(460, 360), true, false),
            ("MapsForm", [http, fixture.Layout.Packs, editorPalette, (Func<bool>)(() => true)], new Size(620, 460), true, false),
            ("HelpForm", [context, (Func<string, bool>)(_ => false), (Action<string>)(_ => { }), editorPalette, EditorVersion.ToString()], new Size(480, 420), true, false),
            ("SettingsForm", [unrealSettings, unrealFixture.Layout, unrealPalette, "Vulkan", false, 1, null], new Size(460, 360), false, true),
        };
        foreach (var dialog in dialogs.Where(d => !settingsOnly || d.Type == "SettingsForm"))
        {
            foreach (bool minimum in dialog.Type == "SettingsForm" ? new[] { false, true } : new[] { true })
            {
                Type type = assembly.GetType("OpenScct.Launcher.App." + dialog.Type, true)!;
                using Form form = (Form)Activator.CreateInstance(type, BindingFlags.Public | Hidden, null, dialog.Args, null)!;
                _ = form.Handle;
                if (scale != 1f) ScaleForPreview(form, fixture, scale);
                form.MinimumSize = Size.Empty;
                if (minimum) form.ClientSize = new Size((int)(dialog.Minimum.Width * scale), (int)(dialog.Minimum.Height * scale));
                string label = $"{(dialog.Editor ? "editor-" : dialog.Unreal ? "unreal-" : "")}{dialog.Type}-{(minimum ? "minimum" : "default")}-{scale * 100:0}";
                Control[] controls = form.Controls.Cast<Control>().ToArray();
                using var host = new PreviewForm
                {
                    Text = form.Text, Icon = form.Icon, ClientSize = form.ClientSize, Font = form.Font,
                    BackColor = form.BackColor, ForeColor = form.ForeColor, Padding = form.Padding,
                    AutoScaleMode = AutoScaleMode.None, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000), ShowInTaskbar = false,
                };
                foreach (Control control in controls) host.Controls.Add(control);
                host.Show();
                Settle(host);
                // Match the Maps Load handler's splitter position without invoking base OnLoad, which
                // queues OnShown (and therefore would start network work on the production dialog).
                if (dialog.Type == "MapsForm")
                {
                    SplitContainer split = Walk(host).OfType<SplitContainer>().Single();
                    split.SplitterDistance = Math.Max(120, split.Height * 3 / 5);
                    Settle(host);
                }
                Save(host, output, label);
                Inspect(() =>
                {
                    if (dialog.Editor) Check(host.BackColor == Color.FromArgb(19, 21, 27), label + " dialog uses editor palette");
                    if (dialog.Unreal) Check(host.BackColor == Color.FromArgb(26, 20, 16), label + " dialog uses Unreal palette");
                    Check(host.SelectNextControl(null, true, true, true, false), label + " keyboard traversal enters controls");
                    foreach (Button button in Walk(host).OfType<Button>().Where(c => c.Visible))
                    {
                        Check(button.Width + 2 >= button.GetPreferredSize(Size.Empty).Width, label + " button text fits: " + button.Text);
                        // Scrolling dialog content can extend below its viewport; pinned footer actions cannot.
                        bool scrolling = false;
                        for (Control? parent = button.Parent; parent is not null && parent != host; parent = parent.Parent)
                            scrolling |= parent is ScrollableControl { AutoScroll: true };
                        if (!scrolling) Check(IsInClient(host, button), label + " fixed action is visible: " + button.Text);
                    }
                    if (dialog.Type == "MapsForm")
                    {
                        MethodInfo include = type.GetMethod("IncludeMap", BindingFlags.Static | BindingFlags.NonPublic)!;
                        Check(!(bool)include.Invoke(null, [SteamVersusMaps.DefaultHostMap])! && (bool)include.Invoke(form, ["AquaE"])!,
                            label + " only additional maps are offered");
                    }
                    if (dialog.Editor && dialog.Type == "MapsForm")
                    {
                        type.GetMethod("RemoveSelected", Hidden)!.Invoke(form, null);
                        Check(Walk(host).OfType<Label>().Any(c => c.Text.Contains("Close any running OpenSCCT", StringComparison.Ordinal)), label + " shared-content changes are guarded");
                    }
                    if (!dialog.Editor && dialog.Type == "HelpForm")
                    {
                        Check(!Walk(host).OfType<Button>().Any(c => c.Text.Contains("bug", StringComparison.OrdinalIgnoreCase)), label + " game help omits bug reporting");
                        Check(Walk(host).OfType<Button>().Any(c => c.Name == "FeatureRequest"), label + " offers feature suggestions");
                    }
                    if (dialog.Editor && dialog.Type == "HelpForm")
                    {
                        Button report = Walk(host).OfType<Button>().Single(c => c.Name == "EditorBugReport");
                        typeof(Button).GetMethod("OnClick", Hidden)!.Invoke(report, [EventArgs.Empty]);
                        Check(Walk(host).OfType<TextBox>().Single(c => c.Name == "SelectedLink").Text.StartsWith("https://github.com/OpenSCCT/OpenSCCT-Editor/issues/new", StringComparison.Ordinal), label + " editor reports route to editor repository");
                        Check(Walk(host).OfType<TextBox>().Single(c => c.Name == "ReportDetails").Text.Contains("Editor version: " + EditorVersion, StringComparison.Ordinal), label + " report details include editor version");
                    }
                    if (dialog.Type == "SettingsForm" && dialog.Unreal) InspectUnrealSettings(host, unrealFixture.Layout, label);
                    else if (dialog.Type == "SettingsForm") InspectSettings(host, dialog.Editor ? editorFixture.Layout : fixture.Layout, dialog.Editor, label);
                });
                foreach (Control control in controls) form.Controls.Add(control);
                host.Hide();
                if (dialog.Type == "SettingsForm" && dialog.Unreal)
                {
                    Walk(form).OfType<CheckBox>().Single(c => c.Name == "IncludeUnrealPreviewReleases").Checked = true;
                    CheckBox startup = Walk(form).OfType<CheckBox>().Single(c => c.Name == "CheckForUnrealUpdatesOnStart");
                    startup.Checked = !startup.Checked;
                    type.GetMethod("Save", Hidden)!.Invoke(form, null);
                    var saved = (LauncherSettings)type.GetProperty("Result")!.GetValue(form)!;
                    Inspect(() =>
                    {
                        Check(saved.SelectedProduct == "Unreal", label + " saving preserves the Unreal tab");
                        Check(saved.UnrealIncludePrereleases && !saved.IncludePrereleases && !saved.EditorIncludePrereleases,
                            label + " saving changes the Unreal preview preference and preserves the other products");
                        Check(!saved.UnrealCheckOnStart && saved.CheckOnStart && saved.EditorCheckOnStart,
                            label + " saving changes the Unreal startup preference and preserves the other products");
                        Check(saved.UnrealRepository == SmokeUnrealRepository, label + " saving keeps the Unreal release repository");
                        Check(!(bool)type.GetProperty("UninstallUnrealRequested")!.GetValue(form)! && !(bool)type.GetProperty("UninstallEditorRequested")!.GetValue(form)!
                            && !(bool)type.GetProperty("RemoveGameFilesRequested")!.GetValue(form)!, label + " saving settings requests no removal");
                        Check(!(bool)type.GetProperty("CanChangeRenderer")!.GetValue(form)! && !(bool)type.GetProperty("MonitorChanged")!.GetValue(form)!,
                            label + " Unreal settings never change the Godot game's renderer or monitor");
                    });
                }
                else if (dialog.Type == "SettingsForm")
                {
                    CheckBox preview = Walk(form).OfType<CheckBox>().Single(c => c.Name == (dialog.Editor ? "IncludeEditorPreviewReleases" : "IncludePreviewReleases"));
                    CheckBox startup = Walk(form).OfType<CheckBox>().Single(c => c.Name == (dialog.Editor ? "CheckForEditorUpdatesOnStart" : "CheckForUpdatesOnStart"));
                    preview.Checked = true;
                    startup.Checked = !startup.Checked;
                    type.GetMethod("Save", Hidden)!.Invoke(form, null);
                    var saved = (LauncherSettings)type.GetProperty("Result")!.GetValue(form)!;
                    Inspect(() =>
                    {
                        Check(saved.SelectedProduct == (dialog.Editor ? "Editor" : "Game"), label + " saving preserves selected product");
                        Check(saved.IncludePrereleases == !dialog.Editor && saved.EditorIncludePrereleases == dialog.Editor, label + " saving changes active preview preference and preserves other product");
                        Check(saved.CheckOnStart == dialog.Editor && saved.EditorCheckOnStart == !dialog.Editor,
                            label + " saving changes active startup preference and preserves other product");
                        Check(saved.UnrealCheckOnStart && !saved.UnrealIncludePrereleases, label + " saving preserves the Unreal preferences");
                        Check(!(bool)type.GetProperty("UninstallEditorRequested")!.GetValue(form)! && !(bool)type.GetProperty("RemoveGameFilesRequested")!.GetValue(form)!,
                            label + " saving settings requests no removal");
                        Check((bool)type.GetProperty("CanChangeRenderer")!.GetValue(form)! == !dialog.Editor,
                            label + " renderer changes are limited to game settings");
                        // The game offers Vulkan only, and the renderer has no control in Settings.
                        Check((string)type.GetProperty("GraphicsDriver")!.GetValue(form)! == "Vulkan", label + " saving retains the Vulkan renderer");
                        Check(!(bool)type.GetProperty("MonitorChanged")!.GetValue(form)!, label + " saving other settings leaves the monitor unchanged");
                    });
                }
            }
        }
    }

    private static void InspectSettings(Form host, LauncherLayout layout, bool editor, string label)
    {
        Control[] controls = Walk(host).ToArray();
        Check(host.Text == (editor ? "Editor settings" : "Game settings"), label + " title names the selected product");
        CheckBox[] checks = controls.OfType<CheckBox>().ToArray();
        Check(checks.Length == 2, label + " only active product update settings appear");
        Check(checks.Any(c => c.Name == (editor ? "IncludeEditorPreviewReleases" : "IncludePreviewReleases")) &&
            checks.All(c => c.Text.Contains(editor ? "editor" : "game", StringComparison.OrdinalIgnoreCase)),
            label + " update options explicitly name their product");
        foreach (CheckBox check in checks)
        {
            Check(check.Width + 2 >= check.GetPreferredSize(Size.Empty).Width, label + " checkbox text fits: " + check.Text);
        }
        Check(controls.OfType<Button>().Any(c => c.Name == (editor ? "UninstallEditor" : "RemoveGameFiles")), label + " correct removal action appears");
        Check(!controls.OfType<Button>().Any(c => c.Name == (editor ? "RemoveGameFiles" : "UninstallEditor")), label + " other product removal action is absent");
        Check(!controls.Any(c => c.Name == "GameRenderer"), label + " settings show no renderer choice (Vulkan only)");
        ComboBox? monitor = controls.OfType<ComboBox>().SingleOrDefault(c => c.Name == "GameMonitor");
        if (editor) Check(monitor is null, label + " editor settings omit the game monitor");
        else Check(monitor is not null && monitor.Enabled && monitor.SelectedIndex >= 0, label + " game monitor can be chosen before launch");
        if (editor)
        {
            Button uninstall = controls.OfType<Button>().Single(c => c.Name == "UninstallEditor");
            Check(uninstall.Enabled, label + " managed editor can be uninstalled");
            Check(uninstall.Text.Contains("Uninstall editor", StringComparison.OrdinalIgnoreCase), label + " uninstall wording is explicit");
        }
        else Check(controls.OfType<Button>().Single(c => c.Name == "RemoveGameFiles").Text.Contains("Remove game and shared files", StringComparison.OrdinalIgnoreCase), label + " game removal names shared files");
        LinkLabel[] links = controls.OfType<LinkLabel>().ToArray();
        Check(links.Any(c => c.Name == (editor ? "OpenEditorFolder" : "OpenGameFolder")) &&
            links.Any(c => c.Name == (editor ? "OpenEditorUserData" : "OpenGameUserData")), label + " product folders are available");
        Check(!links.Any(c => c.Name == (editor ? "OpenGameUserData" : "OpenEditorUserData")), label + " other product settings folder is absent");
        if (editor)
        {
            Check(links.Single(c => c.Name == "OpenEditorFolder").AccessibleDescription == layout.Editor, label + " editor folder targets fixture install");
            Check(links.Single(c => c.Name == "OpenEditorUserData").AccessibleDescription ==
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSCCT Editor"),
                label + " editor data folder targets editor settings and autosaves");
        }
        else Check(links.Single(c => c.Name == "OpenGameUserData").AccessibleDescription == LauncherLayout.GameUserData, label + " game data folder targets game settings");
        Button remove = controls.OfType<Button>().Single(c => c.Name == (editor ? "UninstallEditor" : "RemoveGameFiles"));
        if (!IsInClient(host, remove))
        {
            // Follow keyboard focus to the action before forcing layout. Otherwise WinForms restores the
            // earlier focused update checkbox into view during Settle, undoing the deliberate scroll.
            for (int step = 0; step < controls.Length && host.ActiveControl != remove; step++)
                host.SelectNextControl(host.ActiveControl, true, true, true, true);
            Check(host.ActiveControl == remove, label + " keyboard traversal reaches the removal action");
            for (Control? parent = remove.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is not ScrollableControl { AutoScroll: true } scroll) continue;
                scroll.ScrollControlIntoView(remove);
                Settle(host);
                break;
            }
        }
        Check(IsInClient(host, remove), label + " removal action is reachable by scrolling when needed");
    }

    private static void InspectUnrealSettings(Form host, LauncherLayout layout, string label)
    {
        Control[] controls = Walk(host).ToArray();
        Check(host.Text == "Unreal settings", label + " title names the Unreal tab");
        CheckBox[] checks = controls.OfType<CheckBox>().ToArray();
        Check(checks.Length == 2 && checks.Any(c => c.Name == "IncludeUnrealPreviewReleases") && checks.Any(c => c.Name == "CheckForUnrealUpdatesOnStart"),
            label + " only the Unreal update settings appear");
        Check(checks.All(c => c.Text.Contains("Unreal", StringComparison.Ordinal)), label + " update options name Unreal");
        foreach (CheckBox check in checks)
            Check(check.Width + 2 >= check.GetPreferredSize(Size.Empty).Width, label + " checkbox text fits: " + check.Text);
        Check(!controls.Any(c => c.Name is "GameRenderer" or "GameMonitor" or "ChooseEnhancedFolder"), label + " Godot game options are absent");
        Check(!controls.OfType<Button>().Any(c => c.Name is "RemoveGameFiles" or "UninstallEditor"), label + " other products' removal actions are absent");
        LinkLabel[] links = controls.OfType<LinkLabel>().ToArray();
        Check(links.SingleOrDefault(c => c.Name == "OpenUnrealFolder")?.AccessibleDescription == layout.Unreal, label + " Unreal folder targets the fixture install");
        Check(links.SingleOrDefault(c => c.Name == "OpenUnrealMapPacks")?.AccessibleDescription == layout.UnrealPacks, label + " Unreal map packs folder targets the fixture");
        Button uninstall = controls.OfType<Button>().Single(c => c.Name == "UninstallUnreal");
        Check(uninstall.Enabled && uninstall.Text.StartsWith("Uninstall Unreal", StringComparison.Ordinal), label + " installed Unreal build can be uninstalled");
        if (!IsInClient(host, uninstall))
        {
            for (int step = 0; step < controls.Length && host.ActiveControl != uninstall; step++)
                host.SelectNextControl(host.ActiveControl, true, true, true, true);
            for (Control? parent = uninstall.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is not ScrollableControl { AutoScroll: true } scroll) continue;
                scroll.ScrollControlIntoView(uninstall);
                Settle(host);
                break;
            }
        }
        Check(IsInClient(host, uninstall), label + " Unreal uninstall is reachable by scrolling when needed");
    }

    private static void SettingsScopeChecks()
    {
        Inspect(() =>
        {
            // Without an install the Unreal uninstall is offered but disabled.
            using var fixture = new Fixture(new Scenario("unreal-absent", true, true, "Install Unreal"));
            Type settingsType = MainType.Assembly.GetType("OpenScct.Launcher.App.SettingsForm", true)!;
            using Form form = (Form)Activator.CreateInstance(settingsType, BindingFlags.Public | Hidden, null,
                new object?[] { new LauncherSettings { SelectedProduct = "Unreal" }, fixture.Layout, null, "Vulkan", false, 1, null }, null)!;
            Check(form.Text == "Unreal settings", "Saved Unreal tab opens Unreal settings without a palette");
            Check(!Walk(form).OfType<Button>().Single(c => c.Name == "UninstallUnreal").Enabled, "Absent Unreal build cannot be uninstalled");
            Check(!(bool)settingsType.GetProperty("UninstallUnrealRequested")!.GetValue(form)!, "Unreal settings never request uninstall on construction");
        });
        Type type = MainType.Assembly.GetType("OpenScct.Launcher.App.SettingsForm", true)!;
        foreach (string state in new[] { "absent", "unmanaged", "recovery" })
        {
            Inspect(() =>
            {
                using var fixture = new Fixture(new Scenario(state == "absent" ? "editor-absent" : state == "recovery" ? "editor-recovery" : "editor-ready", true, true, "Open Editor"));
                if (state == "unmanaged") File.Delete(Path.Combine(fixture.Layout.Editor, ".launcher-managed"));
                using Form form = (Form)Activator.CreateInstance(type, BindingFlags.Public | Hidden, null,
                    new object?[] { new LauncherSettings { SelectedProduct = "Editor" }, fixture.Layout, null, "Vulkan", false, 1, null }, null)!;
                Button uninstall = Walk(form).OfType<Button>().Single(c => c.Name == "UninstallEditor");
                Check(uninstall.Enabled == (state == "recovery"), state + " editor uninstall availability follows launcher management");
                Check(!(bool)type.GetProperty("UninstallEditorRequested")!.GetValue(form)!, state + " settings never requests uninstall on construction");
                Check(!(bool)type.GetProperty("CanChangeRenderer")!.GetValue(form)!, state + " editor settings cannot change the game renderer");
                Check(!Walk(form).Any(c => c.Name == "GameRenderer"), state + " editor settings have no game renderer control");
                Check(File.Exists(fixture.LauncherPath), state + " settings inspection preserves fixture launcher");
            });
        }
        Inspect(() =>
        {
            using var fixture = new Fixture(new Scenario("ready", true, true, "PLAY"));
            using Form form = (Form)Activator.CreateInstance(type, BindingFlags.Public | Hidden, null,
                new object?[] { new LauncherSettings(), fixture.Layout, null, "Vulkan", true, 1, null }, null)!;
            ComboBox monitor = Walk(form).OfType<ComboBox>().Single(c => c.Name == "GameMonitor");
            Check(!monitor.Enabled, "Monitor selection is disabled while the game runs");
            Check(!(bool)type.GetProperty("CanChangeRenderer")!.GetValue(form)!, "Running game prevents renderer changes");
            type.GetMethod("Save", Hidden)!.Invoke(form, null);
            Check((string)type.GetProperty("GraphicsDriver")!.GetValue(form)! == "Vulkan" && !(bool)type.GetProperty("MonitorChanged")!.GetValue(form)!,
                "Saving other settings while the game runs keeps its renderer and monitor");
        });
    }
    private static void ScaleForPreview(Form form, Fixture fixture, float scale)
    {
        // Control.Scale changes geometry at this monitor's real DPI. Scale point sizes explicitly too,
        // so text measurement exercises the same relative space as a higher-DPI monitor.
        var fonts = Walk(form).Select(c => (Control: c, Font: c.Font)).ToArray();
        form.AutoScaleMode = AutoScaleMode.None;
        form.Scale(new SizeF(scale, scale));
        foreach (var item in fonts)
        {
            var font = new Font(item.Font.FontFamily, item.Font.SizeInPoints * scale, item.Font.Style, GraphicsUnit.Point);
            fixture.Fonts.Add(font);
            item.Control.Font = font;
        }
    }

    private static void LibraryChecks(string output)
    {
        Inspect(() =>
        {
            using var fixture = new Fixture(new Scenario("fresh", false, false, ""));
            using Form form = (Form)Activator.CreateInstance(MainType, Hidden, null,
                new object?[] { fixture.Layout, null, null, fixture.LauncherPath }, null)!;
            Check(Selected(form) == "Ps2", "Fresh launcher opens the PS2 edition");
            Field<LauncherSettings>(form, "_settings").CheckOnStart = false;
            Field<LauncherSettings>(form, "_settings").UnrealCheckOnStart = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            Settle(form);
            Save(form, output, "library-ps2-games");
            Check(Field<Button>(form, "_main").Text == "Choose disc image", "Owned-disc setup action");
            ComboBox editions = Field<ComboBox>(form, "_compactPicker");
            Check(editions.Items.Cast<string>().SequenceEqual(new[] { "Originals  /  PlayStation 2", "Originals  /  PC / Steam",
                "Modernisations  /  OpenSCCT Godot", "Modernisations  /  OpenSCCT Unreal", "Tools  /  OpenSCCT Editor" }),
                "The library groups the originals, both modernisations and the editor.");
            foreach (string edition in new[] { "Ps2", "Retail", "Game", "Unreal" })
            {
                Call(form, "SelectProduct", Product(edition));
                foreach (string page in new[] { "_modsPage", "_settingsButton", "_gamesPage" })
                {
                    Field<Button>(form, page).PerformClick();
                    Settle(form);
                    Check(Selected(form) == edition && Field<Button>(form, page).CanSelect, edition + " page navigation " + page);
                    bool games = page == "_gamesPage";
                    Check(Field<Control>(form, "_launchCard").Visible == games, edition + " primary action belongs to Games");
                    if (page == "_modsPage") Check(Field<Control>(form, "_details").Visible, edition + " has mod controls");
                    Save(form, output, "library-" + edition + "-" + page.TrimStart('_'));
                }
            }
            Call(form, "SelectProduct", Product("Retail"));
            Field<Button>(form, "_modsPage").PerformClick();
            Call(form, "ShowEdition", "Unreal");
            Settle(form);
            Check(Selected(form) == "Unreal" && Field<Control>(form, "_launchCard").Visible,
                "Standalone Unreal setup opens its Games page in the central launcher");
            form.ClientSize = new Size(600, 420);
            Settle(form);
            ComboBox picker = Field<ComboBox>(form, "_compactPicker");
            Check(picker.CanSelect && IsInClient(form, picker), "Compact layout provides an accessible edition chooser");
            picker.SelectedIndex = 0;
            Settle(form);
            Check(Selected(form) == "Ps2" && IsInClient(form, Field<Button>(form, "_main")), "Compact PS2 setup action fits");
            Save(form, output, "library-ps2-compact");
            Check(LauncherSettings.Load(fixture.Layout.Settings).SelectedProduct == "Ps2", "Edition is persisted");
            form.Hide();
        });
    }

    private static void LifecycleChecks()
    {
        Inspect(() =>
        {
            var scenario = new Scenario("editor-ready", true, true, "Open Editor");
            using var fixture = new Fixture(scenario);
            new LauncherSettings { SelectedProduct = "Editor", CheckOnStart = false, EditorCheckOnStart = false }.Save(fixture.Layout.Settings);
            using Form form = Create(fixture, scenario);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            Settle(form);
            EditorService editor = Field<EditorService>(form, "_editor");
            typeof(EditorService).GetProperty("RunningOverride", Hidden)!.SetValue(editor, (Func<string, bool>)(_ => true));
            Func<CancellationToken, Task> operation = cancel =>
                (Task)MainType.GetMethod("SetupOrUpdateEditorAsync", Hidden)!.Invoke(form, [cancel])!;
            Task work = (Task)MainType.GetMethod("RunAsync", Hidden)!.Invoke(form, [operation])!;
            PumpUntil(work);
            Settle(form);
            Button retry = Field<Button>(form, "_main");
            Button current = Field<Button>(form, "_playCurrent");
            Button forceClose = Field<Button>(form, "_forceClose");
            Check(retry.Visible && retry.Enabled && retry.Text == "Retry update", $"Failed editor setup retains retry even when installed version matches latest (text={retry.Text}, visible={retry.Visible}, enabled={retry.Enabled})");
            Check(current.Visible && current.Enabled && current.Text == "Use current version", "Failed editor setup offers the usable installed version");
            Check(Field<Label>(form, "_status").Text.Contains("close the editor", StringComparison.Ordinal), "Retry preserves the actual setup failure");
            Check(forceClose.Visible && forceClose.Enabled && forceClose.Text == "Force close and retry…",
                "Running editor offers force close and retry");
            Call(form, "SelectProduct", false);
            Check(!forceClose.Visible, "Editor force close is hidden on the Game tab");
            Call(form, "SelectProduct", true);
            Settle(form);
            Check(retry.Text == "Retry update" && current.Visible && forceClose.Visible, "Editor retry actions survive tab changes");
            string swapLock = Path.Combine(fixture.Layout.Editor, ".update", "swap.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(swapLock)!);
            using (File.Create(swapLock))
            {
                PumpUntil((Task)MainType.GetMethod("RunAsync", Hidden)!.Invoke(form, [operation])!);
                Settle(form);
                Check(!forceClose.Visible && Field<Label>(form, "_status").Text.Contains("updater to finish", StringComparison.Ordinal),
                    "A locked editor swap asks the user to wait");
            }
            form.Hide();
        });
        foreach (bool applying in new[] { false, true })
        {
            Inspect(() =>
            {
                var scenario = new Scenario("editor-ready", true, true, "Open Editor");
                using var fixture = new Fixture(scenario);
                using Form form = Create(fixture, scenario);
                var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken operationToken = default;
                Func<CancellationToken, Task> operation = async cancel =>
                {
                    operationToken = cancel;
                    await finish.Task;
                    cancel.ThrowIfCancellationRequested();
                };
                Task work = (Task)MainType.GetMethod("RunAsync", Hidden)!.Invoke(form, [operation])!;
                Set(form, "_operationCanCancel", !applying);
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Call(form, "OnFormClosing", closing);
                Check(closing.Cancel && !form.IsDisposed && !work.IsCompleted, "Launcher waits for active " + (applying ? "apply" : "download") + " before closing");
                Check(operationToken.IsCancellationRequested == !applying, "Launcher only cancels a cancellable phase on close");
                finish.SetResult();
                PumpUntil(work);
                Check(form.IsDisposed, "Launcher closes after active operation has settled");
            });
        }

        Inspect(() =>
        {
            using var fixture = new Fixture(new Scenario("ready", true, true, "PLAY"));
            using var http = new HttpClient(new NoNetworkHandler());
            Type type = MainType.Assembly.GetType("OpenScct.Launcher.App.MapsForm", true)!;
            using Form form = (Form)Activator.CreateInstance(type, BindingFlags.Public | Hidden, null,
                new object?[] { http, fixture.Layout.Packs, null, null }, null)!;
            _ = form.Handle;
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken operationToken = default;
            Func<CancellationToken, Task> operation = async cancel =>
            {
                operationToken = cancel;
                await finish.Task;
                cancel.ThrowIfCancellationRequested();
            };
            Task work = (Task)type.GetMethod("RunAsync", Hidden)!.Invoke(form, ["Fixture content operation", operation])!;
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            type.GetMethod("OnFormClosing", Hidden)!.Invoke(form, [closing]);
            Check(closing.Cancel && !form.IsDisposed && !work.IsCompleted, "Maps keeps its modal owner blocked until content cleanup finishes");
            Check(operationToken.IsCancellationRequested, "Maps asks content operation to cancel before closing");
            finish.SetResult();
            PumpUntil(work);
            Check(form.IsDisposed, "Maps closes only after content operation settles");
        });

        Inspect(() =>
        {
            using var fixture = new Fixture(new Scenario("editor-ready", true, true, "Open Editor"));
            new LauncherSettings { SelectedProduct = "Editor", CheckOnStart = false, EditorCheckOnStart = false }.Save(fixture.Layout.Settings);
            using Form form = (Form)Activator.CreateInstance(MainType, Hidden, null,
                new object?[] { fixture.Layout, null, null, fixture.LauncherPath }, null)!;
            _ = form.Handle;
            Call(form, "OnShown", EventArgs.Empty);
            Check(Selected(form) == "Editor" && Field<bool>(form, "_webJoinReady"), "Actual startup restores Editor and enables Join handling");
            Check(Field<object?>(form, "_work") is null && Field<EditorCheck?>(form, "_editorCheck") is null, "CheckOnStart=false leaves startup offline");
            Call(form, "SelectProduct", false);
            Call(form, "SelectProduct", true);
            Check(Field<object?>(form, "_work") is null && Field<EditorCheck?>(form, "_editorCheck") is null, "Changing tabs respects disabled automatic checks");
        });

        Inspect(() =>
        {
            using var fixture = new Fixture(new Scenario("unreal-ready", true, true, "Play"));
            new LauncherSettings { SelectedProduct = "Unreal", CheckOnStart = true, EditorCheckOnStart = true, UnrealCheckOnStart = false }.Save(fixture.Layout.Settings);
            using Form form = (Form)Activator.CreateInstance(MainType, Hidden, null,
                new object?[] { fixture.Layout, null, null, fixture.LauncherPath }, null)!;
            Set(form, "_editor", new EditorService(fixture.Layout, fixture.Http));
            Set(form, "_service", new UpdateService(fixture.Layout, new VersionStore(fixture.Layout),
                new GitHubReleaseClient(fixture.Http, ReleaseSource.Official, fixture.Layout.Cache), fixture.Http, Field<IGameProcessProbe>(form, "_games")));
            var unreal = new UnrealService(fixture.Layout, fixture.Http);
            typeof(UnrealService).GetProperty("RunningOverride", Hidden)!.SetValue(unreal, (Func<bool>)(() => false));
            Set(form, "_unreal", unreal);
            int attempts = _networkAttempts;
            _ = form.Handle;
            Call(form, "OnShown", EventArgs.Empty);
            Check(Selected(form) == "Unreal" && Field<bool>(form, "_webJoinReady"), "Actual startup restores the Unreal tab and enables Join handling");
            Check(_networkAttempts == attempts && Field<object?>(form, "_work") is null && Field<UnrealCheck?>(form, "_unrealCheck") is null
                && Field<UpdateCheck?>(form, "_lastCheck") is null, "Unreal startup ignores the other products' enabled check preferences");
            Check(Field<Label>(form, "_heading").Text == "Ready to play" && Field<Button>(form, "_main").Text == "Play",
                "Installed Unreal build is ready to play before any check");
            Check(Field<Label>(form, "_installedInfo").Text == "OpenSCCT Unreal " + UnrealVersion + " · OpenSCCT " + InstalledVersion,
                "Unreal version line names the OpenSCCT version it was built from");
            Call(form, "SelectProduct", Product("Unreal"));
            Check(_networkAttempts == attempts, "Reselecting the Unreal tab with checks off stays offline");
        });
        Inspect(() =>
        {
            using var fixture = new Fixture(new Scenario("unreal-ready", true, true, "Play"));
            new LauncherSettings { SelectedProduct = "Unreal", CheckOnStart = false, UnrealCheckOnStart = false }.Save(fixture.Layout.Settings);
            using Form form = (Form)Activator.CreateInstance(MainType, Hidden, null,
                new object?[] { fixture.Layout, "smoke-session", null, fixture.LauncherPath }, null)!;
            Check(Selected(form) == "Game", "A website Join opens the Game tab even when Unreal was last selected");
        });

        foreach (bool editor in new[] { false, true })
        {
            Inspect(() =>
            {
                using var fixture = new Fixture(new Scenario(editor ? "editor-ready" : "ready", true, true, editor ? "Open Editor" : "PLAY"));
                new LauncherSettings { SelectedProduct = editor ? "Editor" : "Game", CheckOnStart = editor, EditorCheckOnStart = !editor }.Save(fixture.Layout.Settings);
                using Form form = (Form)Activator.CreateInstance(MainType, Hidden, null,
                    new object?[] { fixture.Layout, null, null, fixture.LauncherPath }, null)!;
                // Reject every possible startup HTTP route even if this regression fails and starts a check.
                using var http = new HttpClient(new NoNetworkHandler());
                Set(form, "_editor", new EditorService(fixture.Layout, http));
                Set(form, "_service", new UpdateService(fixture.Layout, new VersionStore(fixture.Layout),
                    new GitHubReleaseClient(http, ReleaseSource.Official, fixture.Layout.Cache), http, Field<IGameProcessProbe>(form, "_games")));
                int attempts = _networkAttempts;
                _ = form.Handle;
                Call(form, "OnShown", EventArgs.Empty);
                Check(Field<bool>(form, "_webJoinReady"), "Disabled selected-product startup reaches ready state");
                Check(_networkAttempts == attempts && Field<object?>(form, "_work") is null && Field<EditorCheck?>(form, "_editorCheck") is null &&
                    Field<UpdateCheck?>(form, "_lastCheck") is null, (editor ? "Editor" : "Game") + " startup ignores the other product's enabled check preference");
                Call(form, "SelectProduct", editor);
                Check(_networkAttempts == attempts, "Reselecting disabled product remains offline");
            });
        }
    }

    private static void EditorSizingChecks(string output)
    {
        foreach (string state in new[] { "editor-absent-with-game", "editor-ready", "editor-update" })
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            Inspect(() =>
            {
                string label = $"sizing-{state}-{scale * 100:0}";
                var scenario = new Scenario(state, true, true, state == "editor-ready" ? "Open Editor" : state == "editor-update" ? "Update Editor" : "Set up Editor");
                using var fixture = new Fixture(scenario);
                if (state == "editor-ready")
                {
                    string path = Path.Combine(fixture.Layout.Editor, "version.json");
                    var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
                    manifest["version"] = "0.1.1";
                    File.WriteAllText(path, manifest.ToJsonString());
                }
                new LauncherSettings { CheckOnStart = false, SelectedProduct = "Game" }.Save(fixture.Layout.Settings);
                using Form form = Create(fixture, scenario);
                Set(form, "_editorCheck", null);
                Set(form, "_lastCheck", null);
                Call(form, "SelectProduct", false);
                Size initial = form.ClientSize;
                if (scale != 1f) ScaleForPreview(form, fixture, scale);
                form.MinimumSize = Size.Empty;
                form.ClientSize = new Size((int)(initial.Width * scale), (int)(initial.Height * scale));
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                // Exercise the real form hierarchy and autosizing. OnShown is explicitly offline:
                // no release checks, protocol registration, process launch, or editor installation.
                form.Show();
                Settle(form);
                Check(!Field<bool>(form, "_initialSizeSet"), label + " waits for populated changelog before fitting");
                var gameRelease = new ReleaseInfo { TagName = "v0.8.94", Name = "OpenSCCT 0.8.94", Body = PublicGameNotes,
                    PublishedAt = new DateTimeOffset(2026, 11, 5, 15, 20, 30, TimeSpan.Zero) };
                var editorRelease = new ReleaseInfo { TagName = "v0.1.1", Name = "OpenSCCT Editor 0.1.1", Body = PublicEditorNotes,
                    PublishedAt = new DateTimeOffset(2026, 11, 5, 16, 27, 49, TimeSpan.Zero),
                    Assets = [new ReleaseAsset { Name = "OpenSCCT-Editor-0.1.1-win-x64.zip" },
                        new ReleaseAsset { Name = "OpenSCCT-Editor-0.1.1-win-x64.zip.sha256" },
                        new ReleaseAsset { Name = "OpenSCCT-Editor-0.1.1-win-x64.launcher.json" }] };
                // Match release information arriving asynchronously while Game is visible.
                form.BeginInvoke(() =>
                {
                    Call(form, "ShowReleaseCheck", new UpdateCheck(new ReleaseFeed([gameRelease], ReleaseFeedSource.Cache, null, null),
                        new VersionedRelease(SemVersion.Parse("0.8.94"), gameRelease), EditorGameVersion, false, false));
                    Set(form, "_editorCheck", new EditorCheck(new ReleaseFeed([editorRelease], ReleaseFeedSource.Cache, null, null),
                        EditorService.Offer(editorRelease), null, state == "editor-update"));
                    Call(form, "RefreshReleasePreview");
                });
                Settle(form);
                int commonHeight = form.ClientSize.Height;
                int commonWidth = form.ClientSize.Width;
                Check(Field<bool>(form, "_initialSizeSet"), label + " populated changelog triggers initial fit");
                CheckPreviewFits(form, label + " Game populated changelog");
                Call(form, "SelectProduct", true);
                Settle(form);
                Check(form.ClientSize.Height == commonHeight && form.ClientSize.Width == commonWidth, label + " switching to Editor keeps common window dimensions");
                CheckPreviewFits(form, label + " Editor populated changelog");
                int idleTextChanges = 0, idleSizeChanges = 0;
                Label heading = Field<Label>(form, "_heading");
                EventHandler textChanged = (_, _) => idleTextChanges++;
                EventHandler sizeChanged = (_, _) => idleSizeChanges++;
                heading.TextChanged += textChanged;
                form.SizeChanged += sizeChanged;
                for (int tick = 0; tick < 3; tick++)
                {
                    Call(form, "OnPendingTick");
                    Settle(form);
                }
                heading.TextChanged -= textChanged;
                form.SizeChanged -= sizeChanged;
                Check(idleTextChanges == 0 && idleSizeChanges == 0, label + " idle polling does not swap product text or resize the window");
                Check(Field<Label[]>(form, "_previewItems").Count(c => c.Visible && c.Text.Length > 0) == 3, label + " Editor shows all three public release highlights");
                Check(Field<Label[]>(form, "_previewItems")[1].Text.Contains("installation root", StringComparison.Ordinal), label + " actual public editor changelog is used");
                Save(form, output, label + "-default");

                int screenMaximum = Screen.FromControl(form).WorkingArea.Height - (form.Height - form.ClientSize.Height);

                // Users can deliberately choose a smaller window and scroll its content.
                Call(form, "OnResizeBegin", EventArgs.Empty);
                form.ClientSize = new Size(commonWidth, (int)(100 * scale));
                Settle(form);
                TableLayoutPanel outer = Field<TableLayoutPanel>(form, "_mainLayout");
                Check(outer.AutoScroll, label + " extremely short fixture exercises outer scrolling mode");
                int shortHeight = form.ClientSize.Height;
                Call(form, "OnResizeEnd", EventArgs.Empty);
                Call(form, "FitUpdateHeight");
                Settle(form);
                Check(form.ClientSize.Height == shortHeight, label + " preserves a user-chosen short Editor window");
                Check(outer.AutoScroll, label + " short window retains scrolling after resize ends");
                int editorHeight = form.ClientSize.Height;
                Call(form, "FitUpdateHeight");
                Settle(form);
                Check(form.ClientSize.Height == editorHeight, label + " first fit accounts for outer scrollbar removal");
                Call(form, "SelectProduct", false);
                Settle(form);
                Check(form.ClientSize.Height == editorHeight, label + " switching back to Game does not resize");

                Call(form, "FitUpdateHeight");
                Settle(form);
                Check(form.ClientSize.Height == editorHeight, label + " repeated fitting stays stable after scrollbars clear");
                Check(Field<object?>(form, "_work") is null, label + " sizing started no update operation");
                if (commonHeight + (int)(40 * scale) < screenMaximum)
                {
                    form.ClientSize = new Size(commonWidth, commonHeight + (int)(40 * scale));
                    Call(form, "FitUpdateHeight");
                    Settle(form);
                    Check(form.ClientSize.Height == commonHeight + (int)(40 * scale), label + " preserves a larger window after startup");
                }
                foreach (bool widthOnly in new[] { false, true })
                {
                    int heightBefore = form.ClientSize.Height;
                    Call(form, "OnResizeBegin", EventArgs.Empty);
                    if (widthOnly) form.ClientSize = new Size(commonWidth + (int)(40 * scale), heightBefore);
                    else form.Location = new Point(form.Left + 1, form.Top);
                    Call(form, "OnResizeEnd", EventArgs.Empty);
                    gameRelease.Body = "- Short game release note.";
                    editorRelease.Body = "- Short editor release note.";
                    Call(form, "RefreshReleasePreview");
                    Settle(form);
                    Check(form.ClientSize.Height == heightBefore, label + (widthOnly ? " width-only resize" : " window move") + " and shorter notes preserve chosen height");
                    form.ClientSize = new Size(commonWidth, form.ClientSize.Height);
                    gameRelease.Body = PublicGameNotes;
                    editorRelease.Body = PublicEditorNotes;
                    Call(form, "RefreshReleasePreview");
                    Settle(form);
                }
                int chosenHeight = Math.Min(screenMaximum, form.ClientSize.Height + (int)(60 * scale));
                if (chosenHeight > form.ClientSize.Height)
                {
                    Call(form, "OnResizeBegin", EventArgs.Empty);
                    form.ClientSize = new Size(commonWidth, chosenHeight);
                    Call(form, "OnResizeEnd", EventArgs.Empty);
                    Settle(form);
                    Call(form, "SelectProduct", true);
                    Settle(form);
                    Check(form.ClientSize.Height == chosenHeight, label + " preserves extra height explicitly chosen by the user across tabs");
                }
                form.Hide();
            });
        }
        Inspect(() =>
        {
            var scenario = new Scenario("game-update", true, true, "Update");
            using var fixture = new Fixture(scenario);
            new LauncherSettings { CheckOnStart = false }.Save(fixture.Layout.Settings);
            using Form form = Create(fixture, scenario);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            Settle(form);
            Button defer = Field<Button>(form, "_playCurrent");
            Check(defer.Visible && defer.Focus(), "Game update deferral can receive keyboard focus");
            Call(form, "FitUpdateHeight");
            Settle(form);
            Check(form.ActiveControl == defer || defer.ContainsFocus, "Measuring Editor does not steal focus from Game update deferral");
            form.Hide();
        });
    }

    private static void CheckPreviewFits(Form form, string label)
    {
        Panel preview = Field<Panel>(form, "_releasePreview");
        int maximum = Screen.FromControl(form).WorkingArea.Height - (form.Height - form.ClientSize.Height);
        if (preview.VerticalScroll.Visible && form.ClientSize.Height >= maximum - 2)
        {
            Console.WriteLine(label + ": scrolling retained because the physical working area is full.");
            return;
        }
        Check(!preview.VerticalScroll.Visible, label + " has no vertical changelog scrollbar when the working area has room");
        Check(IsInClient(form, Field<Button>(form, "_whatsNew")), label + " changelog action fits");
        Check(IsInClient(form, Field<Label[]>(form, "_previewItems").Last(c => c.Visible)), label + " last release highlight fits");
    }

    private static void PumpUntil(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Check(task.IsCompleted, "Fixture operation completed within five seconds");
        task.GetAwaiter().GetResult();
    }
    private static void Settle(Control root)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            foreach (Control control in Walk(root).Reverse()) control.PerformLayout();
            Application.DoEvents();
        }
    }
    private static bool IsInClient(Control host, Control child)
    {
        Rectangle bounds = child.RectangleToScreen(child.ClientRectangle);
        for (Control? parent = child.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds)) return false;
            if (parent == host) return true;
        }
        return false;
    }
    private static void Save(Form host, string output, string label)
    {
        using var bitmap = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(output, label + ".png"), ImageFormat.Png);
        _captures++;
    }
    private static T Field<T>(Form form, string name) => (T)MainType.GetField(name, Hidden)!.GetValue(form)!;
    private static void Set(Form form, string name, object? value) => MainType.GetField(name, Hidden)!.SetValue(form, value);
    // Overloads (SelectProduct takes a bool or a Product) are chosen by the arguments' runtime types.
    private static void Call(Form form, string name, params object?[] args) => MainType.GetMethods(Hidden | BindingFlags.Public)
        .Single(m => m.Name == name && Accepts(m.GetParameters(), args)).Invoke(form, args);
    private static bool Accepts(ParameterInfo[] parameters, object?[] args) =>
        parameters.Length == args.Length && parameters.Zip(args).All(p => p.Second is null
            ? !p.First.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.First.ParameterType) is not null
            : p.First.ParameterType.IsInstanceOfType(p.Second));
    /// <summary>MainForm's private Product enum value with this name (Game, Editor or Unreal).</summary>
    private static object Product(string name) => Enum.Parse(MainType.GetNestedType("Product", BindingFlags.NonPublic)!, name);
    private static string Selected(Form form) => MainType.GetField("_selected", Hidden)!.GetValue(form)!.ToString()!;
    private static string OperationProduct(Form form) => MainType.GetField("_operationProduct", Hidden)!.GetValue(form)!.ToString()!;
    private static void SetStatus(Form form, string text, Color color) => Call(form, "SetStatus", text, color, null, false);
    private static IEnumerable<Control> Walk(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (Control nested in Walk(child)) yield return nested;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static void Inspect(Action inspect)
    {
        try { inspect(); }
        catch (InvalidOperationException ex)
        {
            _failures++;
            Console.Error.WriteLine(ex.Message);
        }
    }
    private sealed class PreviewForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _networkAttempts);
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("Layout smoke must not request HTTP resources."));
        }
    }
    private sealed record Scenario(string Name, bool Game, bool Content, string Button)
    {
        public bool Editor => Name.StartsWith("editor-", StringComparison.Ordinal);
        public bool Unreal => Name.StartsWith("unreal-", StringComparison.Ordinal);
        /// <summary>An Unreal build is installed in the fixture (absent and unreleased have none).</summary>
        public bool UnrealInstalled => Unreal && Name is not ("unreal-absent" or "unreal-unreleased");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openscct-launcher-smoke-" + Guid.NewGuid().ToString("N"));
        public LauncherLayout Layout { get; }
        public string LauncherPath { get; }
        public CancellationTokenSource? Work { get; set; }
        public List<Font> Fonts { get; } = [];
        /// <summary>Rejects every request, so fixture services cannot reach the network.</summary>
        public HttpClient Http { get; } = new(new NoNetworkHandler());

        public Fixture(Scenario scenario)
        {
            Layout = new LauncherLayout(_root);
            Directory.CreateDirectory(_root);
            LauncherPath = Path.Combine(_root, "OpenSCCT.Launcher.exe");
            File.WriteAllText(LauncherPath, "fixture; never executed");
            if (scenario.Name is "launcher-restart" or "editor-launcher-restart") File.WriteAllText(LauncherPath + ".new", "fixture; never executed");
            if (scenario.Game)
            {
                SemVersion gameVersion = GameVersionFor(scenario);
                Directory.CreateDirectory(Layout.VersionDir(gameVersion.ToString()));
                File.WriteAllText(Path.Combine(Layout.VersionDir(gameVersion.ToString()), VersionStore.GameExe), "fixture; never executed");
                var manifest = new InstallManifest { Current = gameVersion.ToString() };
                if (scenario.Name == "pending-game")
                {
                    Directory.CreateDirectory(Layout.VersionDir(LatestVersion.ToString()));
                    File.WriteAllText(Path.Combine(Layout.VersionDir(LatestVersion.ToString()), VersionStore.GameExe), "fixture; never executed");
                    manifest.Pending = LatestVersion.ToString();
                }
                manifest.Save(Layout.Manifest);
            }
            if (scenario.Content)
            {
                if (scenario.Name != "legacy-packs")
                {
                    string generation = Guid.NewGuid().ToString("N");
                    foreach (string name in SteamContentInstall.RequiredFiles())
                    {
                        string path = Path.Combine(Layout.Content, "steam-builds", generation, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllText(path, "{}");
                    }
                    string root = Path.Combine(Layout.Content, "steam-builds", generation);
                    File.WriteAllText(Path.Combine(root, "content-build.json"), JsonSerializer.Serialize(new { version = scenario.Name == "unreal-steam-update" ? "0.8.165" : InstalledVersion.ToString(), ok = true }));
                    File.WriteAllText(Path.Combine(Layout.Content, "steam-current.json"), JsonSerializer.Serialize(new { format = 1, build = generation }));
                }
                Directory.CreateDirectory(Layout.Packs);
                var packs = new List<InstalledContent>();
                foreach (string id in new[] { "core", "chars", "audio" })
                    packs.Add(new InstalledContent { Id = "shared/" + id, Code = id, Version = "1", Sha256 = "installed-" + id });
                foreach (string map in new[] { "AquaE", "ClubE", "BankE" })
                    packs.Add(new InstalledContent { Id = "stock/" + map, Code = map, Version = "1", Sha256 = "installed-" + map });
                File.WriteAllText(Path.Combine(Layout.Packs, "installed.json"), JsonSerializer.Serialize(packs, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                if (scenario.Editor || scenario.Unreal)
                {
                    Directory.CreateDirectory(Path.Combine(Layout.Packs, "shared"));
                    File.WriteAllText(Path.Combine(Layout.Packs, "shared", "classes.json"), "{}");
                    Directory.CreateDirectory(Path.Combine(Layout.Packs, "svmap", "AquaE"));
                    File.WriteAllText(Path.Combine(Layout.Packs, "svmap", "AquaE", "manifest.json"), "{}");
                }
            }
            if (scenario.UnrealInstalled)
            {
                // The Unreal build is laid out like the root: unreal\install.json and unreal\versions\<v>\.
                var unreal = new LauncherLayout(Layout.Unreal);
                void Version(SemVersion version)
                {
                    string folder = unreal.VersionDir(version.ToString());
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, UnrealService.StarterExe), "fixture; never executed");
                    File.WriteAllText(Path.Combine(folder, "version.json"), JsonSerializer.Serialize(new
                    {
                        product = UnrealService.Product, version = version.ToString(),
                        openscctVersion = InstalledVersion.ToString(), openscctCommit = "0123456789abcdef",
                    }));
                }
                Version(UnrealVersion);
                var manifest = new InstallManifest { Current = UnrealVersion.ToString() };
                if (scenario.Name == "unreal-pending")
                {
                    Version(UnrealLatestVersion);
                    manifest.Pending = UnrealLatestVersion.ToString();
                }
                manifest.Save(unreal.Manifest);
            }
            if (scenario.Editor && !scenario.Name.StartsWith("editor-absent", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(Layout.Editor, "ose"));
                File.WriteAllText(Path.Combine(Layout.Editor, EditorService.ExeName), "fixture; never executed");
                File.WriteAllText(Path.Combine(Layout.Editor, "ose", "ose.exe"), "fixture; never executed");
                File.WriteAllText(Path.Combine(Layout.Editor, ".launcher-managed"), "1");
                File.WriteAllText(Path.Combine(Layout.Editor, "version.json"), JsonSerializer.Serialize(new
                {
                    format = 1, product = "OpenSCCT Editor", version = EditorVersion.ToString(), platform = "win-x64",
                    launcherProtocol = 1, minimumGameVersion = EditorGameVersion.ToString(),
                    entries = new[] { EditorService.ExeName, "ose", "version.json" },
                    files = new[] { new { path = EditorService.ExeName, size = 23, sha256 = new string('0', 64) } },
                }));
                if (scenario.Name == "editor-recovery")
                {
                    Directory.CreateDirectory(Path.Combine(Layout.Editor, ".update"));
                    File.WriteAllText(Path.Combine(Layout.Editor, ".update", "journal.json"), "{}");
                    File.Delete(Path.Combine(Layout.Editor, EditorService.ExeName));
                }
            }
        }

        public void Dispose()
        {
            Work?.Dispose();
            Http.Dispose();
            foreach (Font font in Fonts) font.Dispose();
            // The only recursive deletion target is the exact absolute temp root created by this fixture.
            string target = Path.GetFullPath(_root);
            if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("openscct-launcher-smoke-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected smoke fixture cleanup path.");
            Directory.Delete(target, recursive: true);
        }
    }
}
