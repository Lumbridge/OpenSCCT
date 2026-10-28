using OpenScct.Core;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SHelpPath: a "help path" arrow or info mesh placed along a map's routes, local to each machine
/// (ROLE_None). Shown only while the local profile's in-game help is on (bDisplayHelpInGame); the options toggle calls
/// <see cref="Visible"/> on every one (SPlayerController.ShowHelpPath). See spec/devices/help-path.md.
/// </summary>
public partial class SHelpPath
{
    /// <summary>
    /// Shown when the local profile has in-game help on, hidden otherwise (no profile: hidden). The game keeps the
    /// choice in its profile options (<see cref="IProfileOptions.DisplayHelpInGame"/>: on for Visit a Map).
    /// </summary>
    [Uc("SBase.SHelpPath.PostBeginPlay", Sha1 = "5832d0cf4410286f0f411a58eb1ebead3ee39d3a")]
    [PortOf("uc:SBase.SHelpPath:PostBeginPlay", Spec = "spec/devices/help-path.md#rules")]
    protected override void PostBeginPlay() => bHidden = !DisplayHelpInGame(XLevel);

    /// <summary>The local profile's bDisplayHelpInGame: the profile object when there is one, else the profile options.</summary>
    public static bool DisplayHelpInGame(Level? level) =>
        level?.GetService<IDeviceHooks>()?.GetPlayerProfile() is { } profile
            ? profile.bDisplayHelpInGame
            : level?.GetService<IProfileOptions>()?.DisplayHelpInGame ?? false;

    /// <summary>Shows or hides the help mesh.</summary>
    [Uc("SBase.SHelpPath.Visible", Sha1 = "7885fc55eae84c9a7318f912fe79590da89a302c")]
    [PortOf("uc:SBase.SHelpPath:Visible", Spec = "spec/devices/help-path.md#rules")]
    public void Visible(bool bVisible) => bHidden = !bVisible;
}
