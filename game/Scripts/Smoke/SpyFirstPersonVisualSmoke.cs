using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net.Cosmetics;
using OpenScct.Physics.Collision;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Windowed Spy viewmodel captures with stock lighting, cosmetics and vision at fixed coordinates.</summary>
public sealed class SpyFirstPersonVisualSmoke : SmokeTest
{
    public const string TestName = "spy-first-person-visual";
    private const string Map = "AquaE";
    private const string Perspective = "RB:perspective";
    private const string Ready = ControlActions.VirtualPrefix + "Y:ready";
    private const string Suit = "spy.body.red_tiger";
    private const string Gun = "spy.pistol.golden";
    private const int Normal = RenderK.RendMapNormal;
    private const ETraceFlags Geometry = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_LevelGeometry;
    // Inside the walkable room, just forward of the offscreen viewmodel-light reference.
    private static readonly FVector BlueRoomEye = new(7152f, 1200f, 272.5f);
    private static readonly Vector2I Standard = new(1024, 576);
    private static readonly Vector2I Narrow = new(800, 600);
    private static readonly Vector2I Wide = new(1280, 540);

    private sealed record Shot(string Label, bool FirstPerson, int Vision, Vector2I Size, string? Key = null, bool Dressed = false);
    private static readonly Shot[] Shots =
    [
        new("stock-third-normal", false, Normal, Standard),
        new("stock-first-normal", true, Normal, Standard, Perspective),
        new("cosmetics-first-normal", true, Normal, Standard, Dressed: true),
        new("cosmetics-third-normal", false, Normal, Standard, Perspective, true),
        new("cosmetics-first-narrow-normal", true, Normal, Narrow, Perspective, true),
        new("cosmetics-first-narrow-night", true, SGoggle.REN_NightVision, Narrow, "vision1", true),
        new("cosmetics-first-wide-thermal", true, SGoggle.REN_ThermalVision, Wide, "vision2", true),
        new("cosmetics-first-wide-normal", true, Normal, Wide, "vision2", true),
    ];

    private PlayRoot? _root;
    private GameSettings? _settings;
    private string[] _oldCosmetics = [];
    private bool _oldHideMine;
    private string _oldPerspective = "";
    private Vector2I _oldSize;
    private FVector? _position;
    private int _phase = -2, _teardown = -1;
    private long _since = -1;
    private SmokeResult _result;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown >= 0) return ++_teardown < 6 ? SmokeResult.Running : _result;
        if (_root is null)
        {
            if (DisplayServer.GetName() == "headless")
                return SmokeResult.Fail("requires a window and renderer");
            _settings = SettingsStore.Current;
            (_oldCosmetics, _oldHideMine, _oldPerspective) =
                (_settings.CosmeticsSpy, _settings.CosmeticsHideMine, _settings.SpyKbm.PerspectiveBehavior);
            _settings.CosmeticsHideMine = true;
            _settings.CosmeticsSpy = [];
            _settings.SpyKbm.PerspectiveBehavior = "Toggle";
            _oldSize = context.Tree.Root.Size;
            context.Tree.Root.Size = Standard;
            PlayConfig config = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(config with
            {
                Settings = config.Settings with { Map = Map, Mode = "Story", Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Thread, Bots = 0 },
                Render = true, Hud = true, TicksPerFrame = 0, Pilot = null,
            });
            context.Tree.Root.AddChild(_root);
        }

        if (_root.LoadError is { } error) return Finish(context, SmokeResult.Fail(error));
        if (_root.Session is not { } session || _root.Controls is not { } input) return SmokeResult.Running;
        if (session.ServerFailure is { } failure) return Finish(context, SmokeResult.Fail(failure.Message));
        if (session.LoginError is { } login) return Finish(context, SmokeResult.Fail(login));
        if (session.LocalController is not { } pc || session.LocalPawn is not SPawnAttaque pawn ||
            _root.Pawns?.ViewOf(pawn) is not { } body || _root.SpyFirstPerson is not { } rig)
            return SmokeResult.Running;
        if (!rig.Available) return Finish(context, SmokeResult.Fail("the converted first-person arms or P190 are missing"));

        long elapsed = session.TicksRun - _since;
        if (_since >= 0 && elapsed > 360)
            return Finish(context, SmokeResult.Fail($"phase {_phase} timed out, state={pc.GetStateName()}, weapon={pawn.WeaponLoaded}, " +
                $"eye={pc.SpyFirstPersonActive}, rig={rig.Shown}, " +
                $"vision={pc.RendMap}, drawn={_root.View?.Effect.LastDrawnRendMap}, size={context.Tree.Root.Size}"));
        if (_since < 0)
        {
            if (!TestRange.Walking(pc)) return SmokeResult.Running;
            input.Enabled = true;
            input.RequireCapture = false;
            input.CapturePointer = false;
            if (session.Client.Map?.Physics?.Collision is not { } collision ||
                !collision.LineCheck(out var floor, pawn, BlueRoomEye - new FVector(0, 0, 160), BlueRoomEye, default, Geometry))
                return Finish(context, SmokeResult.Fail("no floor below the AquaE Blue Room reference"));
            TestRange.Place(session, _ => session.ServerController, floor.Location, 16384);
            TestRange.Face(pc, 16384);
            _since = session.TicksRun;
            return SmokeResult.Running;
        }
        if (_phase == -2 && elapsed >= 35 && TestRange.Walking(pc))
        {
            input.Key(Ready, true);
            _phase = -1;
            _since = session.TicksRun;
        }
        else if (_phase == -1 && elapsed >= 8)
        {
            input.Key(Ready, false);
            Advance(context, session);
        }
        else if (_phase >= 0 && elapsed >= 30)
        {
            Shot shot = Shots[_phase];
            int vision = pc.RendMap == 0 ? Normal : pc.RendMap;
            if (pawn.WeaponLoaded != 2 || pc.SpyFirstPersonActive != shot.FirstPerson || body.ShadowOnly != shot.FirstPerson ||
                rig.Shown != shot.FirstPerson || vision != shot.Vision ||
                _root.View?.Effect.LastDrawnRendMap != shot.Vision || context.Tree.Root.Size != shot.Size)
                return SmokeResult.Running;
            if (shot.FirstPerson && _root.Camera is not { Occluded: false })
                return Finish(context, SmokeResult.Fail($"{shot.Label}: camera is occluded"));
            if (shot.FirstPerson && rig.SupportContactError is < 0f or > 0.008f)
                return Finish(context, SmokeResult.Fail($"{shot.Label}: the support hand lost the P190 grip ({rig.SupportContactError:F3} m)"));
            _position ??= pawn.Location;
            if ((_position.Value - pawn.Location).SizeSquared() > 64f ||
                MathF.Abs(pawn.Location.X - BlueRoomEye.X) > 30f || MathF.Abs(pawn.Location.Y - BlueRoomEye.Y) > 30f)
                return Finish(context, SmokeResult.Fail($"{shot.Label}: pawn left the reference spot ({pawn.Location})"));

            SkeletalModel? weapon = rig.WeaponModel;
            if (!shot.FirstPerson && (body.Attachments is null || !body.Attachments.TryGetWeapon(out _, out weapon)))
                return Finish(context, SmokeResult.Fail($"{shot.Label}: world weapon is missing"));
            if (shot.Dressed && !HasFinish(weapon, Gun))
                return Finish(context, SmokeResult.Fail($"{shot.Label}: the chosen P190 finish is missing"));
            if (shot.Dressed && !HasFinish(shot.FirstPerson ? rig.ArmsModel : body.Model, Suit))
                return Finish(context, SmokeResult.Fail($"{shot.Label}: the chosen Spy suit is missing"));

            TestRange.Shot(_root, Name, session.TicksRun / 60.0, shot.Label);
            if (_phase == Shots.Length - 1)
                return Finish(context, SmokeResult.Pass("AquaE Blue Room, matching third- and first-person finishes, normal/night/thermal at 16:9, 4:3 and 64:27"));
            Advance(context, session);
        }
        return SmokeResult.Running;
    }

    private void Advance(SmokeContext context, PlaySession session)
    {
        Shot shot = Shots[++_phase];
        context.Tree.Root.Size = shot.Size;
        if (shot.Dressed) _settings!.CosmeticsSpy = [Suit, Gun];
        if (shot.Key is { } key)
        {
            _root!.Controls!.Key(ControlActions.VirtualPrefix + key, true);
            _root.Controls.Key(ControlActions.VirtualPrefix + key, false);
        }
        _since = session.TicksRun;
    }

    private static bool HasFinish(SkeletalModel? model, string id)
    {
        string suffix = "+" + CosmeticCatalog.Find(id)!.Pattern;
        if (model is null) return false;
        foreach (MeshInstance3D mesh in model.Meshes)
            for (int surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++)
                if (mesh.GetSurfaceOverrideMaterial(surface)?.ResourceName.EndsWith(suffix, StringComparison.Ordinal) == true)
                    return true;
        return false;
    }

    private SmokeResult Finish(SmokeContext context, SmokeResult result)
    {
        _result = result;
        _teardown = 0;
        if (_settings is not null)
        {
            (_settings.CosmeticsSpy, _settings.CosmeticsHideMine, _settings.SpyKbm.PerspectiveBehavior) =
                (_oldCosmetics, _oldHideMine, _oldPerspective);
            context.Tree.Root.Size = _oldSize;
        }
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
