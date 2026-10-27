using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController: the server's music and jingles for this player (spec/native/audio.md rule 30).
public partial class SPlayerController
{
    /// <summary>
    /// The objective jingles (no transition) are skipped when the player's profile turns objective sounds off; the
    /// rest goes to PlayerController.ClientSetMusic.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientSetMusic", Sha1 = "82d0dcee19c1a5ff003a93fe2a7c018329bc4ec3")]
    [PortOf("uc:SBase.SPlayerController:ClientSetMusic", Spec = "spec/native/audio.md#controller-sound-calls")]
    public override void ClientSetMusic(AssetRef NewSong, EMusicTransition NewTransition)
    {
        if (NewTransition == EMusicTransition.MTRAN_None &&
            XLevel?.GetService<IDeviceHooks>()?.GetPlayerProfile() is { bObjectiveSounds: false })
        {
            return;
        }

        base.ClientSetMusic(NewSong, NewTransition);
    }
}
