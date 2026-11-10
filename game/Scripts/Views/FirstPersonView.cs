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
    private readonly ObservedWeaponViewModel _observed = new();

    /// <summary>What <see cref="Preload"/> read for this match.</summary>
    public FirstPersonNeeds Needs { get; private set; } = FirstPersonNeeds.All;

    /// <summary>
    /// Reads and converts the view models this match can draw during map loading, so a weapon switch (a pistol drawn
    /// mid-fight) never reads a file. A model left out still loads on its first draw, as before any preload.
    /// </summary>
    public void Preload(FirstPersonNeeds needs)
    {
        Needs = needs;
        // Every file read below, started on worker threads first: the pistols are about 10 MB each.
        string? shared = SharedRoot ?? Map?.SharedRoot;
        var ahead = new List<string?>();
        if (needs.Merc)
        {
            foreach (string primary in new[] { "SWeaponFirst.1DEF_01", "SWeaponFirst.1DEF_Uzi", "SWeaponFirst.1DEF_shotgun" })
                ahead.Add(SkeletalModel.FindGlb(primary, shared));
        }

        if (needs.Sidearms)
        {
            foreach (var pistol in Sidearms.All)
            {
                if (needs.Merc) ahead.Add(SkeletalModel.FindGlb(pistol.Asset + "_first", shared));
                ahead.Add(SkeletalModel.FindGlb(pistol.Asset, shared));
            }
        }

        SkeletalModel.Prefetch(ahead);
        if (needs.Merc)
        {
            foreach (string primary in new[] { "SWeaponFirst.1DEF_01", "SWeaponFirst.1DEF_Uzi", "SWeaponFirst.1DEF_shotgun" })
                Load(primary);
        }
        if (needs.Sidearms)
        {
            foreach (var pistol in Sidearms.All)
            {
                if (needs.Merc) Load(pistol.Asset + "_first");
                // Any merc can carry one, the local player or not: his body draws it (PawnAttachments).
                if (SkeletalModel.FindGlb(pistol.Asset, SharedRoot ?? Map?.SharedRoot) is { } third)
                    SkeletalModel.Load(third).Root.Free();
            }
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

    /// <summary>Samples an observed merc using the attachment and actions present in spectator recordings.</summary>
    public void OnObservedTick(SBasePawn? pawn) => _pose.OnTick(_observed.Update(pawn, Sim.DeltaTime)?.ViewAnim);

    public void DrawObserved(SBasePawn? pawn, FVector cameraLocation, FRotator cameraRotation, float dt, float alpha = 1f)
    {
        SWeaponDefense? weapon = _observed.Update(pawn, 0f);
        if (!ReferenceEquals(_lastWeapon, weapon)) _pose.OnTick(weapon?.ViewAnim);
        DrawWeapon(weapon, pawn, weapon is not null, cameraLocation, cameraRotation, dt, alpha, _observed.ShotCount);
    }

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
        DrawWeapon(weapon, pc?.Pawn, show, cameraLocation, cameraRotation, dt, alpha, weapon?.ShotCounter ?? 0);
    }

    private void DrawWeapon(SWeaponDefense? weapon, Pawn? pawn, bool show, FVector cameraLocation,
        FRotator cameraRotation, float dt, float alpha, int shotCount)
    {
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

        _model!.ApplyAnimations(weapon!.XLevel.GetService<OpenScct.Server.AnimationWorld>()?.Project, weapon.Mesh.Path);
        _model.Place(cameraLocation, cameraRotation, default);
        _model.SetVision(GameplayObjects.VisionOf(weapon!));
        if (!ReferenceEquals(_lastWeapon, weapon))
        {
            _lastWeapon = weapon;
            _lastShot = shotCount;
            _revolverKick = 0f;
        }
        if (weapon is MercSidearm { Kind: (int)SidearmKind.Magnum44 } && shotCount != _lastShot)
            _revolverKick = 0.42f;
        _lastShot = shotCount;
        DrawCosmetic(pawn, weapon);
        DrawSkins(weapon!);
        Pose(weapon!.ViewAnim, dt, alpha, exclusiveAction: weapon is MercSidearm { ReloadUntil: > 0 },
            blendLandingRecovery: weapon is MercSidearm);
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

    /// <summary>
    /// The merc's first-person sleeves and gloves, in every merc view model: the guns' 1DEF_01, 1DEF_Uzi and
    /// 1DEF_shotgun, and the pistols' arms (a model of their own under the same material name).
    /// </summary>
    public const string ArmsMaterial = "SWeaponTex.1DEF_01";

    private IReadOnlyList<MeshInstance3D> _arms = [];
    private (RenderMap Map, bool Wears)? _mercOwnModel;
    private CosmeticItem? _gunCosmetic;
    private CosmeticItem? _bodyCosmetic;
    private SkeletalModel? _cosmeticModel;

    /// <summary>
    /// OpenSCCT's own: the player's gun camo or tint on the gun, under the weapon's Skins (the rate-of-fire screen
    /// stays), and the body's on the arms, so the sleeves match the merc's suit. A map's own outfit model has no camo,
    /// so while the mercs wear one the arms keep the outfit's sleeves whatever the cosmetics policy, as the body does.
    /// </summary>
    private void DrawCosmetic(Pawn? pawn, SWeaponDefense? weapon)
    {
        CosmeticItem? gun = pawn is null ? null : PawnCosmetics.GunItem(pawn, weapon);
        CosmeticItem? body = pawn is null || MercWearsOwnModel ? null : PawnCosmetics.LookOf(pawn)[CosmeticSlot.Body];
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
        DressArms(glb);
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
        _model.SetFirstPersonDepth();

        _applier = new ChannelPoseApplier(_model.Skeleton);
        _loaded[asset] = (_model, _applier, _arms);
    }

    /// <summary>
    /// The map's merc outfit on the arms (spec/additions/map-outfits.md): its sleeves and gloves, when it has a model
    /// or a body texture. The pistols' arms share no bones with the merc and keep their own look.
    /// </summary>
    private void DressArms(string viewModelGlb)
    {
        string? root = SharedRoot ?? Map?.SharedRoot;
        if (_arms.Count == 0 || Map is null || SkeletalModel.FindGlb(PawnOutfits.StockAsset(spy: false), root) is not { } stock)
        {
            return;
        }

        if (PawnOutfits.FirstPersonArms(viewModelGlb, ArmsMaterial, stock, Map.Outfits.For(spy: false), Map.Folder, root) is { } arms)
        {
            _model!.SetOutfitTexture(ArmsMaterial, arms.Key, arms.Texture);
        }
    }

    // The mercs wear a model of the map's own (one that is found), which has no camo textures.
    private bool MercWearsOwnModel
    {
        get
        {
            if (Map is null)
            {
                return false;
            }

            if (_mercOwnModel is not { } known || !ReferenceEquals(known.Map, Map))
            {
                known = (Map, Map.Outfits.For(spy: false)?.Model is { Glb: not null } model &&
                    PawnOutfits.ModelPath(model, Map.Folder, SharedRoot ?? Map.SharedRoot) is not null);
                _mercOwnModel = known;
            }

            return known.Wears;
        }
    }

    private void Pose(ViewModelAnim anim, float dt, float alpha, bool exclusiveAction, bool blendLandingRecovery) =>
        _applier!.Apply(_pose.Update(anim, _model!.Sampler, dt, alpha, exclusiveAction, blendLandingRecovery));
}

/// <summary>
/// The view models a match can draw, decided when its map loads.
/// </summary>
/// <param name="Merc">The merc's guns in first person: the local player can be a merc, or a demo shows observed mercs.</param>
/// <param name="Spy">The spy's eye-view P190: the local player can be a spy in a live round.</param>
/// <param name="Sidearms">The session can give mercs pistols, or its rules have not arrived yet.</param>
public readonly record struct FirstPersonNeeds(bool Merc, bool Spy, bool Sidearms)
{
    /// <summary>Everything, as every match read before.</summary>
    public static readonly FirstPersonNeeds All = new(true, true, true);

    /// <summary>
    /// The needs of a round. Offline the local player keeps the side he asked for (no Switch Team, no travel); a remote
    /// round can move him to the other side (the hub's side change, practice Switch Team), so it keeps both. Merc
    /// Deathmatch puts everyone on the mercs. A demo draws observed mercs' guns and never the spy eye view
    /// (PlayRoot draws it only without FirstPersonOverride). Pistols exist only with SvmSidearms
    /// (SidearmEquipment.Available): offline that is the host's own option, remote the replicated rules when they are
    /// already here, else they are assumed.
    /// </summary>
    public static FirstPersonNeeds Of(bool demo, bool remote, bool merc, string? mode, bool sidearms, Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        bool mercsOnly = OpenScct.Server.MapCatalog.ModeOfKey(mode) == EGameMode.GM_MercDeathMatch;
        bool mayBeMerc = demo || mercsOnly || remote || merc;
        bool mayBeSpy = !demo && !mercsOnly && (remote || !merc);
        bool pistols = !remote && !demo ? sidearms : RulesAllowSidearms(level) ?? true;
        return new FirstPersonNeeds(mayBeMerc, mayBeSpy, pistols);
    }

    // The client's replicated game rules, or null before they arrive.
    private static bool? RulesAllowSidearms(Level level)
    {
        if (level.Info.GRI is { bDeleteMe: false } rules) return rules.SidearmsEnabled;
        foreach (GameReplicationInfo info in level.AllActors<GameReplicationInfo>())
            if (!info.bDeleteMe) return info.SidearmsEnabled;
        return null;
    }
}
