using System;
using System.IO;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Mirrored traversal poses retain the root and reflect the body's mesh-space bones.</summary>
public sealed class TraversalMirrorSmoke : SmokeTest
{
    public const string TestName = "traversal-mirror";
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
        SkeletalModel source = SkeletalModel.Load(path), mirrored = SkeletalModel.Load(path);
        try
        {
            var a = new ChannelPoseApplier(source.Skeleton);
            var b = new ChannelPoseApplier(mirrored.Skeleton, new SPawnAttaque_1Mesh().BoneMirrors, mirrored.PackageBones);
            int checks = 0;
            foreach ((string name, int frames, float fps) in new[]
            {
                ("LedgStAlTiL", 16, 15f), ("LedgStAlToL", 26, 15f), ("PipVStAlNtL", 15, 15f),
            })
            {
                AnimClip clip = source.Sampler.Find(name) ?? throw new InvalidOperationException("Missing " + name);
                foreach (float progress in new[] { 0f, 0.2f, 0.5f, 0.9f })
                {
                    ChannelPose pose = new() { Clip = clip, Sequence = name, Frame = progress, ClipFrame = progress,
                        Frames = frames, Fps = fps, Alpha = 1 };
                    a.Apply([pose]);
                    pose.Mirror = true;
                    b.Apply([pose]);
                    // Root B is explicitly -1 in the original pawn's BoneMirrors table. Reflecting it turns the
                    // whole skeleton before the limb transforms are applied.
                    int root = a.FindBone("B");
                    RequireNear(source.Skeleton.GetBonePose(root).Origin, mirrored.Skeleton.GetBonePose(root).Origin, name + " root");
                    RequireNear(source.Skeleton.GetBonePose(root).Basis.X, mirrored.Skeleton.GetBonePose(root).Basis.X, name + " root rotation");
                    foreach (string bone in new[] { "B Pelvis", "B Spine", "B Head", "B L UpperArm", "B L ForeArm", "B L Hand", "B L Thigh", "B L Foot" })
                    {
                        string target = bone.Replace(" L ", " R ", StringComparison.Ordinal);
                        Transform3D from = source.CurrentBonePose(a.FindBone(bone));
                        Transform3D to = mirrored.CurrentBonePose(b.FindBone(target));
                        RequireNear(new Vector3(-from.Origin.X, from.Origin.Y, from.Origin.Z), to.Origin, name + " " + target);
                        RequireNear(new Vector3(-from.Basis.X.X, from.Basis.X.Y, from.Basis.X.Z), to.Basis.X, name + " " + target + " X axis");
                        RequireNear(new Vector3(from.Basis.Y.X, -from.Basis.Y.Y, -from.Basis.Y.Z), to.Basis.Y, name + " " + target + " Y axis");
                        RequireNear(new Vector3(-from.Basis.Z.X, from.Basis.Z.Y, from.Basis.Z.Z), to.Basis.Z, name + " " + target + " Z axis");
                        checks++;
                    }
                }
            }

            _result = $"{checks} corner/pipe bone transforms; unmirrored root; inside/outside corner phases";
            if (System.Environment.GetEnvironmentVariable("SVM_TRAVERSAL_MIRROR_PREVIEW") is { Length: > 0 } preview)
            {
                MakePreview(context, path, preview);
                return SmokeResult.Running;
            }
            return SmokeResult.Pass(_result);
        }
        catch (Exception error) { return SmokeResult.Fail(error.ToString()); }
        finally { source.Root.Free(); mirrored.Root.Free(); }
    }

    private void MakePreview(SmokeContext context, string modelPath, string output)
    {
        _previewPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        _preview = new SubViewport { Size = new Vector2I(1600, 620), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
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
        foreach ((string clipName, int frames, string label) in new[] { ("LedgStAlTiL", 16, "INSIDE"), ("LedgStAlToL", 26, "OUTSIDE"), ("PipVStAlNtL", 15, "PIPE HOLD") })
        {
            foreach (bool mirror in new[] { false, true })
            {
                SkeletalModel model = SkeletalModel.Load(modelPath);
                world.AddChild(model.Root);
                model.Pivot.Transform = Transform3D.Identity;
                model.Root.Position = new Vector3(-5f + index * 2f, 0f, 0f);
                var material = new StandardMaterial3D { AlbedoColor = new Color(0.48f, 0.6f, 0.7f), Roughness = 0.85f };
                foreach (MeshInstance3D mesh in model.Meshes) mesh.MaterialOverride = material;
                var applier = new ChannelPoseApplier(model.Skeleton, mirrors, model.PackageBones);
                applier.Apply([new ChannelPose { Clip = model.Sampler.Find(clipName), Sequence = clipName, Frame = 0.5f,
                    ClipFrame = 0.5f, Frames = frames, Fps = 15f, Alpha = 1f, Mirror = mirror }]);
                world.AddChild(new Label3D { Text = label + (mirror ? " / MIRRORED" : " / ORIGINAL CLIP"), FontSize = 28,
                    PixelSize = 0.004f, Position = new Vector3(model.Root.Position.X, 1.6f, 0f) });
                index++;
            }
        }
    }

    private static void RequireNear(Vector3 expected, Vector3 actual, string label)
    {
        if (expected.DistanceTo(actual) > 0.0001f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
