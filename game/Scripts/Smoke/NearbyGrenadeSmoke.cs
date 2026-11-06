using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Timed nearby frag blasts through a real client, including its renderer, audio, HUD and death camera.</summary>
public sealed class NearbyGrenadeSmoke : SmokeTest
{
    public const string TestName = "nearby-grenade";
    public override string Name => TestName;
    private static readonly (float Distance, int Count, bool Lethal)[] Cases =
        [(650, 1, false), (425, 1, false), (100, 1, false), (0, 1, false), (100, 8, false), (100, 1, true)];
    private PlayRoot? _root;
    private SBasePawn? _victim;
    private int _side, _case, _frames, _teardown;
    private int _startedBefore;
    private bool _armed, _sawDamage, _sawDeath;
    private readonly List<string> _results = [];

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer and converted gameplay effects");
        if (_teardown > 0)
        {
            if (--_teardown > 0) return SmokeResult.Running;
            if (++_side == 2) return SmokeResult.Pass(string.Join("; ", _results));
        }
        if (_root is null)
        {
            context.Tree.Root.Msaa3D = Viewport.Msaa.Msaa4X;
            PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launcher with
            {
                Settings = launcher.Settings with
                {
                    Map = context.App.Options.PlayMap ?? "SteeE",
                    Side = _side == 0 ? PlaySide.Spy : PlaySide.Merc,
                    Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0,
                },
                Render = true, Hud = true, Audio = true, TicksPerFrame = 1, Pilot = null,
            });
            context.Tree.Root.AddChild(_root);
            _case = _frames = 0;
            _armed = false;
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (!_root.IsLoaded || _root.Session is not { } session) return SmokeResult.Running;
        if (session.ServerFailure is { } failure) return SmokeResult.Fail(failure.ToString());
        if (session.IsStopped || session.Client.State == EClientState.Disconnected)
            return SmokeResult.Fail("session stopped: " + session.Client.Connection?.CloseReason);
        if (++_frames < 900 && !_armed && _case == 0) return SmokeResult.Running;
        if (!_armed)
        {
            if (session.ServerController is not { Pawn: SBasePawn victim } server || session.LocalPawn is null)
                return SmokeResult.Fail("missing player before blast");
            if (_root.Fx?.Effects is not { } effects || _root.Hud is null || _root.Audio is null)
                return SmokeResult.Fail("missing effects, HUD or audio");
            _victim = victim;
            var scenario = Cases[_case];
            victim.Health = session.LocalPawn.Health = scenario.Lethal ? 100 : 10000;
            server.fSpawnProtectionEnd = 0;
            // Use the eye's forward direction and the pawn's height: the close cases are in clear space around
            // the player rather than accidentally behind the spawn room's floor or wall.
            FVector direction = new FRotator(0, server.Rotation.Yaw, 0).Vector();
            FVector at = victim.Location + direction * scenario.Distance;
            _startedBefore = effects.Started.GetValueOrDefault(nameof(SGrenadeExplosion));
            for (int i = 0; i < scenario.Count; i++)
            {
                SProjFrag grenade = session.Host.World.Level.SpawnActor(new SProjFrag(), at, default, victim, victim)!;
                grenade.SetPhysics(EPhysics.PHYS_None);
                grenade.SetTimer(.2f, false);
            }
            _sawDamage = _sawDeath = false;
            _frames = 0;
            _armed = true;
            GD.Print($"SVM_GRENADE side={_side} case={_case} distance={scenario.Distance} count={scenario.Count} lethal={scenario.Lethal}");
            return SmokeResult.Running;
        }
        _sawDamage |= _victim!.Health < (Cases[_case].Lethal ? 100 : 10000);
        _sawDeath |= _victim.Health <= 0;
        if (_root.View is { } view && !view.Camera.GlobalPosition.IsFinite())
            return SmokeResult.Fail("non-finite camera after blast");
        if (_frames < 240) return SmokeResult.Running;
        int started = _root.Fx!.Effects!.Started.GetValueOrDefault(nameof(SGrenadeExplosion)) - _startedBefore;
        if (started != Cases[_case].Count) return SmokeResult.Fail($"case {_case}: expected {Cases[_case].Count} rendered explosions, saw {started}");
        if (Cases[_case].Distance <= 100 && !_sawDamage) return SmokeResult.Fail($"case {_case}: nearby blast did not reach the player");
        if (Cases[_case].Lethal != _sawDeath) return SmokeResult.Fail($"case {_case}: unexpected death state {_sawDeath}");
        if (_root.Fx.Effects.PeakParticles.GetValueOrDefault(nameof(SGrenadeExplosion)) <= 0)
            return SmokeResult.Fail("explosion particles were never drawn");
        _results.Add($"{(_side == 0 ? "spy" : "merc")} {_case}: {started} blasts, damage={_sawDamage}, death={_sawDeath}");
        GD.Print("SVM_GRENADE OK " + _results[^1]);
        _armed = false;
        if (++_case == Cases.Length)
        {
            _root.QueueFree();
            _root = null;
            _teardown = 8;
        }
        return SmokeResult.Running;
    }
}
