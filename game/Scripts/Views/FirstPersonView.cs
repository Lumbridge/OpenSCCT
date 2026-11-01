using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>
/// The merc's first-person arms and rifle (the weapon's own mesh, SWeaponFirst.1DEF_01 by default): drawn while the
/// local view is first person and the weapon draws its view model (bDrawWeapon), placed at the camera, and posed
/// from the weapon's view model channels (<see cref="ViewModelAnim"/>: clip, alpha, one-shot at its play rate,
/// backward or loop; <see cref="ViewModelPose"/>) by the <see cref="ChannelPoseApplier"/>. The one-shots are sampled
/// after every client tick (<see cref="OnTick"/>) and drawn between ticks like the camera. Reads the simulation and
/// never writes it.
/// </summary>
public partial class FirstPersonView : Node3D
{
    private ViewModelPose _pose = new();
    private SkeletalModel? _model;
    private ChannelPoseApplier? _applier;
    private string? _asset;
    private float _relightClock;
    private readonly Dictionary<string, (SkeletalModel Model, ChannelPoseApplier Applier, IReadOnlyList<MeshInstance3D> Arms)> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private int _lastShot;
    private SWeaponDefense? _lastWeapon;
    private float _revolverKick;
    private readonly Dictionary<SkeletalModel, Transform3D> _sidearmSightOffsets = new();

    /// <summary>Read and convert pistol scenes during map loading, before the player can switch weapons.</summary>
    public void PreloadSidearms()
    {
        foreach (string primary in new[] { "SWeaponFirst.1DEF_01", "SWeaponFirst.1DEF_Uzi", "SWeaponFirst.1DEF_shotgun" })
            Load(primary);
        foreach (var pistol in Sidearms.All)
        {
            Load(pistol.Asset + "_first");
            if (SkeletalModel.FindGlb(pistol.Asset, SharedRoot ?? Map?.SharedRoot) is { } third)
                SkeletalModel.Load(third).Root.Free();
        }
        if (_model is not null) _model.Root.Visible = false;
        _asset = null;
        _model = null;
    }

    public FirstPersonView()
    {
        Name = "FirstPerson";
    }

    public RenderMap? Map { get; set; }

    public string? SharedRoot { get; set; }

    /// <summary>The view model drawn last frame, or null.</summary>
    public SkeletalModel? Model => _model;

    /// <summary>True when the view model was drawn last frame.</summary>
    public bool Shown { get; private set; }

    /// <summary>The sampled position (0 to 1) of a view model channel in the last draw, or -1 when it drew nothing.</summary>
    public float ChannelFrame(int channel) => Shown ? _pose.ClipFrameOf(channel) : -1f;

    /// <summary>After each client tick: samples the view model channels of <paramref name="pc"/>'s weapon.</summary>
    public void OnTick(PlayerController? pc) => _pose.OnTick(((pc?.Pawn as SBasePawn)?.MyWeapon as SWeaponDefense)?.ViewAnim);

    /// <summary>
    /// Draws the view model of <paramref name="pc"/>'s weapon at the camera, or hides it; <paramref name="alpha"/> is how
    /// far the clock is into the next tick.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "View model placed at the camera location and rotation (the weapon's draw offset is not in the specs)")]
    public void Draw(PlayerController? pc, FVector cameraLocation, FRotator cameraRotation, float dt, float alpha = 1f)
    {
        var weapon = (pc?.Pawn as SBasePawn)?.MyWeapon as SWeaponDefense;
        bool show = pc is { bBehindView: false } && weapon is { bDrawWeapon: true, bDeleteMe: false } && !weapon.Mesh.IsNone
            && ReferenceEquals(pc.ViewTarget ?? pc.Pawn, pc.Pawn);
        if (show && (_model is null || !string.Equals(_asset, weapon!.Mesh.Path, StringComparison.OrdinalIgnoreCase)))
        {
            Load(weapon!.Mesh.Path!);
        }

        Shown = show && _model is not null;
        Visible = Shown;
        if (!Shown)
        {
            return;
        }

        _model!.Place(cameraLocation, cameraRotation, default);
        _model.SetVision(GameplayObjects.VisionOf(weapon!));
        if (!ReferenceEquals(_lastWeapon, weapon))
        {
            _lastWeapon = weapon;
            _lastShot = weapon!.ShotCounter;
            _revolverKick = 0f;
        }
        if (weapon is MercSidearm { Kind: (int)SidearmKind.Magnum44 } && weapon.ShotCounter != _lastShot)
            _revolverKick = 0.42f;
        _lastShot = weapon!.ShotCounter;
        DrawCosmetic(pc!.Pawn);
        DrawSkins(weapon!);
        Pose(weapon!.ViewAnim, dt, alpha, exclusiveAction: weapon is MercSidearm { ReloadUntil: > 0 });
        if (weapon is MercSidearm && _sidearmSightOffsets.TryGetValue(_model, out Transform3D sightOffset))
            _model.Root.Transform *= sightOffset;
        if (_revolverKick > 0f)
        {
            // Actor-local +X is forward and +Z is screen-right (UE mapping),
            // so pitch around +Z; rotating around +X only rolls the gun.
            _model.Root.RotateObjectLocal(Vector3.Back, _revolverKick);
            _model.Root.RotateObjectLocal(Vector3.Up, -_revolverKick * 0.45f);
            _revolverKick *= MathF.Exp(-10f * dt);
        }
        _relightClock -= dt;
        if (_relightClock <= 0f && Map is not null)
        {
            _relightClock = 0.1f;
            _model.Light(LightAtEye(Map, cameraLocation));
        }
    }

    /// <summary>A fixed ten-metre sight zero, computed once in camera-local space.</summary>
    internal static Transform3D SidearmSightOffset(Transform3D neutralBarrel)
    {
        Vector3 target = Vector3.Right * 10f;
        Vector3 direction = (target - neutralBarrel.Origin).Normalized();
        var rotation = new Basis(new Quaternion(neutralBarrel.Basis.X.Normalized(), direction));
        return new Transform3D(rotation, neutralBarrel.Origin - rotation * neutralBarrel.Origin);
    }

    /// <summary>
    /// Ordinary lights near the eye. The 64 uu sphere and unknown zone remain approximations; the original
    /// uses the weapon's bounds and BSP leaves. Reflections belong to the material and eye adaptation to the
    /// scene renderer, so neither is replaced with a viewmodel ambient floor here.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Eye-centred light selection; original FActorSceneNode::Render 0x10a172f0 uses weapon bounds/BSP leaves")]
    internal static ActorLightSet LightAtEye(RenderMap map, FVector eye) =>
        map.ActorLighting.Select(eye, 64f, map.AmbientAt(eye, 0), -1);

    /// <summary>
    /// The weapon's Skins over the view model's material slots: the rifle's rate-of-fire screen is Skins[3], which
    /// SetMode swaps between SI_rof_single, SI_rof_burst and SI_rof_auto (spec/equipment/merc-rifle.md rule 16).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SWeaponDefense defaults Skins[3] = SWeaponTex.Armes.SI_rof_auto (ROFMode ROF_Auto); the mesh's own slot 3 is SI_rof_single")]
    private void DrawSkins(SWeaponDefense weapon)
    {
        int slots = _model!.Slots.Count;
        for (int i = 0; i < slots; i++)
        {
            _model.SetSlotSkin(i, i < weapon.Skins.Count ? weapon.Skins[i].Path : null);
        }
    }

    /// <summary>The merc's first-person sleeves and gloves, in every merc view model (1DEF_01, 1DEF_Uzi, 1DEF_shotgun).</summary>
    public const string ArmsMaterial = "SWeaponTex.1DEF_01";

    private IReadOnlyList<MeshInstance3D> _arms = [];
    private CosmeticItem? _gunCosmetic;
    private CosmeticItem? _bodyCosmetic;
    private SkeletalModel? _cosmeticModel;

    /// <summary>
    /// OpenSCCT's own: the player's gun camo or tint on the gun, under the weapon's Skins (the rate-of-fire screen
    /// stays), and the body's on the arms, so the sleeves match the merc's suit.
    /// </summary>
    private void DrawCosmetic(Pawn? pawn)
    {
        CosmeticItem? gun = pawn is null ? null : PawnCosmetics.GunItem(pawn);
        CosmeticItem? body = pawn is null ? null : PawnCosmetics.LookOf(pawn)[CosmeticSlot.Body];
        if (ReferenceEquals(_cosmeticModel, _model) && ReferenceEquals(_gunCosmetic, gun) && ReferenceEquals(_bodyCosmetic, body))
        {
            return;
        }

        foreach (MeshInstance3D mesh in _model!.Meshes)
        {
            PawnCosmetics.ApplyItem(_model, mesh, _arms.Contains(mesh) ? body : gun);
        }

        _gunCosmetic = gun;
        _bodyCosmetic = body;
        _cosmeticModel = _model;
    }

    /// <summary>The material drawn over view model slot <paramref name="slot"/> last frame (null: the mesh's own), for tests.</summary>
    public string? SlotSkin(int slot) => _model?.SlotSkin(slot);

    /// <summary>
    /// Laid over every mesh of the view model: marks its pixels in the stencil (MaterialCompiler.StencilFirstPerson) so
    /// the merc's laser beam, drawn from 50 uu behind the muzzle, is hidden where the gun covers it, as the original's
    /// weapon overlay does (spec/equipment/merc-laser.md rule 9a).
    /// </summary>
    public const string MaskShaderPath = "res://Shaders/svm_first_person_mask.gdshader";

    private static ShaderMaterial? s_mask;

    private void Load(string asset)
    {
        _asset = asset;
        if (_model is not null) _model.Root.Visible = false;
        if (_loaded.TryGetValue(asset, out var cached))
        {
            (_model, _applier, _arms) = cached;
            _model.Root.Visible = true;
            return;
        }
        _model = null;
        _arms = [];
        _applier = null;
        if (SkeletalModel.FindGlb(asset, SharedRoot ?? Map?.SharedRoot) is not { } glb)
        {
            GD.PushWarning("first-person mesh not in the shared packs: " + asset);
            return;
        }

        _model = SkeletalModel.Load(glb);
        _arms = _model.SplitPart(ArmsMaterial);
        s_mask ??= new ShaderMaterial { Shader = GD.Load<Shader>(MaskShaderPath), RenderPriority = (int)Material.RenderPriorityMin };
        foreach (MeshInstance3D mesh in _model.Meshes)
        {
            mesh.MaterialOverlay = s_mask;
        }

        AddChild(_model.Root);
        if (asset.StartsWith("OpenScctSidearms.", StringComparison.OrdinalIgnoreCase) &&
            _model.Skeleton.FindBone("FX") is var fx and >= 0 && _model.Sampler.Find("FirstWaitStFd2") is { } idle)
        {
            Transform3D neutral = _model.Root.GlobalTransform.AffineInverse() * _model.Skeleton.GlobalTransform *
                _model.Sampler.SampleBone(idle, fx, .5f);
            _sidearmSightOffsets[_model] = SidearmSightOffset(neutral);
        }
        // Arms and gun float at the eye: the main view draws them, the shadow cameras do not (RenderLayers.FirstPerson).
        foreach (MeshInstance3D mesh in _model.Root.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            mesh.Layers = RenderLayers.FirstPerson;
        }

        _applier = new ChannelPoseApplier(_model.Skeleton);
        _loaded[asset] = (_model, _applier, _arms);
    }

    private void Pose(ViewModelAnim anim, float dt, float alpha, bool exclusiveAction) =>
        _applier!.Apply(_pose.Update(anim, _model!.Sampler, dt, alpha, exclusiveAction));
}
