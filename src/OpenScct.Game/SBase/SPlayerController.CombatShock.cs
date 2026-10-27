using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController sticky shock, taser, jammer and smoke: Checkshock and its immunity window,
// ClientStickyShock on the owner, the shock, jammer and cough timers of PlayerTick, GoInSmoke and the gas
// knock-out credit. Written from spec/combat/shock-and-taser.md, spec/equipment/sticky-shock-effect.md,
// spec/equipment/smoke-grenade.md and spec/combat/knockout-and-terminate.md (smoke and gas faint).
public partial class SPlayerController
{
    private static readonly Name CNAME_s_Dmg_Electric = new("s_Dmg_Electric");
    private static readonly Name CNAME_s_Dmg_Electric_End = new("s_Dmg_Electric_End");
    private static readonly Name CNAME_s_Selected = new("s_Selected");
    private static readonly Name CNAME_ReceiveLocalizedMessage = new("ReceiveLocalizedMessage");
    private static readonly Name CNAME_ClientPlaySound = new("ClientPlaySound");
    private static readonly Name CNAME_ClientStickyShock = new("ClientStickyShock");
    private static readonly Name CNAME_Client_Jammed = new("Client_Jammed");
    private static readonly Name CNAME_ServerSetVelocityForDyingPawn = new("ServerSetVelocityForDyingPawn");
    private static readonly Name CNAME_GoInSmoke = new("GoInSmoke");
    private static readonly AssetRef SndShockPaf = new("Armes_impacts.Armes.StikyShok_Paf1");
    private static readonly AssetRef SndShockOno = new("Sound_Persos.onomatopees.RA_onoPaf");
    private static readonly AssetRef SndCough = new("Sound_Persos.onomatopees.RA_Tousse");

    /// <summary>A state version of <see cref="Checkshock"/> (server).</summary>
    public interface ICheckshock
    {
        void Checkshock(SPlayerController self, int PillTag, bool bPlayAnim);
    }

    /// <summary>A state version of <see cref="ClientStickyShock"/> (owner).</summary>
    public interface IClientStickyShock
    {
        void ClientStickyShock(SPlayerController self, int PillTag, bool bPlayAnim);
    }

    /// <summary>A state version of <see cref="GoInSmoke"/> (owner; states that ignore smoke implement it empty).</summary>
    public interface IGoInSmoke
    {
        void GoInSmoke(SPlayerController self, float tCough, float tFaint, float tHud, float blur);
    }

    // ---- Checkshock ------------------------------------------------------------------------------------------

    /// <summary>Checkshock through the current state (the s_Inactive pill conversion, the charge-hit forced animation).</summary>
    public void eventCheckshock(int PillTag, bool bPlayAnim = false)
    {
        if (StateNode is ICheckshock s)
        {
            s.Checkshock(this, PillTag, bPlayAnim);
            return;
        }

        Name state = GetStateName();
        if (CombatDispatch.IsInactive(state))
        {
            InactiveCheckshock(PillTag, bPlayAnim);
        }
        else if (CombatDispatch.IsChargeHit(state))
        {
            ChargeHitCheckshock(PillTag);
        }
        else
        {
            Checkshock(PillTag, bPlayAnim);
        }
    }

    /// <summary>
    /// The shock test (server, rules 1 to 4): accepted after the immunity window, or for a taser when the pawn is not
    /// shocking; an accepted shock opens the next window and tells the owner; a merc head shot counts for the
    /// shooter; a refused shock sounds the immune cue for a spy shooter.
    /// </summary>
    [Uc("SBase.SPlayerController.Checkshock", Sha1 = "ce696f2e991ee6b68af72facfa32b4282dc66739")]
    [PortOf("uc:SBase.SPlayerController:Checkshock", Spec = "spec/combat/shock-and-taser.md#checkshockpilltag-bplayanim-server-called-from-damage-handlers")]
    public void Checkshock(int PillTag, bool bPlayAnim = false)
    {
        if (SBPawn is not { } pawn)
        {
            ShockInstigator = null;
            return;
        }

        float now = Level.TimeSeconds;
        bool accept = PillTag != K.TaserPill ? now - TimeShock > RealTimeInvulSticky : !pawn.bStickyShocking;
        bool instigatorThisTick = ShockInstigator is not null && ShockInstigatorTime == now &&
            ShockInstigator.Controller is SPlayerController;
        if (accept)
        {
            CombatRules.ShockTimes(pawn, PillTag, fRainFreezeFactor, out _, out float shock);
            RealTimeInvulSticky = shock + TempsInvulSticky;
            TimeShock = now;
            TimeShockForGrab = now;
            ClientStickyShock(PillTag, bPlayAnim);
            if (PillTag == 1 && pawn.SPawnType == PawnType.SPAWNDEFENSE && instigatorThisTick)
            {
                IncStat(ShockInstigator!.Controller!, static s => s.Spy_ShockerGunHeadshots++);
            }
        }
        else if (PillTag != K.TaserPill && instigatorThisTick && ShockInstigator!.Controller is SPlayerController { SPawnType: PawnType.SPAWNOFFENSE } shooter)
        {
            shooter.ClientPlaySoundCombat(SndShockPaf);
        }

        ShockInstigator = null;
    }

    /// <summary>s_Inactive.Checkshock: a taser on a downed player is an ordinary shock (pill 5).</summary>
    [Uc("SBase.SPlayerController.s_Inactive.Checkshock", Sha1 = "b9249e5501348f10d7b78c7656e976596caf08ad")]
    [PortOf("uc:SBase.SPlayerController:s_Inactive.Checkshock", Spec = "spec/combat/shock-and-taser.md#checkshockpilltag-bplayanim-server-called-from-damage-handlers")]
    public void InactiveCheckshock(int PillTag, bool bPlayAnim) =>
        Checkshock(PillTag == K.TaserPill ? K.InactiveTaserPill : PillTag, bPlayAnim);

    /// <summary>s_PlayerCharging_Hit.Checkshock: the shock animation always plays.</summary>
    [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.Checkshock", Sha1 = "bd9696da51d630b942e5c4bbe2415b3922a4e787")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.Checkshock", Spec = "spec/combat/shock-and-taser.md#checkshockpilltag-bplayanim-server-called-from-damage-handlers")]
    public void ChargeHitCheckshock(int PillTag) => Checkshock(PillTag, true);

    // ---- ClientStickyShock -----------------------------------------------------------------------------------

    /// <summary>
    /// The shock on the owner (rules 7 to 18, sticky-shock-effect.md rule 3): use and fire cancelled, the freeze and
    /// electric deadlines, the shock action, the merc weapon's electric state, vision reset, torch malfunction, the
    /// HUD perturbation (and the head-shot shutdown) and the shock sounds. Runs in the owner's current state.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientStickyShock", Sha1 = "e01d43dfb1b013c00198d32f388907d884cd0dc6")]
    [PortOf("uc:SBase.SPlayerController:ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    [PortOf("rep:SBase.SPlayerController.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#network")]
    public void ClientStickyShock(int PillTag, bool bPlayAnim = false)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_ClientStickyShock) { Int1 = PillTag, Bool1 = bPlayAnim });
            return;
        }

        if (StateNode is IClientStickyShock s)
        {
            s.ClientStickyShock(this, PillTag, bPlayAnim);
            return;
        }

        GlobalClientStickyShock(PillTag, bPlayAnim);
    }

    /// <summary>The global ClientStickyShock.</summary>
    public void GlobalClientStickyShock(int PillTag, bool bPlayAnim)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        float now = Level.TimeSeconds;
        CancelUseAndFire(now);
        CombatRules.ShockTimes(pawn, PillTag, fRainFreezeFactor, out float freeze, out float shock);
        RealTimeInvulSticky = shock + TempsInvulSticky;
        Vibration_StickyShock(freeze);
        pawn.bStickyShockedInTheHead = PillTag == 1;
        pawn.bStickyShocking = true;
        iDontUpdatePadInput = 1;
        if (bBinocularVision)
        {
            ClientStopBinoculars();
        }

        CombatPorts.ChangeEnumAction(pawn, bPlayAnim ? SBasePawn.SA_DMG_ELECTRIC : SBasePawn.SA_DMG_ELECTRIC_NOANIMS, true);
        if (pawn.MyWeapon is SWeaponDefense rifle)
        {
            rifle.GotoState(CNAME_s_Dmg_Electric);
        }

        Goggle?.ResetVisionMode();
        if (pawn is SPAWNDEFENSE { Torche: { } torch })
        {
            torch.Malfunction(shock);
        }

        if (PillTag == K.TaserPill)
        {
            bTazered = true;
        }

        if (PillTag == 1)
        {
            HudShutDown(1f, freeze + pawn.Shock_HeadShotHudSfxDuration);
        }

        SetHudPerturbationLength(shock);
        iTimeElecStickyShock = SvmMath.Trunc(now + shock);
        iTimeToStopStickyShock = SvmMath.Trunc(now + freeze);
        ClientPlaySoundCombat(SndShockPaf);
        ClientPlaySoundCombat(SndShockOno);
    }

    /// <summary>Cancels the alt-fire charge, the ballistic gauge, fire and the use button (rule 7).</summary>
    private void CancelUseAndFire(float now)
    {
        bAltFire = 0;
        if (myHUD is { } hud)
        {
            hud.BallisticValue = 0f;
        }

        bFire = 0;
        TimerUSe = now;
        fThrowForce = 0f;
        bUsingNow = false;
    }

    /// <summary>s_TerminatePawn_Base: the finishing move is abandoned (view, reticle, weapon), then the global shock.</summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn_Base.ClientStickyShock", Sha1 = "161e92ddd04bec25913654b54baf97f15d373bde")]
    [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    public void TerminateClientStickyShock(int PillTag, bool bPlayAnim)
    {
        CombatFx.Notify(this, CombatFx.HeadCamera, 0f);
        CombatFx.Notify(this, CombatFx.FirstPersonView, 1f);
        bNoReticule = false;
        (SBPawn?.MyWeapon as SWeaponDefense)?.GotoState(CNAME_s_Selected);
        GotoWaitingStateCombat();
        GlobalClientStickyShock(PillTag, bPlayAnim);
    }

    /// <summary>s_GrabbingPlayer: a taser first tries a knock-out release; without room the spy is shocked in pill 2.</summary>
    [Uc("SBase.SPlayerController.s_GrabbingPlayer.ClientStickyShock", Sha1 = "2be2dda2b401e9f4c53a260e6222f0dfe7406105")]
    [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    public void GrabbingClientStickyShock(int PillTag)
    {
        if (PillTag == K.TaserPill && !CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.KO))
        {
            PillTag = K.ConductedShockPill;
        }

        GlobalClientStickyShock(PillTag, false);
    }

    /// <summary>s_PlayerCharging: the shock without animation, then the end of the charge.</summary>
    [Uc("SBase.SPlayerController.s_PlayerCharging.ClientStickyShock", Sha1 = "17f5bd5a2ec86b148f009b91beeb313f8da6763c")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    public void ChargingClientStickyShock(int PillTag)
    {
        GlobalClientStickyShock(PillTag, false);
        if (!bFinDeChargeEnCours)
        {
            FinDeCharge();
        }
    }

    /// <summary>The co-op climber states: the global shock, then back to walking.</summary>
    [Uc("SBase.SPlayerController.s_Coop_S_Goto.ClientStickyShock", Sha1 = "b8d084616975ac42e3199e6cb1a451d68c5ad7d1")]
    [Uc("SBase.SPlayerController.s_Hoist_S_Jump.ClientStickyShock", Sha1 = "752aa1e37a975c425a252b6d350a0b01ff223347")]
    [Uc("SBase.SPlayerController.s_ShortLadder_S_Jump.ClientStickyShock", Sha1 = "a38d9568c35a383c80ea30e42f34533108a1a1bc")]
    [PortOf("uc:SBase.SPlayerController:s_Coop_S_Goto.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    [PortOf("uc:SBase.SPlayerController:s_Hoist_S_Jump.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Jump.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
    public void CoopClientStickyShock(int PillTag, bool bPlayAnim)
    {
        GlobalClientStickyShock(PillTag, bPlayAnim);
        GotoWaitingStateCombat();
    }

    /// <summary>Shock rumble for the freeze time.</summary>
    [Uc("SBase.SPlayerController.Vibration_StickyShock", Sha1 = "a739fe4b46bb370fc9af4dc23ea5032f4322c684")]
    [PortOf("uc:SBase.SPlayerController:Vibration_StickyShock", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
    public void Vibration_StickyShock(float Time) => CombatFx.Notify(this, CombatFx.Vibration_StickyShock, Time);

    // ---- jammer ----------------------------------------------------------------------------------------------

    /// <summary>The jammer on the owner (rule 30): a 4 s slow-down, the start action and message once.</summary>
    [Uc("SBase.SPlayerController.Client_Jammed", Sha1 = "788576dcf7b6224069637782a4dc48d48ce8fb8b")]
    [PortOf("uc:SBase.SPlayerController:Client_Jammed", Spec = "spec/combat/shock-and-taser.md#jammer-sdamage_jammered")]
    [PortOf("rep:SBase.SPlayerController.Client_Jammed", Spec = "spec/combat/shock-and-taser.md#network")]
    public void Client_Jammed(Pawn? instigatedBy)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_Client_Jammed) { Actor1 = instigatedBy });
            return;
        }

        fTimeToStopJammer = SvmMath.Trunc(Level.TimeSeconds + fJammerDuration);
        if (!bJammered && SBPawn is { } pawn)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_JAMMER_START, false);
            CombatFx.Notify(this, CombatFx.LocalMessage, K.JammerMessageTime);
        }

        bJammered = true;
    }

    // ---- the combat sections of PlayerTick ---------------------------------------------------------------

    /// <summary>
    /// The combat and equipment parts of SPlayerController.PlayerTick that the movement port's PlayerTick leaves out
    /// (it already runs the freeze end, the taser knock-down, the jammer and the heartbeat timer): the electric end
    /// and the rifle's electric clip, the HUD perturbation, the equipment input (gadget cycling, throw charge, spy
    /// energy, the cloak button) and the merc cough. The global PlayerTick calls it once per frame.
    /// </summary>
    [Uc("SBase.SPlayerController.PlayerTick", Sha1 = "5e6178b93ec98230e270a689b61159665cd12ee7")]
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/combat/shock-and-taser.md#shock-end-owner-playertick-every-frame")]
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public void CombatPlayerTick(float DeltaTime)
    {
        ElectricTick();
        HudPerturbationTick();
        EquipmentPlayerTick(DeltaTime);
        CoughTick();
    }

    /// <summary>
    /// The end of the electric state (rule 21) and the rifle's electric end clip, at the freeze end (rule 20) and at
    /// the electric end.
    /// </summary>
    private void ElectricTick()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        float now = Level.TimeSeconds;
        if (iDontUpdatePadInput == 0 && now > iTimeToStopStickyShock && pawn.MyWeapon is SWeaponDefense rifle &&
            rifle.IsInState(CNAME_s_Dmg_Electric))
        {
            rifle.GotoState(CNAME_s_Dmg_Electric_End);
        }

        if (pawn.bStickyShocking && now > iTimeElecStickyShock)
        {
            pawn.bStickyShocking = false;
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_DMG_ELECTRIC_STOP, true);
            EndRifleElectric(pawn);
        }
    }

    /// <summary>The rifle leaves its electric clip when the shock ends.</summary>
    private void EndRifleElectric(SBasePawn pawn)
    {
        if (pawn.MyWeapon is SWeaponDefense rifle && (rifle.IsInState(CNAME_s_Dmg_Electric) || rifle.IsInState(CNAME_s_Selected)))
        {
            rifle.GotoState(CNAME_s_Dmg_Electric_End);
        }
    }

    /// <summary>Ends a shock at once (s_Blast, s_FallDown and the knock-back states, s_Dead).</summary>
    public void EndStickyShockNow()
    {
        if (SBPawn is not { } pawn || iDontUpdatePadInput == 0)
        {
            return;
        }

        iDontUpdatePadInput = 0;
        pawn.bStickyShocking = false;
        CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_DMG_ELECTRIC_STOP, true);
        EndRifleElectric(pawn);
    }

    /// <summary>The merc cough and smoke knock-out (smoke spec rule 6).</summary>
    private void CoughTick()
    {
        if (!btoCought || SBPawn is not { } pawn || pawn.SPawnType != PawnType.SPAWNDEFENSE)
        {
            bInSmoke = false;
            return;
        }

        float now = Level.TimeSeconds;
        if (bInSmoke)
        {
            bPressedJump = false;
        }

        if (bInSmoke && now > fSmoke_FallDate && pawn.Physics != EPhysics.PHYS_RootMotion)
        {
            btoCought = false;
            CountGasKO();
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_COUGHT_STOP, false);
            if (pawn.Base is null)
            {
                eventChangeState(CNAME_s_FallDropAttacked);
            }
            else
            {
                eventChangeState(CNAME_s_FallDown, CNAME_DIE_InPlace);
            }
        }
        else if (now > fSmoke_EndOfCoughDate)
        {
            btoCought = false;
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_COUGHT_STOP, false);
        }
        else if (now > fSmoke_NextMovementCoughDate)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_COUGHT_START, false);
            fSmoke_NextMovementCoughDate = now + K.CoughPeriod;
            pawn.PlaySound(SndCough, ESoundSlot.SLOT_None, K.CoughVolume, false, K.CoughRadius);
        }

        bInSmoke = false;
    }

    // ---- smoke -------------------------------------------------------------------------------------------

    /// <summary>GoInSmoke through the current state (smoke and sticky cam gas, every tick inside).</summary>
    public void eventGoInSmoke(float tCough, float tFaint, float tHud, float blur)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_GoInSmoke) { Float1 = tCough, Vector1 = new FVector(tFaint, tHud, blur) });
            return;
        }

        if (StateNode is IGoInSmoke s)
        {
            s.GoInSmoke(this, tCough, tFaint, tHud, blur);
            return;
        }

        if (CombatDispatch.IgnoresSmoke(GetStateName()))
        {
            return;
        }

        GoInSmoke(tCough, tFaint, tHud, blur);
    }

    /// <summary>
    /// Inside a smoke or gas cloud (KO spec rule 29, smoke spec rule 5): a merc that did not just wake starts or keeps
    /// coughing, the faint date only moves earlier, the HUD blurs, and the server copy records the gasser.
    /// </summary>
    [Uc("SBase.SPlayerController.GoInSmoke", Sha1 = "8150e8a560a987d29bced2b298c9a39cce848447")]
    [PortOf("uc:SBase.SPlayerController:GoInSmoke", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public void GoInSmoke(float tCough, float tFaint, float tHud, float blur)
    {
        if (SBPawn is not { SPawnType: PawnType.SPAWNDEFENSE })
        {
            return;
        }

        SmokeCough(tCough, tFaint, tHud, blur);
    }

    /// <summary>The cough bookkeeping of GoInSmoke without the merc test (s_GrabbingPlayer uses it as is).</summary>
    public void SmokeCough(float tCough, float tFaint, float tHud, float blur)
    {
        float now = Level.TimeSeconds;
        if (!(now - fLastTimeWakeUp > K.SmokeWakeGrace))
        {
            return;
        }

        float newFall = now + tFaint;
        if (!btoCought)
        {
            fSmoke_FallDate = newFall;
            fSmoke_NextMovementCoughDate = now;
            btoCought = true;
        }
        else if (newFall < fSmoke_FallDate)
        {
            fSmoke_FallDate = newFall;
        }

        fSmoke_EndOfCoughDate = now + tCough;
        CombatFx.Notify(this, CombatFx.SmokeBlur, blur, tHud);
        bInSmoke = true;
        if (Role == ENetRole.ROLE_Authority && now - SmokeInstigatorTime > K.GasCreditRefresh)
        {
            FindGasInstigator();
        }
    }

    /// <summary>s_GrabbedByPlayer: smoke only blurs the HUD.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer.GoInSmoke", Sha1 = "5fe38c8c4efb1fd3c88a9ff5dad3c2220df6da2b")]
    [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.GoInSmoke", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public void GrabbedGoInSmoke(float tHud, float blur) => CombatFx.Notify(this, CombatFx.SmokeBlur, blur, tHud);

    /// <summary>
    /// The nearest smoke cloud within its effect radius gives the gas instigator, unless a shock was credited more
    /// recently (stats only).
    /// </summary>
    [Uc("SBase.SPlayerController.FindGasInstigator", Sha1 = "6694d54e72ab52aa290a7a450ec0d7c35bee203e")]
    [PortOf("uc:SBase.SPlayerController:FindGasInstigator", Spec = "spec/combat/knockout-and-terminate.md#smoke-and-gas-faint-mercs")]
    public void FindGasInstigator()
    {
        if (Pawn is not { } pawn)
        {
            return;
        }

        float now = Level.TimeSeconds;
        if (now - TimeShock < K.GasCreditRefresh)
        {
            return;
        }

        SSmokeEmitter? nearest = null;
        float best = float.MaxValue;
        foreach (SSmokeEmitter smoke in XLevel.AllActors<SSmokeEmitter>())
        {
            float d = FVector.Dist(smoke.Location, pawn.Location);
            if (d <= smoke.RadiusEffect + pawn.CollisionRadius && d < best)
            {
                best = d;
                nearest = smoke;
            }
        }

        if (nearest is not null)
        {
            SmokeInstigator = nearest.Instigator;
            SmokeInstigatorTime = now;
        }
    }

    /// <summary>The gas knock-out statistics of the victim and of the gasser (smoke or sticky cam).</summary>
    [Uc("SBase.SPlayerController.CountGasKO", Sha1 = "3080cebdaa11313c3f7d66c299df07bcc360d0a4")]
    [PortOf("uc:SBase.SPlayerController:CountGasKO", Spec = "spec/combat/knockout-and-terminate.md#smoke-and-gas-faint-mercs")]
    public void CountGasKO()
    {
        bool sticky = StickyCamInstigator is not null && StickyCamInstigatorTime >= SmokeInstigatorTime;
        Pawn? gasser = sticky ? StickyCamInstigator : SmokeInstigator;
        if (gasser is null)
        {
            return;
        }

        if (sticky)
        {
            IncStat(this, static s => s.Merc_KnockedFromStickyCam++);
            if (gasser.Controller is { } c)
            {
                IncStat(c, static s => s.Spy_StickyCamKOs++);
            }
        }
        else
        {
            IncStat(this, static s => s.Merc_KnockedFromSmoke++);
            if (gasser.Controller is { } c)
            {
                IncStat(c, static s => s.Spy_SmokeKOs++);
            }
        }

        SmokeInstigator = null;
        StickyCamInstigator = null;
    }

    /// <summary>
    /// The gas knock-out credit of PCChangeServerState (KO spec rule 31): when the server follows a client into
    /// s_FallDown at DIE_InPlace or into s_FallDropAttacked, the nearest cloud is the instigator. The game-flow port of
    /// PCChangeServerState calls it before following.
    /// </summary>
    [Uc("SBase.SPlayerController.PCChangeServerState", Sha1 = "cc31e893422013af3f50a4f84f08b33c96e05059")]
    [PortOf("uc:SBase.SPlayerController:PCChangeServerState", Spec = "spec/combat/knockout-and-terminate.md#smoke-and-gas-faint-mercs")]
    public void GasKnockOutCredit(Name nomdustate, Name nomduLabel)
    {
        bool knockDown = (nomdustate == CNAME_s_FallDown && nomduLabel == CNAME_DIE_InPlace) ||
            (nomdustate == CNAME_s_FallDropAttacked && !IsInState(CNAME_s_FallDropAttacked));
        if (!knockDown)
        {
            return;
        }

        if (Level.TimeSeconds - SmokeInstigatorTime > K.GasCreditRefresh)
        {
            FindGasInstigator();
        }

        CountGasKO();
    }

    // ---- small owner calls -------------------------------------------------------------------------------

    /// <summary>The dying push on the server (death spec rule 8): falling physics and the velocity at 1/100 scale.</summary>
    [Uc("SBase.SPlayerController.ServerSetVelocityForDyingPawn", Sha1 = "281e781023cf187b496c062fecc895ba4f1b3857")]
    [PortOf("uc:SBase.SPlayerController:ServerSetVelocityForDyingPawn", Spec = "spec/combat/death-and-respawn.md#rules")]
    public void ServerSetVelocityForDyingPawn(FVector V)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_ServerSetVelocityForDyingPawn) { Vector1 = V });
            return;
        }

        if (Pawn is { } pawn)
        {
            pawn.SetPhysics(EPhysics.PHYS_Falling);
            pawn.Velocity = V * (1f / K.BlastVelocityWireScale);
        }
    }

    /// <summary>An SGameMessages message for this player only.</summary>
    public void ReceiveCombatMessage(int Switch, PlayerReplicationInfo? RelatedPRI_1 = null, PlayerReplicationInfo? RelatedPRI_2 = null) =>
        GameHost.ClientCall(this, new GameCall(CNAME_ReceiveLocalizedMessage)
        {
            Class = SBasePawn.GameMessagesClass,
            Int = Switch,
            Pri1 = RelatedPRI_1,
            Pri2 = RelatedPRI_2,
        });

    /// <summary>ClientPlaySound for this player.</summary>
    public void ClientPlaySoundCombat(AssetRef Sound) =>
        GameHost.ClientCall(this, new GameCall(CNAME_ClientPlaySound) { Asset = Sound });

    // ---- HUD perturbation -------------------------------------------------------------------------------

    /// <summary>
    /// SHud.SetHudPerturbationLength (native): the HUD is perturbed for <paramref name="Length"/> seconds; while it
    /// lasts reload, full reload, goggle switching and the scope are refused.
    /// </summary>
    [UcNative("ASHud::execSetHudPerturbationLength")]
    [Uc("SBase.SHud.SetHudPerturbationLength", Sha1 = "8e9837b6ae09e7892167b82e504f65ffd00e9729")]
    [PortOf("nat:SBase.SHud.SetHudPerturbationLength", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SHud.SetHudPerturbationLength game 0x10a92880: P = owner's pawn; merc pawns only (P.SPawnType == SPAWNDEFENSE); not perturbed: start now, length L; perturbed: start kept, length (now - start) + L")]
    public void SetHudPerturbationLength(float Length)
    {
        if (myHUD is not SHud hud)
        {
            return;
        }

        hud.P = Pawn as SBasePawn;
        if (hud.P is not { SPawnType: PawnType.SPAWNDEFENSE })
        {
            // Spies are never perturbed: the native does nothing, so their reload, goggles and camera stay usable.
            return;
        }

        float now = Level.TimeSeconds;
        if (!hud.bPerturbation)
        {
            hud.HudPerturbationLength = Length;
            hud.TimetobeginPerturb = now;
            hud.bPerturbation = true;
        }
        else
        {
            hud.HudPerturbationLength = now - hud.TimetobeginPerturb + Length;
        }

        CombatFx.Notify(this, CombatFx.SetHudPerturbationLength, Length);
    }

    /// <summary>The HUD perturbation ends when its length has passed (SHud.StopHudPerturbation).</summary>
    private void HudPerturbationTick()
    {
        if (myHUD is SHud { bPerturbation: true } hud && Level.TimeSeconds >= hud.TimetobeginPerturb + hud.HudPerturbationLength)
        {
            hud.bPerturbation = false;
        }
    }

    /// <summary>True while the HUD is perturbed (shock, chaff).</summary>
    public bool IsHudPerturbed => myHUD is { bPerturbation: true };

    /// <summary>The merc head-shot HUD shutdown (sticky-shock-effect.md rule 3.11); spies are not affected.</summary>
    public void HudShutDown(float CloseTime, float Length)
    {
        if (SPawnType != PawnType.SPAWNDEFENSE)
        {
            return;
        }

        if (myHUD is SHud hud)
        {
            hud.HudShutDownLength = Length;
            hud.TimeToBeginShutDown = Level.TimeSeconds;
            hud.TimeToCloseShut = CloseTime;
        }

        CombatFx.Notify(this, CombatFx.HudShutDown, CloseTime, Length);
    }
}
