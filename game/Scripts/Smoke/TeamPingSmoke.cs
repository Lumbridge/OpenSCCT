using System;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Both roles exercise the actual play camera, input, loopback server and HUD on TestRange.</summary>
public sealed class TeamPingSmoke : SmokeTest
{
    public const string TestName = "team-pings";
    public override string Name => TestName;

    private PlayRoot? _root;
    private PlaySide _side = PlaySide.Spy;
    private TeamPingMark? _lastMark;
    private MeshInstance3D? _cover;
    private int _stage;
    private int _frames;
    private long _genericTick;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_root is null)
        {
            _root = PlayRoot.Create(new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = TestRange.Map, Side = _side, Mode = "Story", Rules = PlayRules.MapSettings,
                    HostMode = PlayHostMode.Inline, MapRoots = [.. context.App.Options.MapRoots],
                },
                Audio = false, TicksPerFrame = 2,
            });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (++_frames > 900)
        {
            var local = _root.Session?.LocalController;
            return Finish(SmokeResult.Fail($"{_side} timed out at stage {_stage}: capability={_root.Session?.Client.SupportsTeamPings} " +
                $"started={local?.GameReplicationInfo?.bMatchHasBegun} state={local?.GetStateName()} mark={_lastMark?.Kind}"));
        }
        if (_root.Session is not { } session || _root.Controls is not { } input || _root.View?.Camera is not { } camera ||
            _root.Camera is not { Occluded: false } || !session.View.Valid) return SmokeResult.Running;
        if (session.ServerFailure is { } failure) return Finish(SmokeResult.Fail(failure.Message));
        var overlay = _root.FindChild(nameof(TeamPingOverlay), owned: false) as TeamPingOverlay;
        if (overlay is null) return Finish(SmokeResult.Fail("the play HUD has no team ping overlay"));

        switch (_stage)
        {
            case 0:
                if (!session.Client.SupportsTeamPings || session.LocalController is not { GameReplicationInfo.bMatchHasBegun: true } ||
                    !TeamPingPlacement.TryGetSource(session.LocalController, out _, out _)) return SmokeResult.Running;
                if (CheckProjection(camera, _root.GetViewport().GetVisibleRect().Size) is { } projectionError)
                    return Finish(SmokeResult.Fail(projectionError));
                input.RequireCapture = input.CapturePointer = false;
                input.Enabled = true;
                input.Bindings = InputBindings.FromLayout(KeyLayout.Parse("[Engine.Input]\nIK_P=TeamPing"), _side);
                session.Client.TeamPingReceived += mark => _lastMark = mark;
                input.Key("P", true);
                input.Key("P", false);
                _stage++;
                break;
            case 1:
                if (_lastMark is not { Kind: TeamPingKind.Generic } generic || !overlay.Visible) return SmokeResult.Running;
                if (generic.Team != (byte)_side) return Finish(SmokeResult.Fail("tap used the wrong team"));
                if (_cover is null && MenuPage.Shot(overlay, $"team-ping-{_side}-marker") is not null)
                {
                    _cover = new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.6f, 1.1f, .05f) },
                        MaterialOverride = new StandardMaterial3D
                        {
                            AlbedoColor = new Color(.12f, .16f, .20f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        },
                        Position = camera.ToLocal(UeSpace.ToGodot(generic.Location)) * .2f,
                    };
                    camera.AddChild(_cover);
                    return SmokeResult.Running;
                }
                if (_cover is { } cover)
                {
                    MenuPage.Shot(overlay, $"team-ping-{_side}-through-cover");
                    cover.QueueFree();
                    _cover = null;
                }
                _genericTick = generic.ServerTick;
                input.ForegroundProbe = () => true;
                input._Process(0);
                input.Key("P", true);
                input.TeamPing.Advance(Time.GetTicksMsec() + TeamPingInput.HoldMilliseconds, inputAvailable: true);
                if (!input.TeamPing.WheelOpen) return Finish(SmokeResult.Fail("could not open the wheel for the focus-loss check"));
                input.TeamPing.StickDirection(0, -1);
                input.ForegroundProbe = () => false;
                input._Process(0);
                input.Key("P", false);
                input.ForegroundProbe = null;
                if (input.TeamPing.WheelOpen) return Finish(SmokeResult.Fail("focus loss left the ping wheel open"));
                _stage++;
                break;
            case 2:
                if (session.Client.Driver.NewestServerTick < _genericTick + Sim.Hz) return SmokeResult.Running;
                input.Key("P", true);
                input.TeamPing.Advance(Time.GetTicksMsec() + TeamPingInput.HoldMilliseconds, inputAvailable: true);
                input.TeamPing.StickDirection(_side == PlaySide.Merc ? 1 : 0, _side == PlaySide.Spy ? 1 : 0);
                if (!input.TeamPing.WheelOpen || input.TeamPing.Selection != ExpectedChoice)
                    return Finish(SmokeResult.Fail($"{_side} could not select {ExpectedChoice}"));
                _stage++;
                break;
            case 3:
                MenuPage.Shot(overlay, $"team-ping-{_side}-wheel");
                input.Key("P", false);
                _stage++;
                break;
            case 4:
                if (_lastMark is not { } selected || selected.Kind != ExpectedChoice || !overlay.Visible) return SmokeResult.Running;
                if (selected.ServerTick < _genericTick + Sim.Hz) return Finish(SmokeResult.Fail("the second ping bypassed cooldown"));
                _stage++;
                break;
            case 5:
                if (session.Client.Driver.NewestServerTick < _lastMark!.Value.ServerTick + 4 * Sim.Hz + 3) return SmokeResult.Running;
                if (overlay.Visible) return Finish(SmokeResult.Fail("the ping did not expire after four seconds"));
                Finish(SmokeResult.Running);
                if (_side == PlaySide.Merc) return SmokeResult.Pass("Spy and Merc tap and wheel, focus cancellation, loopback, projection, expiry");
                _side = PlaySide.Merc;
                _lastMark = null;
                _stage = _frames = 0;
                break;
        }

        return SmokeResult.Running;
    }

    private TeamPingKind ExpectedChoice => _side == PlaySide.Spy ? TeamPingKind.CoopBoost : TeamPingKind.Check;

    private static string? CheckProjection(Camera3D camera, Vector2 screen)
    {
        Vector3 origin = camera.GlobalPosition;
        Basis basis = camera.GlobalBasis;
        Vector3 ahead = origin - basis.Z * 10f;
        float inset = Math.Min(32f, Math.Min(screen.X, screen.Y) * .15f);
        if (!TeamPingOverlay.TryProject(camera, UeSpace.ToUe(ahead), screen, inset, out var middle) ||
            middle.AtEdge || middle.Point.DistanceTo(screen * .5f) > 2f)
            return $"center projection was {middle} at {screen}";
        if (!TeamPingOverlay.TryProject(camera, UeSpace.ToUe(ahead + basis.X * 100f), screen, inset, out var side) ||
            !side.AtEdge || !Mathf.IsEqualApprox(side.Point.X, screen.X - inset))
            return $"side projection was {side} at {screen}";
        if (!TeamPingOverlay.TryProject(camera, UeSpace.ToUe(origin + basis.Z * 10f), screen, inset, out var behind) || !behind.AtEdge)
            return $"rear projection was {behind} at {screen}";
        return null;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _root?.QueueFree();
        _root = null;
        return result;
    }
}
