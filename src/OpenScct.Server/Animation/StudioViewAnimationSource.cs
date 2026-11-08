using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Animation;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;

namespace OpenScct.Server;

public sealed class StudioViewAnimationSource(AnimationWorld world) : IViewAnimationSource
{
    private readonly Dictionary<(string, string), AnimationClip?> _cache = new();
    private AnimationClip? Clip(Actor owner, Name sequence)
    {
        string target = owner.Mesh.Path ?? "";
        var key = (target, sequence.ToString());
        if (_cache.TryGetValue(key, out AnimationClip? known)) return known;
        AnimationBinding? binding = world.Binding(target, sequence.ToString());
        return _cache[key] = binding?.Mode == ReplacementMode.Gameplay ? world.Project.Clips.Single(c => c.Id == binding.Clip) : null;
    }
    public ClipData Resolve(Actor owner, Name sequence, ClipData fallback)
    {
        if (Clip(owner, sequence) is not { } clip) return fallback;
        int frames = Math.Max(2, (int)MathF.Ceiling(clip.Duration * clip.FramesPerSecond) + 1);
        return new(frames, (frames - 1) / clip.Duration);
    }
    public void Advance(Actor owner, Name sequence, float from, float to, bool includeStart, bool backward, bool loop)
    {
        if (backward && sequence == ViewModelClips.FirstDrawStEd2 && Clip(owner, new Name("FirstHolsterStFd2")) is { })
        { sequence = new Name("FirstHolsterStFd2"); backward = false; }
        if (owner.Role != ENetRole.ROLE_Authority || owner.XLevel.World.IsReplaying || Clip(owner, sequence) is not { } clip) return;
        AnimationClip source = clip;
        if (backward || source.Loop != loop)
        {
            source = AnimationJson.Clone(clip);
            source.Loop = loop;
            if (backward) foreach (AnimationEvent e in source.Events) e.Time = source.Duration - e.Time;
        }
        foreach (AnimationEvent e in ClipEvaluator.Events(source, from, to, includeStart))
            StudioPawnAnimationSource.Notify(e, 1, 1)?.Notify.Notify(owner);
    }
}
