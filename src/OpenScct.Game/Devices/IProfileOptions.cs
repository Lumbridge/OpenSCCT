using OpenScct.Game.SBase;

namespace OpenScct.Game.Devices;

/// <summary>
/// The player profile choices the script reads during play (SPlayerProfile's <c>bObjectiveSounds</c>,
/// <c>SelectedMouse</c> and <c>bDisplayHelpInGame</c>). The rewrite keeps them in the player's settings file rather than in a profile object, so the
/// game installs this as a level service; <see cref="IDeviceHooks.GetPlayerProfile"/> stays empty (a profile there also
/// carries the kit, which the gear change owns). Read only for the machine's own player
/// (<see cref="SPlayerController.LocalProfileOptions"/>), so a server level in the same process is not affected.
/// </summary>
public interface IProfileOptions
{
    /// <summary>bObjectiveSounds: the objective jingles play (default on).</summary>
    bool ObjectiveSounds { get; }

    /// <summary>SelectedMouse: 0 makes the mouse wheel change gadgets, 1 makes it toggle walk and run.</summary>
    byte SelectedMouse { get; }

    /// <summary>
    /// bDisplayHelpInGame: the map's help paths and help-only hints show (on for a Visit a Map play, off otherwise;
    /// spec/game/tutorials.md).
    /// </summary>
    bool DisplayHelpInGame => false;
}
