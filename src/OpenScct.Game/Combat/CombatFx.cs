using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Combat;

/// <summary>
/// Presentation side effects of the combat and equipment ports (pad rumble, HUD overlays, screen effects, camera
/// modes, local messages), raised as <see cref="SimEventKind.Notify"/> events named after the original function. The
/// three float parameters travel in <see cref="SimEvent.Location"/>, an int parameter in <see cref="SimEvent.Slot"/>.
/// Views read them; the simulation never reads them back. Two are also applied here, on the machine that raises them
/// (the owner runs the combat states itself): <see cref="HeadCamera"/> and <see cref="FirstPersonView"/>.
/// </summary>
public static class CombatFx
{
    public static readonly Name Vibration_SautHaut = new("Vibration_SautHaut");
    public static readonly Name Vibration_Bousculade = new("Vibration_Bousculade");
    public static readonly Name Vibration_CoupParDefaut = new("Vibration_CoupParDefaut");
    public static readonly Name Vibration_StickyShock = new("Vibration_StickyShock");
    public static readonly Name Vibration_Tir = new("Vibration_Tir");
    public static readonly Name Vibration_Gadget = new("Vibration_Gadget");
    public static readonly Name Vibration_Charge_Course = new("Vibration_Charge_Course");
    public static readonly Name Vibration_Charge_Obstacle = new("Vibration_Charge_Obstacle");
    public static readonly Name Vibration_Grabbed = new("Vibration_Grabbed");
    public static readonly Name Vibration_Grab_Kill = new("Vibration_Grab_Kill");
    public static readonly Name Vibration_Blast = new("Vibration_Blast");
    public static readonly Name ClientDamageHudEffect = new("ClientDamageHudEffect");
    public static readonly Name HudShutDown = new("HudShutDown");
    public static readonly Name SetHudPerturbationLength = new("SetHudPerturbationLength");
    public static readonly Name SetBallisticValue = new("SetBallisticValue");
    public static readonly Name LocalMessage = new("LocalMessage");
    /// <summary>
    /// SetWeirdEffect(factor), 1 on, 0 off. Not a screen effect: the native (game 0x10a25c00, editor 0x11187510)
    /// passes the factor to the audio subsystem (XLevel.Engine.Audio, vtable +0xfc in the game). No listener yet.
    /// </summary>
    public static readonly Name WeirdEffect = new("WeirdEffect");
    public static readonly Name OverlayFade = new("OverlayFade");
    public static readonly Name CrossEyeBlur = new("CrossEyeBlur");
    public static readonly Name SmokeBlur = new("SmokeBlur");
    /// <summary>
    /// m_camera.AttachCamToBone on 'B Head': attached when Slot is not 0 or the offset (A, B, C) is not zero, the
    /// camera offset being (A, B, C); detached otherwise (no parameters). Applied by <see cref="Notify"/>.
    /// </summary>
    public static readonly Name HeadCamera = new("HeadCamera");

    /// <summary>bBehindView = false (A not 0) or true (A = 0). Applied by <see cref="Notify"/>.</summary>
    public static readonly Name FirstPersonView = new("FirstPersonView");
    public static readonly Name ShowHelp = new("ShowHelp");
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

        if (Source is SPlayerController pc)
        {
            if (What == HeadCamera)
            {
                var offset = new FVector(A, B, C);
                pc.CombatHeadCamera(Slot != 0 || !offset.IsZero(), offset);
            }
            else if (What == FirstPersonView)
            {
                pc.bBehindView = A == 0f;
            }
        }
    }
}
