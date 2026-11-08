using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Animation;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>Persistent replicated animation clocks and graph state. Clients never dispatch gameplay events.</summary>
public sealed class AnimationController : Actor
{
    public int BindingIndex = -1;
    public string StateName = "", PreviousState = "", Parameters = "";
    public float PlaybackTime, PreviousTime, BlendTime, BlendDuration;
    public bool Playing;
    public int Generation, SnapshotTick;
    private GraphPlayer? _graph;
    private SequencePlayer? _sequence;
    private bool _initialized;
    private int _receivedTick = -1;
    public AnimationController() { bAlwaysRelevant = true; bHidden = true; RemoteRole = ENetRole.ROLE_SimulatedProxy; }
    public void Initialize()
    {
        if (_initialized || XLevel?.GetService<AnimationWorld>() is not { } world || BindingIndex < 0 || BindingIndex >= world.Project.Bindings.Count) return;
        AnimationBinding binding = world.Project.Bindings[BindingIndex];
        if (binding.Graph is { } graph) { _graph = new(world.Project, world.Project.Graphs.Single(g => g.Id == graph)); if (Role == ENetRole.ROLE_Authority) Playing = true; }
        if (binding.Sequence is { } sequence) _sequence = new(world.Project, world.Project.Sequences.Single(s => s.Id == sequence));
        _initialized = true;
    }
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (Role != ENetRole.ROLE_Authority) return;
        Initialize(); _sequence?.Start(); Playing = true; Publish();
    }
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        if (Role != ENetRole.ROLE_Authority) return;
        _sequence?.Stop(); Playing = false; Publish();
    }
    public void SetParameter(string name, float value) { if (Role != ENetRole.ROLE_Authority) return; Initialize(); _graph?.Set(name, value); Publish(); }
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds); Initialize(); if (!_initialized) return;
        if (Role != ENetRole.ROLE_Authority)
        {
            Receive();
            if (Playing) { _graph?.Advance(DeltaSeconds); _sequence?.Advance(DeltaSeconds); }
            return;
        }
        if (Playing)
        {
            if (_graph is not null) foreach (AnimationEvent edge in _graph.Advance(DeltaSeconds)) Dispatch(edge);
            if (_sequence is not null)
            {
                IReadOnlyList<AnimationEvent> edges = _sequence.Advance(DeltaSeconds);
                foreach (AnimationEvent edge in edges) Dispatch(edge);
                Playing = _sequence.Playing;
                ApplyTransforms(_sequence.Evaluate());
            }
        }
        if (XLevel.TickCount % 6 == 0) Publish();
    }
    private void Dispatch(AnimationEvent edge)
    {
        if (edge.Id.StartsWith("sequence-audio:", StringComparison.Ordinal)) return;
        if (XLevel.World.IsReplaying) return;
        AnimationWorld? world = XLevel.GetService<AnimationWorld>();
        Actor? target = world?.Actor(edge.Target) ?? (world is not null && BindingIndex >= 0 ? world.Actor(world.Project.Bindings[BindingIndex].Target) : null);
        switch (edge.Kind)
        {
            case AnimationEventKind.Trigger: XLevel.TriggerEvent(new Name(edge.Target), this, Instigator); break;
            case AnimationEventKind.Untrigger: XLevel.UntriggerEvent(new Name(edge.Target), this, Instigator); break;
            case AnimationEventKind.Attach:
                target?.SetBase(XLevel.GetService<AnimationWorld>()?.Actor(edge.Value)); break;
            case AnimationEventKind.Detach: target?.SetBase(null); break;
            case AnimationEventKind.Sound:
            case AnimationEventKind.StopSound:
            case AnimationEventKind.ScriptNotify:
                StudioPawnAnimationSource.Notify(edge, 1, 1)?.Notify.Notify(target ?? this); break;
        }
    }
    private void ApplyTransforms(SequenceSample sample)
    {
        foreach (SequenceTransform value in sample.Transforms)
        {
            if (XLevel.GetService<AnimationWorld>()?.Actor(value.Target) is not { } target) continue;
            // SetLocation runs the owning physics service's encroachment check and updates its broadphase.
            FVector position = StudioSpace.ToUe(value.Transform.Position);
            FRotator rotation = StudioSpace.ToUe(value.Transform.Rotation);
            bool moved;
            if (XLevel.GetService<IActorNatives>() is { } natives && (target.bCollideActors || target.bBlockActors || target.bBlockPlayers)) moved = natives.MoveEncroacher(target, position - target.Location, rotation);
            else { moved = target.SetLocation(position); if (moved) target.Rotation = rotation; }
            if (!moved) { _sequence?.Stop(); Playing = false; Publish(); return; }
            target.DrawScale3D = new(value.Transform.Scale.X, value.Transform.Scale.Z, value.Transform.Scale.Y);
        }
    }
    public SequenceTransform? Camera()
    {
        if (!Playing || _sequence is null) return null;
        return _sequence.Evaluate().Transforms.FirstOrDefault(t => t.Target is "camera:all" or "camera:local");
    }
    public IEnumerable<(string Id, string Sound, float Offset, float Rate, string Target)> Audio()
    {
        if (!Playing || _sequence is null || XLevel.GetService<AnimationWorld>() is not { } world) yield break;
        AnimationSequence source = world.Project.Sequences.Single(s => s.Id == world.Project.Bindings[BindingIndex].Sequence);
        float time = source.Loop ? _sequence.Time % source.Duration : _sequence.Time;
        foreach (SequenceTrack track in source.Tracks)
            if (track.Kind == SequenceTrackKind.Audio)
                foreach (SequenceSegment segment in track.Segments)
                    if (segment.Asset is { } sound && time >= segment.Start && time < segment.Start + segment.Duration)
                        yield return (ActorId + "/" + Generation + "/" + segment.Id, sound, segment.Offset + (time - segment.Start) * segment.Rate, segment.Rate, track.Target);
    }
    public string EvaluatedState => _graph?.State ?? "";
    public BoneTransform[]? Pose(string target, out string? skeleton)
    {
        Initialize(); skeleton = null;
        if (XLevel.GetService<AnimationWorld>() is not { } world || BindingIndex < 0) return null;
        AnimationBinding binding = world.Project.Bindings[BindingIndex];
        if (_graph is not null && binding.Target == target)
        {
            skeleton = world.Project.Graphs.Single(g => g.Id == binding.Graph).Skeleton; return _graph.Evaluate();
        }
        if (_sequence is not null)
        {
            SequencePose? pose = _sequence.Evaluate().Poses.FirstOrDefault(p => p.Target == target);
            if (pose is not null) { skeleton = pose.Skeleton; return pose.Bones; }
        }
        return null;
    }
    private void Publish()
    {
        SnapshotTick = checked((int)XLevel.TickCount);
        if (_graph is not null)
        {
            GraphPlaybackState snapshot = _graph.Capture(); StateName = snapshot.State; PreviousState = snapshot.Previous ?? "";
            PlaybackTime = snapshot.Time; PreviousTime = snapshot.PreviousTime; BlendTime = snapshot.BlendTime; BlendDuration = snapshot.BlendDuration;
            AnimationGraph graph = XLevel.GetService<AnimationWorld>()!.Project.Graphs.Single(g => g.Id == XLevel.GetService<AnimationWorld>()!.Project.Bindings[BindingIndex].Graph);
            Parameters = string.Join(',', graph.Parameters.Select(p => snapshot.Parameters[p.Name].ToString("R", CultureInfo.InvariantCulture)));
        }
        if (_sequence is not null) { var snapshot = _sequence.Capture(); PlaybackTime = snapshot.Time; Generation = checked((int)snapshot.Generation); }
    }
    private void Receive()
    {
        if (_receivedTick == SnapshotTick) return;
        _receivedTick = SnapshotTick;
        if (_graph is not null && StateName.Length > 0)
        {
            AnimationGraph graph = XLevel.GetService<AnimationWorld>()!.Project.Graphs.Single(g => g.Id == XLevel.GetService<AnimationWorld>()!.Project.Bindings[BindingIndex].Graph);
            string[] values = Parameters.Split(','); var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < graph.Parameters.Count; i++)
                parameters[graph.Parameters[i].Name] = i < values.Length && float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : graph.Parameters[i].Default;
            _graph.Restore(new(StateName, PlaybackTime, PreviousState.Length == 0 ? null : PreviousState, PreviousTime, BlendTime, BlendDuration, parameters));
        }
        _sequence?.Restore(new(PlaybackTime, Playing, Generation));
    }
    public static RepExtension Replication()
    {
        var result = new RepExtension { PersistentActorChannel = true };
        foreach (string name in new[] { nameof(BindingIndex), nameof(Generation), nameof(SnapshotTick) }) result.Properties.Add(new(name, name, RepLeafKind.Int));
        foreach (string name in new[] { nameof(StateName), nameof(PreviousState), nameof(Parameters) }) result.Properties.Add(new(name, name, RepLeafKind.String));
        foreach (string name in new[] { nameof(PlaybackTime), nameof(PreviousTime), nameof(BlendTime), nameof(BlendDuration) }) result.Properties.Add(new(name, name, RepLeafKind.Float));
        result.Properties.Add(new(nameof(Playing), nameof(Playing), RepLeafKind.Bool)); return result;
    }
}
