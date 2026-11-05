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
using OpenScct.Launcher.Settings;
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
    private static readonly SemVersion InstalledVersion = SemVersion.Parse("0.8.80");
    private static readonly SemVersion LatestVersion = SemVersion.Parse("0.8.81");
    private static readonly SemVersion EditorGameVersion = SemVersion.Parse("0.8.93");
    private static readonly SemVersion EditorVersion = SemVersion.Parse("0.1.0");
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

    [STAThread]
    private static int Main(string[] args)
    {
        // Synthetic captures start at logical 96 DPI regardless of the interactive monitor's scale.
        // The production launcher retains PerMonitorV2; higher scales below change geometry and fonts.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "local/launcher-smoke/artifacts");
        Directory.CreateDirectory(output);
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
        var scenarios = new Scenario[]
        {
            new("fresh-no-feed", false, false, "Install"),
            new("fresh", false, false, "Install"),
            new("incomplete", true, false, "Finish install"),
            new("ready", true, true, "PLAY"),
            new("game-update", true, true, "Update"),
            new("content-update", true, true, "Update"),
            new("downloading", true, true, "Stop"),
            new("game-downloading", true, true, "Cancel"),
            new("stopped", true, false, "Finish install"),
            new("error", true, false, "Finish install"),
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
            new("editor-applying", true, true, "Finishing update"),
            new("editor-recovery", true, true, "Recover Editor"),
            new("editor-offline", true, true, "Open Editor"),
            new("editor-error", true, true, "Open Editor"),
            new("editor-launcher-restart", true, true, "Open Editor"),
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

        EditorSizingChecks(output);
        LifecycleChecks();
        Inspect(() => Check(_networkAttempts == 0, "No dialog HTTP requests were attempted."));
        Console.WriteLine($"Launcher smoke: {_checks} checks passed; {_failures} failed cases; {scenarios.Length * 6 + 30} offscreen captures in {output}.");
        Console.WriteLine("Fixtures used temporary installs and offline startup; live update checks, downloads, game/editor launch, browser, clipboard and protocol registration were not invoked.");
        return _failures == 0 ? 0 : 1;
    }

    private static Form Create(Fixture fixture, Scenario scenario)
    {
        var form = (Form)Activator.CreateInstance(MainType, Hidden, null,
            new object?[] { fixture.Layout, scenario.Name == "join" ? "smoke-session" : null, null, fixture.LauncherPath }, null)!;
        if (!_reportedDpi) Console.WriteLine($"Before handle: DeviceDpi={form.DeviceDpi}, AutoScaleDimensions={form.AutoScaleDimensions}, CurrentAutoScaleDimensions={form.CurrentAutoScaleDimensions}, Font={form.Font.SizeInPoints}pt.");
        _ = form.Handle;
        if (!_reportedDpi)
        {
            Console.WriteLine($"After handle: DeviceDpi={form.DeviceDpi}, AutoScaleDimensions={form.AutoScaleDimensions}, CurrentAutoScaleDimensions={form.CurrentAutoScaleDimensions}, Font={form.Font.SizeInPoints}pt.");
            _reportedDpi = true;
        }
        ReleaseFeedSource source = scenario.Name == "offline" ? ReleaseFeedSource.Cache : ReleaseFeedSource.Network;
        SemVersion version = scenario.Name is "fresh" or "game-update" or "game-downloading" ? LatestVersion : InstalledVersion;
        var release = new ReleaseInfo
        {
            TagName = "v" + version,
            Name = "OpenSCCT " + version,
            PublishedAt = new DateTimeOffset(2026, 11, 3, 19, 30, 0, TimeSpan.Zero),
            Body = """
                ## Gameplay fixes

                - Fixed a movement-action queue issue after quick action sequences, keeping Spy stance transitions and interaction responsive.
                - Improved focus recovery after switching windows, with safer mouse capture when returning to play.
                - Simplified launcher setup around downloading OpenSCCT and ready-to-play content packs, with a clearer path to playing.
                - Additional changes are available in the full changelog.
                """,
        };
        var check = new UpdateCheck(new ReleaseFeed([release], source, null, scenario.Name == "offline" ? "Offline. Showing cached release information." : null),
            new VersionedRelease(version, release), scenario.Game ? InstalledVersion : null,
            scenario.Name is "fresh" or "game-update" or "game-downloading", false);
        if (scenario.Name != "fresh-no-feed") Call(form, "ShowReleaseCheck", check);
        if (scenario.Editor)
        {
            SemVersion editorLatest = scenario.Name == "editor-update" ? SemVersion.Parse("0.1.1") : EditorVersion;
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
        if (scenario.Name == "content-update")
        {
            Set(form, "_packIndex", new ContentIndex
            {
                Shared = [new ContentSharedPack { Id = "core", Title = "Core game files", Download = new("https://example.invalid/core.zip", 1024, "new-content") }],
            });
        }
        if (scenario.Name is "downloading" or "game-downloading" or "editor-busy" or "editor-busy-game" or "editor-applying")
        {
            fixture.Work = new CancellationTokenSource();
            Set(form, "_work", fixture.Work);
            Set(form, "_downloadingContent", scenario.Name == "downloading");
            Set(form, "_operationEditor", scenario.Editor);
            Set(form, "_operationCanCancel", scenario.Name != "editor-applying");
        }
        Call(form, "RefreshLocal");
        switch (scenario.Name)
        {
            case "downloading":
            case "game-downloading":
                Call(form, "SetOperation", scenario.Name == "downloading" ? "Downloading game files" : "Downloading OpenSCCT");
                SetStatus(form, scenario.Name == "downloading" ? "Downloading maps, characters and sounds…" : "Downloading OpenSCCT 0.8.81…", Color.FromArgb(127, 149, 133));
                Field<Control>(form, "_progress").Visible = true;
                Field<Control>(form, "_progress").GetType().GetProperty("Fraction")!.SetValue(Field<Control>(form, "_progress"), 0.42);
                Field<Label>(form, "_progressText").Text = scenario.Name == "downloading" ? "AquaE (4 of 12): 128.0 MB of 304.0 MB" : "128.0 MB of 304.0 MB  12.0 MB/s";
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
                SetStatus(form, "The connection was interrupted while downloading required game files. Press the button to try again; downloads carry on where they stopped.", Color.FromArgb(255, 110, 100));
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
        Check(scenario.Name == "editor-applying" ? !main.Enabled : main.Enabled && main.CanSelect,
            label + (scenario.Name == "editor-applying" ? " apply cannot be cancelled" : " primary action is keyboard selectable"));
        Check(help.Enabled && help.CanSelect, label + " Help stays keyboard selectable");
        Check(Field<Label>(form, "_heading").Text.Length > 0, label + " status heading is populated");
        if (scenario.Game && scenario.Name != "editor-recovery")
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
            Check(Field<Button>(form, "_gameTab").CanSelect && Field<Button>(form, "_editorTab").CanSelect, label + " both product tabs remain keyboard selectable");
        }
        if (scenario.Name is "downloading" or "game-downloading")
        {
            Check(Field<Label>(form, "_heading").Text == (scenario.Name == "downloading" ? "Downloading game files" : "Downloading OpenSCCT"), label + " operation phase is shown");
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
        if (scenario.Name == "fresh-no-feed")
            Check(Field<Label[]>(form, "_previewItems").Count(c => c.Visible && c.Text.Length > 0) == 1, label + " notes placeholder shown before first check");
        else
        {
            Check(Field<Label>(form, "_previewTitle").Text.Contains(VersionFor(scenario), StringComparison.Ordinal), label + " preview identifies its release");
            Check(Field<Label[]>(form, "_previewItems").Count(c => c.Visible && c.Text.Length > 0) == 3, label + " three preview entries shown");
        }
        Check(IsInClient(host, main), label + " primary action is visible inside client area");
        Check(IsInClient(host, help), label + " Help is visible inside client area");
        Check(host.SelectNextControl(null, true, true, true, false), label + " keyboard traversal enters launcher controls");
        if (scenario.Name is "game-update" or "content-update" or "editor-update")
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
            if (scenario.Editor)
            {
                Call(form, "QueueWebJoin", "smoke-session");
                Check(!Field<bool>(form, "_editorSelected") && Field<bool>(form, "_operationEditor"), label + " Join selects Game without changing editor operation owner");
                Check(Field<string>(form, "_pendingWebJoin") == "smoke-session" && !work.IsCancellationRequested, label + " Join queues without cancelling editor operation");
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

    private static SemVersion GameVersionFor(Scenario scenario) => scenario.Editor ? EditorGameVersion : InstalledVersion;
    private static string VersionFor(Scenario scenario) => scenario.Editor && scenario.Name != "editor-busy-game"
        ? scenario.Name == "editor-update" ? "0.1.1" : EditorVersion.ToString()
        : scenario.Name is "fresh" or "game-update" or "game-downloading" ? LatestVersion.ToString() : InstalledVersion.ToString();
    private static void CaptureDialogs(Fixture fixture, float scale, string output)
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
        var dialogs = new (string Type, object?[] Args, Size Minimum, bool Editor)[]
        {
            ("SettingsForm", [new LauncherSettings(), fixture.Layout, null], new Size(460, 360), false),
            ("MapsForm", [http, fixture.Layout.Packs, null, null], new Size(620, 460), false),
            ("HelpForm", [context, null, null, null, null], new Size(480, 420), false),
            ("SettingsForm", [new LauncherSettings { SelectedProduct = "Editor", EditorIncludePrereleases = true }, fixture.Layout, editorPalette], new Size(460, 360), true),
            ("MapsForm", [http, fixture.Layout.Packs, editorPalette, (Func<bool>)(() => true)], new Size(620, 460), true),
            ("HelpForm", [context, (Func<string, bool>)(_ => false), (Action<string>)(_ => { }), editorPalette, EditorVersion.ToString()], new Size(480, 420), true),
        };
        foreach (var dialog in dialogs)
        {
            Type type = assembly.GetType("OpenScct.Launcher.App." + dialog.Type, true)!;
            using Form form = (Form)Activator.CreateInstance(type, BindingFlags.Public | Hidden, null, dialog.Args, null)!;
            _ = form.Handle;
            if (scale != 1f) ScaleForPreview(form, fixture, scale);
            form.MinimumSize = Size.Empty;
            form.ClientSize = new Size((int)(dialog.Minimum.Width * scale), (int)(dialog.Minimum.Height * scale));
            string label = $"{(dialog.Editor ? "editor-" : "")}{dialog.Type}-minimum-{scale * 100:0}";
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
                if (dialog.Editor && dialog.Type == "MapsForm")
                {
                    type.GetMethod("RemoveSelected", Hidden)!.Invoke(form, null);
                    Check(Walk(host).OfType<Label>().Any(c => c.Text.Contains("Close OpenSCCT Editor", StringComparison.Ordinal)), label + " shared-content changes are guarded");
                }
                if (dialog.Editor && dialog.Type == "HelpForm")
                {
                    Button report = Walk(host).OfType<Button>().Single(c => c.Name == "BugReport");
                    typeof(Button).GetMethod("OnClick", Hidden)!.Invoke(report, [EventArgs.Empty]);
                    Check(Walk(host).OfType<TextBox>().Single(c => c.Name == "SelectedLink").Text.StartsWith("https://github.com/Lumbridge/OpenSCCT-Editor/issues/new", StringComparison.Ordinal), label + " editor reports route to editor repository");
                    Check(Walk(host).OfType<TextBox>().Single(c => c.Name == "ReportDetails").Text.Contains("Editor version: " + EditorVersion, StringComparison.Ordinal), label + " report details include editor version");
                }
            });
            foreach (Control control in controls) form.Controls.Add(control);
            host.Hide();
            if (dialog.Editor && dialog.Type == "SettingsForm")
            {
                type.GetMethod("Save", Hidden)!.Invoke(form, null);
                var saved = (LauncherSettings)type.GetProperty("Result")!.GetValue(form)!;
                Inspect(() => Check(saved.SelectedProduct == "Editor" && saved.EditorIncludePrereleases && !saved.IncludePrereleases,
                    label + " saving preserves product and independent editor preview preference"));
            }
        }
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

    private static void LifecycleChecks()
    {
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
            new LauncherSettings { SelectedProduct = "Editor", CheckOnStart = false }.Save(fixture.Layout.Settings);
            using Form form = (Form)Activator.CreateInstance(MainType, Hidden, null,
                new object?[] { fixture.Layout, null, null, fixture.LauncherPath }, null)!;
            _ = form.Handle;
            Call(form, "OnShown", EventArgs.Empty);
            Check(Field<bool>(form, "_editorSelected") && Field<bool>(form, "_webJoinReady"), "Actual startup restores Editor and enables Join handling");
            Check(Field<object?>(form, "_work") is null && Field<EditorCheck?>(form, "_editorCheck") is null, "CheckOnStart=false leaves startup offline");
            Call(form, "SelectProduct", false);
            Call(form, "SelectProduct", true);
            Check(Field<object?>(form, "_work") is null && Field<EditorCheck?>(form, "_editorCheck") is null, "Changing tabs respects disabled automatic checks");
        });
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
                CheckPreviewFits(form, label + " Game after both cached feeds arrive");
                Call(form, "SelectProduct", true);
                Settle(form);
                Check(form.ClientSize.Height == commonHeight && form.ClientSize.Width == commonWidth, label + " switching to Editor keeps common window dimensions");
                CheckPreviewFits(form, label + " Editor");
                Check(Field<Label[]>(form, "_previewItems").Count(c => c.Visible && c.Text.Length > 0) == 3, label + " Editor shows all three public release highlights");
                Check(Field<Label[]>(form, "_previewItems")[1].Text.Contains("installation root", StringComparison.Ordinal), label + " actual public editor changelog is used");
                Save(form, output, label + "-default");

                int screenMaximum = Screen.FromControl(form).WorkingArea.Height - (form.Height - form.ClientSize.Height);

                // A programmatic undersized window must grow to fit both panes when there is room.
                // This fails the old gate for ready/absent Editor when no update is pending.
                Call(form, "OnResizeBegin", EventArgs.Empty);
                form.ClientSize = new Size(commonWidth, (int)(100 * scale));
                Settle(form);
                TableLayoutPanel outer = Field<TableLayoutPanel>(form, "_mainLayout");
                Check(outer.AutoScroll, label + " extremely short fixture exercises outer scrolling mode");
                int shortHeight = form.ClientSize.Height;
                Call(form, "OnResizeEnd", EventArgs.Empty);
                Call(form, "FitUpdateHeight");
                Settle(form);
                Check(form.ClientSize.Height > shortHeight, label + " grows from an undersized Editor window");
                CheckPreviewFits(form, label + " Editor after growth");
                int editorHeight = form.ClientSize.Height;
                Call(form, "FitUpdateHeight");
                Settle(form);
                Check(form.ClientSize.Height == editorHeight, label + " first fit accounts for outer scrollbar removal");
                Call(form, "SelectProduct", false);
                Settle(form);
                Check(form.ClientSize.Height == editorHeight, label + " switching back to Game does not resize");
                CheckPreviewFits(form, label + " Game after growth");
                Call(form, "FitUpdateHeight");
                Settle(form);
                Check(form.ClientSize.Height == editorHeight, label + " repeated fitting stays stable after scrollbars clear");
                Check(Field<object?>(form, "_work") is null, label + " sizing started no update operation");
                if (commonHeight + (int)(40 * scale) < screenMaximum)
                {
                    form.ClientSize = new Size(commonWidth, commonHeight + (int)(40 * scale));
                    Call(form, "FitUpdateHeight");
                    Settle(form);
                    Check(form.ClientSize.Height == commonHeight, label + " removes excess programmatic height instead of retaining a larger default");
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
                    Check(form.ClientSize.Height < heightBefore, label + (widthOnly ? " width-only resize" : " window move") + " does not pin stale automatic height");
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
    }
    private static T Field<T>(Form form, string name) => (T)MainType.GetField(name, Hidden)!.GetValue(form)!;
    private static void Set(Form form, string name, object? value) => MainType.GetField(name, Hidden)!.SetValue(form, value);
    private static void Call(Form form, string name, params object?[] args) => MainType.GetMethod(name, Hidden | BindingFlags.Public)!.Invoke(form, args);
    private static void SetStatus(Form form, string text, Color color) => Call(form, "SetStatus", text, color, null);
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
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openscct-launcher-smoke-" + Guid.NewGuid().ToString("N"));
        public LauncherLayout Layout { get; }
        public string LauncherPath { get; }
        public CancellationTokenSource? Work { get; set; }
        public List<Font> Fonts { get; } = [];

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
                Directory.CreateDirectory(Layout.Packs);
                var packs = new List<InstalledContent>();
                foreach (string id in new[] { "core", "chars", "audio" })
                    packs.Add(new InstalledContent { Id = "shared/" + id, Code = id, Version = "1", Sha256 = "installed-" + id });
                foreach (string map in new[] { "AquaE", "ClubE", "BankE" })
                    packs.Add(new InstalledContent { Id = "stock/" + map, Code = map, Version = "1", Sha256 = "installed-" + map });
                File.WriteAllText(Path.Combine(Layout.Packs, "installed.json"), JsonSerializer.Serialize(packs, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                if (scenario.Editor)
                {
                    Directory.CreateDirectory(Path.Combine(Layout.Packs, "shared"));
                    File.WriteAllText(Path.Combine(Layout.Packs, "shared", "classes.json"), "{}");
                    Directory.CreateDirectory(Path.Combine(Layout.Packs, "svmap", "AquaE"));
                    File.WriteAllText(Path.Combine(Layout.Packs, "svmap", "AquaE", "manifest.json"), "{}");
                }
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
