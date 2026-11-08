namespace OpenScct.Animation;

public static class FirstPersonEvaluation
{
    public static SequencePose? Evaluate(AnimationProject project, FirstPersonProfile profile, string slot, float phase, bool arms)
    {
        FirstPersonAction? action = profile.Actions.FirstOrDefault(a => a.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
            ?? profile.Actions.FirstOrDefault(a => a.Name == "Idle");
        string? id = arms ? action?.ArmsClip : action?.Clip;
        AnimationClip? clip = project.Clips.FirstOrDefault(c => c.Id == id);
        if (clip is null) return null;
        SkeletonDefinition skeleton = project.Skeletons.Single(s => s.Id == clip.Skeleton);
        return new(arms ? profile.ArmsRig ?? profile.MainRig : profile.MainRig, skeleton.Id, ClipEvaluator.Evaluate(skeleton, clip, Math.Clamp(phase, 0, 1) * clip.Duration));
    }
    public static float Duration(AnimationProject project, FirstPersonProfile profile, string slot)
    {
        FirstPersonAction? action = profile.Actions.FirstOrDefault(a => a.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase));
        return project.Clips.FirstOrDefault(c => c.Id == action?.Clip)?.Duration ?? 1;
    }
}
