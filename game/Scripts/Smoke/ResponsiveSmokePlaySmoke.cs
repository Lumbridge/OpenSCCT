using System;
using System.Globalization;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Options;

namespace OpenScctGame.Smoke;

/// <summary>Real map, grenade, rifle and frag hooks through an inline server and rendered client.</summary>
public sealed class ResponsiveSmokePlaySmoke : SmokeTest
{
    public const string TestName = "responsive-smoke-play";
    public override string Name => TestName;
    private PlayRoot? _root;
    private SSmokeEmitter? _cloud;
    private SWeaponDefense? _rifle;
    private FVector _center, _eye;
    private FRotator _aim;
    private int _warmup, _frame = -1, _captures, _teardown;
    private string? _folder;
    private bool _sawClientField;
    private ResponsiveSmokeQuality? _oldQuality;
    private int _vision = RenderK.RendMapNormal, _burstShots;
    private SmokeResult? _finished;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_finished is { } result) return ++_teardown < 4 ? SmokeResult.Running : result;
        if (DisplayServer.GetName() == "headless") return Finish(SmokeResult.Fail("requires a renderer and converted map"));
        if (ResponsiveSmokeRenderer.UnsupportedReason is { } unsupported) return Finish(SmokeResult.Fail(unsupported));
        if (_root is null)
        {
            _oldQuality = SettingsStore.Current.SmokeQuality;
            SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.Medium;
            PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launcher with
            {
                Settings = launcher.Settings with
                {
                    Map = context.App.Options.PlayMap ?? "TestRange", Side = PlaySide.Merc,
                    Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0, ResponsiveSmoke = true,
                },
                Render = true, Hud = true, Audio = true, TicksPerFrame = 1, Pilot = null, CaptureFrameHistory = true,
            });
            context.Tree.Root.AddChild(_root);
            _folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ??
                Path.Combine(RenderSmokeArgs.RepoRoot, "local", "responsive-smoke", "play-" + _root.Config.Settings.Map);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "frames.csv"), "frame,simulation_seconds,server_density,client_density,carves,clouds,quality,vision,simulation_ms,view_ms,gpu_surface_ms,gpu_smoke_ms\n");
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || _root.Session is not { } session) return SmokeResult.Running;
        if (ResponsiveSmokeRenderer.VolumeError is { } volumeError) return Finish(SmokeResult.Fail(volumeError));
        if (session.ServerFailure is { } failure) return Finish(SmokeResult.Fail(failure.ToString()));
        if (session.IsStopped || session.Client.State == EClientState.Disconnected)
            return Finish(SmokeResult.Fail("session stopped: " + session.Client.Connection?.CloseReason));
        if (++_warmup < 300) return SmokeResult.Running;
        if (session.ServerController is not { Pawn: SBasePawn pawn } server || session.LocalPawn is null)
            return Finish(SmokeResult.Fail("missing player"));
        if (_frame < 0)
        {
            if (pawn.MyWeapon is not SWeaponDefense rifle) return Finish(SmokeResult.Fail("merc rifle was not equipped"));
            if (!ResponsiveSmokeWorld.IsEnabled(session.Host.World.Level))
                return Finish(SmokeResult.Fail("host smoke option was not applied"));
            _rifle = rifle;
            pawn.Health = session.LocalPawn.Health = 10000;
            server.fSpawnProtectionEnd = 0;
            if (_root.Controls is { } controls) controls.Enabled = false;
            // Pick a clear direction from the real spawn; --start-at can select another map area.
            FVector eye = _eye = pawn.Location + new FVector(0, 0, pawn.EyeHeight);
            float clear = 0;
            for (int yaw = 0; yaw < 65536; yaw += 4096)
            {
                var candidate = new FRotator(0, yaw, 0);
                MovementNatives.Trace(pawn, out FVector hit, out _, eye + candidate.Vector() * 1000, eye, true, new FVector(40, 40, 40));
                float distance = FVector.Dist(eye, hit);
                if (distance > clear) { clear = distance; _aim = candidate; }
            }
            if (clear < 450) return Finish(SmokeResult.Fail($"spawn has only {clear:F0} uu of clear space; choose --start-at for this map"));
            server.Rotation = server.MyDesiredRotation = rifle.AccurateRotation = _aim;
            var ray = rifle.AimRay(_aim);
            _center = ray.Start + (ray.End - ray.Start).SafeNormal() * Math.Min(600, clear - 100);
            session.ViewOverride = view => view with { Location = eye, Rotation = _aim, Fov = 75, Valid = true };
            RenderingServer.ViewportSetMeasureRenderTime(_root.GetViewport().GetViewportRid(), true);
            GD.Print($"SVM_RESPONSIVE_PLAY map={_root.Config.Settings.Map} eye={eye} center={_center} yaw={_aim.Yaw} capture_hz={Sim.Hz / 4} folder={_folder}");
        }
        _frame++;
        server.Rotation = server.MyDesiredRotation = _rifle!.AccurateRotation = _aim;
        if (session.LocalController is { } local)
        {
            local.Rotation = local.MyDesiredRotation = _aim;
            local.RendMap = _vision;
        }
        ResponsiveSmokeWorld smoke = ResponsiveSmokeWorld.For(session.Host.World.Level);
        ResponsiveSmokeWorld clientSmoke = ResponsiveSmokeWorld.For(session.Client.World.Level);
        _sawClientField |= clientSmoke.Fields.Count > 0;
        if (_frame % 4 == 0) Capture(smoke, clientSmoke);
        if (_frame == 30)
        {
            _cloud = SpawnSmoke(session, pawn, _center);
            if (_cloud is null || !smoke.Fields.ContainsKey(_cloud)) return Finish(SmokeResult.Fail("real smoke grenade did not register a cloud"));
            Phase("grenade");
        }
        if (_frame == 150)
        {
            if (smoke.SampleDensity(_center) < .8f) return Finish(SmokeResult.Fail("map cloud did not mature at its clear seed"));
            _rifle.TraceFire(0, 0, 0);
            if (smoke.SampleDensity(_center) > .1f || !HasCarve(smoke, ResponsiveSmokeCarveKind.Bullet))
                return Finish(SmokeResult.Fail("real rifle shot did not clear the cloud center"));
            Phase("rifle");
        }
        if (_frame == 210)
        {
            if (smoke.SampleDensity(_center) < .8f) return Finish(SmokeResult.Fail("rifle opening did not refill"));
            Phase("rifle-refilled");
        }
        if (_frame == 270)
        {
            SProjFrag frag = session.Host.World.Level.SpawnActor(new SProjFrag(), _center, default, pawn, pawn)!;
            frag.SetPhysics(EPhysics.PHYS_None);
            frag.Explode(_center, new FVector(0, 0, 1));
            if (smoke.SampleDensity(_center) > .1f || !HasCarve(smoke, ResponsiveSmokeCarveKind.Blast))
                return Finish(SmokeResult.Fail("real frag did not clear the cloud center"));
            Phase("frag");
        }
        if (_frame == 420)
        {
            if (smoke.SampleDensity(_center) < .8f) return Finish(SmokeResult.Fail("frag opening did not refill"));
            Phase("frag-refilled");
        }
        if (_frame == 690)
        {
            if (!_sawClientField || smoke.Fields.Count != 0 || clientSmoke.Fields.Count != 0)
                return Finish(SmokeResult.Fail($"first lifecycle mismatch: saw-client={_sawClientField}, server={smoke.Fields.Count}, client={clientSmoke.Fields.Count}"));
            Phase("expired");
        }
        if (_frame == 720)
        {
            _cloud = SpawnSmoke(session, pawn, _center);
            _aim.GetAxes(out _, out FVector right, out _);
            SpawnSmoke(session, pawn, _center + right * 100);
            Phase("overlap");
        }
        if (_frame == 810)
        {
            if (smoke.Fields.Count != 2 || clientSmoke.Fields.Count != 2)
                return Finish(SmokeResult.Fail("overlapping clouds did not replicate"));
            _vision = RenderK.RendMapNight;
            Phase("night-medium");
        }
        if (_frame is >= 820 and <= 1050 && _frame % 6 == 0)
        {
            _rifle.TraceFire(0, 0, 0);
            _burstShots++;
            if (smoke.SampleDensity(_center) > .1f) return Finish(SmokeResult.Fail("sustained shots failed to clear overlapping smoke"));
        }
        if (_frame == 870) { _vision = RenderK.RendMapMotion; SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.Low; Phase("motion-low"); }
        if (_frame == 930) { _vision = RenderK.RendMapThermal; SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.Medium; Phase("thermal-medium"); }
        if (_frame == 990) { _vision = RenderK.RendMapEmf; SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.High; Phase("emf-high"); }
        if (_frame == 1050) { _vision = RenderK.RendMapNormal; Phase("normal-high"); }
        if (_frame is 830 or 890 or 950 or 1010 or 1070 && _root.View?.RendMap != _vision)
            return Finish(SmokeResult.Fail($"vision {_vision} did not reach the rendered view"));
        if (_frame == 1140)
        {
            if (smoke.SampleDensity(_center) < .8f || _burstShots < 35) return Finish(SmokeResult.Fail("overlap did not refill after sustained fire"));
            session.ViewOverride = view => view with { Location = _center, Rotation = _aim, Fov = 75, Valid = true };
            Phase("inside-high");
        }
        if (_frame == 1170)
        {
            session.ViewOverride = view => view with { Location = _eye, Rotation = _aim, Fov = 75, Valid = true };
            Phase("outside-high");
        }
        if (_frame >= 148 && System.Environment.GetEnvironmentVariable("SVM_RESPONSIVE_SMOKE_PREVIEW") == "1")
            return Finish(SmokeResult.Pass($"{_root.Config.Settings.Map}: lighting preview only; {_captures} PNG frames in {_folder}"));
        if (_frame < 1380) return SmokeResult.Running;
        if (!_sawClientField || smoke.Fields.Count != 0 || clientSmoke.Fields.Count != 0)
            return Finish(SmokeResult.Fail($"replicated lifecycle mismatch: saw-client={_sawClientField}, server={smoke.Fields.Count}, client={clientSmoke.Fields.Count}"));
        Phase("expired");
        return Finish(SmokeResult.Pass($"{_root.Config.Settings.Map}: grenade growth, rifle/frag clearing and refill, overlap with {_burstShots} shots, five vision modes, three qualities, inside/outside and replicated expiry; {_captures} PNG frames in {_folder}; {_root.Stats.Summary()}"));
    }

    private static SSmokeEmitter? SpawnSmoke(PlaySession session, SBasePawn pawn, FVector center)
    {
        SProjSmoke grenade = session.Host.World.Level.SpawnActor(new SProjSmoke(), center, default, pawn, pawn)!;
        grenade.SetPhysics(EPhysics.PHYS_None);
        grenade.Explode(center - new FVector(0, 0, grenade.CollisionHeight + K.SmokeRaise), new FVector(0, 0, 1));
        return grenade.Smoke;
    }

    private bool HasCarve(ResponsiveSmokeWorld smoke, ResponsiveSmokeCarveKind kind)
    {
        if (_cloud is null || !smoke.Fields.TryGetValue(_cloud, out ResponsiveSmokeField? field)) return false;
        foreach (ResponsiveSmokeCarve carve in field.Carves) if (carve.Kind == kind) return true;
        return false;
    }

    private void Capture(ResponsiveSmokeWorld smoke, ResponsiveSmokeWorld clientSmoke)
    {
        using Image image = _root!.GetViewport().GetTexture().GetImage();
        Error saved = image.SavePng(Path.Combine(_folder!, $"frame-{_captures++:D5}.png"));
        if (saved != Error.Ok) throw new IOException("Could not save smoke frame: " + saved);
        int carves = _cloud is not null && smoke.Fields.TryGetValue(_cloud, out ResponsiveSmokeField? field) ? field.Carves.Count : 0;
        File.AppendAllText(Path.Combine(_folder!, "frames.csv"), string.Create(CultureInfo.InvariantCulture,
            $"{_frame},{_frame * Sim.DeltaTime:F4},{smoke.SampleDensity(_center):F4},{clientSmoke.SampleDensity(_center):F4},{carves},{smoke.Fields.Count},{SettingsStore.Current.SmokeQuality},{_vision},{_root.Stats.LastSimMs:F4},{_root.Stats.LastViewMs:F4},{RenderingServer.ViewportGetMeasuredRenderTimeGpu(_root.GetViewport().GetViewportRid()):F4},{ResponsiveSmokeRenderer.LastVolumeGpuMilliseconds:F4}\n"));
    }

    private void Phase(string phase) => GD.Print(string.Create(CultureInfo.InvariantCulture,
        $"SVM_RESPONSIVE_PLAY phase={phase} frame={_frame} seconds={_frame * Sim.DeltaTime:F3}"));

    private SmokeResult Finish(SmokeResult result)
    {
        _finished = result;
        if (_oldQuality is { } quality) SettingsStore.Current.SmokeQuality = quality;
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
