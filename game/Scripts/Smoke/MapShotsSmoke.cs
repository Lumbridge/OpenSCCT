using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=map-shots</c> (docs/MAPS.md): plays each converted Story and Deathmatch map in turn as the launcher
/// does (spy, Play Level rules, host thread, wall clock, HUD, no bots) and, once the spy is drawn at his start, turns
/// the view a quarter turn at a time with the mouse (KeyPilot), so the player's own view is seen four ways, then
/// presses the goggle key (night vision, as a spy sees a dark map) and takes the four views again. With a window and
/// <c>SVM_PLAY_SHOTS</c> naming a folder under local/ it saves them as <c>&lt;map&gt;-&lt;n&gt;.png</c> and
/// <c>&lt;map&gt;-nv&lt;n&gt;.png</c>. Each
/// map prints one <c>SVM_MAPSHOTS</c> line: load time, render warnings (missing textures, meshes, materials not drawn)
/// and per view the share of black and of magenta pixels. <c>SVM_MAPSHOTS_MAPS</c> limits the maps (comma list, or
/// story / deathmatch). Fails only when a map does not load or the spy never appears.
/// </summary>
public sealed class MapShotsSmoke : SmokeTest
{
    public const string TestName = "map-shots";

    /// <summary>Frames the spy is left standing at his start before the first view (the HUD and the views settle).</summary>
    private const int SettleFrames = 90;

    /// <summary>Mouse counts sent each frame while turning, the longest a quarter turn may take, and frames after it before the view is saved.</summary>
    private const float TurnCountsPerFrame = 12f;

    private const int MaxTurnFrames = 240;

    private const int AfterTurnFrames = 20;

    private const int QuarterTurn = 16384;

    /// <summary>Views per round: the first round in normal vision, the second with night vision.</summary>
    private const int NightViews = 4;

    private const int GiveUpFrames = 60 * 90;

    private readonly List<string> _maps = new();
    private readonly List<string> _failures = new();
    private readonly List<string> _lines = new();
    private int _index = -1;
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private int _frames;
    private int _view;
    private int _phaseFrames;
    private int _teardown;
    private string _views = string.Empty;
    private int _turnFrom;
    private int _turnedAt = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_index < 0)
        {
            SelectMaps();
            _index = 0;
            if (_maps.Count == 0)
            {
                return SmokeResult.Fail("no converted Story or Deathmatch maps found");
            }
        }

        if (_teardown > 0)
        {
            // Frames after freeing one map's scene, so its server thread stops before the next starts.
            if (--_teardown > 0)
            {
                return SmokeResult.Running;
            }

            _index++;
        }

        if (_index >= _maps.Count)
        {
            string summary = string.Create(CultureInfo.InvariantCulture, $"{_maps.Count} maps, {_failures.Count} failed");
            return _failures.Count == 0 ? SmokeResult.Pass(summary) : SmokeResult.Fail(summary + ": " + string.Join("; ", _failures));
        }

        string map = _maps[_index];
        if (_root is null)
        {
            _root = Start(context, map);
            _keys = null;
            _frames = 0;
            _view = 0;
            _phaseFrames = 0;
            _turnedAt = -1;
            _views = string.Empty;
            return SmokeResult.Running;
        }

        _frames++;
        if (_root.LoadError is { } error)
        {
            return Next(map, "did not load: " + error);
        }

        if (_root.Session?.ServerFailure is { } failure)
        {
            return Next(map, "server failed: " + failure.Message);
        }

        if (!_root.IsLoaded || _root.Session?.LocalPawn is null || _root.Session.LocalController is not { } pc || pc.IsInState("WaitingForPawn"))
        {
            return _frames > GiveUpFrames ? Next(map, "the spy never appeared") : SmokeResult.Running;
        }

        _keys ??= KeyPilot.Attach(_root);
        _keys.Update();
        if (!_keys.Ready)
        {
            return SmokeResult.Running;
        }

        _phaseFrames++;
        if (_view == 0 || _view == NightViews)
        {
            // The second round of views is with night vision, as a spy sees a dark map: the goggle key once.
            if (_view == NightViews && _phaseFrames == 1 && !_keys.Tap("SwitchHeadset 3"))
            {
                _views += " (no night vision key)";
            }

            _turnFrom = pc.Rotation.Yaw;
            if (_phaseFrames < SettleFrames)
            {
                return SmokeResult.Running;
            }
        }
        else if (_turnedAt < 0)
        {
            // Turn with the mouse until the view has come a quarter round (or give up turning after MaxTurnFrames).
            int turned = Math.Abs((short)(pc.Rotation.Yaw - _turnFrom));
            if (turned < QuarterTurn - 400 && _phaseFrames < MaxTurnFrames)
            {
                _keys.Look(TurnCountsPerFrame, 0f);
                return SmokeResult.Running;
            }

            _turnedAt = _phaseFrames;
            return SmokeResult.Running;
        }
        else if (_phaseFrames - _turnedAt < AfterTurnFrames)
        {
            return SmokeResult.Running;
        }

        _turnFrom = pc.Rotation.Yaw;
        _turnedAt = -1;

        _views += " " + Shot(map, _view, pc.Rotation.Yaw);
        _view++;
        _phaseFrames = 0;
        return _view < 2 * NightViews ? SmokeResult.Running : Next(map, null);
    }

    private void SelectMaps()
    {
        string filter = (System.Environment.GetEnvironmentVariable("SVM_MAPSHOTS_MAPS") ?? "all").Trim();
        // Every map the host list or the tutorials play (docs/MAPS.md "The map catalog"); the front end maps are not played.
        var catalog = MapCatalog.CreateDefault().GetCatalog().Where(e => e.CanRender && e.Kind is MapKind.Versus or MapKind.Training)
            .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var all = catalog.Select(e => e.Id).ToList();
        _maps.AddRange(filter.ToLowerInvariant() switch
        {
            "all" or "" => all,
            // The last letter of a map code is its version (E Enhanced, D the default map), not its mode.
            "enhanced" => catalog.Where(e => e.Kind == MapKind.Versus && e.VariantWord is "Enhanced" or "Enh").Select(e => e.Id),
            "default" => catalog.Where(e => e.Kind == MapKind.Versus && e.VariantWord is null).Select(e => e.Id),
            _ => filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        });
    }

    private static PlayRoot Start(SmokeContext context, string map)
    {
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = map,
                // A merc tutorial (TME) is played as a merc, as the tutorials menu does.
                Side = map.StartsWith("TME", StringComparison.OrdinalIgnoreCase) ? PlaySide.Merc : PlaySide.Spy,
                // The mode the host menu preselects on the map (Story when it plays).
                Mode = MapCatalog.ModeKey(MapCatalog.CreateDefault().GetCatalogEntry(map)?.DefaultMode ?? OpenScct.Core.EGameMode.GM_Multi),
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                Bots = 0,
            },
            Render = true,
            Hud = true,
            TicksPerFrame = 0,
            Pilot = null,
        };
        PlayRoot root = PlayRoot.Create(config);
        context.Tree.Root.AddChild(root);
        return root;
    }

    /// <summary>Saves the view (with a window and SVM_PLAY_SHOTS) and returns its pixel shares.</summary>
    private string Shot(string map, int view, int yaw)
    {
        if (DisplayServer.GetName() == "headless" || _root is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"v{view}=yaw{yaw & 0xffff}");
        }

        Image image = _root.GetViewport().GetTexture().GetImage();
        int w = image.GetWidth();
        int h = image.GetHeight();
        int black = 0;
        int magenta = 0;
        int samples = 0;
        for (int y = 0; y < h; y += 4)
        {
            for (int x = 0; x < w; x += 4)
            {
                Color c = image.GetPixel(x, y);
                samples++;
                if (c.R < 0.02f && c.G < 0.02f && c.B < 0.02f)
                {
                    black++;
                }
                else if (c.R > 0.8f && c.B > 0.8f && c.G < 0.25f)
                {
                    magenta++;
                }
            }
        }

        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            System.IO.Directory.CreateDirectory(folder);
            image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{map}-{(view < NightViews ? "" : "nv")}{view % NightViews}.png")));
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"v{view}=yaw{yaw & 0xffff}/black{100f * black / Math.Max(1, samples):F0}%/magenta{100f * magenta / Math.Max(1, samples):F1}%");
    }

    private SmokeResult Next(string map, string? failure)
    {
        RenderMapWarnings(map, failure);
        _keys?.ReleaseAll();
        _root?.QueueFree();
        _root = null;
        _keys = null;
        _teardown = 30;
        return SmokeResult.Running;
    }

    private void RenderMapWarnings(string map, string? failure)
    {
        IReadOnlyList<string> warnings = (IReadOnlyList<string>?)_root?.Map?.Warnings ?? [];
        string line = string.Create(CultureInfo.InvariantCulture,
            $"SVM_MAPSHOTS {map} {(failure is null ? "ok" : "FAIL " + failure)} load={_root?.LoadSeconds ?? 0:F1}s warnings={warnings.Count}{_views}");
        GD.Print(line);
        foreach (IGrouping<string, string> g in warnings.GroupBy(w => w.Split(':')[0]))
        {
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"SVM_MAPSHOTS {map} warning {g.Key} x{g.Count()}: {string.Join(", ", g.Take(6).Select(w => w.Contains(':', StringComparison.Ordinal) ? w[(w.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim() : w))}"));
        }

        _lines.Add(line);
        if (failure is not null)
        {
            _failures.Add(map + " " + failure);
        }
    }
}
