using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>
/// Draws one simulated pawn: its character mesh (spy SPerso.ATT_01, merc SPerso.DEF_01) placed at the pawn's
/// location and yaw (interpolated between the last two ticks) with UE2 placement, posed every frame by the
/// <see cref="ChannelPoseApplier"/> from all 25 of the pawn's animation channels as the simulation keeps them (the
/// actions' clips, the walk blender's channels 1 to 15, the overlays on their start bones, the root lock of a
/// root-motion move), with the weapons on their bones (<see cref="PawnAttachments"/>), and lit by the map's actor
/// lighting. It reads the simulation and never writes it.
/// </summary>
public partial class SimPawnView : Node3D
{
    /// <summary>Bounding sphere radius used to rank lights for a pawn (cylinder 35 x 89 half height).</summary>
    public const float LightRadius = 95f;

    private readonly ChannelPose[] _channels = new ChannelPose[PawnAnimation.ChannelCount];
    private readonly TickChannel[] _prev = new TickChannel[PawnAnimation.ChannelCount];
    private readonly TickChannel[] _cur = new TickChannel[PawnAnimation.ChannelCount];
    private readonly Dictionary<Name, int> _boneOf = new();
    private SkeletalModel? _model;
    private ChannelPoseApplier? _applier;
    private PawnAttachments? _attachments;
    private GoggleGlowView? _goggleGlow;
    private FVector _prevLocation;
    private FVector _location;
    private FVector _prevPrePivot;
    private FVector _prePivot;
    private int _prevYaw;
    private int _yaw;
    private bool _placed;
    private float _relightClock;
    private StickySkins? _stickySkins;
    private string? _glb;
    private SkinShown _skinShown;
    private long _lookDrawn = -1;

    private readonly List<PoseTurn> _overlayTurns = new();

    /// <summary>Extra posing laid over every drawn pawn (the hub's emotes while a visit runs), or null.</summary>
    public static IPawnPoseOverlay? Overlay { get; set; }

    public SimPawnView()
    {
        Name = "Pawn";
    }

    /// <summary>The pawn drawn (on the client world).</summary>
    public SBasePawn? Pawn { get; private set; }

    public RenderMap? Map { get; set; }

    public SkeletalModel? Model => _model;

    /// <summary>The applier posing the mesh (for checks).</summary>
    public ChannelPoseApplier? Applier => _applier;

    /// <summary>The weapons drawn on the pawn's bones.</summary>
    public PawnAttachments? Attachments => _attachments;

    /// <summary>The pawn's goggle glow (spec/equipment/goggle-glow.md).</summary>
    public GoggleGlowView? GoggleGlow => _goggleGlow;

    /// <summary>Loads the mesh for <paramref name="pawn"/>; false when the shared packs do not have it.</summary>
    public bool Bind(SBasePawn pawn, string? sharedRoot)
    {
        Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        string asset = pawn is SPAWNDEFENSE ? "SPerso.DEF_01" : "SPerso.ATT_01";
        string? root = sharedRoot ?? Map?.SharedRoot;
        string? glb = SkeletalModel.FindGlb(asset, root);
        if (glb is null)
        {
            return false;
        }

        _model = SkeletalModel.Load(glb);
        _glb = glb;
        AddChild(_model.Root);
        _stickySkins = StickySkins.For(root);
        _applier = new ChannelPoseApplier(_model.Skeleton);
        _attachments = new PawnAttachments(this, root) { Map = Map };
        _goggleGlow = new GoggleGlowView(this, root, bone => (TryGetBone(bone, out Transform3D world), world));
        Name = $"Pawn{pawn.ActorId}";
        return true;
    }

    /// <summary>Surfaces now drawn with the Stickess skin (0 when the pawn is not shocked).</summary>
    public int StickySurfaces { get; private set; }

    /// <summary>Surfaces now drawn with a wet skin (0 when the pawn is dry or the view is in normal vision).</summary>
    public int WetSurfaces { get; private set; }

    private enum SkinShown
    {
        Own,
        Sticky,
        WetSpyMachine,
        WetMercMachine,
    }

    /// <summary>
    /// The skins over the body: the Stickess shaders while bStickySkinned (BeginStickySkin), else the wet skins while
    /// bWet (BeginWetSkin; RestoreSkins puts them back after a shock), else the own skins. The wet skins differ from the
    /// own ones only in thermal and EMF here (their normal-vision materials are not converted, docs/EFFECTS.md), so in
    /// normal vision the own skins stay, with the player's camo.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Entering rain during a shock keeps the Stickess skin until RestoreSkins (the original's BeginWetSkin would replace it)")]
    private void DrawSkins(SBasePawn pawn)
    {
        if (_model is null)
        {
            return;
        }

        SkinShown want = SkinShown.Own;
        if (pawn.bStickySkinned)
        {
            want = SkinShown.Sticky;
        }
        else if (pawn.bWet && Render.Materials.MaterialCompiler.Vision != RenderK.RendMapNormal)
        {
            // HEAT_SN02_SI_wet where this machine's player is a spy (no local player reads as a spy), HEAT_sn02_wet elsewhere.
            bool localSpy = ((pawn.Level.GetLocalPlayerController() as SPlayerController)?.SPawnType ?? PawnType.SPAWNOFFENSE) == PawnType.SPAWNOFFENSE;
            want = localSpy ? SkinShown.WetSpyMachine : SkinShown.WetMercMachine;
        }

        if (want == _skinShown)
        {
            return;
        }

        _skinShown = want;
        Func<string, Material?>? skin = want switch
        {
            SkinShown.Sticky => _stickySkins is { } sticky ? sticky.Over : null,
            SkinShown.WetSpyMachine or SkinShown.WetMercMachine =>
                WetSkins.For(_glb, pawn is not SPAWNDEFENSE, want == SkinShown.WetSpyMachine) is { } wet ? wet.Over : null,
            _ => null,
        };
        int surfaces = _model.SetSkins(skin);
        StickySurfaces = want == SkinShown.Sticky ? surfaces : 0;
        WetSurfaces = want is SkinShown.WetSpyMachine or SkinShown.WetMercMachine ? surfaces : 0;
    }

    /// <summary>OpenSCCT's own: the player's body camo or tint and light hue (lenses, lamp), when the look changes.</summary>
    private void DrawCosmetics(SBasePawn pawn)
    {
        OpenScct.Net.Cosmetics.CosmeticLook look = PawnCosmetics.LookOf(pawn);
        if (look.Packed == _lookDrawn || _model is null)
        {
            return;
        }

        _lookDrawn = look.Packed;
        PawnCosmetics.ApplyItem(_model, look[OpenScct.Net.Cosmetics.CosmeticSlot.Body]);
        _model.SetGlowHue(PawnCosmetics.HueTurn(look[OpenScct.Net.Cosmetics.CosmeticSlot.Light], PawnCosmetics.SideOf(pawn)));
    }

    /// <summary>
    /// The camo suit: the pawn's GlassEffect (ramped and flickered on this machine, spy-energy.md rule 7a) on the body
    /// and its attachments, in normal vision only: the original draws glass actors only while RendMap is 5, so every
    /// goggle mode shows the spy as uncloaked. The goggle glow is its own actor and keeps drawing.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "scene traversal 0x10a395d2 / 0x10a39c3a / 0x10a3f1ff: an actor with GlassEffect != 0 goes to the glass list only when RendMap == 5")]
    [Provenance(ProvenanceKind.Estimated, "The attachments (the gun) take the pawn's amount; in the original they are separate actors with their own GlassEffect")]
    private void DrawGlass(SBasePawn pawn)
    {
        float amount = Render.Materials.MaterialCompiler.Vision == RenderK.RendMapNormal ? pawn.GlassEffect / 255f : 0f;
        _model?.SetGlass(amount);
        _attachments?.SetGlass(amount);
    }

    private Vector4? _visionDrawn;

    /// <summary>
    /// The pawn's live vision values (HeatIntensity, HeatOpacity, EMF glow, EMFOpacity; replicated from the server's
    /// SetEMF / UnSetEMF) on the body, when they change: a spy is drawn in EMF only while EMF visible (vision-goggles.md
    /// rule 12). docs/RENDERING.md#vision-modes.
    /// </summary>
    private void DrawVision(SBasePawn pawn)
    {
        Vector4 vision = GameplayObjects.VisionOf(pawn);
        if (_visionDrawn == vision || _model is null)
        {
            return;
        }

        _visionDrawn = vision;
        _model.SetVision(vision);
    }

    /// <summary>Takes the pawn's state after a simulation tick (location, yaw and channel frames for the interpolation).</summary>
    public void OnTick()
    {
        if (Pawn is not { } pawn)
        {
            return;
        }

        _prevLocation = _placed ? _location : pawn.Location;
        _prevPrePivot = _placed ? _prePivot : pawn.PrePivot;
        _prevYaw = _placed ? _yaw : pawn.Rotation.Yaw;
        _location = pawn.Location;
        _prePivot = pawn.PrePivot;
        _yaw = pawn.Rotation.Yaw;
        PawnAnimation anim = pawn.PawnAnim;
        for (int c = 0; c < PawnAnimation.ChannelCount; c++)
        {
            _prev[c] = _placed ? _cur[c] : default;
            PawnAnimChannel ch = anim[c];
            _cur[c] = new TickChannel(ch.Sequence, ch.Frame, ch.ClipFrame);
        }

        _placed = true;
    }

    /// <summary>Places and poses the pawn for a frame <paramref name="alpha"/> of the way from the previous tick to the last.</summary>
    public void Draw(float alpha, float dt)
    {
        if (Pawn is not { } pawn || _model is null || _applier is null || !_placed)
        {
            return;
        }

        Visible = !pawn.bHidden && !pawn.bDeleteMe;
        alpha = Math.Clamp(alpha, 0f, 1f);
        FVector location = _prevLocation + (_location - _prevLocation) * alpha;
        // The mesh is drawn at Location + PrePivot. A crouch or stand-up moves the cylinder and changes PrePivot by the
        // same amount in one tick (the mesh stays put), so both are interpolated together.
        FVector prePivot = _prevPrePivot + (_prePivot - _prevPrePivot) * alpha;
        int yaw = _prevYaw + (int)(FRotator.NormalizeAxis(_yaw - _prevYaw) * alpha);
        _model.Place(location, new FRotator(0, yaw, 0), prePivot);
        _drawYaw = yaw;
        SetAim(pawn, yaw);
        Pose(pawn, alpha);
        _attachments?.Draw(pawn, dt);
        _goggleGlow?.Draw(pawn);
        DrawSkins(pawn);
        DrawCosmetics(pawn);
        DrawGlass(pawn);
        DrawVision(pawn);
        _relightClock -= dt;
        if (_relightClock <= 0f && Map is not null)
        {
            _relightClock = 0.1f;
            ActorLightSet lights = Map.LightActor(location, LightRadius, 0);
            _model.Light(lights);
            _attachments?.Light(lights);
        }
    }

    /// <summary>The world transform of one of the pawn's bones as last drawn, or false.</summary>
    public bool TryGetBone(Name bone, out Transform3D world)
    {
        world = default;
        if (_model is null || _applier is null)
        {
            return false;
        }

        int index = BoneIndex(bone);
        if (index < 0)
        {
            return false;
        }

        world = _model.Skeleton.GlobalTransform * _model.Skeleton.GetBoneGlobalPose(index);
        return true;
    }

    /// <summary>
    /// The upper-body aim of this draw: the mesh instance's AAFULL pair (yaw, pitch) turns B Spine2 (bone 4) by the
    /// pitch about the horizontal axis across the pawn's facing (Z x facing), then by the yaw about the vertical
    /// (0x11102160: two axis-angle coords applied to the bone's transposed space base).
    /// </summary>
    private void SetAim(SBasePawn pawn, int yaw)
    {
        if (_applier is null || _model is null)
        {
            return;
        }

        MeshAimIK aim = pawn.PawnAnim.Aim;
        if (aim.FullYaw == 0f && aim.FullPitch == 0f)
        {
            _applier.AimBone = -1;
            return;
        }

        _applier.AimBone = _aimBone ??= _applier.FindBone(MeshAimIK.FullAimBoneName);
        Basis world = AimPose.WorldBasis(aim.FullYaw, aim.FullPitch, new FRotator(0, yaw, 0).Vector());
        Basis skeleton = _model.Skeleton.GlobalTransform.Basis.Orthonormalized();
        _applier.AimRotation = (skeleton.Inverse() * world * skeleton).GetRotationQuaternion();
    }

    private int? _aimBone;

    /// <summary>The skeleton index of a bone by its UE name (cached), or -1.</summary>
    public int BoneIndex(Name bone)
    {
        if (bone.IsNone || _applier is null)
        {
            return 0;
        }

        if (!_boneOf.TryGetValue(bone, out int index))
        {
            index = _applier.FindBone(bone.ToString());
            _boneOf[bone] = index;
        }

        return index;
    }

    private void Pose(SBasePawn pawn, float alpha)
    {
        AnimSampler sampler = _model!.Sampler;
        PawnAnimation anim = pawn.PawnAnim;
        Array.Clear(_channels);
        for (int c = 0; c < PawnAnimation.ChannelCount; c++)
        {
            PawnAnimChannel ch = anim[c];
            if (ch.Sequence.IsNone || ch.Clip is null || sampler.Find(ch.Sequence.ToString()) is not { } clip)
            {
                continue;
            }

            float weight = c == 0 ? 1f : ch.BlendAlpha;
            if (!(weight > 0f))
            {
                continue;
            }

            (float frame, float clipFrame) = Interpolated(c, ch, alpha);
            int start = BoneIndex(ch.StartBone);
            _channels[c] = new ChannelPose
            {
                Clip = clip,
                Sequence = ch.Sequence.ToString(),
                Frame = frame,
                ClipFrame = clipFrame,
                Frames = ch.Frames,
                Fps = ch.Clip.Fps,
                Alpha = weight,
                StartBone = start < 0 ? 0 : start,
                Loop = ch.bLoop,
                Mirror = ch.bMirror,
            };
        }

        RootLock rootLock = RootLock.None;
        PawnAnimChannel c0 = anim[0];
        if (pawn.Physics == EPhysics.PHYS_RootMotion && pawn.iLockRootMotionType != 0)
        {
            // The mesh's own lock once physics has set it (the pose build reads the instance's lock), else the
            // action's.
            int lockType = ((OpenScct.Physics.Movement.ISBasePawnPhysics)pawn).RootMotionMesh is PawnRootMotion { RootLockType: not 0 } locked
                ? locked.RootLockType
                : pawn.iLockRootMotionType;
            rootLock = RootLock.Translation;
            if ((lockType & 2) != 0)
            {
                rootLock |= RootLock.Yaw;
            }
        }

        Vector3? offset = null;
        Quaternion? turn = null;
        if (rootLock != RootLock.None)
        {
            RootLockOf(pawn, c0, out offset, out turn);
        }

        ApplyOverlay(pawn);
        _applier!.Apply(_channels, rootLock, c0.bBackward ? c0.AnimLast : 0f, offset, turn);
    }

    /// <summary>
    /// The locked root's offset in skeleton space (coop.md rule 24, anim.md rule 3.4): R(lock) - R(t) of channel 0 at
    /// the drawn frame (the backward forms for a backward play), in the mesh's axes; null when the mesh is not locked
    /// yet or the clip has no root data (the root is then pinned). With lock bit 2 also the turn that takes the yaw
    /// the root motion carried since the lock back out of the root (position and rotation), so the body stays where the
    /// move put it while the actor turns.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "pose build game 0x109ba4f0 at 0x109bcbac: with lock bit 2 bone 0's position and rotation are turned back by the root yaw since the lock (yaw of field 0x250 minus 0x1f0 with the GetRootRotationDelta sign rules: the root turn the actor carried, not other turns of the actor)")]
    private void RootLockOf(SBasePawn pawn, PawnAnimChannel c0, out Vector3? offset, out Quaternion? turn)
    {
        offset = null;
        turn = null;
        if (_model is null || ((OpenScct.Physics.Movement.ISBasePawnPhysics)pawn).RootMotionMesh is not PawnRootMotion { RootLockType: not 0 } mesh)
        {
            return;
        }

        Basis skeleton = _model.Skeleton.GlobalTransform.Basis;
        float clipFrame = _channels[0].Clip is not null ? _channels[0].ClipFrame : c0.ClipFrame;
        if (RootLockPose.Offset(c0.Clip, clipFrame, c0.bMirror, mesh.LockedRoot, mesh.RootLockType, c0.bBackward, c0.bLoop,
                out FVector local))
        {
            new FRotator(0, _drawYaw, 0).GetAxes(out FVector x, out FVector y, out FVector z);
            Vector3 world = UeSpace.DirectionToGodot((x * local.X) + (y * local.Y) + (z * local.Z)) / UeSpace.UnitsPerMetre;
            offset = skeleton.Inverse() * world;
        }

        if ((mesh.RootLockType & 2) != 0)
        {
            Basis unit = skeleton.Orthonormalized();
            int carried = (int)FRotator.NormalizeAxis(mesh.CarriedRootTurn(c0, clipFrame));
            Basis back = UeSpace.RotationToGodot(new FRotator(0, -carried, 0));
            turn = (unit.Inverse() * back * unit).GetRotationQuaternion();
        }
    }

    private int _drawYaw;

    /// <summary>
    /// Turns the <see cref="Overlay"/>'s pawn-frame turns into skeleton-space turns for the applier (the frame of the
    /// drawn yaw, the way <see cref="SetAim"/> turns the aim), and the hips' drop into a root shift.
    /// </summary>
    private void ApplyOverlay(SBasePawn pawn)
    {
        ChannelPoseApplier applier = _applier!;
        applier.ExtraTurns.Clear();
        applier.ExtraShift = Vector3.Zero;
        _overlayTurns.Clear();
        float drop = Overlay?.Collect(pawn, _overlayTurns) ?? 0f;
        if (_overlayTurns.Count == 0 && drop == 0f)
        {
            return;
        }

        Vector3 forward = UeSpace.DirectionToGodot(new FRotator(0, _drawYaw, 0).Vector()).Normalized();
        Vector3 right = UeSpace.DirectionToGodot(new FRotator(0, _drawYaw + 16384, 0).Vector()).Normalized();
        Vector3 up = Vector3.Up;
        Basis skeleton = _model!.Skeleton.GlobalTransform.Basis.Orthonormalized();
        Basis toSkeleton = skeleton.Inverse();
        foreach (PoseTurn t in _overlayTurns)
        {
            int bone = applier.FindBone(t.Bone);
            if (bone < 0 || t.Radians == 0f)
            {
                continue;
            }

            Vector3 axis = ((forward * t.Axis.X) + (right * t.Axis.Y) + (up * t.Axis.Z)).Normalized();
            Quaternion world = new(axis, t.Radians);
            Quaternion local = (toSkeleton * new Basis(world) * skeleton).GetRotationQuaternion();
            applier.ExtraTurns[bone] = applier.ExtraTurns.TryGetValue(bone, out Quaternion had) ? (local * had).Normalized() : local;
        }

        if (drop != 0f)
        {
            applier.ExtraShift = _model.Skeleton.GlobalTransform.Basis.Inverse() * (Vector3.Down * (drop / UeSpace.UnitsPerMetre));
        }
    }

    /// <summary>
    /// A channel's frame between the last two ticks: interpolated while the same clip moves forward (no new play), and
    /// across the wrap of a loop (the frame went past 1 and came back near 0), else the last tick's. The walk channels
    /// loop all the time; without the wrap case each loop drew one tick ahead and then held that frame for a whole tick
    /// (a hitch every cycle, worst at walking speed where a cycle is about a second).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Draws between ticks interpolate the channel frame; the original draws once per tick")]
    private (float Frame, float ClipFrame) Interpolated(int c, PawnAnimChannel ch, float alpha)
    {
        TickChannel cur = _cur[c];
        TickChannel prev = _prev[c];
        if (cur.Sequence != ch.Sequence || prev.Sequence != cur.Sequence || (prev.Frame < 0f) != (cur.Frame < 0f))
        {
            return (ch.Frame, ch.ClipFrame);
        }

        if (cur.Frame >= prev.Frame)
        {
            return (prev.Frame + ((cur.Frame - prev.Frame) * alpha), prev.ClipFrame + ((cur.ClipFrame - prev.ClipFrame) * alpha));
        }

        // A loop's wrap: the frame fell by more than half a cycle in one tick. Anything else going back (a frame set
        // by hand, a new play of the same clip) shows the last tick's frame as before.
        if (!ch.bLoop || prev.Frame < 0f || prev.Frame - cur.Frame < 0.5f)
        {
            return (ch.Frame, ch.ClipFrame);
        }

        return (Wrapped(prev.Frame, cur.Frame + 1f, alpha), Wrapped(prev.ClipFrame, cur.ClipFrame + (ch.bBackward ? -1f : 1f), alpha));
    }

    /// <summary>The loop position <paramref name="alpha"/> of the way from <paramref name="from"/> to an unwrapped <paramref name="to"/>, back in [0, 1).</summary>
    private static float Wrapped(float from, float to, float alpha)
    {
        float f = from + ((to - from) * alpha);
        return f - MathF.Floor(f);
    }

    private readonly record struct TickChannel(Name Sequence, float Frame, float ClipFrame);
}
