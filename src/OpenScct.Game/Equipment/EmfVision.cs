using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The merc's electromagnetic vision (mode 38, spec/equipment/vision-goggles.md rule 12): electronics glow with their
/// <c>EMFIntensity</c> while <c>bEMFIsOn</c> (mines, sticky cams, grenades, objective computers, ammo crates, the
/// spy's drawn pistol or any spy shown by <see cref="PawnVision.TestAndSetEMFVisible"/>). The renderer draws the glow;
/// this is the gameplay side: what an EMF view shows.
/// </summary>
public static class EmfVision
{
    /// <summary>The glow <paramref name="A"/> draws with in EMF vision: its intensity while on, else none.</summary>
    [Provenance(ProvenanceKind.Decoded, "The EMF render pass reads EMFIntensity while bEMFIsOn and EMFOpacity (vision-goggles.md rule 12)")]
    public static float Glow(Actor A) => A.bEMFIsOn ? A.EMFIntensity : 0f;

    /// <summary>True when <paramref name="A"/> shows in EMF vision: drawn, switched on and not fully transparent.</summary>
    public static bool Shows(Actor A) => !A.bHidden && A.bEMFIsOn && A.EMFOpacity > 0f;

    /// <summary>The actors of <paramref name="Level"/> that show in EMF vision, in actor order.</summary>
    public static IEnumerable<Actor> Electronics(Level Level)
    {
        foreach (Actor a in Level.AllActors<Actor>())
        {
            if (!a.bDeleteMe && Shows(a))
            {
                yield return a;
            }
        }
    }
}
