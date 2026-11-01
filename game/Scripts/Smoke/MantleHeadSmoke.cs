using System;
using System.IO;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>The spy's visible head stays attached through both leading-foot variants of a running mantle.</summary>
public sealed class MantleHeadSmoke : SmokeTest
{
    public const string TestName = "mantle-head";
    public override string Name => TestName;
    private SubViewport? _preview;
    private string? _previewPath;
    private int _previewFrames;
    private string _result = "";

    public override SmokeResult Step(SmokeContext context)
    {
        if (_preview is not null)
        {
            if (++_previewFrames < 8) return SmokeResult.Running;
            using Image image = _preview.GetTexture().GetImage();
            Error saved = image.SavePng(_previewPath!);
            _preview.Free();
            _preview = null;
            return saved == Error.Ok ? SmokeResult.Pass(_result + "; preview=" + _previewPath) : SmokeResult.Fail("Preview save: " + saved);
        }

        string? path = SkeletalModel.FindGlb("SPerso.ATT_01");
        if (path is null) return SmokeResult.Fail("Missing spy model");
        SkeletalModel model = SkeletalModel.Load(path);
        try
        {
            int checks = 0;
            var mirrors = new SPawnAttaque_1Mesh().BoneMirrors;
            foreach (int drawFps in new[] { 30, 60, 144 })
            {
                foreach ((string name, int frames) in new[] { ("WljpStAlUwL", 13), ("WljpStAlUmL", 10) })
                {
                    foreach (bool mirror in new[] { false, true })
                    {
                        var applier = new ChannelPoseApplier(model.Skeleton, mirrors, model.PackageBones);
                        // MovesetActions: the leading foot picks BgL/BgR, then a mirrored small/waist wall climb.
                        // Both plays have zero tween, so the first mirrored pose must already keep the head attached.
                        checks += Sample(model, applier, mirror ? "WljpStAlBgR" : "WljpStAlBgL", 8, 30f, drawFps, RootLock.Translation);
                        checks += Sample(model, applier, name, frames, 30f, drawFps, RootLock.Translation, mirror);
                        // The arms-zone catch starts the fast waist detect, then the unmirrored quick mantle.
                        checks += Sample(model, applier, "LedgStAlBQ0", 2, 15f, drawFps, RootLock.None, tween: 0.08f, rate: 50f);
                        checks += Sample(model, applier, "LedgStAlQk0", 19, 15f, drawFps, RootLock.Translation | RootLock.Yaw, rate: 1.3f);
                    }
                }

                foreach (string name in new[] { "LedgStAlFtL", "LedgStAlFtR" })
                {
                    var applier = new ChannelPoseApplier(model.Skeleton, mirrors, model.PackageBones);
                    checks += Sample(model, applier, "WaitStAlFd0", 8, 8f, drawFps, RootLock.None);
                    checks += Sample(model, applier, name, 7, 15f, drawFps, RootLock.Translation | RootLock.Yaw, tween: 0.1f);
                }
            }

            _result = $"{checks} head attachment poses; both leading feet; wall/waist/feet mantles at 30/60/144 fps";
            if (System.Environment.GetEnvironmentVariable("SVM_MANTLE_HEAD_PREVIEW") is { Length: > 0 } preview)
            {
                MakePreview(context, path, preview);
                return SmokeResult.Running;
            }
            return SmokeResult.Pass(_result);
        }
        catch (Exception error) { return SmokeResult.Fail(error.ToString()); }
        finally { model.Root.Free(); }
    }

    private static int Sample(SkeletalModel model, ChannelPoseApplier applier, string name, int frames, float fps,
        int drawFps, RootLock rootLock, bool mirror = false, float tween = 0f, float rate = 1f)
    {
        AnimClip clip = model.Sampler.Find(name) ?? throw new InvalidOperationException("Missing " + name);
        ChannelPose pose = new() { Clip = clip, Sequence = name, Frames = frames, Fps = fps, Alpha = 1f, Mirror = mirror };
        int checks = 0;
        if (tween > 0f)
        {
            int steps = (int)MathF.Ceiling(tween * drawFps);
            for (int i = 0; i < steps; i++)
            {
                pose.Frame = -(1f - Math.Min(i / (float)drawFps, tween) / tween) / frames;
                applier.Apply([pose], rootLock);
                RequireHeadAttached(model, $"{name} mirror={mirror} tween={i} fps={drawFps}");
                checks++;
            }
        }

        float duration = (frames - 1f) / (fps * rate);
        int samples = (int)MathF.Ceiling(duration * drawFps);
        for (int i = 0; i <= samples; i++)
        {
            pose.Frame = pose.ClipFrame = Math.Min(i / (float)drawFps, duration) * fps * rate / frames;
            applier.Apply([pose], rootLock);
            RequireHeadAttached(model, $"{name} mirror={mirror} frame={pose.Frame} fps={drawFps}");
            checks++;
        }
        return checks;
    }

    private static void RequireHeadAttached(SkeletalModel model, string label)
    {
        int head = model.Skeleton.FindBone("B Head"), attachment = model.Skeleton.FindBone("ATT_02_HEAD");
        if (head < 0 || attachment < 0 || model.Skeleton.GetBoneParent(attachment) != head)
            throw new InvalidOperationException("Missing spy head attachment hierarchy");
        // This static helper skins the visible head. Checking only animated B Head misses its displaced mesh.
        Transform3D expected = model.CurrentBonePose(head) * model.Skeleton.GetBoneRest(attachment);
        Transform3D actual = model.CurrentBonePose(attachment);
        RequireNear(expected.Origin, actual.Origin, label + " head position");
        RequireNear(expected.Basis.X, actual.Basis.X, label + " head X axis");
        RequireNear(expected.Basis.Y, actual.Basis.Y, label + " head Y axis");
        RequireNear(expected.Basis.Z, actual.Basis.Z, label + " head Z axis");
    }

    private static void RequireNear(Vector3 expected, Vector3 actual, string label)
    {
        float error = expected.DistanceTo(actual);
        if (!float.IsFinite(error) || error > 0.0001f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private void MakePreview(SmokeContext context, string modelPath, string output)
    {
        _previewPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        _preview = new SubViewport { Size = new Vector2I(1600, 800), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_preview);
        var world = new Node3D();
        _preview.AddChild(world);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.035f, 0.045f, 0.065f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.65f,
        };
        world.AddChild(new WorldEnvironment { Environment = environment });
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35f, -30f, 0f), LightEnergy = 1.2f });
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 4.8f, Position = new Vector3(0f, 0.2f, 10f), Current = true };
        world.AddChild(camera);
        camera.LookAt(new Vector3(0f, 0.2f, 0f));
        var mirrors = new SPawnAttaque_1Mesh().BoneMirrors;
        int index = 0;
        foreach ((string name, int frames, string label) in new[] { ("WljpStAlUwL", 13, "WAIST MANTLE"), ("WljpStAlUmL", 10, "SMALL MANTLE") })
        {
            foreach (bool mirror in new[] { false, true })
            {
                SkeletalModel model = SkeletalModel.Load(modelPath);
                world.AddChild(model.Root);
                model.Pivot.Transform = Transform3D.Identity;
                model.Root.Position = new Vector3(-3.45f + index * 2.3f, 0f, 0f);
                var material = new StandardMaterial3D { AlbedoColor = new Color(0.48f, 0.6f, 0.7f), Roughness = 0.85f };
                foreach (MeshInstance3D mesh in model.Meshes) mesh.MaterialOverride = material;
                var applier = new ChannelPoseApplier(model.Skeleton, mirrors, model.PackageBones);
                applier.Apply([new ChannelPose { Clip = model.Sampler.Find(name), Sequence = name, Frame = 0.5f,
                    ClipFrame = 0.5f, Frames = frames, Fps = 30f, Alpha = 1f, Mirror = mirror }], RootLock.Translation);
                world.AddChild(new Label3D { Text = label + (mirror ? " / MIRRORED" : " / ORIGINAL"), FontSize = 28,
                    PixelSize = 0.004f, Position = new Vector3(model.Root.Position.X, 1.6f, 0f) });
                index++;
            }
        }
    }
}
