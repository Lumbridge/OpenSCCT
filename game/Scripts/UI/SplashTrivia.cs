using System;
using System.Collections.Generic;

namespace OpenScctGame.UI;

/// <summary>
/// The main menu's title line: a short Splinter Cell fact, a different one each time the page opens (like Minecraft's
/// splash text). It takes the place of the original's "Enhanced Versus" title. Keep lines short enough to fit on one
/// line across the menu, and keep them true.
/// </summary>
public static class SplashTrivia
{
    public static readonly IReadOnlyList<string> Lines = new[]
    {
        "Chaos Theory came out in 2005.",
        "The first Splinter Cell came out in 2002, on the Xbox first.",
        "Ubisoft Montreal made the first Splinter Cell.",
        "Ubisoft Annecy made the Spies vs. Mercenaries mode.",
        "Spies vs. Mercenaries first appeared in Pandora Tomorrow.",
        "Amon Tobin composed the Chaos Theory soundtrack.",
        "Michael Ironside voiced Sam Fisher from the first game to Conviction.",
        "Eric Johnson voiced Sam Fisher in Blacklist.",
        "Chaos Theory gave Sam Fisher his combat knife.",
        "Chaos Theory added the sound meter: stay quieter than the room.",
        "Chaos Theory added a two-player co-op campaign.",
        "Sam Fisher works for Third Echelon, a secret arm of the NSA.",
        "Colonel Irving Lambert is Sam Fisher's handler.",
        "Anna Grimsdottir, Grim, is Third Echelon's technical expert.",
        "Sam Fisher's daughter is called Sarah.",
        "The SC-20K is Sam Fisher's modular assault rifle.",
        "The split jump has been in Splinter Cell since the first game.",
        "Double Agent followed Chaos Theory in 2006.",
        "Spies vs. Mercs came back in Blacklist in 2013.",
        "Chaos Theory came out on PC, Xbox, PS2 and GameCube.",
        "Chaos Theory was remastered in Splinter Cell Trilogy HD for the PS3.",
        "In Versus, the spies are Shadownet agents and the mercs work for ARGUS.",
        "The first Splinter Cell had no multiplayer at all.",
        "Amon Tobin's soundtrack also came out as his album Chaos Theory.",
        "Douglas Shetland, Sam's old friend, runs Displace International.",
        "Chaos Theory was the first Splinter Cell rated M in North America.",
        "Sam Fisher joined Rainbow Six Siege in 2020 as the operator Zero.",
        "Sam Fisher turns up in a mission in Ghost Recon Wildlands.",
        "The Splinter Cell novels came out under the pen name David Michaels.",
        "Liev Schreiber voices Sam Fisher in Netflix's Splinter Cell: Deathwatch.",
        "Ubisoft Toronto announced a remake of the first Splinter Cell in 2021.",
    };

    private static readonly Random Rng = new();
    private static int _last = -1;

    /// <summary>A line at random, never the same one twice in a row.</summary>
    public static string Pick()
    {
        int i = Rng.Next(Lines.Count);
        if (Lines.Count > 1 && i == _last)
        {
            i = (i + 1 + Rng.Next(Lines.Count - 1)) % Lines.Count;
        }

        _last = i;
        return Lines[i];
    }
}
