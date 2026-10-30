using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadGasMask: the merc's gas mask (spec/equipment/gas-mask.md): 30 s of air in total; while it is on, the
/// smoke and sticky cam gas emitters skip him (bGasMask).
/// </summary>
public partial class SGadGasMask
{
    private static readonly Name CNAME_SetAmbientSoundOnServer = new("SetAmbientSoundOnServer");

    /// <summary>Alt-fire: off when on; on with air left (after the refire delay); else the empty sound.</summary>
    [Uc("SBase.SGadGasMask.Use", Sha1 = "d99ee51495b9494b67c8dd4444fc8f986d9d253c")]
    [PortOf("uc:SBase.SGadGasMask:Use", Spec = "spec/equipment/gas-mask.md#rules")]
    public override void Use(float Value)
    {
        if (OwnerPawn is not { } pawn)
        {
            return;
        }

        if (pawn.bGasMask)
        {
            PlayMaskSound(pawn, Snd_Activation);
            Activate(false);
        }
        else
        {
            if (Level.TimeSeconds - LastUsed < RefireRate)
            {
                return;
            }

            if (MaskDuration > 0f)
            {
                PlayMaskSound(pawn, Snd_Activation);
                LastUsed = Level.TimeSeconds;
                Activate(true);
            }
            else
            {
                PlayMaskSound(pawn, Snd_Vide);
            }
        }

        base.Use(Value);
    }

    /// <summary>
    /// The mask sounds of Use: SE_masqGaz_in and SE_masqGaz_vide play 2D waves at radius 100 on the owner's machine
    /// (Use runs on the owning client; s_LyingOnGround calls it only there), so only the owner hears them. A bot runs
    /// Use in the server world, which would relay them (ClientHearSound) to every player in reach as full-volume 2D
    /// sounds: like its vision sounds, a bot's mask sounds play nowhere (<see cref="SGoggle.OwnerHearsVision"/>).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGadGasMask.Use is simulated and runs on the owner's machine; PlaySound(.., SLOT_Misc, 1.0, , 100.0)")]
    private static void PlayMaskSound(SBasePawn pawn, AssetRef sound)
    {
        if (SGoggle.OwnerHearsVision(pawn.Controller as PlayerController))
        {
            pawn.PlaySound(sound, ESoundSlot.SLOT_Misc, 1f, false, K.GasMaskSoundRadius);
        }
    }

    /// <summary>The pawn's breathing or choking sound on the server: 1 breathing, 2 choking, 0 cleared (client to server).</summary>
    [Uc("SBase.SGadGasMask.SetAmbientSoundOnServer", Sha1 = "0a13e524e6821bcb098d42774fb8fa9282977df5")]
    [PortOf("uc:SBase.SGadGasMask:SetAmbientSoundOnServer", Spec = "spec/equipment/gas-mask.md#rules")]
    public void SetAmbientSoundOnServer(SBasePawn P, byte bVal)
    {
        if (GadgetNet.ToServer(this, P.Controller as PlayerController,
            new CombatCall(CNAME_SetAmbientSoundOnServer) { Target = this, Actor1 = P, Int1 = bVal }))
        {
            return;
        }

        switch (bVal)
        {
            case K.GAD_RESPIRE:
                P.AmbientSound = Snd_Respiration;
                break;
            case K.GAD_ETOUFFE:
                P.StopSound(P.AmbientSound);
                P.AmbientSound = Snd_Etouffement;
                break;
            case K.GAD_STOPSOUND when P.AmbientSound == Snd_Etouffement || P.AmbientSound == Snd_Respiration:
                P.StopSound(P.AmbientSound);
                P.AmbientSound = AssetRef.None;
                break;
        }
    }

    /// <summary>The mask on or off: the flag here and on the server, the breathing or choking loop.</summary>
    [Uc("SBase.SGadGasMask.Activate", Sha1 = "59bfca596a1245db9823a42c40763afbdb8fa8fb")]
    [PortOf("uc:SBase.SGadGasMask:Activate", Spec = "spec/equipment/gas-mask.md#rules")]
    public void Activate(bool bActivate)
    {
        if (OwnerPawn is not { } pawn)
        {
            return;
        }

        pawn.bGasMask = bActivate;
        PawnGadgets.SetGasMaskUseOnServer(pawn, bActivate);
        Controller? controller = pawn.Controller;
        if (bActivate)
        {
            if (MaskDuration < K.GasMaskChoking)
            {
                if (controller is not null)
                {
                    controller.StopSound(controller.AmbientSound);
                    controller.AmbientSound = Snd_Etouffement;
                }

                SetAmbientSoundOnServer(pawn, K.GAD_ETOUFFE);
            }
            else
            {
                if (controller is not null)
                {
                    controller.AmbientSound = Snd_Respiration;
                }

                SetAmbientSoundOnServer(pawn, K.GAD_RESPIRE);
            }
        }
        else
        {
            if (controller is not null && (controller.AmbientSound == Snd_Respiration || controller.AmbientSound == Snd_Etouffement))
            {
                controller.AmbientSound = AssetRef.None;
            }

            SetAmbientSoundOnServer(pawn, K.GAD_STOPSOUND);
        }
    }

    /// <summary>The selection hint.</summary>
    [Uc("SBase.SGadGasMask.Load", Sha1 = "8a7bcdbd59bdc9f8214a895d40e5a3eff63f6c31")]
    [PortOf("uc:SBase.SGadGasMask:Load", Spec = "spec/equipment/gas-mask.md#rules")]
    public override void Load() => GadgetHud.AddMessageToAuxiliaryStack(OwnerPawn?.Controller as SPlayerController, TextCanUseGasMask, K.GasMaskMessageTime);

    /// <summary>Leaving the selection takes the mask off.</summary>
    [Uc("SBase.SGadGasMask.Unload", Sha1 = "0b0fd26bf3c41a41661843b277edb5fbbc2f1f29")]
    [PortOf("uc:SBase.SGadGasMask:Unload", Spec = "spec/equipment/gas-mask.md#rules")]
    public override void Unload() => Activate(false);

    /// <summary>The air runs out while the mask is on; under 7.5 s the loop switches to choking.</summary>
    [Uc("SBase.SGadGasMask.Tick", Sha1 = "562c18e11ff19ab722b54f3c630fa20467fe0bc9")]
    [PortOf("uc:SBase.SGadGasMask:Tick", Spec = "spec/equipment/gas-mask.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        if (OwnerPawn is { bGasMask: true } pawn)
        {
            // An OpenSCCT addition: a practice round's mask never runs out of air.
            if (!Modes.PracticeRules.IsOn(this))
            {
                MaskDuration -= DeltaTime;
            }

            if (Role == ENetRole.ROLE_Authority && (pawn.Controller as SPlayerController)?.PlayerReplicationInfo?.Stats is { } stats)
            {
                stats.Merc_GasMaskTimeUsed += DeltaTime + SvmMath.Min(MaskDuration, 0f);
            }

            if (MaskDuration <= 0f)
            {
                MaskDuration = 0f;
                Activate(false);
            }
            else if (MaskDuration < K.GasMaskChoking && pawn.AmbientSound != Snd_Etouffement)
            {
                Activate(false);
                Activate(true);
            }
        }

        base.Tick(DeltaTime);
    }

    /// <summary>The visual mask goes with the gadget (the original does not run the gadget's own Destroyed).</summary>
    [Uc("SBase.SGadGasMask.Destroyed", Sha1 = "bd723a7e7b1e8e69050254e298d603358a867a40")]
    [PortOf("uc:SBase.SGadGasMask:Destroyed", Spec = "spec/equipment/gas-mask.md#rules")]
    protected override void Destroyed()
    {
        DummyGasMask?.Destroy();
    }

    /// <summary>
    /// Based on the owner; the server gives the merc the visual mask (an SGasMask owned by him, his the Instigator)
    /// attached to his 'B Head' bone, where the views draw it.
    /// </summary>
    [Uc("SBase.SGadGasMask.PostBeginPlay", Sha1 = "3e5545a73fa1918ad952429ecf225a860565d311")]
    [PortOf("uc:SBase.SGadGasMask:PostBeginPlay", Spec = "spec/equipment/gas-mask.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        SetBase(Owner);
        if (Role == ENetRole.ROLE_Authority && Owner is SBasePawn owner)
        {
            DummyGasMask = XLevel.SpawnActor(new SGasMask(), owner.Location, owner.Rotation, owner, Instigator ?? owner);
            if (DummyGasMask is { } mask)
            {
                mask.Instigator ??= owner;
                mask.SetOwner(owner);
                ObjectiveNatives.AttachToBone(owner, mask, K.GasMaskBone);
            }
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        DummyGasMask = Live(DummyGasMask);
    }
}
