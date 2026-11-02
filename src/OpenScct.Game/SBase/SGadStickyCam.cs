using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadStickyCam: the spy's sticky cam launcher (spec/equipment/sticky-cam.md rules 1 to 6). The cam is shot
/// from the Shocker's muzzle onto world geometry; the server makes it and the client predicts its own.
/// </summary>
public partial class SGadStickyCam
{
    private static readonly Name CNAME_TellClientToDecCharge = new("TellClientToDecCharge");

    /// <summary>The actor-only line of rule 2.4: other actors and world geometry, first hit, backdrops skipped.</summary>
    [Provenance(ProvenanceKind.Decoded, "UseStickyCam 0x10aa1fb5: flags 0x80290")]
    public const ETraceFlags GpoTraceFlags = ETraceFlags.TRACE_BackdropSkip | ETraceFlags.TRACE_StopAtFirstHit |
        ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Others;

    /// <summary>The stick line and the corner probes (rule 2.5): everything that collides, volumes, materials.</summary>
    [Provenance(ProvenanceKind.Decoded, "UseStickyCam 0x10aa20ec: flags 0xc10bf (bit 0x40000 has no name in this port)")]
    public const ETraceFlags StickTraceFlags = ETraceFlags.TRACE_BackdropSkip | (ETraceFlags)0x40000 | ETraceFlags.TRACE_Material |
        ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_OnlyProjActor | ETraceFlags.TRACE_Others | ETraceFlags.TRACE_Volumes |
        ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Pawns;

    /// <summary>
    /// Alt-fire: refused within the refire delay or with the Shocker holstered; a pawn already using a cam gives the
    /// next cam its default doses; a cam that cannot stick shows the "can't spawn" message.
    /// </summary>
    [Uc("SBase.SGadStickyCam.Use", Sha1 = "772828015749721ab3656ef6a3449a68ffc76308")]
    [PortOf("uc:SBase.SGadStickyCam:Use", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    public override void Use(float Value)
    {
        if (OwnerPawn is not { } pawn || Level.TimeSeconds - LastUsed < RefireRate || pawn.WeaponLoaded == 0)
        {
            return;
        }

        SPlayerController? pc = pawn.Controller as SPlayerController;
        if (pc?.SBPawn?.UsedObject is not null)
        {
            iNbSmokeChargeForCurrentSticky = -1;
        }

        if (!UseStickyCam(false))
        {
            GadgetHud.AddMessageToAuxiliaryStack(pc, pc?.TextCantSpawnStickyCam, K.StickyMessageTime);
        }

        base.Use(Value);
    }

    /// <summary>
    /// UseStickyCam (native 1302, decoded 0x10aa1e30): whether a cam can stick where the Shocker aims; unless
    /// <paramref name="bTest"/>, the refire time is taken and the cam is made (SpawnCam).
    /// </summary>
    [UcNative("ASGadStickyCam::execUseStickyCam", Exe = 0x10aa1e30)]
    [PortOf("nat:SBase.SGadStickyCam.UseStickyCam", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    [Provenance(ProvenanceKind.Decoded, "sticky-cam.md rule 2: aim, two lines, world geometry without bNoStickyCam, 2.5 uu back, four 5.5 uu probes")]
    [Provenance(ProvenanceKind.Estimated, "The FX bone pose comes from the combat ports (the estimated muzzle until skeletal poses run)")]
    public bool UseStickyCam(bool bTest = false)
    {
        if (OwnerPawn is not { MyWeapon: { } weapon } pawn || weapon.Mesh.IsNone)
        {
            return false;
        }

        if (Level.TimeSeconds - LastUsed < RefireRate && !bTest)
        {
            return false;
        }

        if (pawn.WeaponLoaded == 0)
        {
            return false;
        }

        if (Charge < 1 && !GadgetWorld.CurLevelIsTutorial(this))
        {
            PlaySound(SND_EmptyCharge, ESoundSlot.SLOT_Misc, 1f, false, K.EmptyChargeRadius);
            return false;
        }

        FCoords fx = CombatServices.Ports(this).BoneCoords(weapon, SWeaponDefense.FxBone);
        FVector d = fx.XAxis;
        FVector start = fx.Origin - d * K.StickyLaunchPullBack;
        FVector end = start + d * MaxLaunchDist;
        if (GadgetWorld.Line(this, end, start, GpoTraceFlags, out FCheckResult first) && first.Actor is SBasicGamePlayObject gpo &&
            !gpo.AcceptStickableGPO())
        {
            return false;
        }

        // The trace passes zone, sound and physics volumes itself (AVolume::ShouldTrace under OnlyProjActor).
        if (!GadgetWorld.Line(this, end, start, StickTraceFlags, out FCheckResult hit) || !CanStickOn(hit))
        {
            return false;
        }

        if (!bTest)
        {
            LastUsed = Level.TimeSeconds;
        }

        FVector n = FacingBack(hit.Normal, d);
        FVector p = hit.Location - d * K.StickyHitPullBack;
        FRotator r = n.Rotation();
        FVector side = new FRotator(r.Pitch, r.Yaw + K.StickySideYaw, r.Roll).Vector();
        FVector up = new(0f, 0f, 1f);
        p = Probe(pawn, p, side, d);
        p = Probe(pawn, p, -side, d);
        p = Probe(pawn, p, -up, d);
        p = Probe(pawn, p, up, d);
        if (!bTest)
        {
            // The original biases only the final facing, after the corner probes. A vertical normal has no yaw;
            // the tiny reverse-shot component keeps floor/ceiling cams facing the shooter instead of world east.
            FRotator facing = (n - d * K.StickyFacingBias).Rotation();
            SpawnCam(StickyCamClass, pawn, p, facing, iNbSmokeChargeForCurrentSticky, hit.Actor);
        }

        return true;
    }

    /// <summary>The stick surface test: world geometry, not a blocking volume, a material without bNoStickyCam.</summary>
    public static bool CanStickOn(in FCheckResult Hit) =>
        Hit.Actor is not BlockingVolume && (Hit.Actor is null or LevelInfo || Hit.Actor.bWorldGeometry) &&
        (Hit.MaterialFlags & CollisionMaterialFlags.NoStickyCam) == 0;

    /// <summary>A normal turned to face back along -<paramref name="Dir"/> (kept when already facing back).</summary>
    public static FVector FacingBack(FVector Normal, FVector Dir) => (Dir | Normal) >= 0f ? -Normal : Normal;

    /// <summary>A 5.5 uu corner probe: a hit pushes the point 5 uu off that surface.</summary>
    private FVector Probe(SBasePawn pawn, FVector p, FVector axis, FVector d)
    {
        if (GadgetWorld.Line(pawn, p + axis * K.StickyProbe, p, StickTraceFlags, out FCheckResult h))
        {
            return h.Location + FacingBack(h.Normal, d) * K.StickyProbePush;
        }

        return p;
    }

    /// <summary>CanUse (native 1303): the owner uses no object now. No script calls it.</summary>
    [UcNative("ASGadStickyCam::execCanUse", Exe = 0x10aa2870)]
    [PortOf("nat:SBase.SGadStickyCam.CanUse", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    public bool CanUseStickyCam() => OwnerPawn is { UsedObject: null };

    /// <summary>The server's word that a cam was made: the owner's machine uses a charge (server to owner).</summary>
    [Uc("SBase.SGadStickyCam.TellClientToDecCharge", Sha1 = "6502237d97844691d13c3279b405b15f524ef803")]
    [PortOf("uc:SBase.SGadStickyCam:TellClientToDecCharge", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    [PortOf("rep:SBase.SGadStickyCam.TellClientToDecCharge", Spec = "spec/equipment/sticky-cam.md#network")]
    public void TellClientToDecCharge()
    {
        if (GadgetNet.ToOwner(OwnerPawn?.Controller as PlayerController, new CombatCall(CNAME_TellClientToDecCharge) { Target = this }))
        {
            return;
        }

        DecCharge();
    }

    /// <summary>
    /// SpawnCam (rule 4): the pose quantised as the network sends it; the server makes the cam and tells the owner;
    /// a client asks the server and, unless falling, makes a local predicted cam for 0.8 s.
    /// </summary>
    [Uc("SBase.SGadStickyCam.SpawnCam", Sha1 = "531b09e9addb88b62407b582641e8567499127c3")]
    [PortOf("uc:SBase.SGadStickyCam:SpawnCam", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    public void SpawnCam(ClassRef CamClass, Pawn P, FVector Loc, FRotator Rot, int NbGasCharge, Actor? HitActor)
    {
        Rot = new FRotator(Rot.Pitch & K.NetRotationMask, Rot.Yaw & K.NetRotationMask, Rot.Roll & K.NetRotationMask);
        Loc = new FVector(NetRoundVectorAxis(Loc.X), NetRoundVectorAxis(Loc.Y), NetRoundVectorAxis(Loc.Z));
        if (Level.NetMode != ENetMode.NM_Client)
        {
            if (CreateStickyCam(CamClass, P, Loc, Rot, NbGasCharge, HitActor) is not null)
            {
                TellClientToDecCharge();
            }

            return;
        }

        if (P.Controller is not SPlayerController pc)
        {
            return;
        }

        pc.ServerSpawnStickyCam(this, CamClass, Loc, Rot, NbGasCharge, HitActor);
        if (P.Physics == EPhysics.PHYS_Falling)
        {
            return;
        }

        if (CreateStickyCam(CamClass, P, Loc, Rot, NbGasCharge, HitActor) is not { } cam)
        {
            return;
        }

        cam.SetCollision(false, false, false);
        cam.InteractionObj?.Destroy();
        pc.PredictedStickyCam = cam;
        pc.PredictedStickyCamDeadline = Level.TimeSeconds + K.StickyPredictionTimeout;
    }

    /// <summary>Round half up as the network rounds a coordinate (int(v + 0.5), one less when that is above v + 0.5).</summary>
    [Uc("SBase.SGadStickyCam.NetRoundVectorAxis", Sha1 = "65a7680b1a1fc2cde12640a76aaeae04f2777f45")]
    [PortOf("uc:SBase.SGadStickyCam:NetRoundVectorAxis", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    public static float NetRoundVectorAxis(float V)
    {
        int q = SvmMath.Trunc(V + 0.5f);
        if (q > V + 0.5f)
        {
            q--;
        }

        return q;
    }

    /// <summary>
    /// Makes the cam owned by the pawn; on a gameplay object it rides that object; a dose count other than -1 is
    /// written to it.
    /// </summary>
    [Uc("SBase.SGadStickyCam.CreateStickyCam", Sha1 = "77f25fd86d50e1f12768735a992e27cb91fdcccf")]
    [PortOf("uc:SBase.SGadStickyCam:CreateStickyCam", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    public SStickyCam? CreateStickyCam(ClassRef CamClass, Pawn P, FVector Loc, FRotator Rot, int NbGasCharge, Actor? HitActor)
    {
        if (DeviceClasses.Spawn(this, CamClass, P, Name.None, Loc, Rot) is not SStickyCam cam)
        {
            return null;
        }

        GadgetFx.Notify(cam, GadgetFx.StickyCamSpawned);
        if (HitActor is SBasicGamePlayObject gpo)
        {
            cam.SetBase(gpo);
            if (gpo.bWorldGeometry)
            {
                gpo.Attached.Insert(0, cam);
            }
        }

        if (NbGasCharge != -1)
        {
            cam.Smoke_NbDoses = NbGasCharge;
        }

        return cam;
    }
}
