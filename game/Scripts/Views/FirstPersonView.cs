using System;
using Godot;
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
            _model.Light(Map.LightActor(cameraLocation, 64f, 0));
        }
    }

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

    private OpenScct.Net.Cosmetics.CosmeticItem? _cosmetic;
    private SkeletalModel? _cosmeticModel;

    /// <summary>OpenSCCT's own: the player's gun camo or tint, under the weapon's Skins (the rate-of-fire screen stays).</summary>
    private void DrawCosmetic(Pawn? pawn)
    {
        OpenScct.Net.Cosmetics.CosmeticItem? item = pawn is null ? null : PawnCosmetics.GunItem(pawn);
        if (ReferenceEquals(_cosmeticModel, _model) && ReferenceEquals(_cosmetic, item))
        {
            return;
        }

        PawnCosmetics.ApplyItem(_model!, item);
        _cosmetic = item;
        _cosmeticModel = _model;
    }

    /// <summary>The material drawn over view model slot <paramref name="slot"/> last frame (null: the mesh's own), for tests.</summary>
    public string? SlotSkin(int slot) => _model?.SlotSkin(slot);

    private void Load(string asset)
    {
        _asset = asset;
        _model?.Root.QueueFree();
        _model = null;
        _applier = null;
        if (SkeletalModel.FindGlb(asset, SharedRoot ?? Map?.SharedRoot) is not { } glb)
        {
            GD.PushWarning("first-person mesh not in the shared packs: " + asset);
            return;
        }

        _model = SkeletalModel.Load(glb);
        AddChild(_model.Root);
        _applier = new ChannelPoseApplier(_model.Skeleton);
    }

    private void Pose(ViewModelAnim anim, float dt, float alpha) => _applier!.Apply(_pose.Update(anim, _model!.Sampler, dt, alpha));
}
