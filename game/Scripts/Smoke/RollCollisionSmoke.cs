using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
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
            return SmokeResult.Pass($"{checks} collision points match the rendered roll skeleton; max error {worst:F5} uu");
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
}
