using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Game.SBase;

// A bomb spot: a spy plants a charge, it goes off after CaptureTime and takes the objective; mercs remove it, any
// damage defuses it. Written from spec/objectives/bomb-target.md.
public partial class SBombTargetObjectiveTrigger
{
    public static new readonly StateTable ClassStates = new(SObjectiveTrigger.ClassStates, s_TemporaryInactive.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Unusable until a mission resets it; on the server, hidden outside Story (rule 1).</summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.PostBeginPlay", Sha1 = "f8c701849e2790ece8beca8dc6d47ba01273d3a1")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:PostBeginPlay", Spec = "spec/objectives/bomb-target.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (InteractionObj is not null)
        {
            InteractionObj.bUsable = false;
        }

        // SGameInfo(Level.Game).MatchInfo.GameMode: a level without game rules reads GM_Undefined.
        EGameMode mode = (Level.Game as SGameInfo)?.MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
        if (Role == ENetRole.ROLE_Authority && mode != EGameMode.GM_Multi)
        {
            bHidden = true;
        }
    }

    /// <summary>In play and unarmed (rule 2): the unarmed look (or hidden without one), the entry usable.</summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.Reset", Sha1 = "b2820ac76b5b96df7fa577e7b1d113c020a6e1f4")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:Reset", Spec = "spec/objectives/bomb-target.md#rules")]
    public override void Reset()
    {
        base.Reset();
        ShowUnarmed();
        if (InteractionObj is not null)
        {
            InteractionObj.bUsable = true;
        }
    }

    /// <summary>
    /// Out of play (rule 3; it replaces the objective trigger version): inactive, unlocked, any charge stopped, no EMF,
    /// hidden and unusable. The spot keeps its state.
    /// </summary>
    [UcNative("ASBombTargetObjectiveTrigger::execDesactivate", Exe = 0x10A98A40, Editor = 0x111BD350)]
    [PortOf("nat:SBase.SBombTargetObjectiveTrigger.Desactivate", Spec = "spec/objectives/bomb-target.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Shared thunk 0x10AE9C60, vtable slot 0x138: bActive and bLocked cleared, Triggering(false), bEMFIsOn false, bHidden, InteractionObj.bUsable false")]
    public override void Desactivate()
    {
        bActive = false;
        bLocked = false;
        eventTriggering(false);
        bEMFIsOn = false;
        bHidden = true;
        if (InteractionObj is not null)
        {
            InteractionObj.bUsable = false;
        }
    }

    /// <summary>
    /// Whether this spot may be armed: no other spot of the objective is counting down. The spots walked past before
    /// an armed one is found are made unusable and hidden (rule 9).
    /// </summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.CheckOtherTriggers", Sha1 = "3e3153a308226d496786d9c907a0091939eb2205")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:CheckOtherTriggers", Spec = "spec/objectives/bomb-target.md#rules")]
    public bool CheckOtherTriggers()
    {
        if (Objective is null)
        {
            return true;
        }

        for (i = 0; i < Objective.Triggers.Count; i++)
        {
            SObjectiveTrigger? other = Objective.Triggers[i];
            if (ReferenceEquals(other, this) || other is null)
            {
                continue;
            }

            if (other.bTriggering)
            {
                return false;
            }

            if (other.InteractionObj is not null)
            {
                other.InteractionObj.bUsable = false;
            }

            other.bHidden = true;
        }

        return true;
    }

    /// <summary>
    /// Arms or disarms the charge (rule 9). Armed: the armed look, the fuse timer, the beep, the entry for mercs to
    /// remove it, "under attack" to everyone. Disarmed: the unarmed look, the other spots back, the entry for spies.
    /// </summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.Triggering", Sha1 = "276bea2efe226ddd4221207cfa04f0d3f9ae7c12")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:Triggering", Spec = "spec/objectives/bomb-target.md#rules")]
    protected internal override void Triggering(bool Status)
    {
        if (Status == bTriggering || (Status && !bActive) || (Status && !CheckOtherTriggers()))
        {
            return;
        }

        base.Triggering(Status);
        if (bTriggering)
        {
            if (!AbsentSkin.IsNone)
            {
                bSkinState = true;
            }
            else
            {
                bHidden = false;
            }

            bIgnoreEchelonLighting = false;
            SetTimer(CaptureTime, false);
            AmbientSound = MK.BombArmedSound;
            if (InteractionObj is not null)
            {
                InteractionObj.UsableBy = Usable.U_SPAWNDEFENSE;
            }

            (InteractionObj as SBombTargetInteraction)?.Update(true);
            ObjectiveMessages.Broadcast(this, MK.MsgBombUnderAttack, this);
            return;
        }

        ShowUnarmed();
        SetTimer(0f, false);
        if (Objective is not null)
        {
            for (i = 0; i < Objective.Triggers.Count; i++)
            {
                if (Objective.Triggers[i] is { bActive: true } other && !ReferenceEquals(other, this))
                {
                    if (other.InteractionObj is not null)
                    {
                        other.InteractionObj.bUsable = true;
                    }

                    other.bHidden = false;
                }
            }
        }

        AmbientSound = AssetRef.None;
        if (InteractionObj is not null)
        {
            InteractionObj.bUsable = true;
            InteractionObj.UsableBy = Usable.U_SPAWNOFFENSE;
        }

        (InteractionObj as SBombTargetInteraction)?.Update(false);
    }

    /// <summary>
    /// The fuse is done (rule 12): the spot disappears, the native blast goes off, the charge is disarmed (so the
    /// blast's own damage sends no "secured"), then the objective is taken.
    /// </summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.Timer", Sha1 = "4e885550dc501a8fb14ed64727a1a2ff0ceab798")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:Timer", Spec = "spec/objectives/bomb-target.md#rules")]
    protected override void Timer()
    {
        if (InteractionObj is not null)
        {
            InteractionObj.bUsable = false;
        }

        bHidden = true;
        bIgnoreEchelonLighting = true;
        eventProcessDamage(HitPoints);
        HitPoints = 1;
        eventTriggering(false);
        eventTakeDamage(HitPoints, ControllerWhoTakesIt?.Pawn, Location, FVector.Zero, SDamageType.SDTBase);
        eventTakeObjective();
    }

    /// <summary>Any damage defuses a charge; while armed, everyone hears it was secured and a merc is credited (rule 11).</summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.TakeDamage", Sha1 = "89803e9f3aefb061835f0c31284cde582b3ac50f")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:TakeDamage", Spec = "spec/objectives/bomb-target.md#rules")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (bTriggering)
        {
            ObjectiveMessages.Broadcast(this, MK.MsgBombSecured, this);
            if (EventInstigator is SPAWNDEFENSE { Controller: SPlayerController { PlayerReplicationInfo.Stats: { } stats } })
            {
                stats.Merc_SecuredBombs++;
            }
        }

        eventTriggering(false);
        SetTimer(0f, false);
    }

    /// <summary>An alarm locks the spot (rule 16): "secured" in Story or Sabotage when unarmed, then locked.</summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.Lock", Sha1 = "2ca6ca0d1110aec9e91027e7c3f51e2635d93192")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:Lock", Spec = "spec/objectives/bomb-target.md#rules")]
    protected internal override void Lock()
    {
        if (bHidden)
        {
            return;
        }

        EGameMode mode = (Level.Game as SGameInfo)?.MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
        if ((mode == EGameMode.GM_Sabotage || mode == EGameMode.GM_Multi) && !bTriggering)
        {
            ObjectiveMessages.Broadcast(this, MK.MsgBombSecured, this);
        }

        if (InteractionObj is not null)
        {
            InteractionObj.bUsable = false;
        }

        GotoState(K.s_TemporaryInactive);
    }

    /// <summary>The blast's effects and "bombed" to everyone, then the objective is taken (rule 13).</summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.TakeObjective", Sha1 = "c486bfbb9ffcd64c6571758864846c812adffd2b")]
    [PortOf("uc:SBase.SBombTargetObjectiveTrigger:TakeObjective", Spec = "spec/objectives/bomb-target.md#rules")]
    protected internal override void TakeObjective()
    {
        DeviceClasses.Spawn(this, MK.BombEmitterClass, this, Name.None, Location, Rotation);
        PlaySound(MK.BombExplosionSound, ESoundSlot.SLOT_Misc, 1f, false, MK.BombExplosionSoundRadius);
        ObjectiveMessages.Broadcast(this, MK.MsgBombBombed, this);
        base.TakeObjective();
    }

    /// <summary>
    /// The armed or unarmed look on every machine (rule 14): when the replicated flag changes, armed shows the mesh's
    /// own materials and takes projectors, unarmed shows <see cref="SBombTargetObjectiveTrigger.AbsentSkin"/>.
    /// </summary>
    [UcNative("ASBombTargetObjectiveTrigger::Tick", Exe = 0x10A98B00, Editor = 0x111BD290)]
    [PortOf("eng:ASBombTargetObjectiveTrigger::Tick", Spec = "spec/objectives/bomb-target.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ASBombTargetObjectiveTrigger::Tick (game 0x10A98B00): Skins[0..2] and bAcceptsProjectors follow bSkinState")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || bSkinState == bLocalSkinState)
        {
            return;
        }

        bLocalSkinState = bSkinState;
        AssetRef skin = bSkinState ? AssetRef.None : AbsentSkin;
        for (int s = 0; s < Skins.Count && s < 3; s++)
        {
            Skins[s] = skin;
        }

        bAcceptsProjectors = bSkinState;
    }

    /// <summary>The unarmed look: the absent skin when there is one, else hidden; no Echelon lighting.</summary>
    private void ShowUnarmed()
    {
        if (!AbsentSkin.IsNone)
        {
            bSkinState = false;
            bHidden = false;
        }
        else
        {
            bHidden = true;
        }

        bIgnoreEchelonLighting = true;
    }
}
