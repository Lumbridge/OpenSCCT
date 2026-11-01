using System;
using System.Linq;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Exact cached/uncached bone parity and shadow-layer lifecycle checks for the performance changes.</summary>
public sealed class PresentationPerformanceSmoke : SmokeTest
{
    public const string TestName = "presentation-performance";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            int poses = CheckModel("SPerso.ATT_01") + CheckModel("SPerso.DEF_01")
                + CheckModel("SWeaponFirst.M160") + CheckModel("SWeaponFirst.p190")
                + CheckModel("SWeaponFirst.1DEF_01") + CheckModel("SWeaponFirst.1DEF_Uzi")
                + CheckModel("SWeaponFirst.1DEF_shotgun");
            foreach (string sidearm in new[] { "automatic", "desert_eagle", "magnum44", "suppressed", "unsuppressed" })
                poses += CheckModel("OpenScctSidearms." + sidearm + "_first");
            CheckLayers(context.Tree);
            CheckSignedScale();
            return SmokeResult.Pass($"{poses} exact cached/uncached poses; signed/zero scale; vision and nested attachment/reparent/reentry layers");
        }
        catch (Exception error)
        {
            return SmokeResult.Fail(error.ToString());
        }
    }

    private static int CheckModel(string asset)
    {
        string path = SkeletalModel.FindGlb(asset) ?? throw new InvalidOperationException("Missing " + asset);
        SkeletalModel cached = SkeletalModel.Load(path), reference = SkeletalModel.Load(path);
        try
        {
            var a = new ChannelPoseApplier(cached.Skeleton);
            var b = new ChannelPoseApplier(reference.Skeleton) { ReuseUnchangedPoses = false };
            int poses = 0;
            foreach (string name in cached.Sampler.ClipNames.Take(24))
            {
                AnimClip clip = cached.Sampler.Find(name)!;
                ChannelPose[] channels = new ChannelPose[2];
                for (int step = 0; step < 20; step++)
                {
                    // Repeated inputs exercise the skipped writes; changing overlays/tweens must still advance.
                    float frame = (step / 2) / 10f;
                    channels[0] = new ChannelPose { Clip = clip, Sequence = name, Frame = step < 4 ? -0.1f / (step + 1) : frame,
                        ClipFrame = frame, Frames = 31, Fps = 30, Alpha = 1, Loop = true, Mirror = (poses / 20) % 2 == 0 };
                    channels[1] = channels[0];
                    channels[1].Alpha = 0.3f;
                    channels[1].StartBone = Math.Min(2, cached.Skeleton.GetBoneCount() - 1);
                    channels[1].ClipFrame = 1 - frame;
                    a.AimBone = b.AimBone = channels[1].StartBone;
                    a.AimRotation = b.AimRotation = new Quaternion(Vector3.Up, frame * 0.1f);
                    a.ExtraShift = b.ExtraShift = new Vector3(0, frame * 0.01f, 0);
                    a.Directions[a.AimBone] = b.Directions[b.AimBone] = (new Quaternion(Vector3.Right, frame), 0.2f);
                    a.ExtraTurns[a.AimBone] = b.ExtraTurns[b.AimBone] = new Quaternion(Vector3.Forward, frame * 0.1f);
                    RootLock rootLock = (poses / 20) % 3 == 0 ? RootLock.Translation | RootLock.Yaw : RootLock.None;
                    a.Apply(channels, rootLock, 0.25f);
                    b.Apply(channels, rootLock, 0.25f);
                    SamePose(cached.Skeleton, reference.Skeleton, asset + "/" + name);
                    poses++;
                }
            }

            Vector4 vision = new(0.2f, 0.4f, 0.6f, 0.8f);
            cached.SetVision(vision);
            cached.SetVision(vision);
            if (cached.Slots.Count > 0) cached.SplitPart(cached.Slots[0]);
            cached.SetVision(vision);
            foreach (MeshInstance3D mesh in cached.Meshes)
                if (mesh.GetInstanceShaderParameter(GameplayObjects.VisionActorParameter).AsVector4() != vision)
                    throw new InvalidOperationException("Vision differs after repeat/split");
            return poses;
        }
        finally
        {
            cached.Root.Free();
            reference.Root.Free();
        }
    }

    private static void CheckSignedScale()
    {
        using var animation = new Animation { Length = 1 };
        int track = animation.AddTrack(Animation.TrackType.Scale3D);
        animation.ScaleTrackInsertKey(track, 0, new Vector3(-1, 1, 1));
        animation.ScaleTrackInsertKey(track, 0.5, new Vector3(0, 1, 1));
        animation.ScaleTrackInsertKey(track, 1, Vector3.One);
        var clip = new AnimClip("signed", animation, [(0, track, Animation.TrackType.Scale3D)]);
        var cached = new Skeleton3D();
        var reference = new Skeleton3D();
        try
        {
            cached.AddBone("root");
            reference.AddBone("root");
            var a = new ChannelPoseApplier(cached);
            var b = new ChannelPoseApplier(reference) { ReuseUnchangedPoses = false };
            foreach (float frame in new[] { 0f, 0f, 0.5f, 0.5f, 1f, 1f, 0f })
            {
                ChannelPose[] channels = [new() { Clip = clip, Sequence = "signed", Frame = frame, ClipFrame = frame, Frames = 31, Fps = 30, Alpha = 1 }];
                a.Apply(channels);
                b.Apply(channels);
                SamePose(cached, reference, "signed-scale");
            }
        }
        finally { cached.Free(); reference.Free(); }
    }

    private static void SamePose(Skeleton3D a, Skeleton3D b, string context)
    {
        for (int bone = 0; bone < a.GetBoneCount(); bone++)
            if (a.GetBonePose(bone) != b.GetBonePose(bone))
                throw new InvalidOperationException($"Pose mismatch {context} bone={bone}");
    }

    private static void CheckLayers(SceneTree tree)
    {
        var view = new SimPawnView();
        var outside = new Node3D();
        var branch = new Node3D();
        var body = new MeshInstance3D { Layers = RenderLayers.World | RenderLayers.Effects };
        branch.AddChild(body);
        view.AddChild(branch);
        tree.Root.AddChild(view);
        tree.Root.AddChild(outside);
        try
        {
            view.SetShadowOnly(true);
            RequireLayers(body, RenderLayers.ShadowOnly | RenderLayers.Effects);
            var late = new MeshInstance3D { Layers = RenderLayers.World };
            branch.AddChild(late);
            RequireLayers(late, RenderLayers.ShadowOnly);
            var glow = new MeshInstance3D { Layers = RenderLayers.Effects };
            branch.AddChild(glow);
            view.SetShadowOnly(true);
            RequireLayers(glow, RenderLayers.Effects);
            late.Reparent(outside);
            view.SetShadowOnly(false);
            RequireLayers(body, RenderLayers.World | RenderLayers.Effects);
            RequireLayers(late, RenderLayers.ShadowOnly); // Removed geometry is no longer rewritten.
            late.Reparent(branch);
            view.SetShadowOnly(true);
            view.SetShadowOnly(false);
            RequireLayers(late, RenderLayers.World);
            tree.Root.RemoveChild(view);
            view.SetShadowOnly(true);
            var offTree = new MeshInstance3D { Layers = RenderLayers.World };
            branch.AddChild(offTree);
            view.SetShadowOnly(true);
            RequireLayers(offTree, RenderLayers.ShadowOnly);
            tree.Root.AddChild(view);
            view.SetShadowOnly(false);
            RequireLayers(offTree, RenderLayers.World);
        }
        finally
        {
            view.Free();
            outside.Free();
        }
    }

    private static void RequireLayers(GeometryInstance3D geometry, uint expected)
    {
        if (geometry.Layers != expected) throw new InvalidOperationException($"Layers {geometry.Layers} != {expected}");
    }
}
