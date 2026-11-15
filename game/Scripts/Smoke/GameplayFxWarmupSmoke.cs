using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.Audio;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Check loading completion, warmup cleanup, and first-use pipeline reuse with a real renderer.</summary>
public sealed class GameplayFxWarmupSmoke : SmokeTest
{
    public const string TestName = "gameplay-fx-warmup";
    public override string Name => TestName;
    private PlayRoot? _root;
    private GameplayFxWarmup? _repeat;
    private int _frames;
    private bool _complete;
    private double _drawCompiles;
    private int _securitySamples;
    private int _modelFilesAtLoad;
    private int _explosionSounds;
    private int _teardown;
    private SmokeResult? _result;
    private GameplayFxWarmup? _sceneWarmup;
    private bool _sceneComplete;
    private int _sceneVision, _controllerVision, _sceneLayouts;
    private long _shotAt;
    private int _shotsBefore;
    private double _shotPipelines;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return ++_teardown < 5 ? SmokeResult.Running : result;
        if (_root is null)
        {
            Diagnostics.LoadTimer.Begin("gameplay warmup smoke");
            context.Tree.Root.Msaa3D = Viewport.Msaa.Msaa4X;
            if (context.App.Options.PlayMap is not null)
            {
                _root = PlayRoot.Create(PlayConfig.FromOptions(context.App.Options));
                context.Tree.Root.AddChild(_root);
            }
            else
            {
                _root = TestRange.Start(context, PlaySide.Merc, 0);
            }
            _modelFilesAtLoad = Render.Characters.SkeletalModel.FilesRead;
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || _root.Session?.LocalPawn is null || ++_frames < 30) return SmokeResult.Running;
        if (Render.Characters.SkeletalModel.FilesRead != _modelFilesAtLoad)
            return Finish(SmokeResult.Fail($"first pawn appearance read {Render.Characters.SkeletalModel.FilesRead - _modelFilesAtLoad} model files after scene loading"));
        if (_root.GetNodeOrNull("GameplayFxWarmup") is not null) return Finish(SmokeResult.Fail("loading viewport leaked into gameplay"));
        if (!_sceneComplete)
        {
            if (_sceneWarmup is null)
            {
                _sceneVision = _root.View!.RendMap;
                _controllerVision = _root.Session.LocalController!.RendMap;
                _sceneWarmup = GameplayFxWarmup.For(_root.GetViewport());
                context.Tree.Root.AddChild(_sceneWarmup);
                _sceneWarmup.PreloadView(_root.View);
                // The browser uses general world shaders. The native proxy also has LightReach shortcut
                // materials: warm those at the now-bound player camera before measuring browser FX reuse.
                _root.Map?.LightReach.Warm();
                _sceneWarmup.PreloadMap(_root.Map);
                _sceneLayouts = _sceneWarmup.PreloadScene(_root, force: true);
                if (_sceneLayouts == 0) return Finish(SmokeResult.Fail("browser scene warmup had no real map/weapon layouts"));
                _sceneWarmup.Begin(() => _sceneComplete = true);
            }
            return SmokeResult.Running;
        }
        if ((_repeat is null || _complete) &&
            (_root.View!.RendMap != _sceneVision || _root.Session.LocalController!.RendMap != _controllerVision))
            return Finish(SmokeResult.Fail("scene warmup changed the player's vision state"));
        if (_root.Fx?.Effects is not { } effects || _root.MercLasers is not { } lasers)
            return Finish(SmokeResult.Fail("missing effects views"));
        if (_repeat is null)
        {
            if (CheckExplosionSounds() is { } soundError) return Finish(SmokeResult.Fail(soundError));
            int started = effects.Started.Count;
            int cameras = _root.Fx.DisabledCameraCount;
            int beams = lasers.BeamsDrawn;
            _drawCompiles = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);
            _repeat = GameplayFxWarmup.For(_root.GetViewport());
            if (_repeat.Msaa3D != Viewport.Msaa.Msaa4X) return Finish(SmokeResult.Fail("warmup lost gameplay MSAA setting"));
            context.Tree.Root.AddChild(_repeat);
            _root.Fx.Preload(_repeat.Samples);
            lasers.Preload(_repeat.Samples);
            _root.SpyLasers?.Preload(_repeat.Samples);
            _root.FirstPerson?.Warmup(_repeat.Samples);
            _root.SpyFirstPerson?.Warmup(_repeat.Samples);
            if (_root.Map is { } characterMap)
            {
                Render.Characters.PawnOutfits.Warmup(characterMap.Outfits, characterMap.Folder, characterMap.SharedRoot, _repeat.Samples);
                DeviceViews.PreloadSkeletalProps(characterMap, _repeat.Samples);
            }
            int effectSamples = _repeat.Samples.GetChildCount();
            if (_root.Map is { } map) DeviceViews.PreloadSecuritySkins(map, _repeat.Samples);
            _securitySamples = _repeat.Samples.GetChildCount() - effectSamples;
            _repeat.PreloadView(_root.View);
            _repeat.PreloadScene(_root, force: true);
            if (effects.Started.Count != started || _root.Fx.DisabledCameraCount != cameras || lasers.BeamsDrawn != beams)
                return Finish(SmokeResult.Fail("warmup changed live effect counters"));
            _repeat.Begin(() => _complete = true);
            return SmokeResult.Running;
        }
        if (!_complete) return SmokeResult.Running;
        double late = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) - _drawCompiles;
        if (_shotAt == 0)
        {
            if (late != 0) return Finish(SmokeResult.Fail($"first-use sample draw compiled {late} new pipelines after loading"));
            if (_root.Session.LocalPawn.MyWeapon is not SWeaponDefense { bDrawWeapon: true } gun || _root.FirstPerson?.Shown != true)
                return SmokeResult.Running;
            _shotAt = _root.Session.TicksRun;
            _shotsBefore = gun.ShotCounter;
            _shotPipelines = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);
            if (_root.Controls is { } controls) controls.Enabled = false;
            _root.Session.Input.Set("bFire", true);
            _root.Session.Input.Commands.Add(new InputCommand("Fire"));
            return SmokeResult.Running;
        }
        if (_root.Session.TicksRun - _shotAt >= 12) _root.Session.Input.Set("bFire", false);
        if (_root.Session.TicksRun - _shotAt < 45) return SmokeResult.Running;
        if (_root.Session.LocalPawn.MyWeapon is not SWeaponDefense fired || fired.ShotCounter == _shotsBefore)
            return Finish(SmokeResult.Fail("first-shot input did not fire the real rifle"));
        double shotLate = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) - _shotPipelines;
        if (shotLate != 0) return Finish(SmokeResult.Fail($"the real first rifle shot compiled {shotLate} new draw pipelines"));
        return Finish(SmokeResult.Pass($"effects and {_sceneLayouts} real scene/weapon layouts warmed across vision modes; warmup left gameplay counters untouched; vision restored; cleanup complete; security skin samples={_securitySamples}; explosion sound leaves cached={_explosionSounds}; repeat and real first-shot draw pipeline compilations=0"));
    }

    private string? CheckExplosionSounds()
    {
        if (_root?.Audio?.Library is not { } library || library.Find(new SProjFrag().SoundExplode.Path) is not { } sound)
            return "missing explosion audio fixture";
        int reads = library.FilesRead;
        var pending = new Stack<SoundDef>(); pending.Push(sound);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out SoundDef? next))
        {
            if (!visited.Add(next.Path)) continue;
            if (next.IsList)
            {
                foreach (var entry in next.Entries)
                {
                    if (library.Find(entry.Sound) is not { } child) return $"missing explosion sound {entry.Sound}";
                    pending.Push(child);
                }
            }
            else
            {
                if (library.Stream(next, false) is null) return $"unplayable explosion sound {next.Path}";
                _explosionSounds++;
            }
        }
        if (_explosionSounds == 0) return "explosion random list has no playable leaves";
        return library.FilesRead == reads ? null : "first explosion still read an audio file after loading";
    }

    private SmokeResult Finish(SmokeResult result)
    {
        if (GodotObject.IsInstanceValid(_repeat)) _repeat!.QueueFree();
        if (GodotObject.IsInstanceValid(_sceneWarmup)) _sceneWarmup!.QueueFree();
        _root?.QueueFree();
        _result = result;
        return SmokeResult.Running;
    }
}
