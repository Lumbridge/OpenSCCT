using System;
using System.Globalization;
using System.IO;
using Godot;
using OpenScct.Bots;
using OpenScct.Content;
using OpenScct.Game.AI;
using OpenScct.Maps;
using OpenScct.Server;

namespace OpenScctGame.Online;

/// <summary>Offers the map's prebuilt navigation before a local host starts a game with bots.</summary>
internal sealed partial class BotNavigationPrompt : ConfirmationDialog
{
    private const string PromptName = "BotNavigationPrompt";
    private string _folder = null!;
    private SvMapManifest _map = null!;
    private Action _launch = null!;
    private Func<bool> _stillValid = null!;
    private ContentBotNavigation? _navigation;
    private ContentPacks.DownloadJob? _job;
    private ulong _lookupDeadline;
    private bool _finished;

    public static bool Offer(Node parent, MapCatalog maps, string map, Action launch, Func<bool> stillValid)
    {
        if (parent.FindChild(PromptName, recursive: false, owned: false) is not null) return true;
        if (maps.FindFolder(map) is not { } folder) return false;

        SvMapManifest manifest;
        try
        {
            manifest = SvMapManifest.Load(Path.Combine(folder, SvMapHash.ManifestFile));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SvMapFormatException)
        {
            return false;
        }

        ContentIndex? index = ContentPacks.Index;
        ContentBotNavigation? nav = index?.FindBotNavigation(manifest.Map, manifest.SimHash, NavGraph.FormatVersion);
        if (nav is null ? index is not null || ContentPacks.IndexError is not null
            : AiNavCache.HasPreparedGraphs(folder, manifest.Map, nav.SimHash, nav.ActivationMask))
        {
            return false;
        }

        var prompt = new BotNavigationPrompt
        {
            Name = PromptName, _folder = folder, _map = manifest, _launch = launch, _stillValid = stillValid, _navigation = nav,
        };
        parent.AddChild(prompt);
        prompt.PopupCentered();
        return true;
    }

    public override void _Ready()
    {
        Title = "Bot navigation";
        OkButtonText = "Download and play";
        CancelButtonText = "Cancel";
        DialogHideOnOk = false;
        MinSize = new Vector2I(480, 155);
        AddButton("Build locally", true, "local");
        Confirmed += Download;
        Canceled += () => Finish(launch: false);
        CustomAction += action => { if (action == "local") Finish(launch: true); };
        _lookupDeadline = Time.GetTicksMsec() + 3000;
        UpdateOffer();
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        if (!_stillValid())
        {
            Finish(launch: false);
            return;
        }

        if (_job is { Done: true })
        {
            Finish(launch: true);
        }
        else if (_job is { Error: { } error })
        {
            DialogText = $"The bot navigation download failed. {error}\nTry again or build it locally.";
            OkButtonText = "Try again";
            GetOkButton().Disabled = false;
        }
        else if (_job is { } job)
        {
            DialogText = job.Describe();
        }
        else if (_navigation is null)
        {
            if (ContentPacks.Index is { } index)
            {
                _navigation = index.FindBotNavigation(_map.Map, _map.SimHash, NavGraph.FormatVersion);
                if (_navigation is null || AiNavCache.HasPreparedGraphs(_folder, _map.Map, _map.SimHash, _navigation.ActivationMask))
                {
                    Finish(launch: true);
                    return;
                }

                UpdateOffer();
            }
            else if (ContentPacks.IndexError is not null || Time.GetTicksMsec() >= _lookupDeadline)
            {
                Finish(launch: true);
            }
        }
    }

    public override void _ExitTree()
    {
        if (_job is { Running: true } job) job.Cancel();
    }

    private void UpdateOffer()
    {
        GetOkButton().Disabled = _navigation is null;
        DialogText = _navigation is { } nav
            ? string.Create(CultureInfo.InvariantCulture,
                $"Download prebuilt bot routes for {_map.Map} ({nav.Download.Size / 1048576.0:0.0} MiB)?\nBuilding them on this PC can take a few minutes.")
            : "Checking for prebuilt bot navigation…";
    }

    private void Download()
    {
        if (_navigation is not { } nav || _job is { Running: true } || _finished) return;
        GetOkButton().Disabled = true;
        _job = ContentPacks.DownloadBotNavigation(_map.Map, nav);
    }

    private void Finish(bool launch)
    {
        if (_finished) return;
        _finished = true;
        if (_job is { Running: true } job) job.Cancel();
        Hide();
        QueueFree();
        if (launch && _stillValid()) _launch();
    }
}
