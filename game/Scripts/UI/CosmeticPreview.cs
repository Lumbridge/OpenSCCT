using System;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.UI;

/// <summary>
/// A small turning 3D view of one cosmetic on what wears it (OpenSCCT's own): the spy or merc for a suit, armour or
/// light (lights framed on the head), the third-person gun for a gun camo. The model is the in-game one with the same
/// camo, tint or light hue the game draws; it turns slowly and the mouse can drag it round. Without the built content
/// the item's flat picture shows instead.
/// </summary>
public partial class CosmeticPreview : Control
{
    private const float TurnDegreesPerSecond = 24f;

    /// <summary>Seconds after a drag before the view starts turning by itself again.</summary>
    private const float DragRest = 2.5f;

    private const float FieldOfView = 28f;

    private static readonly string[] s_idleClips = ["WaitStAlFd0", "WaitStSpFd2"];

    private readonly SubViewport _viewport;
    private readonly Node3D _turntable;
    private readonly Camera3D _camera;
    private readonly TextureRect _flat;
    private SkeletalModel? _model;
    private GoggleGlowView? _glow;
    private bool _glowMerc;
    private float? _glowHue;
    private bool _glowOn;
    private string? _asset;
    private bool _frameNext;
    private bool _headShot;
    private bool _dragging;
    private float _rest;

    public CosmeticPreview()
        : this(new Vector2(400f, 300f))
    {
    }

    public CosmeticPreview(Vector2 size)
    {
        Name = "Preview";
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Drag;
        ClipContents = true;
        var container = new SubViewportContainer { Name = "View", Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(container);
        // No MSAA: the post pass writes the colour buffer as a storage image, and the MSAA resolve target lacks that
        // usage (the GPU call failed and the game crashed opening the armoury). FXAA smooths the edges after the pass.
        _viewport = new SubViewport
        {
            Name = "Viewport", OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa,
            Size = new Vector2I((int)size.X, (int)size.Y), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        container.AddChild(_viewport);
        _turntable = new Node3D { Name = "Turntable" };
        _viewport.AddChild(_turntable);
        // The materials write display values, as in the game: the game's post pass turns them into linear light, or
        // every colour would be sRGB-encoded twice (washed out, reds turning pink).
        _camera = new Camera3D
        {
            Name = "Camera", Fov = FieldOfView, Near = 0.02f, Far = 100f, Current = true,
            CullMask = RenderLayers.MainView,
            Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor
            {
                CompositorEffects = [new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } }],
            },
        };
        _viewport.AddChild(_camera);
        _flat = new TextureRect
        {
            Name = "Flat", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore, Visible = false, TextureFilter = TextureFilterEnum.Nearest,
        };
        _flat.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 48);
        AddChild(_flat);
    }

    /// <summary>The item shown, or null for none.</summary>
    public CosmeticItem? Item { get; private set; }

    /// <summary>Shows <paramref name="item"/> (null clears the view).</summary>
    public void ShowItem(CosmeticItem? item)
    {
        Item = item;
        string? asset = item is null ? null : AssetOf(item);
        if (asset != _asset)
        {
            _asset = asset;
            Load(asset);
        }

        if (_model is null)
        {
            _flat.Texture = item is null ? null : CosmeticThumbs.For(item, 128);
            _flat.Visible = _flat.Texture is not null;
            return;
        }

        _flat.Visible = false;
        if (item is { Slot: CosmeticSlot.Light })
        {
            PawnCosmetics.ApplyItem(_model, null);
            _model.SetGlowHue(PawnCosmetics.HueTurn(item, item.Side));
        }
        else
        {
            PawnCosmetics.ApplyItem(_model, item);
            _model.SetGlowHue(null);
        }

        // The goggle beam as the game draws it, on the character views: in the item's colour for a light.
        bool character = item is { Slot: CosmeticSlot.Body or CosmeticSlot.Light };
        SetGlow(character, item?.Side == CosmeticSide.Merc, item is { Slot: CosmeticSlot.Light } ? PawnCosmetics.HueTurn(item, item.Side) : null);

        bool head = item is { Slot: CosmeticSlot.Light };
        if (head != _headShot)
        {
            _headShot = head;
            _frameNext = true;
        }
    }

    /// <summary>The model an item is drawn on: the side's character for a body or light, the gun the others see.</summary>
    public static string AssetOf(CosmeticItem item) => AssetFor(item.Side, item.Slot);

    /// <summary>The model a slot is seen on: the side's character for a body or light, else that gun.</summary>
    public static string AssetFor(CosmeticSide side, CosmeticSlot slot) => slot switch
    {
        CosmeticSlot.Suppressed => "OpenScctSidearms.suppressed",
        CosmeticSlot.Unsuppressed => "OpenScctSidearms.unsuppressed",
        CosmeticSlot.Automatic => "OpenScctSidearms.automatic",
        CosmeticSlot.Magnum44 => "OpenScctSidearms.magnum44",
        CosmeticSlot.DesertEagle => "OpenScctSidearms.desert_eagle",
        CosmeticSlot.Pistol => "SWeaponFirst.p190",
        CosmeticSlot.GunAr => "SWeaponFirst.M160",
        CosmeticSlot.GunSmg => "SWeaponFirst.Hk81",
        CosmeticSlot.GunShotgun => "SWeaponFirst.BM401",
        _ => side == CosmeticSide.Spy ? "SPerso.ATT_01" : "SPerso.DEF_01",
    };

    /// <summary>
    /// Shows a whole kit as the game draws it, seen from <paramref name="view"/>: for the suit or the light, the
    /// character wearing the look's suit with its light colour (framed on the head for the light); for a gun slot, that
    /// gun with its camo. Without the built content the viewed slot's flat picture shows.
    /// </summary>
    public void ShowLook(CosmeticSide side, CosmeticLook look, CosmeticSlot view)
    {
        Item = look[view];
        string asset = AssetFor(side, view);
        if (asset != _asset)
        {
            _asset = asset;
            Load(asset);
        }

        if (_model is null)
        {
            _flat.Texture = Item is null ? null : CosmeticThumbs.For(Item, 128);
            _flat.Visible = _flat.Texture is not null;
            return;
        }

        _flat.Visible = false;
        bool character = view is CosmeticSlot.Body or CosmeticSlot.Light;
        PawnCosmetics.ApplyItem(_model, character ? look[CosmeticSlot.Body] : look[view]);
        float? hue = character ? PawnCosmetics.HueTurn(look[CosmeticSlot.Light], side) : null;
        _model.SetGlowHue(hue);
        SetGlow(character, side == CosmeticSide.Merc, hue);
        bool head = view == CosmeticSlot.Light;
        if (head != _headShot)
        {
            _headShot = head;
            _frameNext = true;
        }
    }

    /// <summary>Whether the goggle beam shows, for which side and in which colour (drawn each frame on the posed head).</summary>
    private void SetGlow(bool on, bool merc, float? hueTurn)
    {
        _glowOn = on;
        _glowMerc = merc;
        _glowHue = hueTurn;
        if (!on)
        {
            _glow?.HideGlow();
        }
    }

    private void Load(string? asset)
    {
        if (_model is not null)
        {
            _glow?.HideGlow();
            _glow = null;
            foreach (Node child in _turntable.GetChildren())
            {
                _turntable.RemoveChild(child);
                child.QueueFree();
            }

            _model = null;
        }

        if (asset is null || SkeletalModel.FindGlb(asset) is not { } glb)
        {
            return;
        }

        try
        {
            MaterialCompiler.EnsureGlobals();
            _model = SkeletalModel.Load(glb);
        }
        catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException)
        {
            GD.PushWarning($"Cosmetic preview: {asset} did not load ({e.Message})");
            return;
        }

        _turntable.AddChild(_model.Root);
        _model.Light(StudioLights());
        SkeletalModel character = _model;
        _glow = asset.StartsWith("SPerso.", StringComparison.Ordinal)
            ? new GoggleGlowView(_turntable, null, bone =>
            {
                int index = character.Skeleton.FindBone(bone.ToString());
                return index < 0
                    ? (false, default)
                    : (true, character.Skeleton.GlobalTransform * character.Skeleton.GetBoneGlobalPose(index));
            })
            : null;
        foreach (string clip in s_idleClips)
        {
            if (_model.Sampler.Play(clip))
            {
                break;
            }
        }

        _turntable.Rotation = new Vector3(0f, Mathf.DegToRad(-60f), 0f);
        _frameNext = true;
    }

    public override void _Process(double delta)
    {
        if (_model is null)
        {
            return;
        }

        if (_model.Sampler.Current is not null)
        {
            _model.Sampler.Advance(delta);
            _model.Pose();
        }

        if (_frameNext)
        {
            _frameNext = false;
            Frame();
        }

        if (_glowOn && _glow is not null && _model.Root.IsInsideTree())
        {
            _glow.DrawStill(_glowMerc, _glowHue);
        }

        if (_dragging)
        {
            return;
        }

        if (_rest > 0f)
        {
            _rest -= (float)delta;
            return;
        }

        _turntable.RotateY(Mathf.DegToRad(TurnDegreesPerSecond) * (float)delta);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                _rest = DragRest;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                _turntable.RotateY(motion.Relative.X * 0.012f);
                AcceptEvent();
                break;
        }
    }

    /// <summary>Centres the model on the turntable and puts the camera where the whole model (or its head) fills the view.</summary>
    private void Frame()
    {
        if (_model is null || !_model.Root.IsInsideTree())
        {
            return;
        }

        // The bounds in the turntable's space, with the turntable straight (the model is placed under it unturned).
        Basis turn = _turntable.Basis;
        _turntable.Basis = Basis.Identity;
        _model.Root.Position = Vector3.Zero;
        Aabb? bounds = null;
        Transform3D toTable = _turntable.GlobalTransform.AffineInverse();
        foreach (MeshInstance3D mesh in _model.Meshes)
        {
            if (mesh.Mesh is null)
            {
                continue;
            }

            Aabb box = toTable * mesh.GlobalTransform * mesh.GetAabb();
            bounds = bounds is { } b ? b.Merge(box) : box;
        }

        _turntable.Basis = turn;
        if (bounds is not { } all || all.Size.LengthSquared() <= 0f)
        {
            return;
        }

        Vector3 centre = all.GetCenter();
        float height = all.Size.Y;
        float across = new Vector2(all.Size.X, all.Size.Z).Length();
        float radius = Math.Max(height, across) * 0.5f;
        if (_headShot)
        {
            // The lights sit on the head: the top sixth of the character.
            centre = new Vector3(centre.X, all.End.Y - (height * 0.1f), centre.Z);
            radius = height * 0.14f;
        }

        _model.Root.Position = -centre;
        float distance = radius / Mathf.Sin(Mathf.DegToRad(FieldOfView * 0.5f)) * 1.05f;
        _camera.Position = new Vector3(0f, radius * 0.25f, distance);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    /// <summary>A plain studio light: soft ambient, a warm key from the upper front and a green rim from behind.</summary>
    private static ActorLightSet StudioLights()
    {
        var set = new ActorLightSet { Ambient = new Vector3(0.3f, 0.32f, 0.31f), Count = 3 };
        set.Lights[0] = Directional(new Vector3(0.5f, 0.7f, 0.9f), new Vector3(1f, 0.96f, 0.9f) * 0.75f);
        set.Lights[1] = Directional(new Vector3(-0.8f, 0.2f, 0.4f), new Vector3(0.55f, 0.6f, 0.7f) * 0.35f);
        set.Lights[2] = Directional(new Vector3(-0.3f, 0.5f, -1f), new Vector3(0.49f, 1f, 0.58f) * 0.45f);
        return set;
    }

    /// <summary>A directional light in the actor-lit template's layout (w -1), <paramref name="toLight"/> in view space.</summary>
    private static PackedLight Directional(Vector3 toLight, Vector3 colour)
    {
        Vector3 d = toLight.Normalized();
        Vector3 c = colour * RenderK.SunLightGain;
        return new PackedLight(new Vector4(d.X, d.Y, d.Z, -1f), new Vector4(c.X, c.Y, c.Z, 1f), new Vector4(0f, 0f, 0f, -2f));
    }
}
