using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Settings;

namespace OpenScct.Launcher.App;

internal sealed class SettingsForm : Form
{
    private readonly TokenStore _tokens;
    private readonly LauncherLayout _layout;
    private readonly TextBox _owner = Theme.TextBox();
    private readonly TextBox _repo = Theme.TextBox();
    private readonly TextBox _token = Theme.TextBox();
    private readonly Label _tokenState = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly CheckBox _removeToken = new() { Text = "Remove the saved token", AutoSize = true, ForeColor = Theme.Text };
    private readonly CheckBox _prereleases = new() { Text = "Include prereleases", AutoSize = true, ForeColor = Theme.Text };
    private readonly CheckBox _checkOnStart = new() { Text = "Check for updates when the launcher starts", AutoSize = true, ForeColor = Theme.Text };
    private readonly TextBox _gamePath = Theme.TextBox();
    private readonly Label _gameState = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly ComboBox _language = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
        Font = Theme.Body, Width = 200,
    };
    private string _chosenLanguage = ContentBuildPlan.DefaultLanguage;

    public SettingsForm(LauncherSettings settings, TokenStore tokens, LauncherLayout layout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _tokens = tokens;
        _layout = layout;
        Result = settings;

        Text = "Settings";
        Icon = Theme.LoadIcon(32);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(600, 540);
        MinimumSize = new Size(480, 420);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(20, 12, 20, 12);

        // Laid out in tables that size to their text, so nothing is cut off at any display scaling.
        _owner.Text = settings.Owner;
        _repo.Text = settings.Repo;
        _owner.Dock = DockStyle.Fill;
        _repo.Dock = DockStyle.Fill;
        TableLayoutPanel repo = Theme.Grid(new ColumnStyle(SizeType.Percent, 50f), new ColumnStyle(SizeType.Percent, 50f));
        Theme.AddRow(repo, Theme.Label("GitHub owner", Theme.Small, Theme.Muted), Theme.Label("Repository", Theme.Small, Theme.Muted));
        Theme.AddRow(repo, _owner, _repo);

        _token.UseSystemPasswordChar = true;
        _token.PlaceholderText = tokens.HasToken ? "Saved. Leave empty to keep it." : "Empty: public releases, no token";
        _token.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _tokenState.Text = "Stored encrypted for your Windows account (DPAPI), never in plain text.";
        _removeToken.Visible = tokens.HasToken;
        _prereleases.Checked = settings.IncludePrereleases;
        _checkOnStart.Checked = settings.CheckOnStart;

        _gamePath.Text = settings.OriginalGamePath ?? string.Empty;
        _chosenLanguage = ContentBuildPlan.NormaliseLanguage(settings.Language);
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_language.SelectedItem is LanguageItem item)
            {
                _chosenLanguage = item.Code;
            }
        };
        _gamePath.TextChanged += (_, _) => UpdateGameState();
        _gamePath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Button browse = Theme.Button("Browse...");
        browse.Click += (_, _) => Browse();
        TableLayoutPanel game = Theme.Grid(Theme.Fill, Theme.Auto);
        Theme.AddRow(game, _gamePath, browse);
        UpdateGameState();

        Button ok = Theme.Button("Save");
        ok.MinimumSize = new Size(92, 32);
        ok.Click += (_, _) => Save();
        Button cancel = Theme.Button("Cancel");
        cancel.MinimumSize = new Size(92, 32);
        cancel.DialogResult = DialogResult.Cancel;
        FlowLayoutPanel buttons = Theme.Flow(false, ok, cancel);
        buttons.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        AcceptButton = ok;
        CancelButton = cancel;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        void Row(Control c, int top = 0)
        {
            c.Margin = new Padding(0, top, 0, 2);
            int r = root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(c, 0, r);
        }

        Row(Theme.Label("Updates", Theme.Heading, Theme.Accent));
        Row(repo, 6);
        Row(Theme.Wrapping(Theme.Label("Access token (only while the repository is private; read-only contents access is enough)", Theme.Small, Theme.Muted)), 10);
        Row(_token);
        Row(Theme.Wrapping(_tokenState));
        Row(_removeToken);
        Row(Theme.Flow(false, _prereleases, _checkOnStart), 6);
        Row(Theme.Label("Original game (optional)", Theme.Heading, Theme.Accent), 18);
        Row(Theme.Wrapping(Theme.Label("Splinter Cell Chaos Theory folder (read only). With it, game content is built from your copy; without it, it is downloaded. Found by itself for the Steam copy.", Theme.Small, Theme.Muted)), 6);
        Row(game);
        Row(Theme.Wrapping(_gameState));
        Row(Theme.Label("Language", Theme.Heading, Theme.Accent), 18);
        Row(Theme.Wrapping(Theme.Label("The game's text (maps, briefings, messages and menus) from your install's language files. Changing it builds the content again. Downloaded game files are in English.", Theme.Small, Theme.Muted)), 6);
        Row(_language);
        Row(Theme.Label("Folders", Theme.Heading, Theme.Accent), 18);
        Row(Link("Launcher and game versions: " + layout.Root, layout.Root), 6);
        Row(Link("Game settings and saves (never changed by the launcher): " + LauncherLayout.GameUserData, LauncherLayout.GameUserData));
        Row(buttons, 16);
        Controls.Add(root);
        Load += (_, _) => Theme.FitToContent(this, root);
    }

    public LauncherSettings Result { get; private set; }

    private static LinkLabel Link(string text, string folder)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = Theme.Small,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.Text,
            VisitedLinkColor = Theme.Accent,
        };
        link.LinkClicked += (_, _) =>
        {
            if (Directory.Exists(folder))
            {
                using Process? explorer = Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = false });
            }
        };
        return link;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your Splinter Cell Chaos Theory install folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(_gamePath.Text))
        {
            dialog.InitialDirectory = _gamePath.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _gamePath.Text = dialog.SelectedPath;
        }
    }

    private void UpdateGameState()
    {
        string path = _gamePath.Text.Trim();
        if (path.Length == 0)
        {
            _gameState.Text = "Not set.";
            _gameState.ForeColor = Theme.Muted;
        }
        else if (GameLocator.Normalize(path) is not null)
        {
            _gameState.Text = "Looks like a Chaos Theory install.";
            _gameState.ForeColor = Theme.Accent;
        }
        else
        {
            _gameState.Text = "No Versus folder (System and Packages) with the game's files here.";
            _gameState.ForeColor = Theme.Warning;
        }

        FillLanguages(path);
    }

    /// <summary>The languages the picked install has text in; the choice stays when the install has it.</summary>
    private void FillLanguages(string gamePath)
    {
        string keep = _chosenLanguage;
        _language.BeginUpdate();
        _language.Items.Clear();
        int index = 0;
        foreach ((string code, string name) in ContentBuildPlan.LanguagesIn(gamePath))
        {
            if (code == keep)
            {
                index = _language.Items.Count;
            }

            _language.Items.Add(new LanguageItem(code, name));
        }

        _language.SelectedIndex = index;
        _language.EndUpdate();
    }

    private sealed record LanguageItem(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private void Save()
    {
        if (_removeToken.Checked)
        {
            _tokens.Clear();
        }
        else if (_token.Text.Trim().Length > 0)
        {
            _tokens.Save(_token.Text);
        }

        _token.Clear();
        Result = new LauncherSettings
        {
            Owner = _owner.Text,
            Repo = _repo.Text,
            IncludePrereleases = _prereleases.Checked,
            CheckOnStart = _checkOnStart.Checked,
            OriginalGamePath = GameLocator.Normalize(_gamePath.Text) ?? _gamePath.Text,
            Language = _chosenLanguage,
        };
        Directory.CreateDirectory(_layout.Root);
        DialogResult = DialogResult.OK;
        Close();
    }
}
