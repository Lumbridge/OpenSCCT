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
/// The Maps window: the maps of OpenSCCT-Content (docs/CONTENT-PACKS.md), already converted, so a download is ready
/// to play at once (no "Prepare the game files"). Which ones are downloaded, which the player's own converted content
/// already has, and Download / Update / Remove. Maps downloaded earlier from SCCT-Maps (<see cref="MapLibrary"/>)
/// still show, so they can be removed; removing one is the only change that needs the game files prepared again
/// (<see cref="Changed"/>).
/// </summary>
internal sealed class MapsForm : Form
{
    private readonly MapLibrary _library;
    private readonly ContentStore _store;
    private readonly string _contentMaps;

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

    private ContentIndex? _index;
    private CancellationTokenSource? _work;

    /// <param name="library">Maps downloaded from SCCT-Maps by older launchers (shown so they can be removed).</param>
    /// <param name="packs">Where downloaded content packs go (the game reads them from there).</param>
    /// <param name="content">The player's converted content (its svmap/ holds the maps converted from the install).</param>
    public MapsForm(MapLibrary library, HttpClient http, string packs, string content)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(http);
        _library = library;
        _store = new ContentStore(packs, http);
        _contentMaps = Path.Combine(content, "svmap");

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

        Label title = Theme.Label("Maps", Theme.Heading, Theme.Accent);
        Label intro = Theme.Wrapping(Theme.Label(
            "Every map from the OpenSCCT-Content repository: stock, original, community, recovered and ported maps, already " +
            "converted. A downloaded map is ready to play straight away. The game also downloads a map you are missing when " +
            "you queue for it in the hub.",
            Theme.Small, Theme.Muted));

        _search.PlaceholderText = "Search maps";
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 8, 0, 6);
        _search.TextChanged += (_, _) => Fill();

        _list.Columns.Add("Map", 280);
        _list.Columns.Add("Type", 120);
        _list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Status", 220);
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
        _refresh.Click += async (_, _) => await LoadIndexAsync();
        _stop.Click += (_, _) => _work?.Cancel();
        _web.Click += (_, _) => OpenWeb();
        _close.Click += (_, _) => Close();
        CancelButton = _close;
        Load += (_, _) => split.SplitterDistance = Math.Max(120, split.Height * 3 / 5);

        var tips = new ToolTip();
        tips.SetToolTip(_download, "Download the selected maps (or update them to the newest version).");
        tips.SetToolTip(_remove, "Delete the selected downloaded maps from this PC.");
        tips.SetToolTip(_web, "Open the content repository in your browser.");
        RefreshButtons();
    }

    /// <summary>A map downloaded from SCCT-Maps was removed: the game files need preparing again without it.</summary>
    public bool Changed { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Fill();
        await LoadIndexAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _work?.Cancel();
        base.OnFormClosing(e);
    }

    // ---- the list --------------------------------------------------------------------------------------------------

    /// <summary>
    /// One row: a map of the index, a downloaded pack (listed or not), or a map downloaded from SCCT-Maps before.
    /// </summary>
    private sealed record Row(ContentMap? Map, InstalledContent? Pack, InstalledMap? Old)
    {
        public string Key => Old is not null ? "old:" + Old.Id : Map?.Code ?? Pack!.Code;

        public string Name => Map?.DisplayTitle ?? Old?.Name ?? Pack!.Code;

        public string Category => Map?.Category ?? Old?.Category ?? (Pack!.Id.Split('/') is [var c, _] ? c : string.Empty);
    }

    private enum RowState
    {
        Available,
        Downloaded,
        UpdateAvailable,
        InGame,
        Unlisted,
        Old,
    }

    private IReadOnlyList<Row> Rows()
    {
        var packs = _store.Installed().Where(p => !p.IsShared).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<Row>();
        if (_index is not null)
        {
            foreach (ContentMap m in _index.PlayableMaps())
            {
                rows.Add(new Row(m, packs.GetValueOrDefault(m.Code), null));
                packs.Remove(m.Code);
            }
        }

        // Downloaded packs the index no longer lists (or before it has loaded), so they can still be removed.
        rows.AddRange(packs.Values.Select(p => new Row(null, p, null)));
        rows.AddRange(_library.Installed().Select(m => new Row(null, null, m)));
        return rows;
    }

    private bool InContent(string code) => File.Exists(Path.Combine(_contentMaps, code, "manifest.json"));

    private RowState StateOf(Row row)
    {
        if (row.Old is not null)
        {
            return RowState.Old;
        }

        if (row.Map is null)
        {
            return RowState.Unlisted;
        }

        if (row.Pack is not null)
        {
            return _store.Has(row.Map) ? RowState.Downloaded : RowState.UpdateAvailable;
        }

        return InContent(row.Map.Code) ? RowState.InGame : RowState.Available;
    }

    private static string StatusText(Row row, RowState state) => state switch
    {
        RowState.Downloaded => "Downloaded" + Version(row.Pack!.Version),
        RowState.UpdateAvailable => "Update available" + Version(row.Map!.Version),
        RowState.InGame => "Converted from your game",
        RowState.Unlisted => "Downloaded (no longer listed)",
        RowState.Old => "From SCCT-Maps (old download)",
        _ => string.Empty,
    };

    private static string Version(string version) => version.Length > 0 ? " (" + version + ")" : string.Empty;

    private void Fill()
    {
        string query = _search.Text.Trim();
        var keep = SelectedRows().Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (Row row in Rows())
        {
            if (query.Length > 0
                && !row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !(row.Map?.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            RowState state = StateOf(row);
            long bytes = row.Map?.Full.Size ?? row.Old?.Files.Sum(f => f.Size) ?? 0;
            var item = new ListViewItem([row.Name, row.Category, bytes > 0 ? Mb(bytes) : string.Empty, StatusText(row, state)])
            {
                Tag = row,
                ForeColor = state switch
                {
                    RowState.Downloaded => Theme.Accent,
                    RowState.UpdateAvailable => Theme.Warning,
                    RowState.InGame or RowState.Unlisted or RowState.Old => Theme.Muted,
                    _ => Theme.Text,
                },
            };
            _list.Items.Add(item);
            item.Selected = keep.Contains(row.Key);
        }

        _list.EndUpdate();
        RefreshButtons();
    }

    private List<Row> SelectedRows() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag!).ToList();

    private void RefreshButtons()
    {
        bool busy = _work is not null;
        List<Row> rows = SelectedRows();
        var states = rows.Select(StateOf).ToList();
        _download.Text = states.Count > 0 && states.All(s => s == RowState.UpdateAvailable) ? "Update" : "Download";
        _download.Enabled = !busy && states.Any(s => s is RowState.Available or RowState.UpdateAvailable);
        _remove.Enabled = !busy && rows.Any(r => r.Pack is not null || r.Old is not null);
        _refresh.Enabled = !busy;
        _stop.Visible = busy;
    }

    private void OnSelection()
    {
        RefreshButtons();
        List<Row> rows = SelectedRows();
        _details.Text = rows.Count switch
        {
            0 => string.Empty,
            1 => Describe(rows[0]),
            _ => $"{rows.Count} maps selected.",
        };
    }

    private string Describe(Row row)
    {
        var lines = new List<string> { row.Name };
        if (row.Map is { } map)
        {
            lines.Add($"{map.Category}, map code {map.Code}" + (map.Version.Length > 0 ? $", version {map.Version}" : string.Empty));
            if (map.Modes.Count > 0)
            {
                lines.Add("Modes: " + string.Join(", ", map.Modes));
            }

            lines.Add($"Download {Mb(map.Full.Size)}");
            if (InContent(map.Code))
            {
                lines.Add($"Your converted game files already have {map.Code}; the game uses that copy.");
            }
        }

        if (row.Pack is { } pack)
        {
            lines.Add("Downloaded " + pack.InstalledAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ".");
        }

        if (row.Old is { } old)
        {
            lines.Add("Downloaded from SCCT-Maps by an older launcher and converted with your game files. " +
                "Remove it and download the map from this list instead.");
            lines.Add("Downloaded " + old.InstalledAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ".");
        }

        return string.Join("\r\n", lines);
    }

    // ---- the work --------------------------------------------------------------------------------------------------

    private async Task LoadIndexAsync()
    {
        await RunAsync("Loading the map list...", async cancel =>
        {
            _index = await _store.FetchIndexAsync(cancel);
            Fill();
            int downloaded = _store.Installed().Count(p => !p.IsShared);
            string problem = _index.Problem is { } p ? " " + p : string.Empty;
            SetStatus($"{_index.PlayableMaps().Count()} maps" + (downloaded > 0 ? $", {downloaded} downloaded." : ".") + problem,
                _index.Problem is null ? Theme.Muted : Theme.Warning);
        });
    }

    private async Task DownloadSelectedAsync()
    {
        List<ContentMap> maps = SelectedRows()
            .Where(r => StateOf(r) is RowState.Available or RowState.UpdateAvailable)
            .Select(r => r.Map!)
            .ToList();
        if (maps.Count == 0 || _work is not null || _index is not { } index)
        {
            return;
        }

        await RunAsync("Downloading...", async cancel =>
        {
            // Without converted game files the game has no shared content: the downloaded packs supply it.
            if (!Directory.Exists(Path.Combine(Path.GetDirectoryName(_contentMaps)!, "shared")))
            {
                foreach (ContentSharedPack pack in index.Shared.Where(p => !_store.HasShared(p)))
                {
                    string name = pack.Title.Length > 0 ? pack.Title : pack.Id;
                    await _store.InstallSharedAsync(pack, Progress(name), cancel);
                }
            }

            for (int i = 0; i < maps.Count; i++)
            {
                ContentMap map = maps[i];
                string prefix = maps.Count > 1 ? $"{map.DisplayTitle} ({i + 1} of {maps.Count})" : map.DisplayTitle;
                await _store.InstallMapAsync(map, ContentKind.Full, Progress(prefix), cancel);
                Fill();
            }

            SetStatus((maps.Count == 1 ? maps[0].DisplayTitle + " is" : $"{maps.Count} maps are") + " downloaded and ready to play.", Theme.Accent);
        });
    }

    private Progress<PackProgress> Progress(string prefix)
    {
        _progress.Fraction = 0;
        _progress.Visible = true;
        return new Progress<PackProgress>(p =>
        {
            if (IsDisposed)
            {
                return;
            }

            _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 1;
            SetStatus(p.Extracting ? $"{prefix}: installing..." : $"{prefix}: {Mb(p.Received)} of {Mb(p.Total)}", Theme.Muted);
        });
    }

    private void RemoveSelected()
    {
        List<Row> rows = SelectedRows().Where(r => r.Pack is not null || r.Old is not null).ToList();
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
                if (row.Old is { } old)
                {
                    _library.Remove(old.Id);
                    Changed = true;
                }
                else
                {
                    _store.RemoveMap(row.Pack!.Code);
                }
            }

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
        try
        {
            Process.Start(new ProcessStartInfo(ContentStore.WebUrl) { UseShellExecute = true })?.Dispose();
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
