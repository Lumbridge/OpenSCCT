using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenScct.Animation;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;

namespace OpenScct.Server;

public sealed class StudioPawnAnimationSource(AnimationWorld world, IPawnAnimationSource fallback) : IPawnAnimationSource
{
    private readonly Dictionary<(string Target, string Slot), PawnClip> _clips = new();
    public bool TryGetClip(SBasePawn Pawn, Name Sequence, out PawnClip Clip)
    {
        string target = Pawn.Mesh.Path ?? (Pawn is SPAWNDEFENSE ? "SPerso.DEF_01" : "SPerso.ATT_01");
        AnimationBinding? binding = world.Binding(target, Sequence.ToString());
        if (binding?.Mode != ReplacementMode.Gameplay) return fallback.TryGetClip(Pawn, Sequence, out Clip);
        var key = (target, Sequence.ToString());
        if (_clips.TryGetValue(key, out PawnClip? cached)) { Clip = cached; return true; }
        AnimationClip source = world.Project.Clips.Single(c => c.Id == binding.Clip);
        SkeletonDefinition skeleton = world.Project.Skeletons.Single(s => s.Id == source.Skeleton);
        int frames = Math.Max(2, (int)MathF.Round(source.Duration * source.FramesPerSecond) + 1);
        float fps = (frames - 1) / source.Duration;
        var positions = new FVector[frames]; var times = new float[frames]; var yaw = new float[frames];
        int root = source.RootMotionBone is { } name ? skeleton.Bones.FindIndex(b => b.Name == name) : 0;
        if (root < 0) root = 0;
        AnimationClip sample = AnimationJson.Clone(source); sample.Loop = false;
        for (int i = 0; i < frames; i++)
        {
            Matrix4x4 matrix = source.RootTracks.Count > 0 ? ClipEvaluator.RootMotion(source, i / fps).Matrix : ClipEvaluator.World(skeleton, ClipEvaluator.Evaluate(skeleton, sample, i / fps))[root];
            Vector3 p = matrix.Translation; positions[i] = new(p.X * 100, p.Z * 100, p.Y * 100); times[i] = i;
            Vector3 forward = Vector3.TransformNormal(Vector3.UnitX, matrix); yaw[i] = SvmMath.Atan2(forward.Z, forward.X) * (65536 / (2 * MathF.PI));
        }
        var notifies = source.Events.Select(e => Notify(e, frames, fps)).OfType<PawnClipNotify>().ToArray();
        Clip = new(Sequence, frames, fps) { RootTrack = new(times, positions) { Yaw = yaw }, Notifies = notifies };
        _clips[key] = Clip; return true;
    }
    internal static PawnClipNotify? Notify(AnimationEvent e, int frames, float fps)
    {
        UAnimNotify? notify = e.Kind switch
        {
            AnimationEventKind.Trigger => new UAnimNotify_Trigger { EventName = new Name(e.Target) },
            AnimationEventKind.Sound => new UAnimNotify_Sound { Sound = new AssetRef(e.Value) },
            AnimationEventKind.StopSound => new UAnimNotify_StopSound { Sound = new AssetRef(e.Value) },
            AnimationEventKind.ScriptNotify => new UAnimNotify_Script { NotifyName = new Name(e.Value) },
            _ => null,
        };
        return notify is null ? null : new PawnClipNotify(e.SourceNormalizedTime ?? e.Time * fps / frames, notify);
    }
}
public sealed class StudioMeshAnimations(AnimationWorld world, IMeshAnimations fallback) : IMeshAnimations
{
    private readonly Dictionary<(AssetRef Mesh, Name Slot), MeshAnimSequence> _cache = new();
    public MeshAnimSequence? FindSequence(AssetRef Mesh, Name Sequence)
    {
        AnimationBinding? binding = world.Binding(Mesh.Path ?? "", Sequence.ToString());
        if (binding is null || binding.Mode == ReplacementMode.Visual) return fallback.FindSequence(Mesh, Sequence);
        if (_cache.TryGetValue((Mesh, Sequence), out MeshAnimSequence? cached)) return cached;
        AnimationClip clip = world.Project.Clips.Single(c => c.Id == binding.Clip);
        int frames = Math.Max(2, (int)MathF.Ceiling(clip.Duration * clip.FramesPerSecond) + 1); float rate = (frames - 1) / clip.Duration;
        var notifies = clip.Events.Select(e => StudioPawnAnimationSource.Notify(e, frames, rate)).OfType<PawnClipNotify>().Select(n => new MeshAnimNotify(n.Time, n.Notify)).ToArray();
        return _cache[(Mesh, Sequence)] = new(Sequence, frames, rate, notifies);
    }
}
