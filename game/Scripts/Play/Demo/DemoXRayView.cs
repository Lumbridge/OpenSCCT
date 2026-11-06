using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Render;
using OpenScctGame.Views;

namespace OpenScctGame.Play.Demo;

/// <summary>Draws the contours of posed player bodies in a mask camera that cannot see map geometry.</summary>
public sealed partial class DemoXRayView : Node
{
    private const int MaxMaskSide = 1920;
    private static readonly Vector2I IdleSize = new(16, 16);

    private readonly Dictionary<MeshInstance3D, MeshInstance3D> _copies = new();
    private readonly HashSet<MeshInstance3D> _seen = new();
    private readonly List<MeshInstance3D> _gone = [];
    private readonly StandardMaterial3D _spy = MaskMaterial(HudK.SpyColour);
    private readonly StandardMaterial3D _merc = MaskMaterial(HudK.MercColour);
    private SubViewport? _mask;
    private Camera3D? _maskCamera;
    private TextureRect? _contour;

    public DemoXRayView()
    {
        Name = "DemoXRay";
    }

    /// <summary>Submits the same body meshes and skeleton poses as the main camera.</summary>
    public void Draw(Camera3D camera, IReadOnlyCollection<SimPawnView> views)
    {
        _seen.Clear();
        foreach (SimPawnView view in views)
        {
            if (!view.Visible || view.ShadowOnly || view.Model is not { } model ||
                view.Pawn is not { Health: > 0, bDeleteMe: false, PlayerReplicationInfo: { bOnlySpectator: false } pri } pawn)
                continue;

            Material material = (pri.Team?.TeamIndex == 1 || pri.Team is null && pawn is SPAWNDEFENSE) ? _merc : _spy;
            foreach (MeshInstance3D source in model.Meshes)
            {
                if (!source.IsVisibleInTree() || source.Mesh is null) continue;
                _seen.Add(source);
                UpdateCopy(source, material);
            }
        }

        _gone.Clear();
        foreach (MeshInstance3D source in _copies.Keys)
            if (!_seen.Contains(source)) _gone.Add(source);
        foreach (MeshInstance3D source in _gone)
            if (_copies.Remove(source, out MeshInstance3D? copy)) Retire(copy);

        DrawMask(camera, _seen.Count > 0);
    }

    private void UpdateCopy(MeshInstance3D source, Material material)
    {
        if (!_copies.TryGetValue(source, out MeshInstance3D? copy))
        {
            copy = new MeshInstance3D
            {
                Name = "DemoXRayBody",
                Layers = RenderLayers.DemoXRay,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                IgnoreOcclusionCulling = true,
            };
            // Sibling placement preserves the source's relative Skeleton3D path and keeps the current pose.
            source.GetParent().AddChild(copy);
            _copies.Add(source, copy);
        }

        if (copy.Mesh != source.Mesh) copy.Mesh = source.Mesh;
        if (copy.Skin != source.Skin) copy.Skin = source.Skin;
        if (copy.Skeleton != source.Skeleton) copy.Skeleton = source.Skeleton;
        if (copy.Transform != source.Transform) copy.Transform = source.Transform;
        if (copy.CustomAabb != source.CustomAabb) copy.CustomAabb = source.CustomAabb;
        if (copy.ExtraCullMargin != source.ExtraCullMargin) copy.ExtraCullMargin = source.ExtraCullMargin;
        if (copy.MaterialOverride != material) copy.MaterialOverride = material;
    }

    /// <summary>Turns off rendering and releases the copies until X-ray is enabled again.</summary>
    public void Disable()
    {
        HideMask();
        if (GodotObject.IsInstanceValid(_mask) && _mask!.Size != IdleSize) _mask.Size = IdleSize;
        foreach (MeshInstance3D copy in _copies.Values) Retire(copy);
        _copies.Clear();
        _seen.Clear();
        _gone.Clear();
    }

    public override void _ExitTree() => Disable();

    private static void Retire(MeshInstance3D copy)
    {
        if (!GodotObject.IsInstanceValid(copy) || copy.IsQueuedForDeletion()) return;
        copy.Visible = false;
        copy.QueueFree();
    }

    internal void DrawMask(Camera3D camera, bool visible)
    {
        if (!visible)
        {
            HideMask();
            return;
        }

        if (_mask is null) CreateMask(camera);
        SubViewport mask = _mask!;
        Camera3D maskCamera = _maskCamera!;
        Vector2 sizeInPixels = camera.GetViewport().GetVisibleRect().Size;
        float scale = Math.Min(1f, MaxMaskSide / Math.Max(2f, Math.Max(sizeInPixels.X, sizeInPixels.Y)));
        var size = new Vector2I(Math.Max(2, Mathf.RoundToInt(sizeInPixels.X * scale)),
            Math.Max(2, Mathf.RoundToInt(sizeInPixels.Y * scale)));
        if (mask.Size != size) mask.Size = size;

        maskCamera.GlobalTransform = camera.GlobalTransform;
        maskCamera.KeepAspect = camera.KeepAspect;
        maskCamera.HOffset = camera.HOffset;
        maskCamera.VOffset = camera.VOffset;
        switch (camera.Projection)
        {
            case Camera3D.ProjectionType.Frustum:
                maskCamera.SetFrustum(camera.Size, camera.FrustumOffset, camera.Near, camera.Far);
                break;
            case Camera3D.ProjectionType.Orthogonal:
                maskCamera.SetOrthogonal(camera.Size, camera.Near, camera.Far);
                break;
            default:
                maskCamera.SetPerspective(camera.Fov, camera.Near, camera.Far);
                break;
        }

        mask.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _contour!.Visible = true;
    }

    private void HideMask()
    {
        if (!GodotObject.IsInstanceValid(_mask) || _mask!.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled) return;
        _mask.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (GodotObject.IsInstanceValid(_contour)) _contour!.Visible = false;
    }

    private void CreateMask(Camera3D camera)
    {
        _mask = new SubViewport
        {
            Name = "BodyMask",
            Size = IdleSize,
            World3D = camera.GetWorld3D(),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Disabled,
        };
        Godot.Environment environment = FaithfulView.CreateEnvironment();
        environment.BackgroundMode = Godot.Environment.BGMode.ClearColor;
        _maskCamera = new Camera3D
        {
            Name = "BodyMaskCamera",
            CullMask = RenderLayers.DemoXRay,
            Current = true,
            Environment = environment,
        };
        _mask.AddChild(_maskCamera);
        AddChild(_mask);

        var canvas = new CanvasLayer { Name = "ContourLayer", Layer = 8 };
        _contour = new TextureRect
        {
            Name = "PlayerContours",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Disabled,
            Texture = _mask.GetTexture(),
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/svm_demo_xray.gdshader") },
            Visible = false,
        };
        canvas.AddChild(_contour);
        AddChild(canvas);
        _contour.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    private static StandardMaterial3D MaskMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
