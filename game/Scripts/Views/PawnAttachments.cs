using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>
/// The weapons a pawn carries on its bones, drawn with its view: the merc's third-person rifle (SWeaponDefenseDummy,
/// SWeaponFirst.M160) in the right hand ("B R Hand") while WeaponLoaded is above 0 and at the "HOLSTER" bone otherwise
/// (spec/equipment/merc-rifle.md rule 22), and the spy's pistol (SWeaponAttaque, SWeaponFirst.p190) in the right hand
/// while drawn (the left hand, turned half a roll, at WeaponLoaded 7) and at the holster otherwise, posed by its own clips (DrawP190, SnipeP190, WaitP190) from its view
/// model channels. Attachments follow the bone's frame with the relative place the script or native sets. Reads the
/// simulation and never writes it.
/// </summary>
public sealed class PawnAttachments
{
    [Provenance(ProvenanceKind.Decoded, "SWeaponDefenseDummy.Tick, SWeaponAttaque.AnimateDrawWeapon: AttachToBone 'B R Hand' with relative location and rotation 0")]
    public static readonly Name BoneRightHand = new("B R Hand");

    [Provenance(ProvenanceKind.Decoded, "execTickSWeaponAttaque (game 0x10ab7870): WeaponLoaded 7 attaches the pistol to 'B L Hand', RelativeLocation (2, -8, 2), RelativeRotation roll 0x8000")]
    public static readonly Name BoneLeftHand = new("B L Hand");

    [Provenance(ProvenanceKind.Decoded, "SWeaponDefenseDummy.Tick: AttachToBone 'HOLSTER' while WeaponLoaded is 0 (unless lying on the ground)")]
    public static readonly Name BoneHolster = new("HOLSTER");

    private readonly SimPawnView _view;
    private readonly string? _sharedRoot;
    private readonly Dictionary<string, Attached> _models = new(StringComparer.OrdinalIgnoreCase);

    public PawnAttachments(SimPawnView view, string? sharedRoot)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _sharedRoot = sharedRoot;
    }

    public RenderMap? Map { get; set; }

    /// <summary>The weapon models drawn last frame, by mesh asset, with the bone each hangs on.</summary>
    public IEnumerable<(string Asset, Name Bone, bool Shown)> Drawn
    {
        get
        {
            foreach (KeyValuePair<string, Attached> kv in _models)
            {
                yield return (kv.Key, kv.Value.Bone, kv.Value.Model.Root.Visible);
            }
        }
    }

    /// <summary>
    /// The weapon in use last frame: its actor frame (the bone's, with the attachment's relative place) and its model,
    /// or false when the pawn draws no weapon.
    /// </summary>
    public bool TryGetWeapon(out Transform3D frame, out SkeletalModel? model)
    {
        foreach (Attached a in _models.Values)
        {
            if (a.Used && a.Bone != BoneHolster)
            {
                frame = a.Frame;
                model = a.Model;
                return true;
            }
        }

        frame = default;
        model = null;
        return false;
    }

    /// <summary>The world transform of <paramref name="bone"/> of the weapon in use (its FX bone, say), or false.</summary>
    public bool TryGetWeaponBone(Name bone, out Transform3D world)
    {
        world = default;
        if (!TryGetWeapon(out _, out SkeletalModel? model) || model is null)
        {
            return false;
        }

        int index = model.Skeleton.FindBone(bone.ToString());
        if (index < 0)
        {
            return false;
        }

        world = model.Skeleton.GlobalTransform * model.CurrentBonePose(index);
        return true;
    }

    /// <summary>
    /// A drawn bone or attachment frame in UE terms, scale removed (as GetBoneCoords gives it): Godot column 0 is UE X,
    /// column 2 UE Y and column 1 UE Z (UeSpace swaps Y and Z).
    /// </summary>
    public static FCoords ToCoords(Transform3D world)
    {
        Basis b = world.Basis.Orthonormalized();
        return new FCoords(UeSpace.ToUe(world.Origin), UeSpace.DirectionToUe(b.Column0), UeSpace.DirectionToUe(b.Column2), UeSpace.DirectionToUe(b.Column1));
    }

    /// <summary>Places every attachment of <paramref name="pawn"/> on its bone and poses the animated ones.</summary>
    public void Draw(SBasePawn pawn, float dt)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        foreach (Attached a in _models.Values)
        {
            a.Used = false;
        }

        NetworkPawnAttachments presentation = NetworkPawnAttachments.For(pawn);
        SWeaponDefense? selectedWeapon = presentation.ActiveSlot == 0 ? pawn.PrimaryWeapon as SWeaponDefense : pawn.SidearmWeapon;
        SWeaponDefenseDummy? selectedDummy = selectedWeapon?.Dummy ?? (pawn as SPAWNDEFENSE)?.WeaponDummy;
        if (pawn is SPAWNDEFENSE merc && selectedDummy is { bDeleteMe: false } dummy && !dummy.Mesh.IsNone)
        {
            Place(dummy.Mesh.Path!, (presentation.WeaponInHand ?? dummy.InHand) ? BoneRightHand : BoneHolster, null, dt, null,
                PawnCosmetics.GunItem(pawn, selectedWeapon));
            DrawVision(dummy.Mesh.Path!, dummy);
            SWeaponDefense? holstered = presentation.ActiveSlot == 0 ? merc.SidearmWeapon : merc.PrimaryWeapon as SWeaponDefense;
            if (holstered is { bDeleteMe: false, Dummy: { } carried } && !carried.Mesh.IsNone)
            {
                Place(carried.Mesh.Path!, BoneHolster, null, dt, null, PawnCosmetics.GunItem(pawn, holstered));
                DrawVision(carried.Mesh.Path!, carried);
            }
        }
        // MyWeapon replicates to the owner only (bNetOwner); other players' spies (bots too) have the pistol in
        // WeaponDummy, which replicates to everyone.
        else if (pawn is SPawnAttaque spy && (spy.MyWeapon ?? spy.WeaponDummy) is SWeaponAttaque { bDeleteMe: false } pistol &&
                 !pistol.Mesh.IsNone)
        {
            // TickSWeaponAttaque (spy-shocker.md rule 14.1): the holster at 0, the other hand at 7 with its relative
            // place, else the right hand.
            switch (pawn.WeaponLoaded)
            {
                case 0:
                    Place(pistol.Mesh.Path!, BoneHolster, pistol.ViewAnim, dt, null, PawnCosmetics.GunItem(pawn));
                    break;
                case 7:
                    Place(pistol.Mesh.Path!, BoneLeftHand, pistol.ViewAnim, dt,
                        UeSpace.TransformToGodot(SWeaponAttaque.OtherHandOffset, new FRotator(0, 0, SWeaponAttaque.OtherHandRoll), new FVector(1f, 1f, 1f), default),
                        PawnCosmetics.GunItem(pawn));
                    break;
                default:
                    Place(pistol.Mesh.Path!, BoneRightHand, pistol.ViewAnim, dt, null, PawnCosmetics.GunItem(pawn));
                    break;
            }

            DrawVision(pistol.Mesh.Path!, pistol);
        }

        foreach (Attached a in _models.Values)
        {
            a.Model.Root.Visible = a.Used;
        }
    }

    /// <summary>The pawn's camo suit on its attachments too (<see cref="SkeletalModel.SetGlass"/>).</summary>
    public void SetGlass(float amount)
    {
        foreach (Attached a in _models.Values)
        {
            a.Model.SetGlass(amount);
        }
    }

    /// <summary>Lights the attachments with the pawn's light set.</summary>
    public void Light(ActorLightSet lights)
    {
        foreach (Attached a in _models.Values)
        {
            a.Model.Light(lights);
        }
    }

    private void Place(string asset, Name bone, ViewModelAnim? anim, float dt, Transform3D? relative,
        OpenScct.Net.Cosmetics.CosmeticItem? cosmetic)
    {
        if (!_models.TryGetValue(asset, out Attached? a))
        {
            if (SkeletalModel.FindGlb(asset, _sharedRoot) is not { } glb)
            {
                return;
            }

            SkeletalModel model = SkeletalModel.Load(glb);
            _view.AddChild(model.Root);
            a = new Attached(model, new ChannelPoseApplier(model.Skeleton));
            _models[asset] = a;
        }

        a.Used = true;
        a.Bone = bone;
        if (!ReferenceEquals(a.Cosmetic, cosmetic) || !a.CosmeticSet)
        {
            // OpenSCCT's own: the player's gun camo or tint.
            PawnCosmetics.ApplyItem(a.Model, cosmetic);
            a.Cosmetic = cosmetic;
            a.CosmeticSet = true;
        }

        if (!_view.TryGetBone(bone, out Transform3D world))
        {
            a.Used = false;
            return;
        }

        // The attachment's actor frame is the bone's frame (with its relative place); the model keeps its own RotOrigin
        // and scale below it.
        a.Frame = relative is { } r ? world.Orthonormalized() * r : world.Orthonormalized();
        a.Model.Root.GlobalTransform = a.Frame;
        if (anim is not null)
        {
            a.Applier.Apply(a.Pose.Update(anim, a.Model.Sampler, dt));
        }
    }

    private sealed class Attached(SkeletalModel model, ChannelPoseApplier applier)
    {
        public SkeletalModel Model { get; } = model;

        public ChannelPoseApplier Applier { get; } = applier;

        public ViewModelPose Pose { get; } = new();

        public Name Bone { get; set; }

        public Transform3D Frame { get; set; }

        public bool Used { get; set; }

        public OpenScct.Net.Cosmetics.CosmeticItem? Cosmetic { get; set; }

        public bool CosmeticSet { get; set; }

        public Vector4? Vision { get; set; }
    }

    /// <summary>
    /// The attachment's own actor's live vision values (the pistol's SetEMFWeapon 1 / UnSetEMFWeapon 0.3, replicated;
    /// the merc's dummy keeps the Actor defaults), when they change.
    /// </summary>
    private void DrawVision(string asset, Actor source)
    {
        if (!_models.TryGetValue(asset, out Attached? a))
        {
            return;
        }

        Vector4 vision = GameplayObjects.VisionOf(source);
        if (a.Vision != vision)
        {
            a.Vision = vision;
            a.Model.SetVision(vision);
        }
    }
}
