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
        if (show && !string.Equals(_asset, weapon!.Mesh.Path, StringComparison.OrdinalIgnoreCase))
        {
            Load(weapon.Mesh.Path!);
        }

        Shown = show && _model is not null;
        Visible = Shown;
        if (!Shown)
        {
            return;
        }

        _model!.Place(cameraLocation, cameraRotation, default);
        DrawCosmetic(pc!.Pawn);
        DrawSkins(weapon!);
        Pose(weapon!.ViewAnim, dt, alpha);
        _relightClock -= dt;
        if (_relightClock <= 0f && Map is not null)
        {
            _relightClock = 0.1f;
            _model.Light(LightAtEye(Map, cameraLocation));
        }
    }

    /// <summary>
    /// The view model's light set: the eye zone's ambient and the lights reaching a 64 uu sphere at the eye, with no
    /// own-zone filter (zone -1). On AquaE, 5 m in front of the Tech Room door, the eye is in zone 9 and the only
    /// light on the gun, Light15411 (bAffectOwnZoneOnly), sits 9 uu over the portal in zone 13: the own-zone filter
    /// drew the gun black where the original lights it. The original's first-person weapon is not lit by the eye's
    /// zone; which zone it takes is not decoded.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Original first-person gun lit by an own-zone light of the next zone (AquaE Tech Room door, Ryan's capture 2026-10-30); actor light gathering game 0x10a37610")]
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
        _model?.Root.QueueFree();
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
        // Arms and gun float at the eye: the main view draws them, the shadow cameras do not (RenderLayers.FirstPerson).
        foreach (MeshInstance3D mesh in _model.Root.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            mesh.Layers = RenderLayers.FirstPerson;
        }

        _applier = new ChannelPoseApplier(_model.Skeleton);
    }

    private void Pose(ViewModelAnim anim, float dt, float alpha) => _applier!.Apply(_pose.Update(anim, _model!.Sampler, dt, alpha));
}
