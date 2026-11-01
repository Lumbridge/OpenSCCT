using System;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

// This smoke samples many poses in one frame; flush the skeleton after each
// sample instead of waiting for its ordinary per-frame update.
#pragma warning disable CS0618

/// <summary>Exercise imported animation tracks through the runtime channel applier.</summary>
public sealed class SidearmReloadPoseSmoke : SmokeTest
{
    public const string TestName = "sidearm-reload-pose";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        foreach (var sidearm in Sidearms.All)
        {
            string? path = SkeletalModel.FindGlb(sidearm.Asset + "_first");
            if (path is null) return SmokeResult.Fail($"Missing {sidearm.Id}");
            var model = SkeletalModel.Load(path);
            context.Tree.Root.AddChild(model.Root);
            try
            {
                var applier = new ChannelPoseApplier(model.Skeleton);
                var reload = model.Sampler.Find("FirstReloStFd2")!;
                int wrist = applier.FindBone("R_wrist_028"), gun = applier.FindBone("pistol");
                if (wrist < 0 || gun < 0 || model.Skeleton.GetBoneParent(gun) != wrist)
                    return SmokeResult.Fail($"{sidearm.Id}: pistol is not mounted to the firing wrist");
                var idle = model.Sampler.Find("FirstWaitStFd2")!;
                Transform3D grip = model.Sampler.SampleBone(idle, wrist, 0).AffineInverse() * model.Sampler.SampleBone(idle, gun, 0);
                int fx = model.Skeleton.FindBone("FX");
                Transform3D neutralBarrel = model.Root.GlobalTransform.AffineInverse() * model.Skeleton.GlobalTransform *
                    model.Sampler.SampleBone(idle, fx, .5f);
                Transform3D fixedSight = FirstPersonView.SidearmSightOffset(neutralBarrel);
                Transform3D zeroed = fixedSight * neutralBarrel;
                if (zeroed.Origin.DistanceTo(neutralBarrel.Origin) > .0001f ||
                    zeroed.Basis.X.Normalized().DistanceTo((Vector3.Right * 10 - zeroed.Origin).Normalized()) > .0001f)
                    return SmokeResult.Fail($"{sidearm.Id}: fixed sight zero is wrong");
                foreach (var aim in new[] { new FRotator(0, 0, 0), new FRotator(5000, 16000, 0), new FRotator(-4000, -12000, 0) })
                {
                    Transform3D camera = UeSpace.TransformToGodot(new FVector(100, 200, 300), aim);
                    Transform3D placed = camera * fixedSight;
                    Transform3D local = camera.AffineInverse() * placed;
                    if (!local.IsEqualApprox(fixedSight))
                        return SmokeResult.Fail($"{sidearm.Id}: turning changes the fixed viewmodel offset");
                }
                foreach (Name motion in new[] { ViewModelClips.FirstWaitStFd2, ViewModelClips.FirstWalkStFd2 })
                {
                    var animation = new ViewModelAnim();
                    animation.Loop(0, motion);
                    var pose = new ViewModelPose();
                    float low = float.PositiveInfinity, high = float.NegativeInfinity;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        applier.Apply(pose.Update(animation, model.Sampler, 1f / 60));
                        model.Skeleton.ForceUpdateAllBoneTransforms();
                        float y = (model.Skeleton.GlobalTransform * model.Skeleton.GetBoneGlobalPose(gun)).Origin.Y;
                        low = MathF.Min(low, y); high = MathF.Max(high, y);
                    }
                    float required = motion == ViewModelClips.FirstWaitStFd2 ? .003f : .01f;
                    if (high - low < required)
                        return SmokeResult.Fail($"{sidearm.Id}: {motion} runtime vertical bob is missing ({high-low})");
                    animation.Loop(0, ViewModelClips.FirstJumpStFd2);
                    animation.SetAlpha(4, .25f);
                    animation.Play(4, ViewModelClips.FirstReloStFd2, ViewModelClips.DataOf(ViewModelClips.FirstReloStFd2));
                    animation.SetAlpha(5, 1);
                    animation.Loop(5, ViewModelClips.FirstLandStFd2);
                    var isolated = pose.Update(animation, model.Sampler, 1f / 60, exclusiveAction: true);
                    if (isolated[0].Clip is not null || isolated[5].Clip is not null || isolated[4].Alpha != 1)
                        return SmokeResult.Fail($"{sidearm.Id}: movement or overlays leak into the exclusive reload");
                }
                foreach (string movement in new[] { "FirstJumpStFd2", "FirstLandStFd2", "FirstFallStFd2" })
                foreach (float phase in new[] { .15f, .28f, .58f, .85f })
                {
                    foreach (float weight in new[] { 0f, .25f, .5f, .75f, 1f })
                    {
                        var blended = new ChannelPose[ViewModelAnim.ChannelCount];
                        blended[0] = Channel(model.Sampler.Find(movement)!, phase);
                        blended[4] = Channel(reload, phase) with { Alpha = weight };
                        applier.Apply(blended);
                        model.Skeleton.ForceUpdateAllBoneTransforms();
                        Transform3D attached = model.Skeleton.GetBoneGlobalPose(wrist) * grip;
                        Transform3D actualGun = model.Skeleton.GetBoneGlobalPose(gun);
                        if (actualGun.Origin.DistanceTo(attached.Origin) > .0001f ||
                            (actualGun.Basis.X - attached.Basis.X).Length() > .0001f)
                            return SmokeResult.Fail($"{sidearm.Id}: grip separates during {movement}, reload {phase}, weight {weight}");
                    }
                    var state = new ViewModelAnim();
                    state.SetAlpha(4, 1);
                    var reloadName = new Name(reload.Name);
                    var reloadData = ViewModelClips.DataOf(reloadName);
                    state.Play(4, reloadName, reloadData);
                    state.SetFrame(4, phase * (reloadData.Frames - 1), reloadData);
                    var sampler = new ViewModelPose();
                    applier.Apply(sampler.Update(state, model.Sampler, 0, exclusiveAction: true));
                    model.Skeleton.ForceUpdateAllBoneTransforms();
                    var expected = new Transform3D[model.Skeleton.GetBoneCount()];
                    for (int b = 0; b < expected.Length; b++) expected[b] = model.Skeleton.GetBoneGlobalPose(b);
                    state.Loop(0, new Name(movement));
                    applier.Apply(sampler.Update(state, model.Sampler, 0, exclusiveAction: true));
                    model.Skeleton.ForceUpdateAllBoneTransforms();
                    for (int b = 0; b < expected.Length; b++)
                    {
                        Transform3D actual = model.Skeleton.GetBoneGlobalPose(b);
                        if (actual.Origin.DistanceTo(expected[b].Origin) > .0001f ||
                            (actual.Basis.X - expected[b].Basis.X).Length() > .0001f ||
                            (actual.Basis.Y - expected[b].Basis.Y).Length() > .0001f ||
                            (actual.Basis.Z - expected[b].Basis.Z).Length() > .0001f)
                            return SmokeResult.Fail($"{sidearm.Id}: {movement} changes reload {phase} bone {model.Skeleton.GetBoneName(b)}");
                    }
                }
            }
            finally { model.Root.Free(); }
        }
        return SmokeResult.Pass("Imported sidearm reload poses stay attached during jump, fall and landing");
    }

    private static ChannelPose Channel(AnimClip clip, float phase) => new()
    {
        Clip = clip, Sequence = clip.Name, Frame = phase, ClipFrame = phase,
        Frames = Math.Max(2, (int)MathF.Round(clip.Length * 15) + 1), Fps = 15, Alpha = 1,
    };
}
