using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Game.Combat;

/// <summary>
/// Presentation side effects of the combat and equipment ports (pad rumble, HUD overlays, screen effects, camera
/// modes, local messages), raised as <see cref="SimEventKind.Notify"/> events named after the original function. The
/// three float parameters travel in <see cref="SimEvent.Location"/>, an int parameter in <see cref="SimEvent.Slot"/>.
/// Views read them; the simulation never reads them back.
/// </summary>
public static class CombatFx
{
    public static readonly Name Vibration_SautHaut = new("Vibration_SautHaut");
    public static readonly Name Vibration_Bousculade = new("Vibration_Bousculade");
    public static readonly Name Vibration_CoupParDefaut = new("Vibration_CoupParDefaut");
    public static readonly Name Vibration_StickyShock = new("Vibration_StickyShock");
    public static readonly Name Vibration_Tir = new("Vibration_Tir");
    public static readonly Name Vibration_Gadget = new("Vibration_Gadget");
    public static readonly Name Vibration_Charge = new("Vibration_Charge");
    public static readonly Name Vibration_Grab = new("Vibration_Grab");
    public static readonly Name Vibration_Blast = new("Vibration_Blast");
    public static readonly Name ClientDamageHudEffect = new("ClientDamageHudEffect");
    public static readonly Name HudShutDown = new("HudShutDown");
    public static readonly Name SetHudPerturbationLength = new("SetHudPerturbationLength");
    public static readonly Name SetBallisticValue = new("SetBallisticValue");
    public static readonly Name LocalMessage = new("LocalMessage");
    public static readonly Name WeirdEffect = new("WeirdEffect");
    public static readonly Name OverlayFade = new("OverlayFade");
    public static readonly Name CrossEyeBlur = new("CrossEyeBlur");
    public static readonly Name SmokeBlur = new("SmokeBlur");
    public static readonly Name HeadCamera = new("HeadCamera");
    public static readonly Name FirstPersonView = new("FirstPersonView");
    public static readonly Name ShowHelp = new("ShowHelp");
    public static readonly Name GlassEffect = new("SetDesiredGlassValueOnClients");
    public static readonly Name PerturbationGlass = new("SetPerturbationGlassOnClients");
    public static readonly Name ReticleOn = new("ReticleOn");
    public static readonly Name ImpactEffect = new("ImpactEffect");
    public static readonly Name MuzzleFlash = new("MuzzleFlash");
    public static readonly Name SetFOV = new("SetFOV");
    public static readonly Name HudState = new("HudState");
    public static readonly Name GoggleSound = new("GoggleSound");
    public static readonly Name GoggleMessage = new("GoggleMessage");
    public static readonly Name MuzzleSticky = new("SMuzzleSticky");
    public static readonly Name ThirdPersonMuzzle = new("ClientFireMuzzle");
    public static readonly Name FireTrace = new("SFireTrace");

    /// <summary>Raises <paramref name="What"/> for <paramref name="Source"/>.</summary>
    public static void Notify(Actor? Source, Name What, float A = 0f, float B = 0f, float C = 0f, int Slot = 0)
    {
        if (Source?.XLevel is not { } level)
        {
            return;
        }

        level.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.Notify,
            ActorId = Source.ActorId,
            What = What,
            Location = new FVector(A, B, C),
            Rotation = Source.Rotation,
            Slot = Slot,
        });
    }
}
