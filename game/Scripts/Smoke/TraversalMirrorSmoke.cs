using System;
using System.IO;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Mirrored traversal poses retain the root, reflect the body, and keep the rendered head attached.</summary>
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
            int checks = 0, headChecks = 0;
            foreach ((string name, int frames, float fps) in new[]
            {
                ("LedgStAlTiL", 16, 15f), ("LedgStAlToL", 26, 15f), ("PipVStAlNtL", 15, 15f),
            })
            {
                AnimClip clip = source.Sampler.Find(name) ?? throw new InvalidOperationException("Missing " + name);
                foreach (float progress in new[] { 0f, 0.2f, 0.5f, 0.9f, (frames - 1f) / frames })
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
                    RequireHeadAttached(source, name + " left " + progress);
                    RequireHeadAttached(mirrored, name + " right " + progress);
                    headChecks += 2;
                }

                // The right corner tweens from an unmirrored hang into a mirrored clip and back again.
                // Check both directions through each tween, as a bad helper rotation can move the visible head
                // far from B Head even while the animated B Head bone itself passes every reflection check.
                AnimClip hold = source.Sampler.Find("LedgStAlNt0") ?? throw new InvalidOperationException("Missing ledge hold");
                foreach (bool mirror in new[] { false, true })
                {
                    ChannelPose idle = new() { Clip = hold, Sequence = "LedgStAlNt0", Frame = 0f, ClipFrame = 0f,
                        Frames = 15, Fps = 15f, Alpha = 1f, Loop = true };
                    b.Apply([idle]);
                    ChannelPose corner = new() { Clip = clip, Sequence = name, Frames = frames, Fps = fps,
                        Alpha = 1f, Mirror = mirror };
                    foreach (float fraction in new[] { 1f, 0.75f, 0.5f, 0.25f, 0f })
                    {
                        corner.Frame = -fraction / frames;
                        b.Apply([corner]);
                        RequireHeadAttached(mirrored, name + " enter " + mirror + " " + fraction);
                        headChecks++;
                    }
                    corner.Frame = corner.ClipFrame = 0.5f;
                    b.Apply([corner]);
                    foreach (float fraction in new[] { 1f, 0.75f, 0.5f, 0.25f, 0f })
                    {
                        idle.Frame = -fraction / idle.Frames;
                        b.Apply([idle]);
                        RequireHeadAttached(mirrored, name + " exit " + mirror + " " + fraction);
                        headChecks++;
                    }
                }
            }

            _result = $"{checks} corner/pipe bone transforms; {headChecks} head attachment poses/tweens; unmirrored root";
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
        // Every traversal clip the game mirrors, paired with its original pose. Keep this visual check broader
        // than the corner regression: static head, helmet, laser and holster helpers use different bind frames.
        var clips = new[]
        {
            (false, "LedgStAlTiL", 16, 15f),
            (false, "LedgStAlToL", 26, 15f), (false, "PipVStAlNtL", 15, 15f),
            (false, "PipVStAlTrRL", 19, 15f), (false, "PipVStAlTrLL", 19, 15f), (false, "PipVStLgEuR", 21, 15f),
            (false, "PipVStLgBuR", 21, 15f), (false, "LaddStAlNtL", 9, 8f), (false, "LaddStAlTuR", 9, 8f),
            (false, "WljpStAlUwL", 13, 30f), (false, "WljpStAlUmL", 10, 30f), (true, "LaddStAlNtL", 8, 8f),
            (true, "LaddStAlTuR", 9, 8f),
        };
        int rows = (clips.Length + 2) / 3;
        _preview = new SubViewport { Size = new Vector2I(1600, rows * 520), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
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
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = rows * 4f, Position = new Vector3(0f, 0.2f, 10f), Current = true };
        world.AddChild(camera);
        camera.LookAt(new Vector3(0f, 0.2f, 0f));
        int index = 0;
        foreach ((bool merc, string clipName, int frames, float fps) in clips)
        {
            string path = merc ? SkeletalModel.FindGlb("SPerso.DEF_01") ?? throw new InvalidOperationException("Missing merc model") : modelPath;
            SBasePawn pawn = merc ? new SPAWNDEFENSE() : new SPawnAttaque_1Mesh();
            foreach (bool mirror in new[] { false, true })
            {
                SkeletalModel model = SkeletalModel.Load(path);
                world.AddChild(model.Root);
                model.Pivot.Transform = Transform3D.Identity;
                model.Root.Position = new Vector3(-5f + (index % 6) * 2f, (rows - 1) * 2f - (index / 6) * 4f, 0f);
                var material = new StandardMaterial3D { AlbedoColor = new Color(0.48f, 0.6f, 0.7f), Roughness = 0.85f };
                foreach (MeshInstance3D mesh in model.Meshes) mesh.MaterialOverride = material;
                var applier = new ChannelPoseApplier(model.Skeleton, pawn.BoneMirrors, model.PackageBones);
                applier.Apply([new ChannelPose { Clip = model.Sampler.Find(clipName), Sequence = clipName, Frame = 0.5f,
                    ClipFrame = 0.5f, Frames = frames, Fps = fps, Alpha = 1f, Mirror = mirror }], RootLock.Translation);
                world.AddChild(new Label3D { Text = (merc ? "MERC " : "SPY ") + clipName + (mirror ? "\nMIRRORED" : "\nORIGINAL"), FontSize = 25,
                    PixelSize = 0.004f, Position = model.Root.Position + new Vector3(0f, 1.6f, 0f) });
                index++;
            }
        }
    }

    private static void RequireNear(Vector3 expected, Vector3 actual, string label)
    {
        if (expected.DistanceTo(actual) > 0.0001f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void RequireHeadAttached(SkeletalModel model, string label)
    {
        int head = model.Skeleton.FindBone("B Head"), attachment = model.Skeleton.FindBone("ATT_02_HEAD");
        if (head < 0 || attachment < 0 || model.Skeleton.GetBoneParent(attachment) != head)
            throw new InvalidOperationException("Missing spy head attachment hierarchy");
        // ATT_02_HEAD carries the visible head mesh and has no authored animation tracks. Its fixed offset
        // must follow B Head; unlike the body bones, the mesh attachment uses a different local axis frame.
        Transform3D expected = model.CurrentBonePose(head) * model.Skeleton.GetBoneRest(attachment);
        Transform3D actual = model.CurrentBonePose(attachment);
        RequireNear(expected.Origin, actual.Origin, label + " head attachment position");
        RequireNear(expected.Basis.X, actual.Basis.X, label + " head attachment X axis");
        RequireNear(expected.Basis.Y, actual.Basis.Y, label + " head attachment Y axis");
        RequireNear(expected.Basis.Z, actual.Basis.Z, label + " head attachment Z axis");
    }
}
