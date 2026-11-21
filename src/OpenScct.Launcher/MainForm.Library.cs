using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using OpenScct.Launcher.Unreal;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private enum LibraryPage { Games, Mods, Settings }
    private LibraryPage _page;
    private bool RetailReady => PrimaryIsVersus ? RetailVersus?.HasVersus == true : RetailCampaign?.CampaignExecutable is not null;
    private bool IsOriginalEdition => _selected is Product.Ps2 or Product.Retail;
    private readonly Button _gamesPage = Theme.Button("Games");
    private readonly Button _modsPage = Theme.Button("Mods");
    private readonly LibraryEntry _ps2Tab = new LibraryEntry("PS2", "PlayStation 2", "Original disc");
    private readonly LibraryEntry _retailTab = new LibraryEntry("PC", "PC / Steam", "Original Windows game");
    private readonly Button _retailSecondary = Theme.QuietButton("", onPanel: true);
    private readonly ComboBox _compactPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly Label _editionTitle = Theme.Wrapping(Theme.Label("", Theme.Title));
    private readonly TableLayoutPanel _details = Theme.Grid(Theme.Fill);
    private ProductStatus _ps2ActionStatus = new(string.Empty, StatusKind.Normal);
    private ProductStatus _retailActionStatus = new(string.Empty, StatusKind.Normal);
    private string? _detailsKey;
    private bool _compact;
    private LibraryBanner _banner = null!;
    private Control _launchCard = null!;
    private readonly Dictionary<Product, Bitmap> _artwork = new();
    private readonly Button _moreActions = Theme.Button("⋯");
    private readonly FlowLayoutPanel _milestones = Theme.Flow();
    private readonly Label[] _setupSteps = [Theme.Label(""), Theme.Label(""), Theme.Label("")];
    private static readonly Font CardHeading = new("Segoe UI Semibold", 14.5f);
    private readonly Label _sourceHeading = Theme.Wrapping(Theme.Label("", CardHeading));
    private readonly Label _sourceDetail = Theme.Wrapping(Theme.Label("", Theme.Body, Theme.Muted));
    private readonly Button _sourcePick = Theme.Button("");
    private Control _sourceCard = null!;
    private static readonly Product[] LibraryOrder = [Product.Ps2, Product.Retail, Product.Game, Product.Unreal, Product.Editor];

    private static string EditionName(Product product) => product switch
    {
        Product.Ps2 => "PlayStation 2",
        Product.Retail => "PC / Steam",
        Product.Unreal => "OpenSCCT Unreal",
        Product.Editor => "OpenSCCT Editor",
        _ => "OpenSCCT Godot",
    };

    private static string LibrarySection(Product product) => product switch
    {
        Product.Ps2 or Product.Retail => "Originals",
        Product.Game or Product.Unreal => "Modernisations",
        _ => "Tools",
    };

    private void BuildLibraryShell(Control launch, Control preview, FlowLayoutPanel tools)
    {
        _gamesPage.Name = "GamesNavigation";
        _modsPage.Name = "ModsNavigation";
        _settingsButton.Name = "SettingsNavigation";
        _gamesPage.Click += (_, _) => SelectPage(LibraryPage.Games);
        _modsPage.Click += (_, _) => SelectPage(LibraryPage.Mods);
        FlowLayoutPanel pages = Theme.Flow(false, _gamesPage, _modsPage, _settingsButton, _launcherButton);
        pages.Anchor = AnchorStyles.Left;
        foreach (Button button in pages.Controls) button.Margin = new Padding(0, 0, 6, 0);
        SetGlyph(_gamesPage, "games");
        SetGlyph(_modsPage, "mods");
        SetGlyph(_settingsButton, "settings");
        SetGlyph(_website, "website");
        SetGlyph(_help, "help");
        foreach (Button button in pages.Controls)
        {
            button.Padding = new Padding(10, 4, 10, 4);
            button.MinimumSize = new Size(100, 38);
            button.Anchor = AnchorStyles.Left;
            LibraryVisuals.Round(button, 7);
        }

        using Icon mark = Theme.LoadIcon(48);
        _brandLogo = new PictureBox
        {
            Image = mark.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(34, 34),
            Margin = new Padding(0, 0, 7, 0), Anchor = AnchorStyles.Left, TabStop = false,
        };
        var wordmark = new Wordmark { Margin = new Padding(0), Anchor = AnchorStyles.Left };
        var version = Theme.Label(_launcherVersion?.ToString() ?? "dev", Theme.Mono, Theme.Muted);
        version.Margin = new Padding(9, 0, 17, 0);
        version.Anchor = AnchorStyles.Left;
        var brand = Theme.Grid(Theme.Auto, Theme.Auto, Theme.Auto);
        Theme.AddRow(brand, _brandLogo, wordmark, version);
        brand.Anchor = AnchorStyles.Left;
        FlowLayoutPanel links = Theme.Flow(false, _website, _help);
        links.Anchor = AnchorStyles.Right;
        var header = Theme.Grid(Theme.Auto, Theme.Fill, Theme.Auto);
        Theme.AddRow(header, brand, pages, links);
        header.Padding = new Padding(0, 0, 0, 14);

        var rail = Theme.Grid(Theme.Fill);
        rail.Name = "EditionLibrary";
        rail.Padding = new Padding(0, 0, 18, 0);
        var railButtons = new[] { _ps2Tab, _retailTab, _gameTab, _unrealTab, _editorTab };
        for (int index = 0; index < LibraryOrder.Length; index++)
        {
            Product product = LibraryOrder[index];
            if (product is Product.Ps2 or Product.Game or Product.Editor)
            {
                var section = Theme.Label(LibrarySection(product), Theme.Heading, Theme.Muted);
                section.UseMnemonic = false;
                section.Margin = new Padding(6, index == 0 ? 4 : 18, 0, 9);
                Theme.AddRow(rail, section);
            }
            Button button = railButtons[index];
            string asset = product switch { Product.Ps2 => "ps2", Product.Retail => "pc", Product.Game => "godot", Product.Unreal => "unreal", _ => "editor" };
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream($"OpenScct.Launcher.library-{asset}.jpg")
                ?? throw new InvalidOperationException($"Missing {asset} library artwork.");
            using var original = new Bitmap(stream);
            _artwork[product] = new Bitmap(original);
            ((LibraryEntry)button).Artwork = _artwork[product];
            button.Name = product + "Tab";
            button.AccessibleName = EditionName(product);
            button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            button.Click += (_, _) => SelectProduct(product);
            Theme.AddRow(rail, button);
            _compactPicker.Items.Add(LibrarySection(product) + "  /  " + EditionName(product));
        }

        _compactPicker.Name = "EditionSelector";
        _compactPicker.AccessibleName = "Game or tool";
        _compactPicker.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _compactPicker.BackColor = Theme.Panel;
        _compactPicker.ForeColor = Theme.Text;
        _compactPicker.Margin = new Padding(0, 0, 0, 8);
        _compactPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_compactPicker.SelectedIndex >= 0 && LibraryOrder[_compactPicker.SelectedIndex] != _selected)
                SelectProduct(LibraryOrder[_compactPicker.SelectedIndex]);
        };

        _sourceCard = BuildSourceCard();
        _launchCard = launch;
        launch.Margin = new Padding(0, 0, 0, 12);
        _details.Name = "EditionDetails";
        _details.Margin = new Padding(0);
        preview.Margin = new Padding(0, 0, 0, 10);
        preview.Dock = DockStyle.Top;
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, RowCount = 0, Margin = new Padding(0),
            BackColor = Color.Transparent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        content.ColumnStyles.Add(Theme.Fill);
        foreach (Control row in new Control[] { _compactPicker, _sourceCard, launch, _details, tools, preview })
        {
            int r = content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(row, 0, r);
        }
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowCount++;
        var detailPane = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), AutoScroll = true };
        detailPane.Controls.Add(content);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        body.ColumnStyles.Add(Theme.Fill);
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(rail, 0, 0);
        body.Controls.Add(detailPane, 1, 0);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        shell.ColumnStyles.Add(Theme.Fill);
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(body, 0, 1);
        _mainLayout = content;
        void AdaptWidth()
        {
            if (Disposing || IsDisposed || shell.Disposing) return;
            float scale = Math.Max(DeviceDpi / 96f, _gamesPage.Font.SizeInPoints / Theme.Body.SizeInPoints);
            bool compact = shell.ClientSize.Width < 800 * scale;
            _compact = compact;
            rail.Visible = !compact;
            _compactPicker.Visible = compact;
            body.ColumnStyles[0].Width = compact ? 0 : (int)Math.Ceiling(Math.Clamp(shell.ClientSize.Width * .21f, 218 * scale, 298 * scale));
            brand.Visible = !compact;
            links.Visible = !compact;
            version.Visible = !compact;
            _brandLogo.Visible = !compact;
            _website.Visible = !compact;
            _banner.Compact = compact;
            _editionTitle.Font = compact ? Theme.Heading : LibraryBanner.TitleFont;
            _sourceCard.Visible = !compact && _page == LibraryPage.Games;
            _milestones.Visible = !compact && _page == LibraryPage.Games;
            _moreActions.Visible = true;
        }
        shell.ClientSizeChanged += (_, _) => AdaptWidth();
        Controls.Add(shell);
        AdaptWidth();
        RefreshLocal();
    }

    private void SelectPage(LibraryPage page)
    {
        _page = page;
        _detailsKey = null;
        RefreshLocal();
    }

    private void RefreshOriginalPanel(bool busy)
    {
        bool ps2 = _selected == Product.Ps2;
        _heading.Text = busy ? VisibleOperationTitle
            : (ps2 ? Ps2.NextAction == OpenScct.Launcher.Ps2.Ps2Action.Play : RetailReady) ? "Ready to play"
            : ps2 ? "Disc setup" : "Game installation";
        _installedInfo.Text = ps2 ? Ps2Status : RetailStatus;
        _launchDetail.Text = ps2 ? "Play the original campaign, co-op and Versus through PCSX2."
            : "Campaign, co-op and Versus use your own installation.";
        _launchDetail.Visible = !busy && VisibleStatus.Kind is not (StatusKind.Warning or StatusKind.Error);
        _main.Text = busy ? (_operationCanCancel ? "Cancel" : "Please wait") : ps2 ? Ps2ActionText : RetailActionText;
        _main.Enabled = !busy || _operationCanCancel;
        Theme.SetButtonEmphasis(_main, !busy, Palette);
        _tips.SetToolTip(_main, null);
        _playCurrent.Visible = false;
        _retailSecondary.Text = ps2 ? "" : RetailSecondaryActionText;
        _retailSecondary.Visible = !busy && _retailSecondary.Text.Length > 0;
        _check.Text = "Check launcher updates";
    }

    private void UpdateLibraryState(bool busy)
    {
        if (_mainLayout is null) return;
        _compactPicker.SelectedIndex = Array.IndexOf(LibraryOrder, _selected);
        bool games = _page == LibraryPage.Games;
        RefreshLibraryBadges();
        _editionTitle.Text = _selected is Product.Ps2 or Product.Retail ? "Splinter Cell: Chaos Theory" : EditionName(_selected);
        _banner.Artwork = _artwork[_selected];
        _sourceCard.Visible = games && !_compact;
        _milestones.Visible = games && !_compact;
        _moreActions.Enabled = !busy;
        _heading.Visible = !IsOriginalEdition || busy;
        _installedInfo.Visible = !IsOriginalEdition || busy;
        _sourcePick.Enabled = !busy;
        RefreshSourceAndSteps();
        Theme.SetButtonEmphasis(_gamesPage, games, Palette);
        Theme.SetButtonEmphasis(_modsPage, _page == LibraryPage.Mods, Palette);
        Theme.SetButtonEmphasis(_settingsButton, _page == LibraryPage.Settings, Palette);
        _launchCard.Visible = games || busy;
        _releasePreview!.Visible = games && !IsOriginalEdition && SelectedRelease != null;
        _releasePreview.Height = Math.Max(LogicalToDeviceUnits(76), _releaseNotes!.GetPreferredSize(
            new Size(Math.Max(300, _mainLayout.ClientSize.Width - _releasePreview.Padding.Horizontal), 0)).Height + _releasePreview.Padding.Vertical);
        _mapsButton.Visible = games && !IsOriginalEdition;
        _check.Visible = games;
        _check.Text = IsOriginalEdition ? "Check launcher updates" : "Check for updates";
        _settingsButton.Enabled = !busy;
        _modsPage.Enabled = !busy;
        _details.Enabled = !busy;
        _tools!.Visible = false;
        string key = _selected + "/" + _page + "/" + (_selected switch
        {
            Product.Ps2 => Ps2Status + "/" + Ps2.DiscPath + "/" + Ps2.EmulatorPath + "/" + Ps2.ModPath,
            Product.Retail => _page == LibraryPage.Mods ? "" : RetailStatus + "/" + _settings.Retail.InstallFolder,
            Product.Unreal => _settings.UnrealMapModRoot + "/" + _settings.UnrealMapModsEnabled + "/" + _unreal.ActiveContentRoot(),
            Product.Game or Product.Editor => _steamSource.Value + "/" + _steamContentRoot + "/" + _store.Current(),
            _ => "",
        });
        if (key == _detailsKey) return;
        _detailsKey = key;
        _details.SuspendLayout();
        while (_details.Controls.Count > 0) _details.Controls[0].Dispose();
        _details.RowCount = 0;
        _details.RowStyles.Clear();
        Control? contents = _page switch
        {
            LibraryPage.Settings => BuildEditionSettings(),
            LibraryPage.Mods => _selected switch
            {
                Product.Ps2 => BuildPs2Mods(),
                Product.Retail => BuildRetailMods(),
                Product.Unreal => BuildUnrealMods(),
                _ => LibraryCard("Additional maps", "The original map set is built from Steam. Download other maps individually.",
                    ActionButton("Browse maps", OpenMaps)),
            },
            _ => BuildGameDetails(),
        };
        if (contents is not null)
        {
            if (!games)
            {
                var pageTitle = Theme.Wrapping(Theme.Label(_page + "  /  " + EditionName(_selected), CardHeading));
                pageTitle.Margin = new Padding(0, 8, 0, 13);
                Theme.AddRow(_details, pageTitle);
            }
            contents.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            contents.Margin = new Padding(0, 0, 0, 10);
            Theme.AddRow(_details, contents);
            Theme.Apply(_details, Palette);
        }
        if (!games && contents is { BackColor.A: 0 })
        {
            contents.BackColor = Palette.Panel;
            contents.Padding = new Padding(18, 16, 18, 16);
        }
        _details.Visible = contents is not null;
        _details.ResumeLayout(true);
    }

    private void RefreshLibraryBadges()
    {
        bool discReady = Ps2.NextAction == OpenScct.Launcher.Ps2.Ps2Action.Play;
        _ps2Tab.SetStatus(discReady ? "Ready to play" : Ps2Status, discReady);
        bool retailReady = RetailReady;
        _retailTab.SetStatus(retailReady ? (_settings.Retail.UseMod ? "Modded game ready" : "Ready to play") : "Setup needed", retailReady);
        var unreal = _unreal.Installed();
        bool unrealReady = UnrealService.SupportsSteamContent(unreal?.Version) && _unreal.PrerequisiteProblem() is null;
        _unrealTab.SetStatus(unreal is null ? "Not installed" : unrealReady ? "Ready to play" : "Setup needed", unrealReady);
        var game = _store.Current();
        bool gameReady = Content.GodotSteamContent.SupportsClient(game) && _steamContentRoot is not null;
        _gameTab.SetStatus(game is null ? "Not installed" : !Content.GodotSteamContent.SupportsClient(game) ? "Update needed"
            : gameReady ? "Ready to play" : "Setup needed", gameReady);
        bool hasEditor = _editor.Installed() is not null;
        bool editorReady = hasEditor && _editor.PrerequisiteProblem() is null && !_editor.NeedsRecovery;
        _editorTab.SetStatus(!hasEditor ? "Not installed" : editorReady ? "Ready to create" : "Setup needed", editorReady);
    }

    private Control BuildEditionSettings()
    {
        if (!IsOriginalEdition)
        {
            Button rebuild = ActionButton("Rebuild game files", () =>
            {
                SelectPage(LibraryPage.Games);
                if (_selected == Product.Unreal) _ = RunAsync(cancel => SetUpUnrealContentAsync(cancel, true));
                else _ = RunAsync(BuildSteamContentAsync);
            });
            rebuild.Enabled = _selected == Product.Unreal
                ? UnrealService.SupportsSteamContent(_unreal.Installed()?.Version) && !_unreal.IsRunning
                : Content.GodotSteamContent.SupportsClient(_store.Current()) && _steamSource.Value is not null;
            return LibraryCard("Steam installation", _steamSource.Value ?? "Choose the folder where Steam installed Chaos Theory.",
                ActionButton("Choose Steam folder", ChooseSteamSource), rebuild, ActionButton("Open settings", OpenSettings));
        }

        var grid = Theme.Grid(Theme.Fill);
        Control setup = _selected == Product.Ps2 ? BuildPs2Setup() : LibraryCard("Retail installation", RetailBase?.Folder ?? RetailStatus,
            ActionButton("Choose game folder", ChooseRetailFolder));
        var updates = new CheckBox
        {
            Text = "Check for launcher updates when opening an original game",
            Checked = _settings.OriginalCheckOnStart, AutoSize = true,
            ForeColor = Theme.Text, Margin = new Padding(0, 10, 0, 0),
        };
        updates.Click += (_, _) =>
        {
            bool previous = _settings.OriginalCheckOnStart;
            _settings.OriginalCheckOnStart = updates.Checked;
            try { _settings.Save(_layout.Settings); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                updates.Checked = _settings.OriginalCheckOnStart = previous;
                MessageBox.Show(this, error.Message, "Saving preferences", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        Theme.AddRow(grid, setup);
        Theme.AddRow(grid, updates);
        return grid;
    }

    private Control BuildUnrealMods()
    {
        string? folder = _settings.UnrealMapModRoot;
        var choose = ActionButton("Choose maps folder", () =>
        {
            using var picker = new FolderBrowserDialog { Description = "Choose a folder containing compiled SVMap map folders", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            if (!UnrealMapMods.TryGetMapRoot(picker.SelectedPath, out string root, out string error))
            {
                MessageBox.Show(this, error, "Unreal maps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SaveUnrealMod(root, true);
        });
        var toggle = ActionButton(_settings.UnrealMapModsEnabled ? "Disable maps" : "Enable maps", () =>
            SaveUnrealMod(folder, !_settings.UnrealMapModsEnabled));
        toggle.Enabled = !string.IsNullOrEmpty(folder);
        return LibraryCard("Custom maps", folder is null ? "Choose a collection of compiled SVMap folders to load when Unreal starts."
            : (_settings.UnrealMapModsEnabled ? "Enabled · " : "Disabled · ") + folder, ActionButton("Browse maps", OpenMaps), choose, toggle);
    }

    private void SaveUnrealMod(string? root, bool enabled)
    {
        var oldRoot = _settings.UnrealMapModRoot;
        bool oldEnabled = _settings.UnrealMapModsEnabled;
        try
        {
            _settings.UnrealMapModRoot = root;
            _settings.UnrealMapModsEnabled = enabled;
            _settings.Save(_layout.Settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.UnrealMapModRoot = oldRoot;
            _settings.UnrealMapModsEnabled = oldEnabled;
            MessageBox.Show(this, error.Message, "Saving map preferences", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshLocal();
    }

    private static Button ActionButton(string title, Action action)
    {
        var button = Theme.Button(title);
        button.Click += (_, _) => action();
        return button;
    }

    private static Control LibraryCard(string title, string description, params Button[] buttons)
    {
        var card = Theme.Grid(Theme.Fill);
        card.BackColor = Theme.Panel;
        card.Padding = new Padding(18, 16, 18, 16);
        var heading = Theme.Wrapping(Theme.Label(title, CardHeading));
        heading.UseMnemonic = false;
        heading.Margin = new Padding(0, 0, 12, 2);
        var text = Theme.Wrapping(Theme.Label(description, Theme.Body, Theme.Muted));
        text.UseMnemonic = false;
        text.Margin = new Padding(0, 6, 0, 0);
        Theme.AddRow(card, heading);
        Theme.AddRow(card, text);
        foreach (Button button in buttons)
        {
            button.Anchor = AnchorStyles.Left;
            button.Margin = new Padding(0, 8, 0, 0);
            Theme.AddRow(card, button);
        }
        LibraryVisuals.Round(card, 10);
        return card;
    }
}

/// <summary>A keyboard-accessible library row with edition artwork and local setup status.</summary>
internal sealed class LibraryEntry : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Image? Artwork { get; set; }
    private string _detail;
    private bool _ready;
    public void SetStatus(string status, bool ready)
    {
        if (_detail == status && _ready == ready) return;
        _detail = status;
        _ready = ready;
        AccessibleDescription = status;
        Invalidate();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ThemePalette Palette { get; set; } = ThemePalette.Game;

    public LibraryEntry(string monogram, string name, string detail)
    {
        _detail = detail;
        Text = name;
        AccessibleName = name;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(196, 70);
        Margin = new Padding(0, 0, 0, 4);
        Font = Theme.Body;
    }

    public override Size GetPreferredSize(Size proposedSize) => Size;

    protected override void OnPaint(PaintEventArgs e)
    {
        Color palette = Palette.Accent;
        bool selected = BackColor == palette;
        bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
        Color surface = selected ? Color.FromArgb(30, 55, 38) : hover ? Theme.Panel : Theme.Background;
        e.Graphics.Clear(Palette.Background);
        using var background = new SolidBrush(surface);
        using var selection = new GraphicsPath();
        selection.AddRoundedRectangle(ClientRectangle, new Size(8, 8));
        e.Graphics.FillPath(background, selection);
        int diameter = Math.Min(Height - LogicalToDeviceUnits(10), LogicalToDeviceUnits(52));
        int x = LogicalToDeviceUnits(9);
        var badge = new Rectangle(x, (Height - diameter) / 2, diameter, diameter);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Artwork is { } art)
        {
            var clip = e.Graphics.Save();
            using var shape = new GraphicsPath();
            shape.AddEllipse(badge);
            e.Graphics.SetClip(shape);
            int side = Math.Min(art.Width, art.Height);
            int cropX = (int)Math.Clamp(art.Width * .62f - side / 2f, 0, art.Width - side);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(art, badge, new Rectangle(cropX, (art.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            e.Graphics.Restore(clip);
        }
        using var border = new Pen(selected ? palette : Theme.Border);
        e.Graphics.DrawEllipse(border, badge);
        x = badge.Right + LogicalToDeviceUnits(9);
        var name = new Rectangle(x, LogicalToDeviceUnits(7), Width - x - 3, Height / 2 - LogicalToDeviceUnits(3));
        var detail = new Rectangle(x, Height / 2, name.Width, Height / 2 - LogicalToDeviceUnits(5));
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(e.Graphics, Text, Theme.Heading, name, selected ? palette : Palette.Text, flags);
        int dot = LogicalToDeviceUnits(6);
        using var statusBrush = new SolidBrush(_ready ? Color.FromArgb(140, 221, 152) : Palette.Muted);
        e.Graphics.FillEllipse(statusBrush, detail.X + 2, detail.Y + (detail.Height - dot) / 2, dot, dot);
        detail.X += dot + LogicalToDeviceUnits(5);
        detail.Width -= dot + LogicalToDeviceUnits(5);
        TextRenderer.DrawText(e.Graphics, _detail, Theme.Small, detail, Palette.Muted, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Theme.Text, surface);
    }
}

internal sealed class LibraryBanner : TableLayoutPanel
{
    internal static readonly Font TitleFont = new("Segoe UI", 25f, FontStyle.Bold);
    private Image? _image;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Image? Artwork
    {
        set { if (_image != value) { _image = value; Invalidate(true); } }
    }

    public LibraryBanner()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ColumnCount = 2;
        ColumnStyles.Add(Theme.Fill);
        ColumnStyles.Add(Theme.Auto);
        Margin = new Padding(0, 0, 0, 12);
        BackColor = Theme.Panel;
        LibraryVisuals.Round(this, 18);
        Compact = false;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Compact
    {
        set
        {
            Padding = value ? new Padding(12, 8, 12, 8) : new Padding(24, 23, 24, 20);
            MinimumSize = value ? Size.Empty : new Size(0, LogicalToDeviceUnits(245));
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 1 || Height < 1 || _image is null) return;
        float scale = Math.Max((float)Width / _image.Width, (float)Height / _image.Height);
        float imageHeight = _image.Height * scale;
        e.Graphics.DrawImage(_image, (Width - _image.Width * scale) / 2, (Height - imageHeight) * .43f, _image.Width * scale, imageHeight);
        using var tint = new LinearGradientBrush(ClientRectangle, Color.FromArgb(229, 10, 17, 14), Color.FromArgb(45, 10, 17, 14), 0f);
        e.Graphics.FillRectangle(tint, ClientRectangle);
        using var shade = new LinearGradientBrush(ClientRectangle, Color.Transparent, Color.FromArgb(170, 8, 12, 10), 90f);
        e.Graphics.FillRectangle(shade, ClientRectangle);
    }
}
