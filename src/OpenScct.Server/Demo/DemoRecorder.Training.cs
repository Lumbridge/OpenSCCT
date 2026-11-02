using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Training;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class DemoRecorder : ITrainingInputSink, ITrainingEventSink
{
    private TrainingDataWriter? _training;
    private TrainingObservations? _observations;
    private ITrainingInputSink? _previousTrainingInput;
    private ITrainingEventSink? _previousTrainingEvents;
    private uint _trainingServerTick, _nextObservationTick;
    private uint? _lastObservationTick;

    /// <summary>Optional companion data. A failure never stops the replay or the match.</summary>
    public string? TrainingPath => _training is { IsFinished: true } ? _training.Path : null;
    public string? TrainingError { get; private set; }

    internal void StartTraining()
    {
        try
        {
            _training = new TrainingDataWriter(Writer.Path, Writer.Header);
            if (_training.Error is { } error) { AbortTraining(error); return; }
            _observations = new TrainingObservations(_host);
            _previousTrainingInput = _host.World.Level.GetService<ITrainingInputSink>();
            _previousTrainingEvents = _host.World.Level.GetService<ITrainingEventSink>();
            _host.World.Level.SetService<ITrainingInputSink>(this);
            _host.World.Level.SetService<ITrainingEventSink>(this);
            WriteTraining("capture", (uint)_host.World.TickCount, new
            {
                snapshotHz = 10,
                matchSeed = _host.Settings.MatchSeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                snapshotTiming = "world state before dispatch and application of the following server tick",
                inputTiming = "accepted or applied input; not proof an attempted action succeeded",
                remoteInput = "accepted acceleration, jump and native actions; raw remote keyboard and mouse unavailable",
                perception = "server estimate, not rendered pixels or player attention; see per-sample coverage",
                labels = "event records are privileged outcomes and must not be used as policy observations",
                coordinateUnits = "Unreal units",
                angleUnits = "65536 units per turn",
                playerIdentity = "match-local player and actor IDs; no account identifiers",
            });
        }
        catch (Exception error) when (error is not OutOfMemoryException) { AbortTraining(error.Message); }
    }

    /// <summary>Sample before receiving input so a later action cannot leak into its observation.</summary>
    internal void CaptureTraining()
    {
        if (_training is null || IsFinished || TrainingError is not null) return;
        uint tick = (uint)_host.World.TickCount;
        _trainingServerTick = tick + 1;
        try
        {
            _observations!.ObserveTick();
            if (tick < _nextObservationTick) return;
            _nextObservationTick = tick + Sim.Hz / 10;
            if (WriteTraining("snapshot", tick, _observations!.Capture())) _lastObservationTick = tick;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { AbortTraining(error.Message); }
    }

    public void RecordInput(SPlayerController controller, in TrainingInput input)
    {
        if (!CanRecord(controller)) return;
        WriteTraining("input", (uint)Math.Max(0, input.ServerTick), new
        {
            playerId = controller.PlayerReplicationInfo!.PlayerID,
            controllerId = controller.ActorId,
            pawnId = controller.Pawn?.ActorId,
            observationTick = _lastObservationTick,
            command = input,
        });
    }

    public void RecordAction(SPlayerController controller, in TrainingAction action)
    {
        if (!CanRecord(controller)) return;
        WriteTraining("action", (uint)Math.Max(0, action.ServerTick), new
        {
            playerId = controller.PlayerReplicationInfo!.PlayerID,
            controllerId = controller.ActorId,
            pawnId = controller.Pawn?.ActorId,
            observationTick = _lastObservationTick,
            command = action,
        });
    }

    public void RecordEvent(string kind, Controller? actor = null, Controller? target = null,
        int? amount = null, string? detail = null, int? objectId = null) =>
        WriteTraining("event", TrainingTick, new
        {
            kind,
            actorPlayerId = actor?.PlayerReplicationInfo?.PlayerID,
            targetPlayerId = target?.PlayerReplicationInfo?.PlayerID,
            amount,
            detail,
            objectId,
        });

    // Remote movement temporarily changes World.TickCount to a client's command clock.
    private uint TrainingTick => _trainingServerTick != 0 ? _trainingServerTick : (uint)_host.World.TickCount;

    private bool CanRecord(SPlayerController controller) => _training is not null && TrainingError is null &&
        controller.PlayerReplicationInfo is { bOnlySpectator: false } && !ReferenceEquals(controller, Controller);

    private bool WriteTraining(string type, uint tick, object data)
    {
        if (_training is null || TrainingError is not null) return false;
        if (_training.TryWrite(type, tick, data)) return true;
        if (_training.Error is { } error) AbortTraining(error);
        return false;
    }

    private void WriteTrainingMarker(DemoMarkerKind kind, string text) =>
        WriteTraining("event", TrainingTick, new { kind = "round-marker", marker = kind, detail = text });

    private void FinishTraining()
    {
        if (_training is not null && !_training.Finish()) TrainingError ??= _training.Error;
        DetachTraining();
        _training?.Dispose();
    }

    private void AbortTraining(string message)
    {
        TrainingError ??= message;
        DetachTraining();
        _training?.Dispose();
    }

    private void DetachTraining()
    {
        var level = _host.World.Level;
        if (ReferenceEquals(level.GetService<ITrainingInputSink>(), this)) level.SetService(_previousTrainingInput);
        if (ReferenceEquals(level.GetService<ITrainingEventSink>(), this)) level.SetService(_previousTrainingEvents);
    }
}
