using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenScct.Animation;

public static class AnimationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true, IgnoreReadOnlyProperties = true,
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }, MaxDepth = 64,
    };
    public static byte[] Write(AnimationProject project) => JsonSerializer.SerializeToUtf8Bytes(project, Options);
    public static AnimationProject Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            AnimationProject project = JsonSerializer.Deserialize<AnimationProject>(bytes, Options) ?? throw new InvalidDataException("Empty animation project.");
            Validate(project);
            return project;
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid animation JSON: " + e.Message, e); }
    }
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Options), Options)!;
    public static void Validate(AnimationProject project)
    {
        if (project is null) throw new InvalidDataException("Missing animation project.");
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
        Require(project.Format == "osanim/1", "Unsupported animation project format: " + project.Format);
        bool AssetId(string id) => id is { Length: > 0 and <= 128 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        Require(AssetId(project.Id), "Invalid animation project id.");
        Require(project.FramesPerSecond > 0 && float.IsFinite(project.FramesPerSecond), "Invalid project frame rate.");
        Require(project.Skeletons is not null && project.Clips is not null && project.Graphs is not null && project.Scene is not null &&
            project.Bindings is not null && project.Sequences is not null && project.Poses is not null && project.ControlRigs is not null && project.RetargetProfiles is not null && project.FirstPersonProfiles is not null, "Null animation collections.");
        Require(project.Skeletons.All(s => s is not null) && project.Clips.All(c => c is not null) && project.Scene.All(r => r is not null)
            && project.Graphs.All(g => g is not null) && project.Sequences.All(s => s is not null) && project.Bindings.All(b => b is not null)
            && project.FirstPersonProfiles.All(p => p is not null), "Null animation asset.");
        Unique(project.Skeletons!.Select(s => s.Id), "skeleton id");
        Unique(project.Clips!.Select(s => s.Id), "clip id");
        Unique(project.Graphs!.Select(s => s.Id), "graph id");
        Unique(project.Sequences!.Select(s => s.Id), "sequence id");
        Unique(project.Scene!.Select(s => s.Id), "scene id");
        Require(project.Skeletons.All(s => AssetId(s.Id)) && project.Clips.All(c => AssetId(c.Id)) && project.Graphs.All(g => AssetId(g.Id)) && project.Sequences.All(s => AssetId(s.Id)), "Animation asset ids must contain only ASCII letters, digits, underscores and hyphens.");
        foreach (SkeletonDefinition s in project.Skeletons)
        {
            Require(s.Bones is { Count: > 0 and <= 4096 }, s.Name + ": skeleton needs 1..4096 bones.");
            Require(s.Bones.All(b => b is not null), "Null bone definition.");
            Unique(s.Bones.Select(b => b.Name), "bone name");
            Transform(s.DisplayTransform);
            for (int i = 0; i < s.Bones.Count; i++)
            {
                Require(s.Bones[i].Parent >= -1 && s.Bones[i].Parent < i, s.Name + ": bones must be ordered parent first.");
                Transform(s.Bones[i].Rest);
            }
        }
        foreach (AnimationClip clip in project.Clips)
        {
            SkeletonDefinition skeleton = project.Skeletons.FirstOrDefault(s => s.Id == clip.Skeleton) ?? throw new InvalidDataException(clip.Name + ": unknown skeleton.");
            Require(clip.Tracks is not null && clip.RootTracks is not null && clip.Layers is not null && clip.Events is not null && clip.Constraints is not null, "Null clip collections.");
            Require(clip.Layers.All(l => l is not null) && clip.Constraints.All(c => c is not null), "Null animation layer or constraint.");
            Require(clip.RootMotionBone is null || skeleton.Bones.Any(b => b.Name == clip.RootMotionBone), "Unknown root-motion bone.");
            Require(float.IsFinite(clip.Duration) && clip.Duration > 0 && clip.FramesPerSecond > 0 && float.IsFinite(clip.FramesPerSecond), clip.Name + ": invalid duration/frame rate.");
            Tracks(clip.Tracks, clip.Duration, skeleton);
            Tracks(clip.RootTracks, clip.Duration, null);
            foreach (AnimationLayer layer in clip.Layers)
            {
                Require(layer.Tracks is not null && layer.WeightKeys is not null && layer.Mask is not null, "Null layer collections.");
                Require(float.IsFinite(layer.Weight), "Non-finite layer weight.");
                Keys(layer.WeightKeys, clip.Duration, false); Tracks(layer.Tracks, clip.Duration, skeleton);
                Require(layer.Mask.All(b => skeleton.Bones.Any(x => x.Name == b)), "Unknown bone in layer mask.");
            }
            foreach (RigConstraint constraint in clip.Constraints)
            {
                Require(Enum.IsDefined(constraint.Kind) && constraint.WeightKeys is not null, "Invalid constraint kind/weight keys.");
                Require(skeleton.Bones.Any(b => b.Name == constraint.Bone), "Unknown constrained bone: " + constraint.Bone);
                Require(Finite(new Vector4(constraint.Target, constraint.Weight)) && Finite(new Vector4(constraint.Pole, 0)), "Non-finite constraint.");
                Keys(constraint.WeightKeys, clip.Duration, false); Transform(constraint.Offset);
                if (constraint.TargetRig is { Length: > 0 } rig)
                {
                    RigInstance target = project.Scene.FirstOrDefault(r => r.Id == rig) ?? throw new InvalidDataException("Unknown constraint target rig.");
                    SkeletonDefinition targetSkeleton = project.Skeletons.FirstOrDefault(s => s.Id == target.Skeleton) ?? throw new InvalidDataException("Unknown constraint target skeleton.");
                    Require(constraint.TargetBone is null || targetSkeleton.Bones.Any(b => b.Name == constraint.TargetBone), "Unknown constraint target bone.");
                }
            }
            Events(clip.Events, clip.Duration);
        }
        foreach (RigInstance instance in project.Scene)
        {
            Require(project.Skeletons.Any(s => s.Id == instance.Skeleton), "Unknown scene skeleton.");
            Require(instance.Clip is null || project.Clips.Any(c => c.Id == instance.Clip && c.Skeleton == instance.Skeleton), "Incompatible scene clip.");
            Require(float.IsFinite(instance.Start), "Invalid scene start."); Transform(instance.Transform);
        }
        foreach (AnimationGraph graph in project.Graphs)
        {
            Require(graph.Nodes is not null && graph.States is not null && graph.Parameters is not null && graph.Transitions is not null, "Null graph collections.");
            Require(graph.Nodes.All(n => n is not null) && graph.States.All(s => s is not null) && graph.Parameters.All(p => p is not null) && graph.Transitions.All(t => t is not null), "Null graph definition.");
            Require(graph.Parameters.Count <= 32 && graph.States.All(s => s.Name is { Length: > 0 and <= 63 }), "Graphs support 32 parameters and state names up to 63 characters.");
            Require(project.Skeletons.Any(s => s.Id == graph.Skeleton), "Unknown graph skeleton.");
            Unique(graph.Nodes.Select(n => n.Id), "graph node"); Unique(graph.States.Select(s => s.Name), "state name"); Unique(graph.Parameters.Select(p => p.Name), "parameter name");
            foreach (GraphParameter parameter in graph.Parameters) Require(Enum.IsDefined(parameter.Kind) && float.IsFinite(parameter.Default), "Invalid graph parameter.");
            Require(graph.States.Any(s => s.Name == graph.Entry), "Unknown graph entry state.");
            foreach (GraphState state in graph.States) Require(graph.Nodes.Any(n => n.Id == state.Node), "Unknown state node.");
            foreach (GraphNode node in graph.Nodes)
            {
                Require(Enum.IsDefined(node.Kind) && node.Inputs is not null && node.Mask is not null, "Invalid graph node.");
                Require(node.Inputs.All(i => i is not null) && node.Mask.All(b => project.Skeletons.Single(s => s.Id == graph.Skeleton).Bones.Any(bone => bone.Name == b)), "Invalid graph input or bone mask.");
                Require(float.IsFinite(node.Rate) && node.Rate >= 0 && float.IsFinite(node.Weight), "Invalid graph node rate/weight.");
                if (node.Kind == GraphNodeKind.Clip) Require(project.Clips.Any(c => c.Id == node.Clip && c.Skeleton == graph.Skeleton), "Incompatible graph clip.");
                foreach (GraphInput input in node.Inputs) Require(graph.Nodes.Any(n => n.Id == input.Node) && float.IsFinite(input.Point.X) && float.IsFinite(input.Point.Y), "Invalid graph input.");
                if (node.Kind == GraphNodeKind.Blend1D) Require(node.Inputs.Select(i => i.Point.X).Distinct().Count() == node.Inputs.Count, "Duplicate 1D blend points.");
            }
            var visiting = new HashSet<string>(StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (done.Contains(id)) return;
                Require(visiting.Add(id), "Animation graph contains a cycle.");
                foreach (GraphInput input in graph.Nodes.Single(n => n.Id == id).Inputs) Visit(input.Node);
                visiting.Remove(id); done.Add(id);
            }
            foreach (GraphNode node in graph.Nodes) Visit(node.Id);
            foreach (GraphTransition t in graph.Transitions)
                Require(graph.States.Any(s => s.Name == t.From) && graph.States.Any(s => s.Name == t.To) && graph.Parameters.Any(p => p.Name == t.Parameter)
                    && Enum.IsDefined(t.Operator) && float.IsFinite(t.BlendSeconds) && t.BlendSeconds >= 0 && float.IsFinite(t.ExitTime) && t.ExitTime >= 0 && float.IsFinite(t.Value), "Invalid graph transition.");
        }
        foreach (AnimationSequence sequence in project.Sequences)
        {
            Require(sequence.Tracks is not null, "Null sequence tracks.");
            Require(sequence.Tracks.All(t => t is not null), "Null sequence track.");
            Require(float.IsFinite(sequence.Duration) && sequence.Duration > 0, "Invalid sequence duration.");
            foreach (SequenceTrack track in sequence.Tracks)
            {
                Require(Enum.IsDefined(track.Kind) && track.Segments is not null && track.TransformTracks is not null && track.FieldOfView is not null && track.Events is not null, "Invalid sequence track.");
                Require(track.Segments.All(s => s is not null), "Null sequence segment.");
                Tracks(track.TransformTracks, sequence.Duration, null); Keys(track.FieldOfView, sequence.Duration, false); Events(track.Events, sequence.Duration);
                foreach (SequenceSegment segment in track.Segments)
                    Require(float.IsFinite(segment.Start) && segment.Start >= 0 && float.IsFinite(segment.Duration) && segment.Duration > 0 && segment.Start + segment.Duration <= sequence.Duration + .0001f
                        && float.IsFinite(segment.Rate) && segment.Rate >= 0 && float.IsFinite(segment.Offset) && segment.Offset >= 0 && float.IsFinite(segment.BlendIn) && segment.BlendIn >= 0
                        && float.IsFinite(segment.BlendOut) && segment.BlendOut >= 0 && (segment.Clip is null || project.Clips.Any(c => c.Id == segment.Clip)), "Invalid sequence segment.");
            }
        }
        Require(project.Bindings.Select(b => b.Target + "/" + b.Slot).Distinct(StringComparer.OrdinalIgnoreCase).Count() == project.Bindings.Count, "Conflicting replacement bindings in one source.");
        foreach (AnimationBinding b in project.Bindings)
        {
            Require(!string.IsNullOrWhiteSpace(b.Target) && Enum.IsDefined(b.Mode), "Binding needs a target and valid replacement mode.");
            Require(b.Graph is null || b.Target.StartsWith("actor:", StringComparison.Ordinal), "Custom graphs require an actor binding.");
            Require((b.Clip is not null ? 1 : 0) + (b.Graph is not null ? 1 : 0) + (b.Sequence is not null ? 1 : 0) == 1, "Binding needs exactly one clip, graph or sequence.");
            Require(b.Clip is null || project.Clips.Any(c => c.Id == b.Clip), "Unknown binding clip.");
            Require(b.Graph is null || project.Graphs.Any(g => g.Id == b.Graph), "Unknown binding graph.");
            Require(b.Sequence is null || project.Sequences.Any(s => s.Id == b.Sequence), "Unknown binding sequence.");
        }
        Unique(project.FirstPersonProfiles!.Select(p => p.Id), "first-person profile id");
        foreach (FirstPersonProfile profile in project.FirstPersonProfiles)
        {
            Require(Enum.IsDefined(profile.Role) && profile.Actions is not null && float.IsFinite(profile.FieldOfView) && profile.FieldOfView is >= 1 and < 180, "Invalid first-person profile.");
            Require(profile.Actions.All(a => a is not null), "Null first-person action.");
            Require(project.Scene.Any(r => r.Id == profile.MainRig) && (profile.ArmsRig is null || project.Scene.Any(r => r.Id == profile.ArmsRig)), "First-person profile references an unknown rig.");
            Unique(profile.Actions.Select(a => a.Name), "first-person action name"); Transform(profile.WeaponMount);
            foreach (FirstPersonAction action in profile.Actions)
            {
                Require(action.Clip is null || project.Clips.Any(c => c.Id == action.Clip && c.Skeleton == project.Scene.Single(r => r.Id == profile.MainRig).Skeleton), "Incompatible first-person weapon clip.");
                Require(action.ArmsClip is null || project.Clips.Any(c => c.Id == action.ArmsClip && c.Skeleton == project.Scene.Single(r => r.Id == (profile.ArmsRig ?? profile.MainRig)).Skeleton), "Incompatible first-person arm clip.");
            }
        }
        foreach (PosePreset pose in project.Poses)
            Require(pose.Bones is not null && project.Skeletons.Any(s => s.Id == pose.Skeleton && s.Bones.Count == pose.Bones.Count), "Incompatible saved pose.");
        foreach (RetargetProfile profile in project.RetargetProfiles)
        {
            SkeletonDefinition source = project.Skeletons.FirstOrDefault(s => s.Id == profile.SourceSkeleton) ?? throw new InvalidDataException("Unknown retarget source.");
            SkeletonDefinition target = project.Skeletons.FirstOrDefault(s => s.Id == profile.TargetSkeleton) ?? throw new InvalidDataException("Unknown retarget destination.");
            Require(profile.Bones is not null && float.IsFinite(profile.RootScale) && profile.RootScale > 0 && profile.Bones.All(b => source.Bones.Any(s => s.Name == b.Key) && target.Bones.Any(t => t.Name == b.Value))
                && profile.Bones.Values.Distinct(StringComparer.Ordinal).Count() == profile.Bones.Count, "Invalid retarget bone mapping.");
        }
        void Unique(IEnumerable<string> values, string what)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values) Require(!string.IsNullOrWhiteSpace(value) && seen.Add(value), "Missing or duplicate " + what + ": " + value);
        }
        void Transform(BoneTransform t) => Require(Finite(new Vector4(t.Position, 0)) && Finite(new Vector4(t.Scale, 0)) && Finite(ClipEvaluator.V(t.Rotation)) && t.Rotation.LengthSquared() > 1e-12f, "Invalid bone transform.");
        void Keys(List<AnimationKey> keys, float duration, bool rotation)
        {
            float last = -1;
            foreach (AnimationKey key in keys)
            {
                Require(key is not null, "Null animation key.");
                Require(Enum.IsDefined(key.Interpolation), "Invalid key interpolation.");
                Require(float.IsFinite(key.Time) && key.Time >= 0 && key.Time > last && key.Time <= duration + .0001f && Finite(key.Value) && Finite(key.InTangent) && Finite(key.OutTangent)
                    && (!rotation || key.Value.LengthSquared() > 1e-12f), "Invalid, unordered or duplicate animation key.");
                last = key.Time;
            }
        }
        void Tracks(List<BoneTrack> tracks, float duration, SkeletonDefinition? skeleton)
        {
            Require(tracks.All(t => t is not null), "Null animation track.");
            Unique(tracks.Select(t => t.Bone + "/" + t.Channel), "bone track");
            foreach (BoneTrack track in tracks)
            {
                Require(Enum.IsDefined(track.Channel) && track.Keys is not null, "Invalid bone track.");
                Require(skeleton is null || skeleton.Bones.Any(b => b.Name == track.Bone), "Unknown track bone: " + track.Bone);
                Keys(track.Keys, duration, track.Channel == TrackChannel.Rotation);
            }
        }
        void Events(List<AnimationEvent> events, float duration)
        {
            Require(events.All(e => e is not null), "Null animation event.");
            Unique(events.Select(e => e.Id), "event id");
            foreach (AnimationEvent e in events) Require(Enum.IsDefined(e.Kind) && float.IsFinite(e.Time) && e.Time >= 0 && (e.Time <= duration || e.SourceNormalizedTime.HasValue)
                && (!e.SourceNormalizedTime.HasValue || float.IsFinite(e.SourceNormalizedTime.Value)), $"Invalid animation event time: {e.Time}, source normalized {e.SourceNormalizedTime}.");
        }
    }
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
