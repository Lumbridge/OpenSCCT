using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Maps;

namespace OpenScct.Launcher.App;

/// <summary>
/// The Maps window: the maps of the SCCT-Maps repository, which ones are downloaded, and Download / Update /
/// Remove. Downloads go to the launcher's maps folder (<see cref="MapLibrary"/>), never into the player's game; the
/// next "Prepare the game files" converts them with the rest. <see cref="Changed"/> tells the main window that the
/// game files need preparing again.
/// </summary>
internal sealed class MapsForm : Form
{
    private readonly MapLibrary _library;
    private readonly MapRepository _repository;
    private readonly string? _gamePath;
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);

    private readonly TextBox _search = Theme.TextBox();
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        HideSelection = false,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        Font = Theme.Body,
    };
    private readonly TextBox _details = Theme.TextBox();
    private readonly GreenProgressBar _progress = new();
    private readonly Label _status = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _download = Theme.Button("Download");
    private readonly Button _remove = Theme.Button("Remove");
    private readonly Button _refresh = Theme.Button("Refresh");
    private readonly Button _stop = Theme.Button("Stop");
    private readonly Button _web = Theme.Button("Open on GitHub");
    private readonly Button _close = Theme.Button("Close");

    private MapIndex? _catalog;
    private CancellationTokenSource? _work;
    private int _selectionVersion;

    public MapsForm(MapLibrary library, HttpClient http, string? gamePath)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(http);
        _library = library;
        _repository = new MapRepository(http);
        _gamePath = gamePath;

        Text = "Maps";
        Icon = Theme.LoadIcon(32);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(820, 600);
        MinimumSize = new Size(620, 460);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(16, 12, 16, 12);

        Label title = Theme.Label("Maps from the community", Theme.Heading, Theme.Accent);
        Label intro = Theme.Wrapping(Theme.Label(
            "New and remade maps from the SCCT-Maps repository. Downloaded maps are added the next time the game files are " +
            "prepared. Everyone in a match needs the same map, so share what you download with your friends.",
            Theme.Small, Theme.Muted));

        _search.PlaceholderText = "Search maps";
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 8, 0, 6);
        _search.TextChanged += (_, _) => Fill();

        _list.Columns.Add("Map", 280);
        _list.Columns.Add("Type", 120);
        _list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Status", 200);
        _list.SelectedIndexChanged += (_, _) => OnSelection();
        _list.DoubleClick += async (_, _) => await DownloadSelectedAsync();

        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.WordWrap = true;
        _details.Dock = DockStyle.Fill;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Theme.Background,
            SplitterWidth = 6,
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_details);

        _progress.Visible = false;
        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(0, 8, 0, 2);
        Theme.Wrapping(_status);
        _stop.Visible = false;

        TableLayoutPanel buttons = Theme.Grid(Theme.Fill, Theme.Auto);
        buttons.Dock = DockStyle.Fill;
        buttons.Margin = new Padding(0, 8, 0, 0);
        Theme.AddRow(buttons, Theme.Flow(false, _download, _remove, _stop, _refresh, _web), _close);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        void Row(Control c, RowStyle style)
        {
            int r = root.RowCount++;
            root.RowStyles.Add(style);
            root.Controls.Add(c, 0, r);
        }

        Row(title, new RowStyle(SizeType.AutoSize));
        Row(intro, new RowStyle(SizeType.AutoSize));
        Row(_search, new RowStyle(SizeType.AutoSize));
        Row(split, new RowStyle(SizeType.Percent, 100f));
        Row(_progress, new RowStyle(SizeType.Absolute, 18));
        Row(_status, new RowStyle(SizeType.AutoSize));
        Row(buttons, new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        _download.Click += async (_, _) => await DownloadSelectedAsync();
        _remove.Click += (_, _) => RemoveSelected();
        _refresh.Click += async (_, _) => await LoadCatalogAsync();
        _stop.Click += (_, _) => _work?.Cancel();
        _web.Click += (_, _) => OpenWeb();
        _close.Click += (_, _) => Close();
        CancelButton = _close;
        Load += (_, _) => split.SplitterDistance = Math.Max(120, split.Height * 3 / 5);

        var tips = new ToolTip();
        tips.SetToolTip(_download, "Download the selected maps (or update them to the newest version).");
        tips.SetToolTip(_remove, "Delete the selected downloaded maps from this PC.");
        tips.SetToolTip(_web, "Open the map repository in your browser.");
        RefreshButtons();
    }

    /// <summary>Whether a map was downloaded, updated or removed while the window was open.</summary>
    public bool Changed { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Fill();
        await LoadCatalogAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _work?.Cancel();
        base.OnFormClosing(e);
    }

    // ---- the list --------------------------------------------------------------------------------------------------

    /// <summary>One row: a map of the catalogue, a downloaded one, or both.</summary>
    private sealed record Row(MapPackage? Map, InstalledMap? Installed)
    {
        public string Id => Map?.Id ?? Installed!.Id;

        public string Name => Map?.Name ?? Installed!.Name;

        public string Category => Map?.Category ?? Installed!.Category;
    }

    private enum RowState
    {
        Available,
        Downloaded,
        UpdateAvailable,
        InGame,
        Unlisted,
    }

    private IReadOnlyList<Row> Rows()
    {
        var installed = _library.Installed().ToDictionary(m => m.Id, StringComparer.Ordinal);
        var rows = new List<Row>();
        if (_catalog is not null)
        {
            rows.AddRange(_catalog.Maps.Select(m => new Row(m, installed.GetValueOrDefault(m.Id))));
        }

        // Downloaded maps the catalogue no longer lists (or before it has loaded), so they can still be removed.
        var listed = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        rows.AddRange(installed.Values.Where(m => !listed.Contains(m.Id)).Select(m => new Row(null, m)));
        return rows;
    }

    private RowState StateOf(Row row)
    {
        if (row.Installed is not null)
        {
            return row.Map is null ? RowState.Unlisted
                : row.Map.Fingerprint == row.Installed.Fingerprint && row.Installed.DependenciesChecked
                    ? RowState.Downloaded
                    : RowState.UpdateAvailable;
        }

        return row.Map is not null && MapLibrary.CodesInInstall(row.Map, _gamePath).Count == row.Map.Codes.Count
            ? RowState.InGame
            : RowState.Available;
    }

    private static string StatusText(Row row, RowState state) => state switch
    {
        RowState.Downloaded => "Downloaded" + Version(row.Installed!.Version),
        RowState.UpdateAvailable => "Update available" + Version(row.Map!.Version),
        RowState.InGame => "Already in your game",
        RowState.Unlisted => "Downloaded (no longer listed)",
        _ => string.Empty,
    };

    private static string Version(string version) => version.StartsWith('v') ? " (" + version + ")" : string.Empty;

    private void Fill()
    {
        string query = _search.Text.Trim();
        var keep = SelectedIds().ToHashSet(StringComparer.Ordinal);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (Row row in Rows())
        {
            if (query.Length > 0
                && !row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !(row.Map?.Codes.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false)
                && !(row.Map?.Game.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            RowState state = StateOf(row);
            string type = row.Map is { Game.Length: > 0 } port ? port.Game : row.Category;
            long bytes = row.Map?.Bytes ?? row.Installed!.Files.Sum(f => f.Size);
            var item = new ListViewItem([row.Name, type, Mb(bytes), StatusText(row, state)])
            {
                Tag = row,
                ForeColor = state switch
                {
                    RowState.Downloaded => Theme.Accent,
                    RowState.UpdateAvailable => Theme.Warning,
                    RowState.InGame or RowState.Unlisted => Theme.Muted,
                    _ => Theme.Text,
                },
            };
            _list.Items.Add(item);
            item.Selected = keep.Contains(row.Id);
        }

        _list.EndUpdate();
        RefreshButtons();
    }

    private IEnumerable<string> SelectedIds() => _list.SelectedItems.Cast<ListViewItem>().Select(i => ((Row)i.Tag!).Id);

    private List<Row> SelectedRows() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag!).ToList();

    private void RefreshButtons()
    {
        bool busy = _work is not null;
        List<Row> rows = SelectedRows();
        var states = rows.Select(StateOf).ToList();
        _download.Text = states.Count > 0 && states.All(s => s == RowState.UpdateAvailable) ? "Update" : "Download";
        _download.Enabled = !busy && states.Any(s => s is RowState.Available or RowState.UpdateAvailable);
        _remove.Enabled = !busy && rows.Any(r => r.Installed is not null);
        _refresh.Enabled = !busy;
        _stop.Visible = busy;
    }

    private async void OnSelection()
    {
        RefreshButtons();
        int version = ++_selectionVersion;
        List<Row> rows = SelectedRows();
        if (rows.Count != 1)
        {
            _details.Text = rows.Count == 0 ? string.Empty : $"{rows.Count} maps selected.";
            return;
        }

        Row row = rows[0];
        _details.Text = Describe(row);
        if (row.Map is not { } map)
        {
            return;
        }

        if (!_notes.TryGetValue(map.Id, out string? notes))
        {
            try
            {
                notes = await _repository.NotesAsync(map, CancellationToken.None) ?? string.Empty;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                notes = string.Empty;
            }

            _notes[map.Id] = notes;
        }

        if (version == _selectionVersion && notes.Length > 0 && !IsDisposed)
        {
            _details.Text = Describe(row) + "\r\n\r\n" + notes.ReplaceLineEndings("\r\n");
        }
    }

    private string Describe(Row row)
    {
        var lines = new List<string> { row.Name };
        if (row.Map is { } map)
        {
            lines.Add(map.Game.Length > 0 ? $"{map.Category}, from {map.Game}" : map.Category);
            lines.Add($"Map code{(map.Codes.Count == 1 ? string.Empty : "s")}: {string.Join(", ", map.Codes)}");
            lines.Add($"{map.Files.Count} files, {Mb(map.Bytes)}" + (map.Version.StartsWith('v') ? $", version {map.Version}" : string.Empty));
            IReadOnlyList<string> inGame = MapLibrary.CodesInInstall(map, _gamePath);
            if (inGame.Count > 0)
            {
                lines.Add($"Your game already has {string.Join(", ", inGame)}; the game uses that copy.");
            }
        }

        if (row.Installed is { } installed)
        {
            lines.Add("Downloaded " + installed.InstalledAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ".");
            if (MapInstallResult.MissingWarning(row.Name, installed.MissingPackages) is { } warning)
            {
                lines.Add(warning);
            }
            else if (!installed.DependenciesChecked)
            {
                lines.Add("Download it again to fetch the packages it needs that your game does not have.");
            }
        }

        return string.Join("\r\n", lines);
    }

    // ---- the work --------------------------------------------------------------------------------------------------

    private async Task LoadCatalogAsync()
    {
        await RunAsync("Loading the map list...", async cancel =>
        {
            _catalog = await _repository.FetchCatalogAsync(cancel);
            Fill();
            int downloaded = _library.Installed().Count;
            SetStatus($"{_catalog.Maps.Count} maps" + (downloaded > 0 ? $", {downloaded} downloaded." : "."), Theme.Muted);
        });
    }

    private async Task DownloadSelectedAsync()
    {
        List<MapPackage> maps = SelectedRows()
            .Where(r => StateOf(r) is RowState.Available or RowState.UpdateAvailable)
            .Select(r => r.Map!)
            .ToList();
        if (maps.Count == 0 || _work is not null)
        {
            return;
        }

        await RunAsync("Downloading...", async cancel =>
        {
            var warnings = new List<string>();
            for (int i = 0; i < maps.Count; i++)
            {
                MapPackage map = maps[i];
                string prefix = maps.Count > 1 ? $"{map.Name} ({i + 1} of {maps.Count})" : map.Name;
                _progress.Fraction = 0;
                _progress.Visible = true;
                var progress = new Progress<MapDownloadProgress>(p =>
                {
                    _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 1;
                    SetStatus($"{prefix}: {Mb(p.Received)} of {Mb(p.Total)}", Theme.Muted);
                });
                MapInstallResult result = await _library.InstallAsync(map, _repository, progress, cancel, _catalog, _gamePath);
                if (result.Warning(map.Name) is { } warning)
                {
                    warnings.Add(warning);
                }

                Changed = true;
                Fill();
            }

            string done = (maps.Count == 1 ? maps[0].Name + " is" : $"{maps.Count} maps are")
                + " downloaded. Close this window to prepare the game files with them.";
            if (warnings.Count > 0)
            {
                SetStatus(done + " " + string.Join(" ", warnings), Theme.Warning);
                MessageBox.Show(this, string.Join("\r\n\r\n", warnings), "Packages not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetStatus(done, Theme.Accent);
            }
        });
    }

    private void RemoveSelected()
    {
        List<Row> rows = SelectedRows().Where(r => r.Installed is not null).ToList();
        if (rows.Count == 0 || _work is not null)
        {
            return;
        }

        string what = rows.Count == 1 ? rows[0].Name : $"{rows.Count} maps";
        if (MessageBox.Show(this, $"Delete {what} from this PC?", "Remove maps", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            foreach (Row row in rows)
            {
                _library.Remove(row.Id);
            }

            Changed = true;
            SetStatus($"Removed {what}.", Theme.Accent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus("Could not remove: " + ex.Message, Theme.Error);
        }

        Fill();
    }

    private void OpenWeb()
    {
        string url = SelectedRows() is [{ Map: { } map }]
            ? $"{MapRepository.WebUrl}/tree/{map.Commit}/{string.Join('/', Path.GetDirectoryName(map.NotesPath)!.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString))}"
            : MapRepository.WebUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Could not open the browser: " + ex.Message, Theme.Warning);
        }
    }

    private async Task RunAsync(string status, Func<CancellationToken, Task> work)
    {
        if (_work is not null)
        {
            return;
        }

        _work = new CancellationTokenSource();
        SetStatus(status, Theme.Muted);
        RefreshButtons();
        try
        {
            await work(_work.Token);
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                SetStatus("Stopped.", Theme.Muted);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException
            or System.Text.Json.JsonException)
        {
            if (!IsDisposed)
            {
                SetStatus(ex.Message, Theme.Error);
            }
        }
        finally
        {
            _work.Dispose();
            _work = null;
            if (!IsDisposed)
            {
                _progress.Visible = false;
                Fill();
            }
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString(bytes < 10 * 1024 * 1024 ? "0.0" : "0", CultureInfo.CurrentCulture) + " MB";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _work?.Dispose();
        }

        base.Dispose(disposing);
    }
}
