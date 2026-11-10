using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Controls;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Hud;

/// <summary>Connects the local gesture and projected HUD to the match's team ping stream.</summary>
internal sealed partial class TeamPingHud : CanvasLayer
{
    private const int LifetimeTicks = 4 * Sim.Hz;
    private const float FadeTicks = 0.75f * Sim.Hz;

    private readonly Dictionary<int, ActiveMark> _marks = new();
    private readonly List<TeamPingVisual> _visuals = new();
    private readonly TeamPingOverlay _overlay = new() { Name = nameof(TeamPingOverlay) };
    private PlayRoot _root = null!;
    private PlaySession _session = null!;
    private PlayInput _input = null!;
    private PlayHud.Parts _hud = null!;
    private (SBasePawn Pawn, byte Team)? _recipient;

    private readonly record struct ActiveMark(TeamPingMark Mark, long ReceivedTick);

    public static void Attach(PlayRoot root, PlaySession session, PlayInput input, PlayHud.Parts hud, Camera3D camera)
    {
        var layer = new TeamPingHud
        {
            Name = "TeamPingHud", Layer = HudLayer.DefaultLayer + 1,
            _root = root, _session = session, _input = input, _hud = hud,
        };
        layer._overlay.Camera = camera;
        layer.AddChild(layer._overlay);
        root.AddChild(layer);
    }

    public override void _Ready()
    {
        _input.TeamPing.Context = InputContext;
        _input.TeamPing.Submitted += Submit;
        _session.Client.TeamPingReceived += Receive;
    }

    public override void _ExitTree()
    {
        _session.Client.TeamPingReceived -= Receive;
        _input.TeamPing.Context = null;
        _input.TeamPing.Submitted -= Submit;
        _input.TeamPing.Cancel();
    }

    private bool Blocked => _root.Menu?.IsOpen == true || _hud.Chat?.IsOpen == true ||
        _hud.Status?.IsOpen == true || _hud.Scoreboard?.IsOpen == true ||
        _hud.Hud?.Visible == false || _hud.Hud?.Snapshot.HideHud == true ||
        _session.LocalController?.bCinematicHideHud == true;

    private object? InputContext() => PlacementSource() is { } source
        ? (source.Player.SBPawn, source.Actor, source.Team)
        : null;

    private (SPlayerController Player, Actor Actor, byte Team)? PlacementSource()
    {
        if (Blocked || _session.IsStopped || _session.ViewOverride is not null || !_session.View.Valid ||
            _root.Camera is not { Occluded: false } || !_session.Client.SupportsTeamPings ||
            _session.Client.State != EClientState.Joined ||
            _session.LocalController is not { GameReplicationInfo.bMatchHasBegun: true } player ||
            !TeamPingPlacement.TryGetTeam(player, out byte team) ||
            !TeamPingPlacement.TryGetSource(player, out Actor source, out _))
        {
            return null;
        }

        return (player, source, team);
    }

    private (Actor Source, FVector Location)? Target()
    {
        if (PlacementSource() is not { } source || _root.Camera is not { } camera) return null;
        (FVector location, FRotator rotation, _) = camera.Current;
        return TeamPingPlacement.TryProjectTarget(source.Player, source.Actor, location, rotation, out FVector target)
            ? (source.Actor, target)
            : null;
    }

    /// <summary>
    /// The ping lands where the player aims when the key or button is let go, whether it was a tap or a choice on
    /// the wheel: a wheel held open while moving must not mark the spot aimed at when it opened.
    /// </summary>
    private void Submit(TeamPingKind kind, bool fromWheel)
    {
        if (Target() is { } ping && PlacementSource() is { } source && ReferenceEquals(source.Actor, ping.Source))
        {
            _session.Client.SendTeamPing(kind, ping.Source, ping.Location);
        }
    }

    private void RefreshRecipient()
    {
        SPlayerController? player = _session.LocalController;
        (SBasePawn Pawn, byte Team)? recipient = !_session.IsStopped && _session.Client.State == EClientState.Joined &&
            player is { GameReplicationInfo.bMatchHasBegun: true, SBPawn: { } pawn } &&
            TeamPingPlacement.TryGetTeam(player, out byte team) ? (pawn, team) : null;
        if (_recipient != recipient)
        {
            _recipient = recipient;
            _marks.Clear();
        }
    }

    private void Receive(TeamPingMark mark)
    {
        RefreshRecipient();
        if (_recipient?.Team != mark.Team ||
            (_marks.TryGetValue(mark.SenderId, out ActiveMark previous) && previous.Mark.ServerTick >= mark.ServerTick))
        {
            return;
        }

        _marks[mark.SenderId] = new ActiveMark(mark, _session.Client.LocalTick);
    }

    public override void _Process(double delta)
    {
        _visuals.Clear();
        if (_marks.Count == 0 && !_input.TeamPing.WheelOpen)
        {
            if (_overlay.Visible) _overlay.ShowFrame(_visuals, null, null);
            return;
        }

        RefreshRecipient();
        long localTick = _session.Client.LocalTick;
        NetDriver driver = _session.Client.Driver;
        long serverTick = driver.Timeline.IsValid
            ? Math.Max(driver.NewestServerTick, driver.Timeline.RenderTimeFixed(localTick, delayTicks: 0) / NetTimeline.Fraction)
            : driver.NewestServerTick;
        foreach ((int sender, ActiveMark active) in _marks)
        {
            long age = Math.Max(localTick - active.ReceivedTick, serverTick - active.Mark.ServerTick);
            long remaining = LifetimeTicks - age;
            if (remaining <= 0) _marks.Remove(sender);
            else _visuals.Add(new TeamPingVisual(active.Mark, Math.Min(1f, remaining / FadeTicks)));
        }

        if (Blocked || _root.Camera is not { Occluded: false } || !TeamPingPlacement.TryGetSource(_session.LocalController, out _, out _))
        {
            _visuals.Clear();
        }
        byte? wheelTeam = _input.TeamPing.WheelOpen && PlacementSource() is { } source ? source.Team : null;
        _overlay.ShowFrame(_visuals, wheelTeam, _input.TeamPing.Selection);
    }
}
