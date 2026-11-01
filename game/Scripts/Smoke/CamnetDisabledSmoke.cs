using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Fx;

namespace OpenScctGame.Smoke;

/// <summary>Disabled Camnet cues follow replicated state, disappear on recovery, and stay behind world geometry.</summary>
public sealed class CamnetDisabledSmoke : SmokeTest
{
    public const string TestName = "camnet-disabled";
    public override string Name => TestName;
    private SubViewport? _preview;
    private readonly List<CamnetDisabledView> _previewCues = new();
    private string? _previewPath;
    private int _previewFrames;
    private const string Checks = "disabled lens/restoration; cue lifecycle, transform, no lights, depth-tested materials";

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_preview is not null)
            {
                foreach (CamnetDisabledView cue in _previewCues) cue.Update(Sim.DeltaTime);
                if (++_previewFrames < 12) return SmokeResult.Running;
                using Image image = _preview.GetTexture().GetImage();
                int visibleAmber = CountAmber(image, 500, 1000);
                int occludedAmber = CountAmber(image, 1000, 1500);
                Error saved = image.SavePng(_previewPath!);
                _preview.Free();
                _preview = null;
                Require(saved == Error.Ok, "preview save: " + saved);
                Require(visibleAmber > 30, "disabled lens marker was not rendered");
                Require(occludedAmber == 0, $"marker or sparks visible through wall ({occludedAmber} amber pixels)");
                return SmokeResult.Pass(Checks + $"; visible/occluded amber pixels={visibleAmber}/{occludedAmber}; preview={_previewPath}");
            }

            CheckLifecycle(context);
            if (System.Environment.GetEnvironmentVariable("SVM_CAMNET_DISABLED_PREVIEW") is { Length: > 0 } preview)
            {
                MakePreview(context, preview);
                return SmokeResult.Running;
            }
            return SmokeResult.Pass(Checks);
        }
        catch (Exception error)
        {
            _preview?.Free();
            _preview = null;
            return SmokeResult.Fail(error.ToString());
        }
    }

    private static void CheckLifecycle(SmokeContext context)
    {
        var world = new World(1, ENetMode.NM_Standalone);
        SCamNetwork camera = SpawnCamera(world.Level);
        var effects = new CombatFxView();
        context.Tree.Root.AddChild(effects);
        try
        {
            effects.TrackDisabledCameras(world.Level);
            Require(effects.DisabledCameraCount == 0, "active camera acquired a disabled cue");
            camera.SwitchToVision(-2);
            var enabledLens = camera.Skins[1];
            camera.DisabledSeconds = 30;
            camera.eventTick(Sim.DeltaTime);
            Require(camera.Skins[1] == camera.mOff, "disabled camera lens did not turn off");
            effects.TrackDisabledCameras(world.Level);
            Require(effects.DisabledCameraCount == 1, "disabled camera did not acquire one cue");
            CamnetDisabledView cue = effects.GetChildren().OfType<CamnetDisabledView>().Single();
            Require(cue.Visible, "disabled cue is hidden");
            foreach (Node node in Descendants(cue))
            {
                Require(node is not Light3D, "disabled cue created a light");
                if (node is GeometryInstance3D geometry && geometry.MaterialOverride is BaseMaterial3D material)
                    Require(!material.NoDepthTest, "disabled cue bypasses wall depth");
            }
            camera.Location = new FVector(100, 200, 300);
            camera.Rotation = new FRotator(2000, 16000, 0);
            cue.Update(Sim.DeltaTime);
            Require(cue.Transform.IsEqualApprox(UeSpace.TransformToGodot(camera.Location, camera.Rotation,
                camera.DrawScale3D * camera.DrawScale, camera.PrePivot)), "cue did not follow the camera transform");
            camera.bHidden = true;
            cue.Update(Sim.DeltaTime);
            Require(!cue.Visible, "hidden camera retained a visible cue");
            camera.bHidden = false;
            cue.Update(Sim.DeltaTime);
            Require(cue.Visible, "revealed disabled camera did not restore its cue");
            camera.DisabledSeconds = 0;
            camera.eventTick(Sim.DeltaTime);
            Require(camera.Skins[1] == enabledLens, "recovered camera did not restore its lens");
            effects.TrackDisabledCameras(world.Level);
            Require(effects.DisabledCameraCount == 0 && !cue.Visible && cue.IsQueuedForDeletion(), "recovery left a stale cue");
            camera.DisabledSeconds = 20;
            effects.TrackDisabledCameras(world.Level);
            Require(effects.DisabledCameraCount == 1, "second disable did not recreate a cue");
            camera.bNoDelete = false;
            Require(camera.Destroy(), "camera deletion failed");
            effects.TrackDisabledCameras(world.Level);
            Require(effects.DisabledCameraCount == 0, "deleted camera retained a cue");
            SCamNetwork second = SpawnCamera(world.Level);
            second.DisabledSeconds = 20;
            effects.TrackDisabledCameras(world.Level);
            Require(effects.DisabledCameraCount == 1, "replacement camera did not acquire a cue");
            effects.TrackDisabledCameras(new World(2, ENetMode.NM_Standalone).Level);
            Require(effects.DisabledCameraCount == 0, "level change retained a cue from the old level");
        }
        finally { effects.Free(); }
    }

    private void MakePreview(SmokeContext context, string output)
    {
        Require(ContentPaths.Shared is not null, "no shared content");
        FxLibrary library = FxLibrary.Load(ContentPaths.Shared!);
        Mesh mesh = library.Mesh("SGameplayObjects.CamNetwork.Globe") ?? throw new InvalidOperationException("camera mesh missing");
        Require(mesh.GetSurfaceCount() >= 2, "camera lens surface missing");
        _previewPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        _preview = new SubViewport { Size = new Vector2I(1500, 700), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_preview);
        var scene = new Node3D();
        _preview.AddChild(scene);
        scene.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.055f, 0.065f, 0.075f),
        } });
        var observer = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1.05f, Position = new Vector3(4f, 0.08f, 0f), Current = true };
        scene.AddChild(observer);
        observer.LookAt(new Vector3(0f, 0.08f, 0f));
        var wallMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.14f, 0.17f, 0.18f) };
        scene.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 1.5f, 3f) },
            Position = new Vector3(-0.25f, 0f, 0f), MaterialOverride = wallMaterial });
        var world = new World(3, ENetMode.NM_Standalone);
        for (int panel = 0; panel < 3; panel++)
        {
            SCamNetwork camera = SpawnCamera(world.Level);
            camera.Location = new FVector(0f, (1 - panel) * 75f, 0f);
            camera.SwitchToVision(-1);
            camera.DisabledSeconds = panel == 0 ? 0 : 30;
            camera.eventTick(Sim.DeltaTime);
            var body = new MeshInstance3D { Mesh = mesh,
                Transform = UeSpace.TransformToGodot(camera.Location, camera.Rotation) };
            scene.AddChild(body);
            // Globe already carries the authored mOff lens. A ready, unviewed camera also has its lens off.
            // Show the real texture without depending on map-specific actor lighting in this isolated viewport.
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                Texture2D? texture = mesh.SurfaceGetMaterial(surface) switch
                {
                    ShaderMaterial shader => shader.GetShaderParameter("tex_diffuse").As<Texture2D>(),
                    BaseMaterial3D material => material.AlbedoTexture,
                    _ => null,
                };
                Require(texture is not null, "camera preview texture missing");
                body.SetSurfaceOverrideMaterial(surface, new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = texture,
                });
            }
            var cue = new CamnetDisabledView();
            scene.AddChild(cue);
            cue.SetCamera(camera);
            _previewCues.Add(cue);
            var label = new Label { Text = new[] { "READY", "DISABLED", "BEHIND A WALL" }[panel],
                Position = new Vector2(panel * 500, 35), Size = new Vector2(500, 45), HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 26);
            _preview.AddChild(label);
        }
        // Cover the third camera and all of its particles; its cue must not become an overlay through this wall.
        scene.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 1.2f, 0.7f) },
            Position = new Vector3(0.7f, 0.15f, -0.75f), MaterialOverride = wallMaterial });
    }

    private static SCamNetwork SpawnCamera(Level level)
    {
        level.BeginLoad();
        SCamNetwork camera = level.SpawnActor(new SCamNetwork(), default, default)!;
        level.EndLoad();
        camera.Role = ENetRole.ROLE_SimulatedProxy;
        return camera;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }

    private static int CountAmber(Image image, int left, int right)
    {
        int count = 0;
        for (int y = 150; y < 600; y++)
        for (int x = left; x < right; x++)
        {
            Color color = image.GetPixel(x, y);
            if (color.R > 0.18f && color.G > 0.05f && color.R > 1.8f * color.G && color.B < 0.15f) count++;
        }
        return count;
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
