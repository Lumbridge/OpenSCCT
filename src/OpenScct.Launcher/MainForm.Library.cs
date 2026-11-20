using System;
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
    private Bitmap? _heroImage;
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
        FlowLayoutPanel pages = Theme.Flow(false, _gamesPage, _modsPage, _settingsButton);
        pages.Anchor = AnchorStyles.Left;
        foreach (Button button in pages.Controls) button.Margin = new Padding(0, 0, 5, 0);

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
        rail.Padding = new Padding(0, 0, 14, 0);
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

        _brandSubtitle = Theme.Wrapping(Theme.Label("", Theme.Mono, Theme.Accent));
        _brandSubtitle.Margin = new Padding(0, 6, 0, 0);
        _editionTitle.Margin = new Padding(0);
        using Stream image = typeof(MainForm).Assembly.GetManifestResourceStream("OpenScct.Launcher.operation.jpg")!;
        using var decoded = new Bitmap(image);
        _heroImage = new Bitmap(decoded);
        var hero = _banner = new LibraryBanner(_heroImage) { Name = "EditionBanner" };
        Theme.AddRow(hero, _editionTitle);
        Theme.AddRow(hero, _brandSubtitle);
        _launchCard = launch;
        launch.Margin = new Padding(0, 0, 0, 12);
        _details.Name = "EditionDetails";
        _details.Margin = new Padding(0);
        preview.Margin = new Padding(0, 0, 0, 0);
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0, Margin = new Padding(0),
            BackColor = Color.Transparent, AutoScroll = true,
        };
        content.ColumnStyles.Add(Theme.Fill);
        foreach (Control row in new Control[] { _compactPicker, _launcherBanner, hero, launch, _details, preview })
        {
            int r = content.RowCount++;
            content.RowStyles.Add(new RowStyle(row == preview ? SizeType.Percent : SizeType.AutoSize, row == preview ? 100 : 0));
            content.Controls.Add(row, 0, r);
        }
        var detailPane = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        detailPane.Controls.Add(content);
        detailPane.Controls.Add(tools);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226));
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
            body.ColumnStyles[0].Width = compact ? 0 : (int)Math.Ceiling(226 * scale);
            version.Visible = !compact;
            _brandLogo.Visible = !compact;
            _website.Visible = !compact;
            hero.Compact = compact;
            hero.Visible = !compact || _page != LibraryPage.Games;
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
        _launchDetail.Text = ps2 ? "Play from your Chaos Theory disc image with PCSX2."
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
        _banner.Visible = !_compact || !games;
        RefreshLibraryBadges();
        _editionTitle.Text = games ? EditionName(_selected) : _page + " · " + EditionName(_selected);
        Theme.SetButtonEmphasis(_gamesPage, games, Palette);
        Theme.SetButtonEmphasis(_modsPage, _page == LibraryPage.Mods, Palette);
        Theme.SetButtonEmphasis(_settingsButton, _page == LibraryPage.Settings, Palette);
        _launchCard.Visible = games || busy;
        _releasePreview!.Visible = games && !IsOriginalEdition;
        _mapsButton.Visible = games && !IsOriginalEdition;
        _check.Visible = games;
        _check.Text = IsOriginalEdition ? "Check launcher updates" : "Check for updates";
        _settingsButton.Enabled = !busy;
        _modsPage.Enabled = !busy;
        _details.Enabled = !busy;
        _tools!.Visible = games;
        string key = _selected + "/" + _page + "/" + (_selected switch
        {
            Product.Ps2 => Ps2Status + "/" + Ps2.DiscPath + "/" + Ps2.EmulatorPath + "/" + Ps2.ModPath,
            Product.Retail => _page == LibraryPage.Mods ? "" : RetailStatus + "/" + _settings.Retail.InstallFolder,
            Product.Unreal => _settings.UnrealMapModRoot + "/" + _settings.UnrealMapModsEnabled + "/" + _steamSource.Value + "/" + _steamContentRoot,
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
            LibraryPage.Games when _selected == Product.Ps2 => LibraryCard("Disc and emulator",
                "Original disc   ·   " + (File.Exists(Ps2.DiscPath) ? Path.GetFileName(Ps2.DiscPath) : "Choose your ISO")
                + Environment.NewLine + "PCSX2   ·   " + (File.Exists(Ps2.EmulatorPath) ? Path.GetFileName(Ps2.EmulatorPath) : "Choose your emulator"),
                ActionButton("Manage setup", () => SelectPage(LibraryPage.Settings))),
            LibraryPage.Games when _selected == Product.Retail => LibraryCard("PC mods",
                _settings.Retail.UseMod ? "Your separate modded installation is selected." : "Enhanced and Reloaded are optional mods for Versus.",
                ActionButton("Manage mods", () => SelectPage(LibraryPage.Mods))),
            _ => null,
        };
        if (contents is not null)
        {
            contents.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            contents.Margin = new Padding(0, 0, 0, 10);
            Theme.AddRow(_details, contents);
            Theme.Apply(_details, Palette);
        }
        if (contents is { BackColor.A: 0 })
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
                _ = RunAsync(BuildSteamContentAsync);
            });
            rebuild.Enabled = Content.GodotSteamContent.SupportsClient(_store.Current()) && _steamSource.Value is not null;
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
        Theme.AddRow(card, Theme.Label(title, Theme.Heading));
        var text = Theme.Wrapping(Theme.Label(description, Theme.Body, Theme.Muted));
        text.UseMnemonic = false;
        text.Margin = new Padding(0, 6, 0, 12);
        Theme.AddRow(card, text);
        var actions = Theme.Flow(false, buttons);
        actions.WrapContents = true;
        actions.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Theme.AddRow(card, actions);
        return card;
    }
}

/// <summary>Library navigation with a platform monogram and an accessible native button.</summary>
internal sealed class LibraryEntry : Button
{
    private readonly string _monogram;
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
        _monogram = monogram;
        _detail = detail;
        Text = name;
        AccessibleName = name;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(196, 61);
        Margin = new Padding(0, 0, 0, 5);
        Font = Theme.Body;
    }

    public override Size GetPreferredSize(Size proposedSize) => Size;

    protected override void OnPaint(PaintEventArgs e)
    {
        Color palette = Palette.Accent;
        bool selected = BackColor == palette;
        Color surface = selected ? ControlPaint.Dark(palette, 0.76f) : BackColor;
        using var background = new SolidBrush(surface);
        e.Graphics.FillRectangle(background, ClientRectangle);
        int diameter = LogicalToDeviceUnits(39);
        int x = LogicalToDeviceUnits(9);
        var badge = new Rectangle(x, (Height - diameter) / 2, diameter, diameter);
        using var border = new Pen(selected ? palette : Palette.Muted);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawEllipse(border, badge);
        var centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(e.Graphics, _monogram, Theme.Mono, badge, selected ? palette : Palette.Text, centered);
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
    // The project’s night-vision capture from docs/images/website/showcase-bots.webp,
    // encoded as a JPEG so the Windows and Linux launchers can decode the same packaged artwork.
    private readonly Image _image;
    public LibraryBanner(Image image)
    {
        _image = image;
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ColumnCount = 1;
        ColumnStyles.Add(Theme.Fill);
        Margin = new Padding(0, 0, 0, 12);
        BackColor = Theme.Panel;
        Compact = false;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Compact
    {
        set => Padding = value ? new Padding(16, 7, 16, 7) : new Padding(24, 30, 24, 30);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 1 || Height < 1) return;
        float scale = Math.Max((float)Width / _image.Width, (float)Height / _image.Height);
        float imageHeight = _image.Height * scale;
        e.Graphics.DrawImage(_image, (Width - _image.Width * scale) / 2, (Height - imageHeight) * .43f, _image.Width * scale, imageHeight);
        using var tint = new LinearGradientBrush(ClientRectangle, Color.FromArgb(239, 12, 21, 18), Color.FromArgb(116, 12, 21, 18), 0f);
        e.Graphics.FillRectangle(tint, ClientRectangle);
    }
}
