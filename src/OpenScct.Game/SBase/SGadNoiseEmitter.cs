using System.Collections.Generic;
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
/// SBase.SGadNoiseEmitter: the spy's alarm snare (spec/equipment/alarm-snare.md rules 1 to 4). Shot from the Shocker
/// onto a wall; the owner's machine aims and the server makes the emitter.
/// </summary>
public partial class SGadNoiseEmitter
{
    private static readonly Name CNAME_SpawnNoiseEmitter = new("SpawnNoiseEmitter");
    private static readonly Name CNAME_LocalSystemMessage = new("LocalSystemMessage");

    private readonly List<FCheckResult> _hits = new();

    /// <summary>Alt-fire: the snare is shot, the gadget rumbles.</summary>
    [Uc("SBase.SGadNoiseEmitter.Use", Sha1 = "b4f4ee937c252fc13232f4fd6ceba4985da9ffe7")]
    [PortOf("uc:SBase.SGadNoiseEmitter:Use", Spec = "spec/equipment/alarm-snare.md#rules")]
    public override void Use(float Value)
    {
        UseEmitter();
        (OwnerPawn?.Controller as SPlayerController)?.Vibration_Gadget(K.GadgetRumble);
        base.Use(Value);
    }

    /// <summary>
    /// UseEmitter (native 1304, decoded 0x10aa1240): with the Shocker drawn and the refire time over, the first
    /// surface along the aim that takes a snare gets one (laser projectors are passed through).
    /// </summary>
    [UcNative("ASGadNoiseEmitter::execUseEmitter", Exe = 0x10aa1240)]
    [PortOf("nat:SBase.SGadNoiseEmitter.UseEmitter", Spec = "spec/equipment/alarm-snare.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "alarm-snare.md rule 2: FX bone aim, multi line 0xc10bf, world geometry or stickable GPO, laser projectors skipped")]
    [Provenance(ProvenanceKind.Estimated, "The FX bone pose comes from the combat ports (the estimated muzzle until skeletal poses run)")]
    public void UseEmitter()
    {
        if (OwnerPawn is not { } pawn || pawn.WeaponLoaded == 0 || Level.TimeSeconds - LastUsed < RefireRate)
        {
            return;
        }

        if (Charge < 1)
        {
            PlaySound(SND_EmptyCharge, ESoundSlot.SLOT_Misc, 1f, false, K.EmptyChargeRadius);
            return;
        }

        if (pawn.MyWeapon is not { } weapon || GadgetWorld.Collision(this) is not { } world)
        {
            return;
        }

        FCoords fx = CombatServices.Ports(this).BoneCoords(weapon, SWeaponDefense.FxBone);
        FVector d = fx.XAxis;
        FVector start = fx.Origin - d * K.SnareLaunchPullBack;
        FVector end = start + d * MaxLaunchDist;
        _hits.Clear();
        world.MultiLineCheck(_hits, pawn, end, start, FVector.Zero, SGadStickyCam.StickTraceFlags);
        foreach (FCheckResult hit in _hits)
        {
            if (!hit.bHit)
            {
                continue;
            }

            Actor? a = hit.Actor;
            bool takes = a is SBasicGamePlayObject gpo ? gpo.AcceptStickableGPO() : GadgetWorld.IsWorldGeometry(a);
            if (takes)
            {
                DecCharge(1);
                LastUsed = Level.TimeSeconds;
                FRotator r = SGadStickyCam.FacingBack(hit.Normal, d).Rotation();
                SpawnNoiseEmitter(NoiseEmitterClass, pawn, hit.Location, r, a);
                return;
            }

            if (a is not SLaserProjector)
            {
                return;
            }
        }
    }

    /// <summary>
    /// SpawnNoiseEmitter (native 1305, decoded 0x10aa1570; client to server): the emitter at the pose for Duration
    /// seconds, owned by the spy's controller, riding a gameplay object it was put on; the launch sound and the owner's
    /// "on" text.
    /// </summary>
    [UcNative("ASGadNoiseEmitter::execSpawnNoiseEmitter", Exe = 0x10aa1570)]
    [PortOf("nat:SBase.SGadNoiseEmitter.SpawnNoiseEmitter", Spec = "spec/equipment/alarm-snare.md#rules")]
    [PortOf("rep:SBase.SGadNoiseEmitter.SpawnNoiseEmitter", Spec = "spec/equipment/alarm-snare.md#network")]
    [Provenance(ProvenanceKind.Decoded, "alarm-snare.md rule 3: LifeSpan = Duration, Owner = the pawn's controller, SetBase on a GPO, sound radius 100, LocalSystemMessage")]
    [Provenance(ProvenanceKind.Estimated, "The launch sound is one PlaySound at the spy (the native calls each player controller's sound hook)")]
    public void SpawnNoiseEmitter(ClassRef EmitterClass, Pawn P, FVector Loc, FRotator Rot, Actor? HitActor)
    {
        // Only a gameplay object matters to the server; a world hit needs no actor on the wire.
        HitActor = HitActor as SBasicGamePlayObject;
        if (GadgetNet.ToServer(this, P.Controller as PlayerController, new CombatCall(CNAME_SpawnNoiseEmitter)
        {
            Target = this,
            Name1 = new Name(EmitterClass.Path ?? ""),
            Actor1 = P,
            Actor2 = HitActor,
            Vector1 = Loc,
            Rotation = Rot,
        }))
        {
            return;
        }

        if (DeviceClasses.Spawn(this, EmitterClass, P, Name.None, Loc, Rot) is not SNoiseEmitter emitter)
        {
            return;
        }

        emitter.LifeSpan = Duration;
        emitter.Owner = P.Controller as SPlayerController;
        if (HitActor is SBasicGamePlayObject gpo)
        {
            emitter.SetBase(gpo);
            if (gpo.bWorldGeometry)
            {
                gpo.Attached.Insert(0, emitter);
            }
        }

        P.PlaySound(SoundEffect, ESoundSlot.SLOT_Misc, 1f, false, K.SnareLaunchSoundRadius);
        LocalSystemMessage();
    }

    /// <summary>The server's word that the snare is on: the owner's "noise emitter on" text (server to owner).</summary>
    [Uc("SBase.SGadNoiseEmitter.LocalSystemMessage", Sha1 = "66ece3c84e63f30368114cac15598fd137a16724")]
    [PortOf("uc:SBase.SGadNoiseEmitter:LocalSystemMessage", Spec = "spec/equipment/alarm-snare.md#rules")]
    [PortOf("rep:SBase.SGadNoiseEmitter.LocalSystemMessage", Spec = "spec/equipment/alarm-snare.md#network")]
    public void LocalSystemMessage()
    {
        SPlayerController? pc = OwnerPawn?.Controller as SPlayerController;
        if (GadgetNet.ToOwner(pc, new CombatCall(CNAME_LocalSystemMessage) { Target = this }))
        {
            return;
        }

        pc?.AddMessageOnHud(K.SnareHudOn);
    }
}
