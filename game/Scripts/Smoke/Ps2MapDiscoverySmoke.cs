using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Default installed roots, the actual host picker and the two original training cards.</summary>
public sealed class Ps2MapDiscoverySmoke : SmokeTest
{
    public const string TestName = "ps2-map-discovery";
    public override string Name => TestName;
    private static readonly string[] Versus = "DTK02P FAC13P GAR09P INI01P KRA07P MHO05P MIS10P MUS04P ORP14P SCH03P SKY08P WAR06P".Split(' ');
    private HostScreen? _host;
    private MenuPilot? _pilot;
    private MapCatalog? _maps;
    private int _stage, _frames, _map, _team;
    private bool _clicked;
    private readonly List<string> _checked = [];
    public override SmokeResult Step(SmokeContext context)
    {
        if (_host is null && _stage == 0)
        {
            if (context.App.Options.MapRoots.Count > 0) return SmokeResult.Fail("default-path regression must not supply --svm-maps");
            SettingsStore.Current.ActiveMapPlaylist = MapCatalog.AllMapsPlaylist;
            _maps = OnlineService.Get(context.Tree).Catalog.Maps;
            foreach (string code in Versus.Concat(["TSP11P", "TME12P"]))
                if (_maps.GetCatalogEntry(code) is null) return SmokeResult.Fail("default catalog missing " + code);
            _pilot = new MenuPilot(context.Tree);
            _host = FrontEnd.ShowHost(context.Tree);
            _host.Model!.SelectMode(EGameMode.GM_Multi);
            _stage = 1;
            return SmokeResult.Running;
        }
        _pilot!.Update();
        if (_pilot.Busy || ++_frames < 8) return SmokeResult.Running;
        _frames = 0;
        if (_stage == 1)
        {
            if (!_pilot.Type(_host!.Picker!.SearchBox, "stat", "Station search")) return SmokeResult.Running;
            _stage = 2;
        }
        else if (_stage == 2)
        {
            Button? tile = _host!.Picker!.Tile("GAR09P");
            if (tile is not { Visible: true }) return SmokeResult.Fail("Story hides installed Station P (GAR09P) from the regular picker");
            if (!_pilot.Click(tile, "Station P")) return SmokeResult.Running;
            _stage = 3;
        }
        else if (_stage == 3)
        {
            if (_host!.Model!.Setup.Map != "GAR09P" || _maps!.GetCatalogEntry("GAR09P")!.Mode(_host.Model.Setup.Mode)?.Playable != true)
                return SmokeResult.Fail("Station click did not choose a compatible mode: " + _host.Model.Setup);
            Save(context, "ps2-station-normal-picker.png");
            if (!_pilot.Type(_host.Picker!.SearchBox, "", "clear map search")) return SmokeResult.Running;
            _stage = 4;
        }
        else if (_stage == 4)
        {
            if (_map < Versus.Length)
            {
                string code = Versus[_map];
                Button? tile = _host!.Picker!.Tile(code);
                if (tile is not { Visible: true }) return SmokeResult.Fail("installed versus map hidden " + code);
                if (!_clicked)
                {
                    if (!_pilot.Click(tile, code)) return SmokeResult.Running;
                    _clicked = true;
                    return SmokeResult.Running;
                }
                if (_host.Model!.Setup.Map != code || _maps!.GetCatalogEntry(code)!.Mode(_host.Model.Setup.Mode)?.Playable != true)
                    return SmokeResult.Fail("map selection retained an incompatible mode " + code + " " + _host.Model.Setup.Mode);
                _checked.Add(code + ":" + _host.Model.Setup.Mode);
                _map++; _clicked = false;
                return SmokeResult.Running;
            }
            foreach (string code in new[] { "TSP11P", "TME12P" })
                if (_maps!.GetCatalogEntry(code)?.Kind != MapKind.Training || _host!.Picker!.Tile(code) is not null)
                    return SmokeResult.Fail("single-team training map offered as versus " + code);
            TutorialMapsScreen.Show(context.Tree, _team);
            _stage = 5;
        }
        else if (_stage == 5)
        {
            string code = _team == 0 ? "TSP11P" : "TME12P";
            var tile = context.Tree.CurrentScene?.FindChild("Map_" + code, true, false) as Button;
            if (tile is not { Disabled: false, Visible: true }) return SmokeResult.Fail("training browser missing " + code);
            if (!_pilot.Click(tile, code + " training")) return SmokeResult.Running;
            _stage = 6;
        }
        else if (_stage == 6)
        {
            if (context.Tree.CurrentScene is not PlayRoot play || !play.IsLoaded || play.Session?.LocalPawn is null) return SmokeResult.Running;
            string code = _team == 0 ? "TSP11P" : "TME12P";
            if (play.Session.Settings.Map != code || (int)play.Session.LocalController!.SPawnType != _team)
                return SmokeResult.Fail("training card launched wrong map/side " + code);
            _checked.Add(code + ":team" + _team);
            Save(context, "ps2-training-team" + _team + ".png");
            if (_team++ == 0) { TutorialMapsScreen.Show(context.Tree, _team); _stage = 5; }
            else return SmokeResult.Pass("default installed roots; Story search and actual selection of 12 versus maps; both training cards launched correct side: " + string.Join(",", _checked));
        }
        return SmokeResult.Running;
    }
    private static void Save(SmokeContext context, string name)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using Image image = context.Tree.Root.GetTexture().GetImage();
        image.SavePng(Path.Combine(folder, name));
    }
}
