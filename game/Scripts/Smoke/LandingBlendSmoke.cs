using System;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

public sealed class LandingBlendSmoke : SmokeTest
{
    public const string TestName = "landing-blend";
    // 0.1 uu = 1 mm at the converted model's scale. Compare with the authored pose,
    // rather than requiring a fixed grip offset throughout an animated walk cycle.
    private const float PoseTolerance = 0.1f;
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        float maxContactError = 0f, maxBoundaryExcess = 0f;
        foreach ((string asset, string gunBone) in new[]
        {
            ("SWeaponFirst.1DEF_01", "corps"),
            ("SWeaponFirst.1DEF_Uzi", "corps_HK81"),
            ("SWeaponFirst.1DEF_shotgun", "corps_BM401"),
        })
        {
            string? primaryPath = SkeletalModel.FindGlb(asset);
            if (primaryPath is null) return SmokeResult.Fail($"Missing {asset}");
            var primary = SkeletalModel.Load(primaryPath);
            context.Tree.Root.AddChild(primary.Root);
            try
            {
                foreach (int fps in new[] { 30, 60, 144 })
                foreach (Name landing in new[] { ViewModelClips.FirstLandStFd2, ViewModelClips.FirstLandStHi2 })
                foreach (Name recovery in new[] { ViewModelClips.FirstWaitStFd2, ViewModelClips.FirstWalkStFd2 })
                {
                    string? error = CheckPrimary(primary, gunBone, fps, landing, recovery,
                        ref maxContactError, ref maxBoundaryExcess);
                    if (error is not null) return SmokeResult.Fail($"{asset}, {fps} FPS, {landing} -> {recovery}: {error}");
                }
            }
            finally { primary.Root.Free(); }
        }

        // Custom sidearms retain their existing entry and recovery easing.
        string? path = SkeletalModel.FindGlb(Sidearms.All[0].Asset + "_first");
        if (path is null) return SmokeResult.Fail("Missing first-person model");
        var model = SkeletalModel.Load(path);
        context.Tree.Root.AddChild(model.Root);
        try
        {
            foreach (int fps in new[] { 30, 60, 144 })
            foreach (var landing in new[] { ViewModelClips.FirstLandStFd2, ViewModelClips.FirstLandStHi2 })
            {
                var anim = new ViewModelAnim();
                var pose = new ViewModelPose();
                anim.Loop(0, ViewModelClips.FirstFallStFd2);
                pose.Update(anim, model.Sampler, 1f / fps, blendLandingRecovery: true);
                anim.Play(0, landing, ViewModelClips.DataOf(landing));
                if (pose.Update(anim, model.Sampler, 1f / fps, blendLandingRecovery: true)[0].Frame >= 0)
                    return SmokeResult.Fail($"{fps}: landing entry did not blend");
                for (int i = 0; i < fps / 5; i++) pose.Update(anim, model.Sampler, 1f / fps, blendLandingRecovery: true);
                if (pose.Update(anim, model.Sampler, 1f / fps, blendLandingRecovery: true)[0].Frame < 0)
                    return SmokeResult.Fail($"{fps}: entry blend did not finish");
                anim.Loop(0, ViewModelClips.FirstWaitStFd2);
                if (pose.Update(anim, model.Sampler, 1f / fps, blendLandingRecovery: true)[0].Frame >= 0)
                    return SmokeResult.Fail($"{fps}: landing recovery did not blend");
            }
            return SmokeResult.Pass($"36 primary landing dips/recoveries at 30/60/144 FPS: full authored dip after 0.2 s, grip error {maxContactError:F4} uu, " +
                $"boundary movement beyond authored pose {maxBoundaryExcess:F4} uu; custom sidearm entry/recovery still blend");
        }
        finally { model.Root.Free(); }
    }

    private static string? CheckPrimary(SkeletalModel model, string gunBone, int fps, Name landing, Name recovery,
        ref float maxContactError, ref float maxBoundaryExcess)
    {
        var applier = new ChannelPoseApplier(model.Skeleton);
        int gun = applier.FindBone(gunBone), left = applier.FindBone("Bip01 L Hand"), right = applier.FindBone("Bip01 R Hand");
        if (gun < 0 || left < 0 || right < 0) return "hand or gun contact bones are missing";
        var owner = new SWeaponDefense();
        var anim = owner.ViewAnim;
        var pose = new ViewModelPose();
        float dt = 1f / fps;
        anim.Loop(0, ViewModelClips.FirstFallStFd2);
        applier.Apply(pose.Update(anim, model.Sampler, dt, blendLandingRecovery: false));
        if (landing == ViewModelClips.FirstLandStHi2) owner.AnimateLandHigh(); else owner.AnimateLand();
        float entryElapsed = 0f;
        while (entryElapsed < K.LandingTweenSeconds)
        {
            float step = MathF.Min(dt, K.LandingTweenSeconds - entryElapsed);
            var entry = pose.Update(anim, model.Sampler, step, blendLandingRecovery: false);
            if (entry[0].Frame >= 0f || entry[0].ClipFrame != 0f)
                return $"landing recovery advanced before the 0.2 s dip tween ended ({entryElapsed:F3} s)";
            applier.Apply(entry);
            anim.Advance(step, owner, (_, _) => { });
            entryElapsed += step;
        }
        var dipped = pose.Update(anim, model.Sampler, 0f, blendLandingRecovery: false);
        applier.Apply(dipped);
        if (dipped[0].Clip is not { } landingClip) return "the landing clip is missing";
        foreach (int bone in new[] { gun, left, right, applier.FindBone("FX") })
        {
            if (bone < 0) continue;
            float error = model.CurrentBonePose(bone).Origin.DistanceTo(model.Sampler.SampleBone(landingClip, bone, 0f).Origin) * 100f;
            if (error > PoseTolerance) return $"{model.Skeleton.GetBoneName(bone)} never reaches the authored dip ({error:F3} uu short)";
        }
        Vector3[] previous = new Vector3[3], previousAuthored = new Vector3[3];
        bool hadLanding = false, sawRecovery = false;
        for (int frame = 0; frame < (int)MathF.Ceiling((ViewModelClips.DataOf(landing).Seconds(1f) + 0.3f) * fps); frame++)
        {
            var channels = pose.Update(anim, model.Sampler, dt, blendLandingRecovery: false);
            ChannelPose channel = channels[0];
            if (channel.Clip is null) return "the movement channel has no imported clip";
            applier.Apply(channels);
            // Runtime channels normalize by N keys; AnimSampler normalizes by the
            // last key's time. Compare the same imported animation time in each.
            float authoredPhase = channel.Clip.Length > 0f ?
                Math.Clamp(channel.ClipFrame * channel.Frames / (channel.Fps * channel.Clip.Length), 0f, 1f) : 0f;
            Transform3D actualGun = model.CurrentBonePose(gun);
            Transform3D authoredGun = model.Sampler.SampleBone(channel.Clip, gun, authoredPhase);
            int[] bones = [left, right, gun];
            bool recovering = anim.Sequence(0) == recovery;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform3D actual = model.CurrentBonePose(bones[i]);
                Transform3D authored = model.Sampler.SampleBone(channel.Clip, bones[i], authoredPhase);
                if (recovering)
                {
                    float contactError = Relative(actual, actualGun).DistanceTo(Relative(authored, authoredGun));
                    maxContactError = MathF.Max(maxContactError, contactError);
                    if (contactError > PoseTolerance)
                        return $"{model.Skeleton.GetBoneName(bones[i])} leaves its authored gun contact by {contactError:F3} uu";
                    if (hadLanding && !sawRecovery)
                    {
                        float step = actual.Origin.DistanceTo(previous[i]) * 100f;
                        float authoredStep = authored.Origin.DistanceTo(previousAuthored[i]) * 100f;
                        float excess = MathF.Max(0f, step - authoredStep);
                        maxBoundaryExcess = MathF.Max(maxBoundaryExcess, excess);
                        if (excess > PoseTolerance)
                            return $"{model.Skeleton.GetBoneName(bones[i])} jerks {excess:F3} uu beyond the authored recovery";
                    }
                }
                previous[i] = actual.Origin;
                previousAuthored[i] = authored.Origin;
            }
            if (recovering) sawRecovery = true;
            else hadLanding = true;
            anim.Advance(dt, owner, (_, channelIndex) =>
            {
                if (channelIndex == 0) anim.Loop(0, recovery);
            });
        }
        return sawRecovery ? null : "the landing never returned to locomotion";
    }

    private static Vector3 Relative(Transform3D hand, Transform3D gun) =>
        gun.Basis.Orthonormalized().Inverse() * (hand.Origin - gun.Origin) * 100f;
}

