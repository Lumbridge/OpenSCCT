using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SShaker: jolts every player's view when a listed sender fires its Tag (spec/devices/shaker.md).</summary>
public partial class SShaker
{
    /// <summary>
    /// For each entry whose EventSender is <paramref name="Other"/>, in order: every player controller (in its zone
    /// when Zone is set) gets ClientShake with the entry's values. Later entries overwrite earlier ones on the camera.
    /// </summary>
    [Uc("SBase.SShaker.Trigger", Sha1 = "00ef9bf837f5b93d85acb6bfab636b372ead4921")]
    [PortOf("uc:SBase.SShaker:Trigger", Spec = "spec/devices/shaker.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        for (int i = 0; i < ShakeEvents.Count; i++)
        {
            FShakeEvent shake = ShakeEvents[i];
            if (!ReferenceEquals(shake.EventSender, Other))
            {
                continue;
            }

            foreach (SPlayerController pc in XLevel.DynamicActors<SPlayerController>())
            {
                // With a Zone set, a controller without a pawn fails the test (the original reads a None pawn).
                if (shake.Zone is null || ReferenceEquals(pc.Pawn?.Region.Zone, shake.Zone))
                {
                    pc.ClientShake(shake.strenght, shake.speed, shake.FadeOut, shake.bTilt, shake.bShake);
                }
            }
        }
    }
}
