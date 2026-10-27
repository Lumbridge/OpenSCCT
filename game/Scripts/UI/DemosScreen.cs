using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Play.Demo;
using OnlineDemo = OpenScct.Net.Discovery.DemoEntry;

namespace OpenScctGame.UI;

/// <summary>
/// Demos (OpenSCCT's own, docs/DEMOS.md): the recent rounds of trusted servers that the master keeps for 7 days
/// (signed-in players: Watch downloads into the cache, checks the size and SHA-256, and plays), and the demos on this
/// machine (offline recordings, files copied into the demos folder, downloads) with Watch and, for a download, Keep.
/// </summary>
public partial class DemosScreen : MenuPage
{
    private VBoxContainer? _rows;
    private VBoxContainer? _recent;
    private Label? _status;
    private Task<DemoListResponse>? _listing;
    private Task<string>? _download;
    private string? _localNetVersion;

    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton("Demos", () => FrontEnd.ShowDemos(from.GetTree()), "Demos");

    public override void _Ready()
    {
        Name = "Demos";
        BuildFrame("Demos", 820f);
        DemoLibrary.PruneCache(TimeSpan.FromDays(7));

        VBoxContainer online = Panel(Body, "Recent", "Recent rounds (kept 7 days)");
        var recentScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 220f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        online.AddChild(recentScroll);
        _recent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        recentScroll.AddChild(_recent);

        VBoxContainer box = Panel(Body, "Saved", "On this computer");
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 200f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_rows);

        _status = new Label
        {
            Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Demos are kept in " + DemoLibrary.SavedFolder + ". Record an offline match with --svm-record.",
        };
        Body.AddChild(_status);
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back");
        PageButton("Open Folder", () => OS.ShellOpen(DemoLibrary.SavedFolder), "OpenFolder");
        PageButton("Refresh", Refresh, "Refresh");
        Fill();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_listing is { IsCompleted: true } listing)
        {
            _listing = null;
            if (listing.IsCompletedSuccessfully)
            {
                FillRecent(listing.Result);
            }
            else
            {
                RecentMessage(AccountService.ErrorOf(listing));
            }
        }

        if (_download is { IsCompleted: true } download)
        {
            _download = null;
            if (download.IsCompletedSuccessfully)
            {
                ShowStatus(string.Empty);
                DemoLibrary.Watch(GetTree(), download.Result);
            }
            else
            {
                ShowStatus("Could not download the demo: " + AccountService.ErrorOf(download, "the download failed."));
            }
        }
    }

    // ---- recent rounds (the master's list) -------------------------------------------------------------------

    private void Refresh()
    {
        Fill();
        if (!AccountService.HasMaster)
        {
            RecentMessage("Set a master server first (Find a Session) to see recent rounds.");
            return;
        }

        if (!AccountSettings.Current.SignedIn)
        {
            RecentMessage("Sign in (Account) to see and download recent rounds.");
            return;
        }

        if (_listing is not null)
        {
            return;
        }

        RecentMessage("Loading...");
        _listing = AccountService.Call(client => client.DemosAsync(0));
    }

    private void ClearRecent()
    {
        if (_recent is null)
        {
            return;
        }

        foreach (Node child in _recent.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void RecentMessage(string text)
    {
        ClearRecent();
        _recent?.AddChild(new Label { Text = text, Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void FillRecent(DemoListResponse list)
    {
        if (list.Demos.Count == 0)
        {
            RecentMessage("No recent rounds have demos yet.");
            return;
        }

        ClearRecent();
        _localNetVersion ??= MasterApi.Hex(OpenScct.Server.NetClasses.Create().ComputeProtocolHash());
        foreach (OnlineDemo demo in list.Demos)
        {
            _recent?.AddChild(RecentRow(demo));
        }
    }

    private Control RecentRow(OnlineDemo demo)
    {
        var row = new HBoxContainer { Name = "Recent" };
        row.AddThemeConstantOverride("separation", 12);
        string when = demo.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string length = string.Create(CultureInfo.InvariantCulture, $"{demo.DurationSeconds / 60}:{demo.DurationSeconds % 60:00}");
        int days = Math.Max(0, (int)Math.Ceiling((demo.Expires - DateTimeOffset.UtcNow).TotalDays));
        bool playable = string.Equals(demo.NetVersion, _localNetVersion, StringComparison.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (OpenScct.Net.Discovery.DemoPlayer p in demo.Players)
        {
            names.Add(p.Name);
        }

        var label = new Label
        {
            Text = $"{when}   {demo.Map}  {ModeName(demo.Mode)}   {length}   {demo.ServerName}   expires in {days} d" +
                (playable ? string.Empty : $"   (recorded on {demo.GameVersion})"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            Modulate = playable ? Colors.White : UiStyle.MenuDisabled,
            TooltipText = string.Join(", ", names) + "\n" + string.Create(CultureInfo.InvariantCulture, $"{demo.Size / 1024} KB"),
        };
        row.AddChild(label);
        var watch = new Button { Text = "Watch", Disabled = !playable };
        watch.Pressed += () => StartDownload(demo);
        row.AddChild(watch);
        return row;
    }

    private static string ModeName(int mode) => mode switch
    {
        4 => "Disk Hunt",
        6 => "Deathmatch",
        7 => "Merc Deathmatch",
        _ => "Story",
    };

    private void StartDownload(OnlineDemo demo)
    {
        if (_download is not null)
        {
            return;
        }

        string name = string.Create(CultureInfo.InvariantCulture, $"{demo.Id}-{demo.Map}{OpenScct.Net.DemoFile.Extension}");
        string kept = Path.Combine(DemoLibrary.SavedFolder, name);
        if (File.Exists(kept))
        {
            DemoLibrary.Watch(GetTree(), kept);
            return;
        }

        string path = Path.Combine(DemoLibrary.CacheFolder, name);
        ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Downloading {demo.Map} ({demo.Size / 1024} KB)..."));
        _download = AccountService.Call(client => Download(client, demo, path));
    }

    /// <summary>The demo in the cache: the earlier download when its hash matches, else fetched through its S3 link.</summary>
    private static async Task<string> Download(MasterClient client, OnlineDemo demo, string path)
    {
        if (File.Exists(path) && string.Equals(await HashOf(path).ConfigureAwait(false), demo.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        DemoDownloadResponse link = await client.DemoDownloadAsync(demo.Id).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string part = path + ".part";
        using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (HttpResponseMessage response = await http.GetAsync(link.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using FileStream file = File.Create(part);
            await response.Content.CopyToAsync(file).ConfigureAwait(false);
        }

        string expected = link.Sha256.Length > 0 ? link.Sha256 : demo.Sha256;
        if (!string.Equals(await HashOf(part).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidDataException("the file did not match its checksum.");
        }

        File.Move(part, path, overwrite: true);
        return path;
    }

    private static async Task<string> HashOf(string path)
    {
        await using FileStream file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file).ConfigureAwait(false));
    }

    // ---- demos on this computer ------------------------------------------------------------------------------

    private void Fill()
    {
        if (_rows is null)
        {
            return;
        }

        foreach (Node child in _rows.GetChildren())
        {
            child.QueueFree();
        }

        Directory.CreateDirectory(DemoLibrary.SavedFolder);
        var entries = new List<(Play.Demo.DemoEntry Entry, bool Cached)>();
        foreach (Play.Demo.DemoEntry e in DemoLibrary.List(DemoLibrary.SavedFolder))
        {
            entries.Add((e, false));
        }

        foreach (Play.Demo.DemoEntry e in DemoLibrary.List(DemoLibrary.CacheFolder))
        {
            entries.Add((e, true));
        }

        if (entries.Count == 0)
        {
            _rows.AddChild(new Label { Text = "No demos yet.", Modulate = UiStyle.Dim });
            return;
        }

        foreach ((Play.Demo.DemoEntry entry, bool cached) in entries)
        {
            _rows.AddChild(Row(entry, cached));
        }
    }

    private Control Row(Play.Demo.DemoEntry entry, bool cached)
    {
        var row = new HBoxContainer { Name = "Row" };
        row.AddThemeConstantOverride("separation", 12);
        string when = entry.Recorded.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string length = string.Create(CultureInfo.InvariantCulture, $"{(int)entry.Duration.TotalMinutes}:{entry.Duration.Seconds:00}");
        string mode = Play.Demo.DemoPlayer.ModeKey(entry.Header.GameOptions);
        var label = new Label
        {
            Text = $"{when}   {entry.Header.Map}  {mode}   {length}   {entry.Index.Players.Count} players   {entry.Header.ServerName}" +
                (cached ? "   (download)" : string.Empty),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            TooltipText = entry.FileName + string.Create(CultureInfo.InvariantCulture, $"  {entry.Bytes / 1024} KB, version {entry.Header.GameVersion}"),
        };
        row.AddChild(label);
        if (cached)
        {
            var keep = new Button { Text = "Keep" };
            keep.Pressed += () =>
            {
                try
                {
                    DemoLibrary.Keep(entry.Path);
                    Fill();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    ShowStatus("Could not keep the demo: " + e.Message);
                }
            };
            row.AddChild(keep);
        }

        var watch = new Button { Text = "Watch" };
        watch.Pressed += () => DemoLibrary.Watch(GetTree(), entry.Path);
        row.AddChild(watch);
        return row;
    }

    private void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
