using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net.Cosmetics;
using OpenScct.Physics.Movement;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Views;

/// <summary>The P190 and its grip in the live owner's Spy eye view, relative to the final drawn camera.</summary>
public sealed partial class SpyFirstPersonView : Node3D
{
    private const string ArmsAsset = "SPerso.ATT_01_fp";
    private const string ArmsMaterial = "SPersoTextures.Shadw_agent";
    private const string WeaponAsset = "SWeaponFirst.p190_fp";
    private static readonly Name s_laser = new("Laser");
    private static readonly Name s_muzzle = new("FX");
    // X is forward, Y is up and Z is screen-right. Distances are metres from the eye.
    private static readonly Transform3D s_ready = SpyFirstPersonRig.Ready;
    private static readonly Transform3D s_lowered = new(
        new Basis(Vector3.Up, -.23f) * new Basis(Vector3.Back, -.44f) * new Basis(Vector3.Right, .30f),
        new Vector3(.20f, -.35f, .27f));
    private static readonly FVector s_probeExtent = new(1.25f, 1.25f, 1.25f);
    private static ShaderMaterial? s_mask;
    private SWeaponAttaque? _weapon;
    private SPawnAttaque? _pawn;
    private CosmeticItem? _cosmetic;
    private CosmeticItem? _bodyCosmetic;
    private Transform3D _gripped;
    private Transform3D _armsRest;
    private Transform3D _gripInverse;
    private bool _ownOutfit;
    private FVector _eye;
    private float _lightClock;
    private float _supportContactError;
    private Name _sampledClip;
    private float _sampledPhase;
    private bool _preloaded;
    private int _laserBone;
    private int _muzzleBone;
    private int _stockBone;
    private Vector3 _stockRest;
    private float _studioClock, _studioFireAge = float.MaxValue, _studioLastShot = float.MinValue;

    public SpyFirstPersonView() => Name = "SpyFirstPerson";

    public RenderMap? Map { get; init; }
    public string? SharedRoot { get; init; }
    public SkeletalModel? ArmsModel { get; private set; }
    public SkeletalModel? WeaponModel { get; private set; }
    public bool Available => ArmsModel is not null && WeaponModel is not null;
    public bool Shown => Visible;
    internal float SupportContactError => Shown ? _supportContactError : -1f;

    /// <summary>Loads the optional eye-view model during map loading (when the round can show it, else on first use).</summary>
    public void Preload()
    {
        if (_preloaded) return;
        _preloaded = true;
        Visible = false;
        string? shared = SharedRoot ?? Map?.SharedRoot;
        if (SkeletalModel.FindGlb(ArmsAsset, shared) is not { } armsPath ||
            SkeletalModel.FindGlb(WeaponAsset, shared) is not { } gunPath) return;

        try
        {
            ArmsModel = SkeletalModel.Load(armsPath);
            WeaponModel = SkeletalModel.Load(gunPath);
            _laserBone = WeaponModel.Skeleton.FindBone(s_laser.ToString());
            _muzzleBone = WeaponModel.Skeleton.FindBone(s_muzzle.ToString());
            _stockBone = WeaponModel.Skeleton.FindBone("crosse");
            if (_laserBone < 0 || _muzzleBone < 0)
                throw new InvalidDataException("the P190 is missing its laser or muzzle socket");
            if (_stockBone >= 0) _stockRest = WeaponModel.Skeleton.GetBoneRest(_stockBone).Origin;
            Prepare(ArmsModel);
            Prepare(WeaponModel);
            AddChild(ArmsModel.Root);
            AddChild(WeaponModel.Root);
            if (!SpyFirstPersonRig.TryPose(ArmsModel, GlobalTransform, s_ready, out _gripped, out _supportContactError))
                throw new InvalidDataException("the Spy arms are missing the stock two-handed grip");
            _gripInverse = _gripped.AffineInverse();
            _armsRest = ArmsModel.Root.Transform;
            DressArms(ArmsModel, armsPath, shared);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            ArmsModel?.Root.Free();
            WeaponModel?.Root.Free();
            ArmsModel = null;
            WeaponModel = null;
            GD.PushWarning("Spy first-person view unavailable: " + e.Message);
        }
    }

    public void Warmup(Node3D samples)
    {
        if (!Available || DisplayServer.GetName() == "headless") return;
        var arms = (Node3D)ArmsModel!.Root.Duplicate();
        var weapon = (Node3D)WeaponModel!.Root.Duplicate();
        arms.Position = new Vector3(-.7f, 0f, 0f);
        weapon.Position = new Vector3(.55f, 0f, 0f);
        samples.AddChild(arms);
        samples.AddChild(weapon);
    }

    private static void Prepare(SkeletalModel model)
    {
        s_mask ??= new ShaderMaterial
        {
            Shader = GD.Load<Shader>(FirstPersonView.MaskShaderPath),
            RenderPriority = (int)Material.RenderPriorityMin,
        };
        foreach (MeshInstance3D mesh in model.Meshes)
        {
            mesh.Layers = RenderLayers.FirstPerson;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mesh.MaterialOverlay = s_mask;
            mesh.ExtraCullMargin = 1f;
        }
        model.SetFirstPersonDepth();
    }

    private void DressArms(SkeletalModel model, string armsPath, string? shared)
    {
        if (Map is not { } map || map.Outfits.For(spy: true) is not { } outfit) return;
        if (outfit.Model is { } own && PawnOutfits.ModelPath(own, map.Folder, shared) is not null &&
            SkeletalModel.FindGlb(PawnOutfits.StockAsset(spy: true), shared) is { } stock)
        {
            _ownOutfit = true;
            if (PawnOutfits.FirstPersonArms(armsPath, ArmsMaterial, stock, outfit, map.Folder, shared) is { } transferred)
                model.SetOutfitTexture(ArmsMaterial, transferred.Key, transferred.Texture);
        }
        else if (outfit.Body is { } body)
        {
            model.SetOutfitTexture(ArmsMaterial, Path.Combine(map.Folder, "render", body.Texture.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    public void Draw(SPlayerController? pc, SPawnAttaque? pawn, FVector eye, FRotator look, float dt, float alpha)
    {
        // A round not expected to show the eye view (FirstPersonNeeds) reads it on first use. This node has not
        // moved yet (only a drawn view places it), so the grip is solved as at map load.
        if (!_preloaded && pawn is not null && pc is not null && ReferenceEquals(pc.Pawn, pawn)) Preload();

        if (ArmsModel is not { } arms || WeaponModel is not { } model || pc is null ||
            pawn is not { bDeleteMe: false, bHidden: false, Health: > 0 } || !ReferenceEquals(pc.Pawn, pawn))
        {
            Visible = false;
            _weapon = null;
            _pawn = null;
            return;
        }

        // The stance can arrive before the weapon references on the owning client.
        arms.ApplyAnimations(pawn.XLevel.GetService<OpenScct.Server.AnimationWorld>()?.Project, ArmsAsset);
        model.ApplyAnimations(pawn.XLevel.GetService<OpenScct.Server.AnimationWorld>()?.Project, WeaponAsset);
        _weapon = pawn.MyWeapon is SWeaponAttaque { bDeleteMe: false } pistol ? pistol
            : pawn.WeaponDummy is { bDeleteMe: false } dummy ? dummy : null;
        bool wasShown = Shown;
        Visible = pawn.WeaponLoaded != 0 && !HideForAction(pc, pawn);
        if (!Shown) return;

        if (!wasShown || !ReferenceEquals(_pawn, pawn) || (eye - _eye).SizeSquared() > 22500f)
            _lightClock = 0f;
        _pawn = pawn;
        _eye = eye;
        GlobalTransform = UeSpace.TransformToGodot(eye, look);

        bool drawing = pawn.WeaponLoaded is 3 or 4;
        float amount = drawing ? DrawAmount(pawn, _weapon?.ViewAnim, alpha) : 1f;
        float eased = amount * amount * (3f - 2f * amount);
        Transform3D gun = drawing ? s_lowered.InterpolateWith(_gripped, eased) : _gripped;
        model.Root.Transform = gun;
        arms.Root.Transform = drawing ? gun * _gripInverse * _armsRest : _armsRest;
        PoseWeapon(model, drawing ? ViewModelClips.DrawP190 : ViewModelClips.SnipeP190, drawing ? amount : 0f);
        DrawStudioActions(pawn, arms, model, drawing, amount, dt);
        DrawLooks(arms, model, pawn, dt);
    }

    private void DrawStudioActions(SPawnAttaque pawn, SkeletalModel arms, SkeletalModel weapon, bool drawing, float drawPhase, float dt)
    {
        var project = pawn.XLevel.GetService<OpenScct.Server.AnimationWorld>()?.Project;
        var profile = project?.FirstPersonProfiles.FindLast(p => p.Enabled && p.Role == OpenScct.Animation.FirstPersonRole.Spy && p.MainAsset == WeaponAsset);
        if (profile is null || project is null) return;
        var mainRig = project.Scene.Find(r => r.Id == profile.MainRig);
        var armRig = project.Scene.Find(r => r.Id == profile.ArmsRig);
        if (mainRig is not null)
        {
            Transform3D mount = StudioPoseBridge.Transform(mainRig.Transform);
            weapon.Root.Transform = drawing ? s_lowered.InterpolateWith(mount, drawPhase * drawPhase * (3 - 2 * drawPhase)) : mount;
            Transform3D supportMount = armRig is null ? Transform3D.Identity : StudioPoseBridge.Transform(armRig.Transform);
            arms.Root.Transform = drawing ? weapon.Root.Transform * mount.AffineInverse() * supportMount : supportMount;
        }
        _studioClock += dt; _studioFireAge += dt;
        if (_weapon is { } source && source.LastShot != _studioLastShot)
        {
            _studioLastShot = source.LastShot;
            if (!drawing && source.LastShot <= pawn.Level.TimeSeconds && pawn.Level.TimeSeconds - source.LastShot < .1f) _studioFireAge = 0;
        }
        string slot = drawing ? pawn.WeaponLoaded == 4 ? "HolsterP190" : "DrawP190"
            : _studioFireAge < OpenScct.Animation.FirstPersonEvaluation.Duration(project, profile, "FireP190") ? "FireP190"
            : pawn.WeaponLoaded == 2 ? "AimP190" : pawn.Velocity.SizeSquared() > 100 ? "MoveP190" : "SnipeP190";
        float duration = OpenScct.Animation.FirstPersonEvaluation.Duration(project, profile, slot);
        float phase = drawing ? pawn.WeaponLoaded == 4 ? 1 - drawPhase : drawPhase : slot == "FireP190" ? _studioFireAge / duration : (_studioClock % duration) / duration;
        var primary = OpenScct.Animation.FirstPersonEvaluation.Evaluate(project, profile, slot, phase, false);
        var support = OpenScct.Animation.FirstPersonEvaluation.Evaluate(project, profile, slot, phase, true);
        if (primary is not null) StudioPoseBridge.Apply(weapon.Skeleton, project.Skeletons.Find(s => s.Id == primary.Skeleton)!, primary.Bones);
        if (support is not null) StudioPoseBridge.Apply(arms.Skeleton, project.Skeletons.Find(s => s.Id == support.Skeleton)!, support.Bones);
    }

    private static bool HideForAction(SPlayerController pc, SPawnAttaque pawn) =>
        pc.StateNode is SPlayerController.s_InteractWithObject or SPlayerController.s_InteractWithObjectWaitingEvent or
            SPlayerController.s_Computer_Enter or SPlayerController.s_Computer_Use or SPlayerController.s_Computer_Leave or
            SPlayerController.s_Computer_FastLeave or SPlayerController.s_Hack_Enter or SPlayerController.s_Hack_Use or
            SPlayerController.s_Hack_Leave or SPlayerController.s_Hack_FastLeave or SPlayerController.s_Maintain_Enter or
            SPlayerController.s_Maintain_Use or SPlayerController.s_Maintain_Leave or SPlayerController.s_Pickup or
            SPlayerController.s_PutFlagInDropZone or SPlayerController.s_PlaceBomb or SPlayerController.s_PlaceBomb_End or
            SPlayerController.s_PlayerRolling ||
        pawn.Physics is EPhysics.PHYS_OnLadder or EPhysics.PHYS_OnPipe or EPhysics.PHYS_OnPipeH or EPhysics.PHYS_Linear or
            EPhysics.PHYS_OnVerticalPlane or EPhysics.PHYS_Ledge or EPhysics.PHYS_Vandamme;

    private static float DrawAmount(SPawnAttaque pawn, ViewModelAnim? anim, float alpha)
    {
        const int channel = SWeaponAttaque.DEFAULTCHANNEL;
        if (anim is not null && anim.Sequence(channel) == ViewModelClips.DrawP190)
        {
            float duration = anim.Data(channel).Seconds(anim.Rate(channel));
            if (duration > 0f)
            {
                float remaining = anim.Remaining(channel) + ((1f - Mathf.Clamp(alpha, 0f, 1f)) * Sim.DeltaTime);
                float progress = Mathf.Clamp(1f - remaining / duration, 0f, 1f);
                return anim.IsBackward(channel) ? 1f - progress : progress;
            }
        }

        var body = pawn.PawnAnim[0];
        Name clip = body.Sequence;
        if (!body.bPlaying || body.EndFrame <= 0f ||
            (clip != pawn.ADrawCrouch2H && clip != pawn.ADrawStand2H && clip != pawn.AUnDrawCrouch2H && clip != pawn.AUnDrawStand2H))
            return 1f;
        float phase = Mathf.Clamp(body.Frame / body.EndFrame, 0f, 1f);
        return pawn.WeaponLoaded == 4 ? 1f - phase : phase;
    }

    private void PoseWeapon(SkeletalModel model, Name clip, float phase)
    {
        if (clip == _sampledClip && phase == _sampledPhase) return;
        _sampledClip = clip;
        _sampledPhase = phase;
        if (!model.Sampler.Play(clip.ToString(), loop: false, frame: phase)) return;
        model.Pose();
        if (_stockBone >= 0)
        {
            // Shorten the visible stock travel to keep it below the HUD.
            Vector3 stock = model.Skeleton.GetBonePosePosition(_stockBone);
            model.Skeleton.SetBonePosePosition(_stockBone, _stockRest.Lerp(stock, .34f));
        }
    }

    /// <summary>Routes this eye view's local P190 effects to the foreground sockets.</summary>
    public bool OwnsWeapon(SWeaponAttaque pistol) => ReferenceEquals(_weapon, pistol);

    public bool TryGetSocket(SWeaponAttaque pistol, Name socket, out Transform3D world)
    {
        world = default;
        if (!Shown || !OwnsWeapon(pistol) || WeaponModel is not { } model) return false;
        int bone = socket == s_laser ? _laserBone : socket == s_muzzle ? _muzzleBone : -1;
        if (bone < 0) return false;

        Transform3D emitter = model.Skeleton.GlobalTransform * model.CurrentBonePose(bone);
        // Occlusion affects the emitter only. The P190 stays fixed relative to the eye.
        if (_pawn?.XLevel?.PhysicsDriver is PhysicsDriver physics &&
            physics.Collision.LineCheck(out _, null, UeSpace.ToUe(emitter.Origin), _eye, s_probeExtent, CameraK.CameraTraceFlags))
            return false;
        world = emitter;
        return true;
    }

    private void DrawLooks(SkeletalModel arms, SkeletalModel model, SPawnAttaque pawn, float dt)
    {
        CosmeticItem? bodyLook = _ownOutfit ? null : PawnCosmetics.LookOf(pawn)[CosmeticSlot.Body];
        if (!ReferenceEquals(bodyLook, _bodyCosmetic))
        {
            _bodyCosmetic = bodyLook;
            PawnCosmetics.ApplyItem(arms, bodyLook);
        }
        CosmeticItem? look = PawnCosmetics.GunItem(pawn);
        if (!ReferenceEquals(look, _cosmetic))
        {
            _cosmetic = look;
            PawnCosmetics.ApplyItem(model, look);
        }

        var vision = GameplayObjects.VisionOf(pawn);
        arms.SetVision(vision);
        model.SetVision(_weapon is { } pistol ? GameplayObjects.VisionOf(pistol) : vision);
        float glass = MaterialCompiler.Vision is RenderK.RendMapNormal or RenderK.RendMapNight ? pawn.GlassEffect / 255f : 0f;
        arms.SetGlass(glass);
        model.SetGlass(glass);
        _lightClock -= dt;
        if (_lightClock <= 0f && Map is { } map)
        {
            _lightClock = .1f;
            ActorLightSet lights = FirstPersonView.LightAtEye(map, _eye);
            arms.Light(lights);
            model.Light(lights);
        }
    }
}
