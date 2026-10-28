using OpenScct.Game.Devices;

namespace OpenScctGame.Options;

/// <summary>
/// The profile choices the script reads in play (<see cref="IProfileOptions"/>: objective sounds, the mouse wheel),
/// read live from the saved settings (<see cref="SettingsStore.Current"/>), so a change in the options applies at once;
/// and the in-game help, which is on only for a Visit a Map play (<see cref="Tutorials.TutorialProfile.DisplayHelpInGame"/>).
/// </summary>
public sealed class SettingsProfileOptions : IProfileOptions
{
    public bool ObjectiveSounds => SettingsStore.Current.ObjectiveSounds;

    public byte SelectedMouse => SettingsStore.Current.WheelSelectsGadgets ? (byte)0 : (byte)1;

    public bool DisplayHelpInGame => Tutorials.TutorialProfile.DisplayHelpInGame;
}
