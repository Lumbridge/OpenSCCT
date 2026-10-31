using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Maps;
using OpenScct.Server;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// Linux packages start the game directly, without the Windows launcher. Fetch the shared content and a first
/// map before opening menus that cache that content. Uses the game's verified content store and download jobs.
/// </summary>
public partial class LinuxSetupScreen : Control
{
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private Button _download = null!;
    private Button _cancel = null!;
    private CheckBox _allMaps = null!;
    private readonly Queue<ContentMap> _queue = new();
    private ContentPacks.DownloadJob? _job;
    private bool _installing;
    private int _finished;
    private int _count;

    public string? Failure { get; private set; }

    public static bool Needed => !LinuxSetupContent.SharedReady(ContentPaths.Shared) ||
        !(HasMap(ContentPaths.SvMap) || HasMap(Path.Combine(ContentPaths.PacksFolder, "svmap")));

    private static bool HasMap(string? root) => root is not null && Directory.Exists(root) &&
        Directory.EnumerateDirectories(root).Any(dir => File.Exists(Path.Combine(dir, "manifest.json")));

    public override void _Ready()
    {
        Name = "LinuxSetup";
        Theme = new Theme { DefaultFontSize = 22 };
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.035f, 0.055f, 0.045f) };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 40);
        }
        AddChild(margin);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 20);
        margin.AddChild(column);
        column.AddChild(new Label { Text = "OpenSCCT — Linux", LabelSettings = new LabelSettings { FontSize = 36 } });
        column.AddChild(new Label
        {
            Text = "Welcome. Download the characters, sounds and maps to start playing.\n" +
                   "The starter download includes Steel Squat (SteeE), about 490 MiB in total.\n" +
                   "You can download more maps later from Content in the main menu.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _allMaps = new CheckBox { Text = "Download all stock maps instead (about 2 GiB)" };
        column.AddChild(_allMaps);
        _status = new Label { Text = "Loading the content list…", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        _progress = new ProgressBar { MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0, 28) };
        column.AddChild(_progress);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 20);
        column.AddChild(buttons);
        _download = new Button { Text = "Download and play", Disabled = true, CustomMinimumSize = new Vector2(260, 56) };
        _download.Pressed += Begin;
        buttons.AddChild(_download);
        _cancel = new Button { Text = "Quit", CustomMinimumSize = new Vector2(160, 56) };
        _cancel.Pressed += () =>
        {
            if (_installing)
            {
                _queue.Clear();
                if (_job is null)
                {
                    Stop("Download stopped. Completed packs are kept.");
                }
                else
                {
                    _job.Cancel();
                    _cancel.Disabled = true;
                }
            }
            else
            {
                GetTree().Quit();
            }
        };
        buttons.AddChild(_cancel);
        if (LinuxSetupContent.OverrideProblem(ContentPaths.Root) is { } problem)
        {
            Failure = problem;
            _status.Text = problem;
            _download.Disabled = true;
            _allMaps.Disabled = true;
            SetProcess(false);
            _cancel.GrabFocus();
            return;
        }
        ContentPacks.RefreshIndex();
        _allMaps.GrabFocus();
    }

    public override void _Process(double delta)
    {
        if (!_installing)
        {
            _download.Disabled = ContentPacks.Index is null;
            _status.Text = ContentPacks.IndexError is { } error
                ? "Could not load the content list: " + error + "\nCheck your connection. Select Retry to try again."
                : ContentPacks.Index is not null ? "Ready to download. Completed packs are kept if you stop." : "Loading the content list…";
            // A failed index needs a working retry button too.
            if (ContentPacks.IndexError is not null)
            {
                _download.Disabled = false;
                _download.Text = "Retry";
            }
            return;
        }

        if (_job is null)
        {
            if (_queue.TryDequeue(out ContentMap? next))
            {
                _job = ContentPacks.Download(next);
            }
            else
            {
                if (Needed)
                {
                    Stop("The shared packs are incomplete. Retry the download.");
                    return;
                }
                // UcText and the menus must first see the newly installed core pack.
                ContentPaths.Configure(App.From(this).Options.ContentRoot, Path.GetDirectoryName(OS.GetExecutablePath()));
                FrontEnd.ShowMainMenu(GetTree());
                return;
            }
        }

        _status.Text = $"Map {_finished + 1} of {_count}\n{_job.Describe()}";
        _progress.Value = _job.Fraction * 100;
        if (_job.Error is { } problem)
        {
            Stop(problem == "stopped" ? "Download stopped. Completed packs are kept." : "Download failed: " + problem);
        }
        else if (_job.Done)
        {
            _finished++;
            _job = null;
        }
    }

    private void Stop(string message)
    {
        Failure = message;
        _installing = false;
        _queue.Clear();
        _job = null;
        _download.Disabled = false;
        _download.Text = "Retry download";
        _allMaps.Disabled = false;
        _cancel.Disabled = false;
        _cancel.Text = "Quit";
        _status.Text = message;
        SetProcess(false);
        _download.GrabFocus();
    }

    private void Begin()
    {
        Failure = null;
        SetProcess(true);
        if (ContentPacks.Index is not { } index)
        {
            ContentPacks.RefreshIndex(force: true);
            return;
        }

        IEnumerable<ContentMap> maps = _allMaps.ButtonPressed
            ? index.PlayableMaps().Where(m => m.Category == "stock")
            : index.FindMap("SteeE") is { } starter ? new[] { starter } : Array.Empty<ContentMap>();
        _queue.Clear();
        foreach (ContentMap map in maps)
        {
            _queue.Enqueue(map);
        }
        if (_queue.Count == 0)
        {
            _status.Text = index.Problem ?? "No starter map in the content list. Try refreshing the list.";
            ContentPacks.RefreshIndex(force: true);
            return;
        }
        _count = _queue.Count;
        _finished = 0;
        _installing = true;
        _allMaps.Disabled = true;
        _download.Disabled = true;
        _cancel.Disabled = false;
        _cancel.Text = "Stop download";
        _cancel.GrabFocus();
    }

    public override void _ExitTree()
    {
        _job?.Cancel();
    }
}
