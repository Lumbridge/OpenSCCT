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

        Pawn.GoggleBeam?.ChangeSkin();
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

        Pawn.GoggleBeam?.ChangeSkin();
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
    /// the skin of the mode already set. Nothing without a beam.
    /// </summary>
    [Uc("SBase.SBasePawn.ExecuteActionForGoggle", Sha1 = "c5523524219ff4e55a9ad7e13d0385dfcbe7ba28")]
    [PortOf("uc:SBase.SBasePawn:ExecuteActionForGoggle", Spec = "spec/equipment/goggle-glow.md#rules")]
    public static void ExecuteActionForGoggle(SBasePawn Pawn, int Action)
    {
        if (Pawn.GoggleBeam is not { } beam)
        {
            return;
        }

        int rest = Pawn.SPawnType == PawnType.SPAWNDEFENSE ? K.BeamModeNormal : K.BeamModeNone;
        switch (Action)
        {
            case SBasePawn.SA_GOG_DYNNOANIM:
                beam.CurrentMode = rest;
                beam.ChangeSkin();
                break;
            case SBasePawn.SA_GOG_SWITCH_WITHOUTANIM:
                beam.ChangeSkin();
                break;
            case SBasePawn.SA_VISION_THERMAL:
                beam.CurrentMode = K.BeamModeThermal;
                break;
            case SBasePawn.SA_VISION_NIGHT:
                beam.CurrentMode = K.BeamModeNight;
                break;
            case SBasePawn.SA_VISION_EMF:
                beam.CurrentMode = K.BeamModeEmf;
                break;
            case SBasePawn.SA_VISION_MOTION:
                beam.CurrentMode = K.BeamModeMotion;
                break;
            case SBasePawn.SA_GOG_HEADMVT_OFF:
                beam.CurrentMode = rest;
                break;
        }
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
    /// drawn, a vision mode on, binoculars, a spy mark, a cloak target, phospho; mercs always.
    /// </summary>
    [Uc("SBase.SBasePawn.TestAndSetEMFVisible", Sha1 = "26573b4e27f62d552000faefafc18c1cc4b10fc8")]
    [PortOf("uc:SBase.SBasePawn:TestAndSetEMFVisible", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void TestAndSetEMFVisible(SBasePawn Pawn)
    {
        bool visible = Pawn.SPawnType == PawnType.SPAWNDEFENSE || IsSpyEmfVisible(Pawn);
        if (visible)
        {
            Pawn.MyWeapon?.SetEMFWeapon();
            SetEMFToServer(Pawn);
        }
        else
        {
            Pawn.MyWeapon?.UnSetEMFWeapon();
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
        return Pawn.WeaponLoaded != 0 || vision || Pawn.bBinocularsOn || Pawn.bSpyBulletOn ||
            Pawn.DesiredGlassEffect != 0 || Pawn.PhosphoSkinTime > 0f;
    }

    /// <summary>The server makes the pawn EMF-visible (sent by the controlling client).</summary>
    [Uc("SBase.SBasePawn.SetEMFToServer", Sha1 = "0da87f2090280e03813fff1b8edbf2fdbfb1ddd6")]
    [PortOf("uc:SBase.SBasePawn:SetEMFToServer", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void SetEMFToServer(SBasePawn Pawn)
    {
        if (Pawn.Role < ENetRole.ROLE_Authority && Pawn.Controller is PlayerController pc)
        {
            CombatServices.Net(Pawn).ServerCall(pc, new CombatCall(CNAME_SetEMFToServer) { Target = Pawn });
            return;
        }

        SetEMF(Pawn);
    }

    /// <summary>The server clears the pawn's EMF visibility (sent by the controlling client).</summary>
    [Uc("SBase.SBasePawn.UnSetEMFToServer", Sha1 = "4f5d3227dedf57ac22bd7becf90ce7a6e94f6aa8")]
    [PortOf("uc:SBase.SBasePawn:UnSetEMFToServer", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void UnSetEMFToServer(SBasePawn Pawn)
    {
        if (Pawn.Role < ENetRole.ROLE_Authority && Pawn.Controller is PlayerController pc)
        {
            CombatServices.Net(Pawn).ServerCall(pc, new CombatCall(CNAME_UnSetEMFToServer) { Target = Pawn });
            return;
        }

        UnSetEMF(Pawn);
    }

    /// <summary>EMF visible: bEMFIsOn and full opacity.</summary>
    [Uc("SBase.SBasePawn.SetEMF", Sha1 = "805216bac7fa2939a4debc9e866ed9530a822411")]
    [PortOf("uc:SBase.SBasePawn:SetEMF", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void SetEMF(SBasePawn Pawn)
    {
        Pawn.bEMFIsOn = true;
        Pawn.EMFOpacity = 1f;
    }

    /// <summary>EMF invisible: bEMFIsOn off and no opacity.</summary>
    [Uc("SBase.SBasePawn.UnSetEMF", Sha1 = "9b55be2aacbd25625839c92e60c17c3ff33a0620")]
    [PortOf("uc:SBase.SBasePawn:UnSetEMF", Spec = "spec/equipment/vision-goggles.md#rules")]
    public static void UnSetEMF(SBasePawn Pawn)
    {
        Pawn.bEMFIsOn = false;
        Pawn.EMFOpacity = 0f;
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

    /// <summary>
    /// SetDesiredGlassValue (spy-energy.md rule 7): a new cloak target (the same target does nothing), reached
    /// linearly over Duration (0: PerturbationDefaultValue) from the current amount, with a perturbation of the same
    /// length. The original runs it on the server and sends the target to the owning client
    /// (SetDesiredGlassValueOnClients); the port's cloak lives on the controlling machine, so both halves run here.
    /// </summary>
    [Uc("SBase.SBasePawn.SetDesiredGlassValue", Sha1 = "26c096360b43a5ce60f9c760e77dc684c3b52b44")]
    [PortOf("uc:SBase.SBasePawn:SetDesiredGlassValue", Spec = "spec/equipment/spy-energy.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.uc SetDesiredGlassValue: early return on the same target, slope (Desired - Glass) / Duration, B = Glass - now * slope, SetPerturbationGlass(Duration), then the client half")]
    [Provenance(ProvenanceKind.Estimated, "Both halves run on the calling machine (the port keeps the cloak on the controlling machine, not on the server)")]
    public static void SetDesiredGlassValue(SBasePawn Pawn, byte DesiredValue, float Duration = 0f)
    {
        if (DesiredValue == Pawn.DesiredGlassEffect)
        {
            return;
        }

        if (Duration == 0f)
        {
            Duration = Pawn.PerturbationDefaultValue;
        }

        SetGlassRamp(Pawn, DesiredValue, Duration);
        SetPerturbationGlass(Pawn, Duration);
        SetDesiredGlassValueOnClients(Pawn, DesiredValue, Duration);
    }

    /// <summary>
    /// The owning client's half: the same ramp (no same-target test), the in/out sound and, on the controlling
    /// machine, the EMF visibility test.
    /// </summary>
    [Uc("SBase.SBasePawn.SetDesiredGlassValueOnClients", Sha1 = "6f9caefca439d887dad4c850d3551d934ec649ba")]
    [PortOf("uc:SBase.SBasePawn:SetDesiredGlassValueOnClients", Spec = "spec/equipment/spy-energy.md#rules")]
    public static void SetDesiredGlassValueOnClients(SBasePawn Pawn, byte DesiredValue, float Duration = 0f)
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

    /// <summary>
    /// SetPerturbationGlass: unless the ramp base is 0, a flicker window of Duration (0: PerturbationDefaultValue)
    /// at frequency Value (0: 12.8) from now, then the owning client's half (the same window and the perturbation
    /// sound).
    /// </summary>
    [Uc("SBase.SBasePawn.SetPerturbationGlass", Sha1 = "abd42c0b6ab3aabdb61ad2024b4f2871f2e429fc")]
    [PortOf("uc:SBase.SBasePawn:SetPerturbationGlass", Spec = "spec/equipment/spy-energy.md#rules")]
    public static void SetPerturbationGlass(SBasePawn Pawn, float Duration = 0f, float Value = 0f)
    {
        if (SetPerturbationWindow(Pawn, Duration, Value))
        {
            SetPerturbationGlassOnClients(Pawn, Duration);
        }
    }

    /// <summary>The owning client's flicker window, with the perturbation sound.</summary>
    [Uc("SBase.SBasePawn.SetPerturbationGlassOnClients", Sha1 = "358379cbcee7a574fbdc532ca8fb7e17ec1bacd6")]
    [PortOf("uc:SBase.SBasePawn:SetPerturbationGlassOnClients", Spec = "spec/equipment/spy-energy.md#rules")]
    public static void SetPerturbationGlassOnClients(SBasePawn Pawn, float Duration = 0f, float Value = 0f)
    {
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
    /// target drawn as 125.
    /// </summary>
    [UcNative("ASPawnAttaque::execTickNative", Exe = 0x10af5cf0)]
    [PortOf("rl:SBase.SPawnAttaque.TickNative", Spec = "spec/native/reloaded-hooks.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "TickNative game 0x10af5cf0 and Reloaded body 0x100caae0 (events SetDesiredGlassValue 0xbfcf1 and TestAndSetEMFVisible 0xbfced)")]
    [Provenance(ProvenanceKind.Estimated, "Reloaded's speed switch DAT_1036abc0 is taken as off (the map setup 0x10017d70 clears it and no setter was found), so the speed rule applies")]
    [Provenance(ProvenanceKind.Estimated, "Runs with the equipment part of the controlling machine's PlayerTick; the original runs it in every machine's pawn tick")]
    public static void TickGlass(SBasePawn Pawn, float DeltaTime)
    {
        float now = Pawn.Level.TimeSeconds;
        if (Pawn.Controller is SPlayerController pc && pc.IAmOnClient() && Pawn.DesiredGlassEffect != 0 &&
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
    }
}
