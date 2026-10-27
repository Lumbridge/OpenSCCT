using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.AI;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SGoggle: the sounds of a vision mode change, the sound part of Activate (spec/equipment/vision-goggles.md
// rule 8). The owner's controller plays them: the mode's switch sounds once and its loop until the next change.
public partial class SGoggle
{
    [Provenance(ProvenanceKind.Decoded, "SGoggle.Activate: the vision mode sounds")]
    private static readonly AssetRef VisionStartSound = new("Armes_impacts.Gadgets.VisionRUN_1");

    private static readonly AssetRef VisionSwitchSound = new("Armes_impacts.Gadgets.VisionSWIT_1");
    private static readonly AssetRef ThermalLoopSound = new("Armes_impacts.Gadgets.JUMloop");
    private static readonly AssetRef NightLoopSound = new("Armes_impacts.Gadgets.VisionNight_loop");
    private static readonly AssetRef EmfLoopSound = new("Armes_impacts.Gadgets.Emf_fond");
    private static readonly AssetRef MotionInOutSound = new("Armes_impacts.Gadgets.Hud_EMF_motion_In_Out");

    /// <summary>The radius of every vision mode sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "SGoggle.Activate: PlaySound(..., 1.0, , 20.0)")]
    public const float VisionSoundRadius = 20f;

    /// <summary>
    /// Stops the three mode loops, then plays the new mode's sounds at the owner's controller: thermal (36) its start
    /// and switch sounds and the JUMloop, night (34) the same two and the night loop, motion tracking (37) the in/out
    /// sound and the night loop, EMF (38) the in/out sound and the EMF loop. The normal view (5) plays nothing.
    /// Only the owner hears them (<see cref="OwnerHearsVision"/>).
    /// </summary>
    [PortOf("uc:SBase.SGoggle:Activate", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void PlayVisionSounds()
    {
        if (Owner is not PlayerController pc || !OwnerHearsVision(pc))
        {
            return;
        }

        pc.StopSound(ThermalLoopSound);
        pc.StopSound(NightLoopSound);
        pc.StopSound(EmfLoopSound);
        switch (CurrentMode)
        {
            case REN_ThermalVision:
                Play(pc, VisionStartSound, ESoundSlot.SLOT_Interface);
                Play(pc, VisionSwitchSound, ESoundSlot.SLOT_None);
                Play(pc, ThermalLoopSound, ESoundSlot.SLOT_Misc);
                break;
            case REN_NightVision:
                Play(pc, VisionSwitchSound, ESoundSlot.SLOT_Interface);
                Play(pc, VisionStartSound, ESoundSlot.SLOT_None);
                Play(pc, NightLoopSound, ESoundSlot.SLOT_Misc);
                break;
            case REN_MotionTrackingVision:
                Play(pc, MotionInOutSound, ESoundSlot.SLOT_Interface);
                Play(pc, NightLoopSound, ESoundSlot.SLOT_Misc);
                break;
            case REN_EMFVision:
                Play(pc, MotionInOutSound, ESoundSlot.SLOT_Interface);
                Play(pc, EmfLoopSound, ESoundSlot.SLOT_Misc);
                break;
        }
    }

    /// <summary>
    /// Whether <paramref name="pc"/>'s vision sounds play at all: the goggles live on the owner's machine only
    /// (vision-goggles.md rule 1, a local player's only) and a client's PlaySound reaches only its local players
    /// (audio.md rule 4), so only the owner hears them. A bot plays in the server world and has no machine of its own: its vision sounds play nowhere, as a remote client's goggles are heard by nobody else. Without this the
    /// server relayed them (ClientHearSound) to the players near the bot, and as 2D sounds at full volume.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGoggle is client-only; Activate plays on the owner's controller (a client's sounds stay local)")]
    public static bool OwnerHearsVision(PlayerController? pc) => pc is not null && pc.Player is not AiViewport;

    private static void Play(PlayerController pc, AssetRef sound, ESoundSlot slot) =>
        pc.PlaySound(sound, slot, 1f, false, VisionSoundRadius);
}
