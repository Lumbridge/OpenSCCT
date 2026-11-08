using System.Numerics;

namespace OpenScct.Animation;

public sealed record SequencePose(string Target, string Skeleton, BoneTransform[] Bones);
public sealed record SequenceTransform(string Target, BoneTransform Transform, float? FieldOfView);
public sealed class SequenceSample
{
    public List<SequencePose> Poses { get; } = [];
    public List<SequenceTransform> Transforms { get; } = [];
}
public sealed record SequencePlaybackState(float Time, bool Playing, long Generation);

/// <summary>Shared sequence evaluation and edge dispatch. Restoring a snapshot never replays events.</summary>
public sealed class SequencePlayer
{
    private readonly AnimationProject _project;
    private readonly AnimationSequence _sequence;
    private float _time;
    private bool _startPending;
    public bool Playing { get; private set; }
    public long Generation { get; private set; }
    public float Time => _time;
    public SequencePlayer(AnimationProject project, AnimationSequence sequence) { _project = project; _sequence = sequence; }
    public void Start() { _time = 0; Playing = true; _startPending = true; Generation++; }
    public void Stop() { Playing = false; _startPending = false; }
    public SequencePlaybackState Capture() => new(_time, Playing, Generation);
    public void Restore(SequencePlaybackState state)
    {
        if (!float.IsFinite(state.Time) || state.Time < 0 || state.Generation < 0) throw new InvalidDataException("Invalid sequence playback snapshot.");
        _time = state.Time; Playing = state.Playing; Generation = state.Generation; _startPending = false;
    }
    public IReadOnlyList<AnimationEvent> Advance(float delta)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!Playing) return [];
        float next = _sequence.Loop ? _time + delta : MathF.Min(_sequence.Duration, _time + delta);
        var events = new List<(float At, AnimationEvent Event)>();
        int first = _sequence.Loop ? (int)MathF.Floor(_time / _sequence.Duration) : 0;
        int last = _sequence.Loop ? (int)MathF.Floor(next / _sequence.Duration) : 0;
        foreach (SequenceTrack track in _sequence.Tracks)
            for (int loop = first; loop <= last; loop++)
            {
                float origin = loop * _sequence.Duration;
                foreach (AnimationEvent e in track.Events)
                {
                    float at = origin + e.Time;
                    if ((at > _time || (_startPending && at == _time)) && at <= next) events.Add((at, e));
                }
                foreach (SequenceSegment segment in track.Segments)
                {
                    if (track.Kind == SequenceTrackKind.Audio)
                    {
                        AudioEdge(segment.Start, AnimationEventKind.Sound); AudioEdge(segment.Start + segment.Duration, AnimationEventKind.StopSound);
                        void AudioEdge(float time, AnimationEventKind kind)
                        {
                            float at = origin + time;
                            if ((at > _time || (_startPending && at == _time)) && at <= next)
                                events.Add((at, new() { Id = "sequence-audio:" + segment.Id + "/" + kind, Time = time, Kind = kind, Target = track.Target, Value = segment.Asset ?? "" }));
                        }
                    }
                    if (track.Kind != SequenceTrackKind.Skeletal || segment.Clip is null || segment.Rate <= 0) continue;
                    AnimationClip clip = _project.Clips.Single(c => c.Id == segment.Clip);
                    float a = MathF.Max(0, (_time - origin - segment.Start) * segment.Rate + segment.Offset);
                    float b = MathF.Min((segment.Offset + segment.Duration * segment.Rate), (next - origin - segment.Start) * segment.Rate + segment.Offset);
                    if (b < a || next < origin + segment.Start || _time > origin + segment.Start + segment.Duration) continue;
                    foreach (var edge in ClipEvaluator.EventEdges(clip, a, b, _startPending || _time < origin + segment.Start))
                        events.Add((origin + segment.Start + (edge.Time - segment.Offset) / segment.Rate, edge.Event));
                }
            }
        _startPending = false; _time = next;
        if (!_sequence.Loop && _time >= _sequence.Duration) Playing = false;
        return events.OrderBy(e => e.At).ThenBy(e => e.Event.Id, StringComparer.Ordinal).Select(e => e.Event).ToArray();
    }
    public SequenceSample Evaluate() => Evaluate(_project, _sequence, _time);
    public static SequenceSample Evaluate(AnimationProject project, AnimationSequence sequence, float time)
    {
        float t = sequence.Loop ? ((time % sequence.Duration) + sequence.Duration) % sequence.Duration : Math.Clamp(time, 0, sequence.Duration);
        var result = new SequenceSample();
        foreach (SequenceTrack track in sequence.Tracks)
        {
            if (track.Kind == SequenceTrackKind.Skeletal)
            {
                BoneTransform[]? combined = null; string? skeletonId = null; float total = 0;
                foreach (SequenceSegment segment in track.Segments)
                {
                    if (segment.Clip is null || t < segment.Start || t > segment.Start + segment.Duration) continue;
                    AnimationClip clip = project.Clips.Single(c => c.Id == segment.Clip);
                    SkeletonDefinition skeleton = project.Skeletons.Single(s => s.Id == clip.Skeleton);
                    float local = t - segment.Start;
                    float weight = MathF.Min(segment.BlendIn > 0 ? Math.Clamp(local / segment.BlendIn, 0, 1) : 1,
                        segment.BlendOut > 0 ? Math.Clamp((segment.Duration - local) / segment.BlendOut, 0, 1) : 1);
                    if (weight <= 0) continue;
                    BoneTransform[] pose = ClipEvaluator.Evaluate(skeleton, clip, segment.Offset + local * segment.Rate);
                    if (combined is null) { combined = pose; skeletonId = skeleton.Id; total = weight; }
                    else
                    {
                        if (skeletonId != skeleton.Id) throw new InvalidDataException("A sequence track mixes incompatible skeletons.");
                        for (int i = 0; i < combined.Length; i++) combined[i] = BoneTransform.Blend(combined[i], pose[i], weight / (total + weight)); total += weight;
                    }
                }
                if (combined is not null) result.Poses.Add(new(track.Target, skeletonId!, combined));
            }
            if (track.Kind is SequenceTrackKind.Transform or SequenceTrackKind.Camera)
            {
                var pose = new[] { BoneTransform.Identity };
                var skeleton = new SkeletonDefinition { Bones = [new() { Name = "Transform" }] };
                // Transform tracks use a single named transform, independent of an imported mesh's bones.
                ClipEvaluator.ApplyTracks(skeleton, pose, track.TransformTracks, t);
                float? fov = track.Kind == SequenceTrackKind.Camera && track.FieldOfView.Count > 0 ? ClipEvaluator.Sample(track.FieldOfView, t).X : null;
                result.Transforms.Add(new(track.Target, pose[0], fov));
            }
        }
        return result;
    }
}
