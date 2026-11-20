using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Unreal;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private static void SetGlyph(Button button, string glyph)
    {
        void Render()
        {
            Image? previous = button.Image;
            button.Image = LibraryVisuals.Icon(glyph, button.ForeColor, button.LogicalToDeviceUnits(19));
            previous?.Dispose();
        }
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.ForeColorChanged += (_, _) => Render();
        button.DpiChangedAfterParent += (_, _) => Render();
        button.Disposed += (_, _) => button.Image?.Dispose();
        Render();
    }

    private Control BuildSourceCard()
    {
        var card = Theme.Grid(Theme.Auto, Theme.Fill, Theme.Auto);
        card.Name = "EditionSource";
        card.BackColor = Theme.Panel;
        card.Padding = new Padding(17, 13, 15, 13);
        card.Margin = new Padding(0, 0, 0, 12);
        var icon = new PictureBox { Image = LibraryVisuals.Icon("folder", Theme.Text, 30),
            Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
        icon.Disposed += (_, _) => icon.Image?.Dispose();
        icon.Anchor = AnchorStyles.Left;
        icon.Margin = new Padding(0, 0, 13, 0);
        var labels = Theme.Grid(Theme.Fill);
        _sourceHeading.UseMnemonic = _sourceDetail.UseMnemonic = false;
        _sourceHeading.Margin = new Padding(0, 0, 0, 4);
        labels.Margin = new Padding(0, 0, 12, 0);
        Theme.AddRow(labels, _sourceHeading);
        Theme.AddRow(labels, _sourceDetail);
        _sourcePick.Name = "ChooseEditionSource";
        _sourcePick.Anchor = AnchorStyles.Right;
        _sourcePick.Click += (_, _) =>
        {
            if (_selected == Product.Ps2) ChooseDiscFolder();
            else if (_selected == Product.Retail) ChooseRetailFolder();
            else ChooseSteamSource();
        };
        Theme.AddRow(card, icon, labels, _sourcePick);
        LibraryVisuals.Round(card, 10);
        return card;
    }

    private void RefreshSourceAndSteps()
    {
        bool steam = _steamSource.Value is not null;
        string? steamName = steam ? Path.GetFileName(Path.GetDirectoryName(_steamSource.Value)) : null;
        (string Heading, string Detail, string Button) source;
        (string Label, bool Done)[] steps;
        if (_selected == Product.Ps2)
        {
            bool disc = File.Exists(Ps2.DiscPath);
            bool emulator = File.Exists(Ps2.EmulatorPath);
            source = disc ? ("PlayStation 2 disc library", Path.GetDirectoryName(Ps2.DiscPath)!, "Change folder")
                : ("Where are your PlayStation 2 games?", "Choose the folder containing your disc images. OpenSCCT looks for your Chaos Theory ISO there.", "Choose folder");
            steps = [("Disc selected", disc), ("PCSX2 set up", emulator), ("Ready to play", Ps2.NextAction == OpenScct.Launcher.Ps2.Ps2Action.Play)];
        }
        else if (_selected == Product.Retail)
        {
            var game = RetailBase;
            source = game is null ? ("Where is your PC game installed?", "Choose the Chaos Theory game folder, or use the installation detected in Steam.", "Choose folder")
                : ("Original PC installation", game.Folder, "Change folder");
            steps = [("Game located", game is not null), ("Campaign & co-op", RetailCampaign?.CampaignExecutable is not null),
                ("Versus", RetailVersus?.HasVersus == true)];
        }
        else
        {
            source = steam ? ("Steam installation located", steamName ?? _steamSource.Value!, "Change folder")
                : ("Where is your Steam copy of Chaos Theory?", "Choose your Steam game folder to build this edition's maps and assets locally.", "Choose folder");
            var editor = _selected == Product.Editor;
            var unreal = _selected == Product.Unreal;
            bool client = unreal ? UnrealService.SupportsSteamContent(_unreal.Installed()?.Version)
                : editor ? _editor.Installed() is not null : GodotSteamContent.SupportsClient(_store.Current());
            bool assets = unreal ? _unreal.Installed() is not null && _unreal.PrerequisiteProblem() is null : _steamContentRoot is not null;
            steps = [("Steam source", steam), (editor ? "Editor installed" : "Game installed", client),
                (unreal ? "Unreal assets built" : "Steam maps built", assets)];
        }
        (_sourceHeading.Text, _sourceDetail.Text, _sourcePick.Text) = source;
        for (int i = 0; i < _setupSteps.Length; i++)
        {
            var (label, done) = steps[i];
            var control = _setupSteps[i];
            control.Margin = new Padding(0, 2, 18, 2);
            control.Text = (done ? "✓  " : "○  ") + label;
            control.UseMnemonic = false;
            control.Font = Theme.Heading;
            control.ForeColor = done ? Palette.Accent : Color.FromArgb(211, 218, 212);
            control.AccessibleName = label + (done ? ", complete" : ", pending");
        }
    }

    private void OpenEditionActions()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Manage mods", null, (_, _) => SelectPage(LibraryPage.Mods));
        menu.Items.Add("Edition settings", null, (_, _) => SelectPage(LibraryPage.Settings));
        if (!IsOriginalEdition)
        {
            menu.Items.Add("Browse maps", null, (_, _) => OpenMaps());
            menu.Items.Add("Release notes", null, async (_, _) => await OpenChangelogAsync());
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(IsOriginalEdition ? "Check launcher updates" : "Check for updates", null,
            async (_, _) => await RunAsync(CheckSelectedAsync));
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(_moreActions, new Point(0, _moreActions.Height));
    }

    private Control BuildGameDetails()
    {
        var sections = Theme.Grid(Theme.Fill);
        var mods = ActionButton("Manage mods", () => SelectPage(LibraryPage.Mods));
        var preferences = ActionButton("Settings", () => SelectPage(LibraryPage.Settings));
        void Add(string title, string body, params Button[] actions)
        {
            var card = LibraryCard(title, body, actions);
            card.Margin = new Padding(0, 0, 0, 10);
            Theme.AddRow(sections, card);
        }
        Button Folder(string path, string name = "Open folder") => ActionButton(name, () => ShowInExplorer(path));
        switch (_selected)
        {
            case Product.Ps2:
                string? discDir = Path.GetDirectoryName(Ps2.DiscPath);
                Add("Disc and memory cards", "PCSX2 manages your memory cards, save states and controller settings.",
                    Directory.Exists(discDir) ? [Folder(discDir, "Open disc folder"), preferences] : [preferences]);
                Add("Mods for PlayStation 2", Ps2.ModPath is null ? "Select a separately patched ISO to play a modded copy of your disc." : Path.GetFileName(Ps2.ModPath), mods);
                break;
            case Product.Retail:
                var pc = RetailBase;
                Add("Your PC game", pc is null ? "The original Windows game includes campaign, co-op and Spies versus Mercenaries." : pc.Folder,
                    pc is null ? [preferences] : [Folder(pc.Folder), preferences]);
                Add("Mods for Versus", _settings.Retail.UseMod ? "Your separate modded copy is selected." : "Enhanced and Reloaded change Versus. Keep your original installation alongside the modded game.", mods);
                break;
            default:
                string? path = _selected switch
                {
                    Product.Unreal => _unreal.Installed()?.Folder,
                    Product.Editor => Directory.Exists(_layout.Editor) ? _layout.Editor : null,
                    _ => _store.Current() is { } current ? _layout.VersionDir(current.ToString()) : null,
                };
                string title = _selected == Product.Editor ? "Map projects" : "Maps and mods";
                string body = _selected == Product.Editor ? "Create maps with local Steam assets and browse additional maps."
                    : "Build the stock Versus maps from Steam. Browse additional maps and choose which ones to install.";
                Add(title, body, ActionButton("Browse maps", OpenMaps), mods);
                string description = _selected switch
                {
                    Product.Editor => "Set the Steam source and manage your installed map editor.",
                    Product.Unreal => "OpenSCCT Unreal rebuilds Versus in Unreal Engine with new graphics and local Steam assets.",
                    _ => "OpenSCCT Godot rebuilds Versus in Godot with modern rendering, networking and controls.",
                };
                Add(_selected == Product.Editor ? "Editor installation" : "Changes from the original", description,
                    Directory.Exists(path) ? [Folder(path), preferences] : [preferences]);
                break;
        }
        return sections;
    }
}

