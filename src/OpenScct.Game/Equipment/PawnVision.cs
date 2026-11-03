using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The SBasePawn functions of the vision, EMF and weapon-loaded rules (spec/equipment/vision-goggles.md,
/// spy-shocker.md): the goggle clip notifies, EMF visibility and its server calls, the weapon-loaded byte, the cloak
/// target. They are SBasePawn units kept with the equipment port.
/// </summary>
public static class PawnVision
{
    private sealed class GoggleVisualState
    {
        public int RequestedMode;
        public int ActiveMode;
    }

    // Dedicated servers have no beam actor, but still execute the replicated goggle actions and clip notifies.
    // Keep that visual state independently so snapshot observers and late joiners can recover the active colour.
    private static readonly ConditionalWeakTable<SBasePawn, GoggleVisualState> GoggleVisuals = new();

    /// <summary>The committed merc lens colour: 0 normal, 1 motion tracking, 2 EMF.</summary>
    public static byte MercGlowMode(SBasePawn pawn) =>
        pawn.SPawnType == PawnType.SPAWNDEFENSE && GoggleVisuals.TryGetValue(pawn, out GoggleVisualState? state)
            ? state.ActiveMode switch { K.BeamModeMotion => (byte)1, K.BeamModeEmf => (byte)2, _ => (byte)0 }
            : (byte)0;

    /// <summary>Applies a sampled observer colour without replaying goggle actions or local vision changes.</summary>
    public static void RestoreMercGlow(SBasePawn pawn, byte mode)
    {
        if (pawn.SPawnType != PawnType.SPAWNDEFENSE || pawn.GoggleBeam is not { } beam) return;
        int beamMode = mode switch { 1 => K.BeamModeMotion, 2 => K.BeamModeEmf, _ => K.BeamModeNormal };
        if (beam.CurrentMode == beamMode && !beam.Skin.IsNone) return;
        beam.CurrentMode = beamMode;
        beam.ChangeSkin();
    }

    private static void ApplyGoggleSkin(SBasePawn pawn)
    {
        GoggleVisualState state = GoggleVisuals.GetValue(pawn, static _ => new());
        state.ActiveMode = state.RequestedMode;
        if (pawn.GoggleBeam is { } beam)
        {
            beam.CurrentMode = state.ActiveMode;
            beam.ChangeSkin();
        }
    }

    private static readonly Name CNAME_SetEMFToServer = new("SetEMFToServer");
    private static readonly Name CNAME_UnSetEMFToServer = new("UnSetEMFToServer");

    /// <summary>The goggle clip's lower notify: goggles down (only while they are up).</summary>
    [Uc("SBase.SBasePawn.AnimEvent_GoggleSwitchDown", Sha1 = "7e26ed792e62542161ca75949308852abf043733")]
    [PortOf("uc:SBase.SBasePawn:AnimEvent_GoggleSwitchDown", Spec = "spec/equipment/vision-goggles.md#rules")]
    [PortOf("uc:SBase.SBasePawn:AnimEvent_GoggleSwitchDown", Spec = "spec/equipment/goggle-glow.md#rules")]
    public static void AnimEvent_GoggleSwitchDown(SBasePawn Pawn)
    {
        if (!Pawn.bGogglesUp)
        {
            return;
        }

        // The script also turns the spy's BGoggles bone (K.SpyGogglesBone); the Versus meshes have no such bone.
        Pawn.LastZipP = Movement.MovementK.FarZipPoint;
        if (Pawn.Controller is SPlayerController { Goggle: { } goggle })
        {
            goggle.eventDown();
        }

        ApplyGoggleSkin(Pawn);
    }

    /// <summary>The goggle clip's raise notify: goggles up (only while they are down).</summary>
    [Uc("SBase.SBasePawn.AnimEvent_GoggleSwitchUp", Sha1 = "faac69948f93bf4f4acca59df685b94275c01825")]
    [PortOf("uc:SBase.SBasePawn:AnimEvent_GoggleSwitchUp", Spec = "spec/equipment/vision-goggles.md#rules")]
    [PortOf("uc:SBase.SBasePawn:AnimEvent_GoggleSwitchUp", Spec = "spec/equipment/goggle-glow.md#rules")]
    public static void AnimEvent_GoggleSwitchUp(SBasePawn Pawn)
    {
        if (Pawn.bGogglesUp)
        {
            return;
        }

        Pawn.LastZipP = Movement.MovementK.FarZipPoint;
        if (Pawn.Controller is SPlayerController { Goggle: { } goggle })
        {
            goggle.eventUp();
        }

        ApplyGoggleSkin(Pawn);
    }

    private static readonly Name GoglStAlDn2 = new("GoglStAlDn2");
    private static readonly Name GoglStSpDn2 = new("GoglStSpDn2");
    private static readonly Name BoneHead = new("B Head");

    /// <summary>The notify the goggle clip raises where the goggles reach the eyes.</summary>
    public static readonly Name NotifyGoggleSwitchDown = new("AnimEvent_GoggleSwitchDown");

    /// <summary>The notify the goggle clip raises where the goggles leave the eyes.</summary>
    public static readonly Name NotifyGoggleSwitchUp = new("AnimEvent_GoggleSwitchUp");

    [Provenance(ProvenanceKind.Decoded, "execute-action.md goggle head tail (71, 72): to c18 1/0.3")]
    private const float GoggleBlendIn = 0.3f;

    /// <summary>
    /// SBasePawn.ExecuteAction for the goggle actions (execute-action.md cases 66 to 72 and 269, rule 6 of
    /// vision-goggles.md): 71 lowers and 72 raises the goggles with the same head clip on the goggle channel (the spy
    /// with a loaded weapon has its own), from B Head, blending in over 0.3 s; its notifies call
    /// <see cref="AnimEvent_GoggleSwitchDown"/> and <see cref="AnimEvent_GoggleSwitchUp"/>. The vision actions only
    /// raise <see cref="ExecuteActionForGoggle"/> for the goggle beam (72 before its clip). False for any other
    /// action.
    /// </summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/equipment/vision-goggles.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 66 to 72, 269: c18 GoglStAlDn2 (GoglStSpDn2 for a spy with a loaded weapon); blend c18 0 @B Head; to c18 1/0.3")]
    public static bool ExecuteGoggleAction(SBasePawn Pawn, int Action)
    {
        switch (Action)
        {
            case SBasePawn.SA_GOG_HEADMVT_ON:
            case SBasePawn.SA_GOG_HEADMVT_OFF:
                if (Action == SBasePawn.SA_GOG_HEADMVT_OFF)
                {
                    ExecuteActionForGoggle(Pawn, Action);
                }

                Name clip = Pawn.SPawnType != PawnType.SPAWNDEFENSE && Pawn.WeaponLoaded != 0 ? GoglStSpDn2 : GoglStAlDn2;
                Movement.PawnActions.Once(Pawn, SBasePawn.GOGGLECHANNEL, clip, 0f);
                Pawn.PawnAnim.SetBlendParams(SBasePawn.GOGGLECHANNEL, 0f, bone: BoneHead);
                Pawn.PawnAnim.BlendToAlpha(SBasePawn.GOGGLECHANNEL, 1f, GoggleBlendIn);
                return true;
            case SBasePawn.SA_VISION_EMF:
            case SBasePawn.SA_VISION_NIGHT:
            case SBasePawn.SA_VISION_THERMAL:
            case SBasePawn.SA_VISION_MOTION:
            case SBasePawn.SA_GOG_SWITCH_WITHOUTANIM:
            case SBasePawn.SA_GOG_DYNNOANIM:
                ExecuteActionForGoggle(Pawn, Action);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// ExecuteActionForGoggle (goggle-glow.md rule 3): the vision actions set the beam's mode (68 thermal 1, 67 night
    /// 2, 69 motion 3, 66 EMF 4); 72 sets 5 for a merc and 0 for a spy, 269 the same and the skin at once; 70 puts on
    /// the skin of the mode already set. The active mode is retained even on a dedicated server without a beam.
    /// </summary>
    [Uc("SBase.SBasePawn.ExecuteActionForGoggle", Sha1 = "c5523524219ff4e55a9ad7e13d0385dfcbe7ba28")]
    [PortOf("uc:SBase.SBasePawn:ExecuteActionForGoggle", Spec = "spec/equipment/goggle-glow.md#rules")]
    public static void ExecuteActionForGoggle(SBasePawn Pawn, int Action)
    {
        GoggleVisualState state = GoggleVisuals.GetValue(Pawn, static _ => new());
        int rest = Pawn.SPawnType == PawnType.SPAWNDEFENSE ? K.BeamModeNormal : K.BeamModeNone;
        switch (Action)
        {
            case SBasePawn.SA_GOG_DYNNOANIM:
                state.RequestedMode = rest;
                ApplyGoggleSkin(Pawn);
                break;
            case SBasePawn.SA_GOG_SWITCH_WITHOUTANIM:
                ApplyGoggleSkin(Pawn);
                break;
            case SBasePawn.SA_VISION_THERMAL:
                state.RequestedMode = K.BeamModeThermal;
                break;
            case SBasePawn.SA_VISION_NIGHT:
                state.RequestedMode = K.BeamModeNight;
                break;
            case SBasePawn.SA_VISION_EMF:
                state.RequestedMode = K.BeamModeEmf;
                break;
            case SBasePawn.SA_VISION_MOTION:
                state.RequestedMode = K.BeamModeMotion;
                break;
            case SBasePawn.SA_GOG_HEADMVT_OFF:
                state.RequestedMode = rest;
                break;
        }
        if (Pawn.GoggleBeam is { } beam) beam.CurrentMode = state.RequestedMode;
    }

    private static readonly ClassRef SpyBeamClass = new("SBase.SGoggleBeam_ATT");
    private static readonly ClassRef MercBeamClass = new("SBase.SGoggleBeam_DEF");

    /// <summary>
    /// The beam spawn of SPawnAttaque.PostBeginPlay and SPAWNDEFENSE.PostBeginPlay (goggle-glow.md rule 1): a spy's
    /// wherever its RemoteRole is not autonomous, a merc's on clients, standalone and listen servers; attached to B Head
    /// with the side's relative rotation. Called before the parent PostBeginPlay.
    /// </summary>
    [PortOf("uc:SBase.SPawnAttaque:PostBeginPlay", Spec = "spec/equipment/goggle-glow.md#rules")]
    [PortOf("uc:SBase.SPAWNDEFENSE:PostBeginPlay", Spec = "spec/equipment/goggle-glow.md#rules")]
    public static void SpawnGoggleBeam(SBasePawn Pawn)
    {
        bool merc = Pawn is SPAWNDEFENSE;
        bool wanted = merc
            ? Pawn.Role < ENetRole.ROLE_Authority || Pawn.Level.NetMode is ENetMode.NM_Standalone or ENetMode.NM_ListenServer
            : Pawn.RemoteRole != ENetRole.ROLE_AutonomousProxy;
        if (!wanted)
        {
            return;
        }

        Actor? spawned = Devices.DeviceClasses.Spawn(Pawn, merc ? MercBeamClass : SpyBeamClass, Pawn, default, Pawn.Location, Pawn.Rotation);
        Pawn.GoggleBeam = spawned is SGoggleBeam { bDeleteMe: false } beam ? beam : null;
        if (Pawn.GoggleBeam is not { } attached)
        {
            return;
        }

        attached.SetBase(Pawn);
        attached.AttachmentBone = K.GoggleBeamBone;
        attached.RelativeRotation = merc ? K.MercGoggleBeamRotation : K.SpyGoggleBeamRotation;
    }

    /// <summary>SBasePawn.Destroyed's beam part (goggle-glow.md rule 6): the beam loses its owner and goes.</summary>
    [PortOf("uc:SBase.SBasePawn:Destroyed", Spec = "spec/equipment/goggle-glow.md#rules")]
    public static void DestroyGoggleBeam(SBasePawn Pawn)
    {
        if (Pawn.GoggleBeam is { } beam)
        {
            beam.SetOwner(null);
            beam.Destroy();
        }

        Pawn.GoggleBeam = null;
    }

    /// <summary>The goggle clip's script notifies; false for any other notify.</summary>
    public static bool RunGoggleNotify(SBasePawn Pawn, Name NotifyName)
    {
        if (NotifyName == NotifyGoggleSwitchDown)
        {
            AnimEvent_GoggleSwitchDown(Pawn);
            return true;
        }

        if (NotifyName == NotifyGoggleSwitchUp)
        {
            AnimEvent_GoggleSwitchUp(Pawn);
            return true;
        }

        return false;
    }

    /// <summary>
    /// TestAndSetEMFVisible (rule 12, on the controlling machine): a spy shows in EMF vision while any of weapon
    /// drawn, a vision mode on, binoculars, a spy mark, an active or fading cloak, phospho; mercs always.
    /// </summary>
    [Uc("SBase.SBasePawn.TestAndSetEMFVisible", Sha1 = "26573b4e27f62d552000faefafc18c1cc4b10fc8")]
    [PortOf("uc:SBase.SBasePawn:TestAndSetEMFVisible", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void TestAndSetEMFVisible(SBasePawn Pawn)
    {
        bool visible = Pawn.SPawnType == PawnType.SPAWNDEFENSE || IsSpyEmfVisible(Pawn);
        if (visible)
        {
            // Keep the owning client's presentation in sync with the condition it sends to the server.
            SetEMF(Pawn);
            SetEMFToServer(Pawn);
        }
        else
        {
            UnSetEMF(Pawn);
            UnSetEMFToServer(Pawn);
        }
    }

    /// <summary>The spy's EMF visibility conditions.</summary>
    public static bool IsSpyEmfVisible(SBasePawn Pawn)
    {
        // The script reads the beam's mode (0 or 5: no vision mode); a pawn without a beam falls back on its goggles.
        bool vision = Pawn.GoggleBeam is { } beam
            ? beam.CurrentMode != K.BeamModeNone && beam.CurrentMode != K.BeamModeNormal
            : ((Pawn.Controller as SPlayerController)?.Goggle?.CurrentMode ?? SGoggle.REN_DynLight) != SGoggle.REN_DynLight;
        bool fading = Pawn.SlopeGlassEffect < 0f &&
            Pawn.Level.TimeSeconds - Pawn.PerturbationStartTime < Pawn.PerturbationDuration;
        return Pawn.WeaponLoaded != 0 || vision || Pawn.bBinocularsOn || Pawn.bSpyBulletOn ||
            Pawn.DesiredGlassEffect != 0 || Pawn.GlassEffect != 0 || fading || IsPhosphoSkinned(Pawn);
    }

    /// <summary>The phospho skin is on: PhosphoSkinTime (its end time) is set and not reached.</summary>
    public static bool IsPhosphoSkinned(SBasePawn Pawn) =>
        Pawn.PhosphoSkinTime > 0f && Pawn.Level.TimeSeconds < Pawn.PhosphoSkinTime;

    /// <summary>The server makes the pawn EMF-visible (sent by the controlling client).</summary>
    [Uc("SBase.SBasePawn.SetEMFToServer", Sha1 = "0da87f2090280e03813fff1b8edbf2fdbfb1ddd6")]
    [PortOf("uc:SBase.SBasePawn:SetEMFToServer", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void SetEMFToServer(SBasePawn Pawn)
    {
        // A server function: a client sends it for a pawn it controls and drops it for any other.
        if (Pawn.Role < ENetRole.ROLE_Authority)
        {
            if (Pawn.Controller is PlayerController pc)
            {
                CombatServices.Net(Pawn).ServerCall(pc, new CombatCall(CNAME_SetEMFToServer) { Target = Pawn });
            }

            return;
        }

        SetEMF(Pawn);
    }

    /// <summary>The server clears the pawn's EMF visibility (sent by the controlling client).</summary>
    [Uc("SBase.SBasePawn.UnSetEMFToServer", Sha1 = "4f5d3227dedf57ac22bd7becf90ce7a6e94f6aa8")]
    [PortOf("uc:SBase.SBasePawn:UnSetEMFToServer", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void UnSetEMFToServer(SBasePawn Pawn)
    {
        if (Pawn.Role < ENetRole.ROLE_Authority)
        {
            if (Pawn.Controller is PlayerController pc)
            {
                CombatServices.Net(Pawn).ServerCall(pc, new CombatCall(CNAME_UnSetEMFToServer) { Target = Pawn });
            }

            return;
        }

        UnSetEMF(Pawn);
    }

    /// <summary>EMF visible: bEMFIsOn and full opacity, the weapon too (so its replicated values follow).</summary>
    [Uc("SBase.SBasePawn.SetEMF", Sha1 = "805216bac7fa2939a4debc9e866ed9530a822411")]
    [PortOf("uc:SBase.SBasePawn:SetEMF", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void SetEMF(SBasePawn Pawn)
    {
        Pawn.bEMFIsOn = true;
        Pawn.EMFOpacity = 1f;
        Pawn.MyWeapon?.SetEMFWeapon();
    }

    /// <summary>EMF invisible: bEMFIsOn off and no opacity; the weapon back to its undrawn 0.3.</summary>
    [Uc("SBase.SBasePawn.UnSetEMF", Sha1 = "9b55be2aacbd25625839c92e60c17c3ff33a0620")]
    [PortOf("uc:SBase.SBasePawn:UnSetEMF", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void UnSetEMF(SBasePawn Pawn)
    {
        Pawn.bEMFIsOn = false;
        Pawn.EMFOpacity = 0f;
        Pawn.MyWeapon?.UnSetEMFWeapon();
    }

    /// <summary>
    /// SetWeaponLoaded (spy-shocker.md rule 4): the weapon-loaded byte; drawing on the controlling machine also
    /// uncloaks over 2 s and makes the spy EMF-visible, holstering re-tests EMF visibility.
    /// </summary>
    [Uc("SBase.SBasePawn.SetWeaponLoaded", Sha1 = "ef4b18102fb2394da785e6502709757f9b389982")]
    [PortOf("uc:SBase.SBasePawn:SetWeaponLoaded", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static void SetWeaponLoaded(SBasePawn Pawn, int num)
    {
        Pawn.WeaponLoaded = (byte)num;
        if (Pawn.Controller is not SPlayerController pc || !pc.IAmOnClient())
        {
            return;
        }

        if (num > 0)
        {
            SetDesiredGlassValue(Pawn, 0, K.DrawUncloakTime);
            SetEMFToServer(Pawn);
        }
        else
        {
            TestAndSetEMFVisible(Pawn);
        }
    }

    /// <summary>The native-callable SetWeaponLoaded.</summary>
    [Uc("SBase.SBasePawn.EvSetWeaponLoaded", Sha1 = "61c81e410a774d6e53865a44abb13dcd9467df58")]
    [PortOf("uc:SBase.SBasePawn:EvSetWeaponLoaded", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static void EvSetWeaponLoaded(SBasePawn Pawn, int num) => SetWeaponLoaded(Pawn, num);

    /// <summary>The cloak's in and out sound.</summary>
    public static readonly AssetRef SndCapeInOut = new("Armes_impacts.Gadgets.Cape_InOut");

    /// <summary>The cloak's perturbation sound.</summary>
    public static readonly AssetRef SndCapeDamage = new("Armes_impacts.Gadgets.RA_Cape_damage");

    private static readonly Name CNAME_SetDesiredGlassValue = new("SetDesiredGlassValue");
    private static readonly Name CNAME_SetDesiredGlassValueOnClients = new("SetDesiredGlassValueOnClients");
    private static readonly Name CNAME_SetPerturbationGlass = new("SetPerturbationGlass");
    private static readonly Name CNAME_SetPerturbationGlassOnClients = new("SetPerturbationGlassOnClients");
    private sealed class GlassPrediction
    {
        public int NextRequestId = 1;
        public readonly Dictionary<int, (byte Target, float Duration)> Pending = new();
    }

    private static readonly ConditionalWeakTable<SBasePawn, GlassPrediction> PredictedGlassTargets = new();

    /// <summary>
    /// SetDesiredGlassValue (spy-energy.md rule 7): a new cloak target (the same target does nothing), reached
    /// linearly over Duration (0: PerturbationDefaultValue) from the current amount, with a perturbation of the same
    /// length, then the broadcast to every client. The owning client predicts its visual transition while the
    /// client-to-server request (rep-pawn.md block 7) is in flight; the server still broadcasts the accepted target.
    /// </summary>
    [Uc("SBase.SBasePawn.SetDesiredGlassValue", Sha1 = "26c096360b43a5ce60f9c760e77dc684c3b52b44")]
    [PortOf("uc:SBase.SBasePawn:SetDesiredGlassValue", Spec = "spec/equipment/spy-energy.md#rules")]
    [PortOf("rep:SBase.SBasePawn.SetDesiredGlassValue", Spec = "spec/net/rep-pawn.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.uc SetDesiredGlassValue: early return on the same target, slope (Desired - Glass) / Duration, B = Glass - now * slope, SetPerturbationGlass(Duration), then SetDesiredGlassValueOnClients")]
    public static void SetDesiredGlassValue(SBasePawn Pawn, byte DesiredValue, float Duration = 0f, int RequestId = 0)
    {
        if (DesiredValue == Pawn.DesiredGlassEffect)
        {
            // A server-forced change may already have reached this target. Still acknowledge the owner's
            // prediction, without restarting the transition for every other observer.
            if (RequestId != 0 && Pawn.Role >= ENetRole.ROLE_Authority)
            {
                GadgetNet.ToOwner(Pawn.Controller as PlayerController,
                    new CombatCall(CNAME_SetDesiredGlassValueOnClients)
                    { Target = Pawn, Int1 = DesiredValue, Int2 = RequestId, Float1 = Duration, Bool1 = true });
            }
            return;
        }

        if (Pawn.Role < ENetRole.ROLE_Authority && Pawn.Controller is PlayerController)
        {
            GlassPrediction prediction = PredictedGlassTargets.GetOrCreateValue(Pawn);
            RequestId = prediction.NextRequestId++;
            if (prediction.NextRequestId <= 0) prediction.NextRequestId = 1;
            prediction.Pending.Add(RequestId, (DesiredValue, Duration == 0f ? Pawn.PerturbationDefaultValue : Duration));
        }
        if (GadgetNet.ToServer(Pawn, Pawn.Controller as PlayerController,
            new CombatCall(CNAME_SetDesiredGlassValue) { Target = Pawn, Int1 = DesiredValue, Int2 = RequestId, Float1 = Duration }))
        {
            // Begin the visual transition on the input frame rather than waiting for a server round-trip.
            // The request ID identifies both echoed effects, so damage flicker remains independent.
            BeginGlassTransition(Pawn, DesiredValue, Duration);
            ClientPerturbationGlass(Pawn, Duration);
            return;
        }

        if (Duration == 0f)
        {
            Duration = Pawn.PerturbationDefaultValue;
        }

        SetGlassRamp(Pawn, DesiredValue, Duration);
        SetPerturbationGlass(Pawn, Duration, RequestId: RequestId);
        SetDesiredGlassValueOnClients(Pawn, DesiredValue, Duration, RequestId);
    }

    /// <summary>
    /// SetDesiredGlassValueOnClients, a server function on the broadcast flag (rep-pawn.md block 6): every client that
    /// has the pawn runs <see cref="ClientDesiredGlassValue"/>, and so does a listen server or a standalone game.
    /// </summary>
    [PortOf("rep:SBase.SBasePawn.SetDesiredGlassValueOnClients", Spec = "spec/net/rep-pawn.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "A dedicated server also takes the ramp (without the sound): the original skips the call there, so after a damage decloak (StandardReactionsToDamage) the server's target stays 255 and its same-target test refuses the next cloak")]
    public static void SetDesiredGlassValueOnClients(SBasePawn Pawn, byte DesiredValue, float Duration = 0f, int RequestId = 0)
    {
        if (GadgetNet.ToAllClients(Pawn,
            new CombatCall(CNAME_SetDesiredGlassValueOnClients) { Target = Pawn, Int1 = DesiredValue, Int2 = RequestId, Float1 = Duration }))
        {
            ClientDesiredGlassValue(Pawn, DesiredValue, Duration, RequestId);
        }
        else if (Pawn.Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            SetGlassRamp(Pawn, DesiredValue, Duration == 0f ? Pawn.PerturbationDefaultValue : Duration);
        }
    }

    /// <summary>
    /// The body of SetDesiredGlassValueOnClients where it runs: the same ramp (no same-target test), the in/out sound
    /// and, on the controlling machine, the EMF visibility test.
    /// </summary>
    [Uc("SBase.SBasePawn.SetDesiredGlassValueOnClients", Sha1 = "6f9caefca439d887dad4c850d3551d934ec649ba")]
    [PortOf("uc:SBase.SBasePawn:SetDesiredGlassValueOnClients", Spec = "spec/equipment/spy-energy.md#rules")]
    public static void ClientDesiredGlassValue(SBasePawn Pawn, byte DesiredValue, float Duration = 0f, int RequestId = 0,
        bool AcknowledgementOnly = false)
    {
        if (AcknowledgementOnly)
        {
            // The server was already at this target. A forced transition may have superseded the prediction
            // while its request was in flight, so even an unknown acknowledgement must not replay that request.
            if (PredictedGlassTargets.TryGetValue(Pawn, out GlassPrediction? acknowledged))
                acknowledged.Pending.Remove(RequestId);
            return;
        }
        if (PredictedGlassTargets.TryGetValue(Pawn, out GlassPrediction? prediction))
        {
            if (RequestId != 0 && prediction.Pending.Remove(RequestId, out var expected) &&
                expected.Target == DesiredValue &&
                expected.Duration == (Duration == 0f ? Pawn.PerturbationDefaultValue : Duration))
            {
                return;
            }
            // A server-forced state supersedes the outstanding local transitions.
            prediction.Pending.Clear();
        }

        BeginGlassTransition(Pawn, DesiredValue, Duration);
    }

    private static void BeginGlassTransition(SBasePawn Pawn, byte DesiredValue, float Duration)
    {
        if (Duration == 0f)
        {
            Duration = Pawn.PerturbationDefaultValue;
        }

        SetGlassRamp(Pawn, DesiredValue, Duration);
        Pawn.PlaySound(SndCapeInOut, ESoundSlot.SLOT_None, 1f, false, K.CloakSoundRadius);
        if (Pawn.Controller is SPlayerController pc && pc.IAmOnClient())
        {
            TestAndSetEMFVisible(Pawn);
        }
    }

    /// <summary>Silently restores the cloak when an observer first receives this pawn's channel.</summary>
    public static void ClientInitialGlass(SBasePawn Pawn, byte Amount, byte Target, float FadeRemaining,
        float FlickerRemaining, float FlickerFrequency)
    {
        // The owner already predicts its own transitions; the initial observer state must not replace them.
        if (Pawn.Role == ENetRole.ROLE_AutonomousProxy) return;
        Pawn.GlassEffect = Amount;
        SetGlassRamp(Pawn, Target, FadeRemaining);
        Pawn.PerturbationStartTime = Pawn.Level.TimeSeconds;
        Pawn.PerturbationDuration = System.MathF.Max(0f, FlickerRemaining);
        Pawn.PerturbationValue = FlickerFrequency;
    }

    /// <summary>
    /// SetPerturbationGlass: unless the ramp base is 0, a flicker window of Duration (0: PerturbationDefaultValue)
    /// at frequency Value (0: 12.8) from now, then the broadcast (the same window and the perturbation sound on every
    /// client). A client-to-server function, like SetDesiredGlassValue.
    /// </summary>
    [Uc("SBase.SBasePawn.SetPerturbationGlass", Sha1 = "abd42c0b6ab3aabdb61ad2024b4f2871f2e429fc")]
    [PortOf("uc:SBase.SBasePawn:SetPerturbationGlass", Spec = "spec/equipment/spy-energy.md#rules")]
    [PortOf("rep:SBase.SBasePawn.SetPerturbationGlass", Spec = "spec/net/rep-pawn.md#rules")]
    public static void SetPerturbationGlass(SBasePawn Pawn, float Duration = 0f, float Value = 0f, int RequestId = 0)
    {
        if (GadgetNet.ToServer(Pawn, Pawn.Controller as PlayerController,
            new CombatCall(CNAME_SetPerturbationGlass) { Target = Pawn, Int2 = RequestId, Float1 = Duration, Vector1 = new FVector(Value, 0f, 0f) }))
        {
            return;
        }

        if (SetPerturbationWindow(Pawn, Duration, Value))
        {
            SetPerturbationGlassOnClients(Pawn, Duration, RequestId: RequestId);
        }
    }

    /// <summary>SetPerturbationGlassOnClients, broadcast like <see cref="SetDesiredGlassValueOnClients"/>.</summary>
    [PortOf("rep:SBase.SBasePawn.SetPerturbationGlassOnClients", Spec = "spec/net/rep-pawn.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "A dedicated server also takes the flicker window (without the sound), as SetDesiredGlassValueOnClients")]
    public static void SetPerturbationGlassOnClients(SBasePawn Pawn, float Duration = 0f, float Value = 0f, int RequestId = 0)
    {
        if (GadgetNet.ToAllClients(Pawn,
            new CombatCall(CNAME_SetPerturbationGlassOnClients) { Target = Pawn, Int2 = RequestId, Float1 = Duration, Vector1 = new FVector(Value, 0f, 0f) }))
        {
            ClientPerturbationGlass(Pawn, Duration, Value, RequestId);
        }
        else if (Pawn.Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            SetPerturbationWindow(Pawn, Duration, Value);
        }
    }

    /// <summary>The body of SetPerturbationGlassOnClients where it runs: the flicker window, with the perturbation sound.</summary>
    [Uc("SBase.SBasePawn.SetPerturbationGlassOnClients", Sha1 = "358379cbcee7a574fbdc532ca8fb7e17ec1bacd6")]
    [PortOf("uc:SBase.SBasePawn:SetPerturbationGlassOnClients", Spec = "spec/equipment/spy-energy.md#rules")]
    public static void ClientPerturbationGlass(SBasePawn Pawn, float Duration = 0f, float Value = 0f, int RequestId = 0)
    {
        if (RequestId != 0 && PredictedGlassTargets.TryGetValue(Pawn, out GlassPrediction? prediction) &&
            prediction.Pending.ContainsKey(RequestId)) return;

        if (SetPerturbationWindow(Pawn, Duration, Value))
        {
            Pawn.PlaySound(SndCapeDamage, ESoundSlot.SLOT_None, 1f, false, K.CloakSoundRadius);
        }
    }

    private static bool SetPerturbationWindow(SBasePawn Pawn, float Duration, float Value)
    {
        if (Pawn.BGlassEffect == 0f)
        {
            return false;
        }

        Pawn.PerturbationStartTime = Pawn.Level.TimeSeconds;
        Pawn.PerturbationDuration = Duration == 0f ? Pawn.PerturbationDefaultValue : Duration;
        Pawn.PerturbationValue = Value == 0f ? K.GlassPerturbationValue : Value;
        return true;
    }

    /// <summary>The ramp: the target, a signed slope per second over Duration and its base at time 0.</summary>
    private static void SetGlassRamp(SBasePawn Pawn, byte DesiredValue, float Duration)
    {
        Pawn.DesiredGlassEffect = DesiredValue;
        if (!(Duration > 0f))
        {
            // The script divides by the duration; a zero default (never in the class defaults) is an instant change.
            Pawn.SlopeGlassEffect = 0f;
            Pawn.BGlassEffect = DesiredValue;
            Pawn.GlassEffect = DesiredValue;
            return;
        }

        Pawn.SlopeGlassEffect = (DesiredValue - Pawn.GlassEffect) / Duration;
        Pawn.BGlassEffect = Pawn.GlassEffect - (Pawn.Level.TimeSeconds * Pawn.SlopeGlassEffect);
    }

    /// <summary>
    /// The spy's screen glass tick (SPawnAttaque.TickNative, Reloaded's body): on the controlling machine a cloak
    /// target is dropped (SetDesiredGlassValue(0, 1), then the EMF test) in a rain volume, while sticky shocked, or
    /// above 240 uu/s outside root motion; the amount follows the ramp at now + 0.1 until it passes the target or
    /// leaves [0, 255]; during a perturbation, while cos(PerturbationValue x now) > 0, it jitters by a random amount
    /// whose range shrinks from 510 to 10 over the window, kept inside the byte, a jittered value equal to the
    /// target drawn as 125. Runs in every machine's pawn tick, so each machine draws the amount it ramps itself.
    /// </summary>
    [UcNative("ASPawnAttaque::execTickNative", Exe = 0x10af5cf0)]
    [PortOf("rl:SBase.SPawnAttaque.TickNative", Spec = "spec/native/reloaded-hooks.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "TickNative game 0x10af5cf0 and Reloaded body 0x100caae0 (events SetDesiredGlassValue 0xbfcf1 and TestAndSetEMFVisible 0xbfced)")]
    [Provenance(ProvenanceKind.Estimated, "Reloaded's speed switch DAT_1036abc0 is taken as off (the map setup 0x10017d70 clears it and no setter was found), so the speed rule applies")]
    public static void TickGlass(SBasePawn Pawn)
    {
        float now = Pawn.Level.TimeSeconds;
        bool controlsCloak = Pawn.Role >= ENetRole.ROLE_AutonomousProxy &&
            Pawn.Controller is SPlayerController pc && pc.IAmOnClient();
        if (controlsCloak && Pawn.DesiredGlassEffect != 0 &&
            (Pawn.CurrentRainVolume is not null ||
             (Pawn.Velocity.Size() > K.CloakMaxSpeed && Pawn.Physics != EPhysics.PHYS_RootMotion) ||
             Pawn.bStickyShocking))
        {
            SetDesiredGlassValue(Pawn, 0, K.CloakForcedOff);
            TestAndSetEMFVisible(Pawn);
        }

        byte target = Pawn.DesiredGlassEffect;
        if (Pawn.GlassEffect != target)
        {
            float g = ((now + K.GlassRampLead) * Pawn.SlopeGlassEffect) + Pawn.BGlassEffect;
            bool passed = target < Pawn.GlassEffect && g < target;
            Pawn.GlassEffect = passed || g < 0f || g > 255f ? target : (byte)SvmMath.Trunc(g);
        }

        if (now - Pawn.PerturbationStartTime < Pawn.PerturbationDuration &&
            SvmMath.Cos(now * Pawn.PerturbationValue) > 0f && Pawn.XLevel is { } level)
        {
            float k = K.GlassJitterAmplitude / Pawn.PerturbationDuration;
            int range = SvmMath.Trunc(((Pawn.PerturbationStartTime + Pawn.PerturbationDuration) * k) - (now * k));
            range = range < K.GlassJitterMin ? K.GlassJitterMin : range > K.GlassJitterMax ? K.GlassJitterMax : range;
            double r = level.World.Random.Rand(range) - (range * 0.5);
            double v = Pawn.GlassEffect + r;
            if (r <= 0.0)
            {
                if (v < 0.0)
                {
                    v = 1.0;
                }
            }
            else if (v > 255.0)
            {
                v = 254.0;
            }

            byte jittered = (byte)(int)v;
            Pawn.GlassEffect = jittered == target ? K.GlassJitterTargetStandIn : jittered;
        }

        // Releasing cloak keeps the spy detectable until the fade and its flicker finish. The owner then sends
        // the usual EMF change for the server to replicate; other EMF sources must remain visible.
        if (Pawn.bEMFIsOn && Pawn.DesiredGlassEffect == 0 && Pawn.GlassEffect == 0 &&
            now - Pawn.PerturbationStartTime >= Pawn.PerturbationDuration &&
            controlsCloak && !IsSpyEmfVisible(Pawn))
        {
            TestAndSetEMFVisible(Pawn);
        }
    }
}
