using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Movement;
using OpenScct.Server;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Checks head, torso and leg collision points against Godot's actual blended roll skeleton.</summary>
public sealed class RollCollisionSmoke : SmokeTest
{
    public const string TestName = "roll-collision";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        string? path = SkeletalModel.FindGlb("SPerso.ATT_01");
        if (path is null) return SmokeResult.Fail("Missing spy character content");
        string chars = Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
        var source = PawnAnimationSource.FromContent(chars);
        var pose = RollPillPose.FromContent(chars);
        var world = new World(1, ENetMode.NM_Standalone);
        var pawn = world.Level.SpawnActor(new SPawnAttaque_1Mesh(), new FVector(123, -456, 50), default)!;
        pawn.bIsRolling = true;
        pawn.bIsCrouched = true;
        pawn.CollisionHeight = 50f;
        pawn.PrePivot = new FVector(0, 0, 35);
        var model = SkeletalModel.Load(path);
        context.Tree.Root.AddChild(model.Root);
        var applier = new ChannelPoseApplier(model.Skeleton);
        var channels = new ChannelPose[PawnAnimation.ChannelCount];
        var pills = new List<PillShape>();
        float worst = 0f;
        int checks = 0;
        try
        {
            foreach (string clip in new[] { "RollStJpFdL", "RollStJpFdR", "RollStAlFdL", "RollStAlFdR" })
            foreach (int yaw in new[] { 0, 16384, 29000 })
            foreach (float seconds in new[] { 0f, .025f, .05f, .075f, .1f, .2f, .4f, .6f, .8f, 1f, 1.2f })
            {
                pawn.Rotation = new FRotator(0, yaw, 0);
                float alpha = Math.Clamp(seconds / .1f, 0f, 1f);
                Set(0, "WaitStAlFd0", .31f, 1f, true);
                // Include the run loop's final-to-first key interval in the startup blend.
                Set(4, "JoggStAlFd0", .63f, 1f - alpha, true);
                Set(SBasePawn.DBLJUMPCHANNEL, clip, seconds, alpha, false);
                model.Place(pawn.Location, pawn.Rotation, pawn.PrePivot);
                applier.Apply(channels);
                pills.Clear();
                pose.GetPills(pawn, pills);
                if (pills.Count != 15) return SmokeResult.Fail("Missing collision pills");
                foreach (var point in new[]
                {
                    (Pill: 1, End: false, Bone: "B Head", Offset: new FVector(4.172f, -2.376f, 0)),
                    (Pill: 1, End: true, Bone: "B Head", Offset: new FVector(11.519f, -.428f, 0)),
                    (Pill: 2, End: false, Bone: "B Spine2", Offset: new FVector(15.82f, -2.171f, 6.21f)),
                    (Pill: 6, End: false, Bone: "B L Thigh", Offset: new FVector(3.65f, -1.73f, .08f)),
                    (Pill: 14, End: true, Bone: "B R Foot", Offset: new FVector(-4.25f, -1.127f, .066f)),
                })
                {
                    int bone = model.Skeleton.FindBone(point.Bone);
                    if (bone < 0) return SmokeResult.Fail("Missing " + point.Bone);
                    Transform3D transform = model.Skeleton.GlobalTransform * model.CurrentBonePose(bone);
                    FVector expected = UeSpace.ToUe(transform * UeSpace.ToGodot(point.Offset));
                    PillShape pill = pills[point.Pill - 1];
                    float error = FVector.Dist(expected, point.End ? pill.B : pill.A);
                    worst = MathF.Max(worst, error);
                    if (error > .05f) return SmokeResult.Fail($"{clip} at {seconds:F3}s, yaw {yaw}, {point.Bone}: {error:F4} uu");
                    checks++;
                }
            }
            foreach (string asset in new[] { "SPerso.ATT_01", "SPerso.DEF_01" })
            {
                (int extra, float error) = CheckGeneral(context, asset, source, pose);
                checks += extra;
                worst = MathF.Max(worst, error);
            }
            return SmokeResult.Pass($"{checks} collision points match rendered rolls, general poses, mirrors, tweens, global starts and bone controls; max error {worst:F5} uu");
        }
        finally { model.Root.QueueFree(); }

        void Set(int channel, string sequence, float seconds, float alpha, bool loop)
        {
            if (!source.TryGetClip(pawn, new Name(sequence), out PawnClip clip))
                throw new InvalidOperationException("Missing animation " + sequence);
            pawn.PawnAnim.PlayAnim(channel, clip, 1f, 0f, loop);
            float frame = seconds * clip.Fps / clip.Frames;
            frame = loop ? frame % 1f : Math.Min(frame, 1f - 1f / clip.Frames);
            pawn.PawnAnim.SetFrame(channel, frame);
            pawn.PawnAnim.SetBlendAlpha(channel, alpha);
            channels[channel] = new ChannelPose
            {
                Clip = model.Sampler.Find(sequence), Sequence = sequence, Frame = frame, ClipFrame = frame,
                Frames = clip.Frames, Fps = clip.Fps, Alpha = alpha, Loop = loop,
            };
        }
    }

    private static (int Checks, float Worst) CheckGeneral(SmokeContext context, string asset, IPawnAnimationSource source, IPillPose pose)
    {
        var model = SkeletalModel.Load(SkeletalModel.FindGlb(asset)!);
        context.Tree.Root.AddChild(model.Root);
        var world = new World(1, ENetMode.NM_DedicatedServer);
        var pawn = world.Level.SpawnActor<SBasePawn>(asset.EndsWith("ATT_01", StringComparison.Ordinal)
            ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh(), new FVector(123, -456, 50), new FRotator(0, 29000, 0))!;
        pawn.PrePivot = new FVector(0, 0, 35);
        var applier = new ChannelPoseApplier(model.Skeleton, pawn.BoneMirrors, model.PackageBones);
        var channels = new ChannelPose[PawnAnimation.ChannelCount];
        var pills = new List<PillShape>();
        int checks = 0;
        float worst = 0f;
        model.Place(pawn.Location, pawn.Rotation, pawn.PrePivot);
        string[] clips = model.Sampler.ClipNames.Where(name => source.TryGetClip(pawn, new Name(name), out _)).ToArray();
        try
        {
            foreach (string sequence in clips)
            foreach (bool mirror in new[] { false, true })
            {
                Set(0, sequence, .35f, mirror);
                Check(sequence + " mirror=" + mirror);
            }
            foreach (string sequence in clips.Where((_, i) => i % 20 == 0))
            {
                Set(0, sequence, 0f, false);
                pawn.Physics = EPhysics.PHYS_RootMotion;
                pawn.iLockRootMotionType = 3;
                var rootMesh = (PawnRootMotion)((ISBasePawnPhysics)pawn).RootMotionMesh!;
                rootMesh.LockRootMotion(3);
                float lockedFrame = .6f * (1f - 1f / channels[0].Frames);
                pawn.PawnAnim.SetFrame(0, lockedFrame);
                channels[0].Frame = channels[0].ClipFrame = lockedFrame;
                Check(sequence + " root-lock");
                pawn.Physics = EPhysics.PHYS_None;
                rootMesh.LockRootMotion(0);
                Set(0, clips[0], 0f, true);
                foreach (float fraction in new[] { 1f, .75f, .5f, .25f, 0f })
                {
                    float frame = -fraction / channels[0].Frames;
                    pawn.PawnAnim.SetFrame(0, frame);
                    channels[0].Frame = frame;
                    channels[0].ClipFrame = 0f;
                    Check(sequence + " tween=" + fraction);
                }
                Set(0, sequence, .35f, false);
                Set(1, clips[0], .5f, false);
                pawn.PawnAnim.SetBlendParams(1, .6f, bone: new Name("B Spine2"), globalPose: true);
                channels[1].Alpha = .6f;
                channels[1].StartBone = model.Skeleton.FindBone("B Spine2");
                channels[1].GlobalPose = true;
                Check(sequence + " global-start");
                pawn.PawnAnim.SetBlendAlpha(1, 0f);
                channels[1] = default;
                foreach (float alpha in new[] { .5f, 1f })
                {
                    var rotation = new FRotator(5000, 23000, -1000);
                    pawn.PawnAnim.Directions.SetBoneDirection(new Name("B Spine2"), rotation, alpha);
                    Basis toSkeleton = model.Skeleton.GlobalTransform.Basis.Orthonormalized().Inverse();
                    applier.Directions[model.Skeleton.FindBone("B Spine2")] =
                        ((toSkeleton * UeSpace.RotationToGodot(rotation)).GetRotationQuaternion(), alpha);
                    Check(sequence + " bone-control=" + alpha);
                }
                pawn.PawnAnim.Directions.Clear();
                applier.Directions.Clear();
            }
            return (checks, worst);
        }
        finally { model.Root.QueueFree(); }

        void Set(int channel, string sequence, float fraction, bool mirror)
        {
            if (!source.TryGetClip(pawn, new Name(sequence), out PawnClip clip)) throw new InvalidOperationException(sequence);
            pawn.PawnAnim.PlayAnim(channel, clip, 1f, 0f, false);
            float frame = fraction * (1f - 1f / clip.Frames);
            pawn.PawnAnim.SetFrame(channel, frame);
            pawn.PawnAnim.SetBlendAlpha(channel, 1f);
            pawn.PawnAnim.SetMirror(channel, mirror);
            channels[channel] = new ChannelPose { Clip = model.Sampler.Find(sequence), Sequence = sequence,
                Frame = frame, ClipFrame = frame, Frames = clip.Frames, Fps = clip.Fps, Alpha = 1f, Mirror = mirror };
        }

        void Check(string label)
        {
            RootLock rootLock = RootLock.None;
            Vector3? rootOffset = null;
            Quaternion? rootTurn = null;
            if (pawn.Physics == EPhysics.PHYS_RootMotion)
            {
                var mesh = (PawnRootMotion)((ISBasePawnPhysics)pawn).RootMotionMesh!;
                rootLock = (RootLock)mesh.RootLockType;
                PawnAnimChannel c = pawn.PawnAnim[0];
                Basis skeleton = model.Skeleton.GlobalTransform.Basis;
                if (RootLockPose.Offset(c.Clip, c.ClipFrame, c.bMirror, mesh.LockedRoot, mesh.RootLockType,
                    c.bBackward, c.bLoop, out FVector local))
                {
                    pawn.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
                    rootOffset = skeleton.Inverse() * UeSpace.ToGodot(x * local.X + y * local.Y + z * local.Z);
                }
                Basis unit = skeleton.Orthonormalized();
                rootTurn = (unit.Inverse() * UeSpace.RotationToGodot(new FRotator(0, -mesh.CarriedRootTurn(c, c.ClipFrame), 0)) * unit).GetRotationQuaternion();
            }
            applier.Apply(channels, rootLock, rootOffset: rootOffset, rootTurn: rootTurn);
            pose.UpdatePose(pawn);
            pills.Clear();
            pose.GetPills(pawn, pills);
            foreach (var point in new[]
            {
                (Pill: 1, End: false, Bone: "B Head", Offset: new FVector(4.172f, -2.376f, 0)),
                (Pill: 2, End: false, Bone: "B Spine2", Offset: new FVector(15.82f, -2.171f, 6.21f)),
                (Pill: 3, End: true, Bone: "B L ForeArm", Offset: new FVector(.345f, 1.431f, -1.673f)),
                (Pill: 6, End: false, Bone: "B L Thigh", Offset: new FVector(3.65f, -1.73f, .08f)),
                (Pill: 14, End: true, Bone: "B R Foot", Offset: new FVector(-4.25f, -1.127f, .066f)),
            })
            {
                Transform3D transform = model.Skeleton.GlobalTransform * model.CurrentBonePose(model.Skeleton.FindBone(point.Bone));
                FVector expected = UeSpace.ToUe(transform * UeSpace.ToGodot(point.Offset));
                float error = FVector.Dist(expected, point.End ? pills[point.Pill - 1].B : pills[point.Pill - 1].A);
                if (error > .05f) throw new InvalidOperationException($"{asset} {label} {point.Bone}: {error:F4} uu");
                worst = MathF.Max(worst, error);
                checks++;
            }
        }
    }
}
