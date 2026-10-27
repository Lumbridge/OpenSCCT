using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The tutorial maps' hint text (the zone tips), as the HUD's tutorial area shows it.
public partial class SHud
{
    private static readonly AssetRef TutoMapSound = new("Interface.Menu.bip_message");

    /// <summary>A new hint within this many seconds of the last one gives no beep.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.AddTutoMapMessage: Level.TimeSeconds > fTutoMapTime + 0.1")]
    public const float TutoMapBeepGap = 0.1f;

    /// <summary>Radius of the hint beep.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.AddTutoMapMessage: PlaySound(bip_message, SLOT_Misc, 1.0, , 500.0)")]
    public const float TutoMapBeepRadius = 500f;

    /// <summary>
    /// Standalone only: a changed hint is stored (an empty one clears it), with a beep when it is not empty and the
    /// last change was more than 0.1 s ago.
    /// </summary>
    [Uc("SBase.SHud.AddTutoMapMessage", Sha1 = "93e084d07df52fff9f44081c57e4969dc47cbf79")]
    [PortOf("uc:SBase.SHud:AddTutoMapMessage", Spec = "spec/hud/counters-and-prompts.md#rules")]
    public void AddTutoMapMessage(string Message1)
    {
        if (Message1 == sTutoMap || PlayerOwner?.Level.NetMode != ENetMode.NM_Standalone)
        {
            return;
        }

        if (Level.TimeSeconds > fTutoMapTime + TutoMapBeepGap && Message1.Length != 0)
        {
            PlaySound(TutoMapSound, ESoundSlot.SLOT_Misc, 1f, Radius: TutoMapBeepRadius);
        }

        fTutoMapTime = Level.TimeSeconds;
        sTutoMap = Message1;
    }
}
