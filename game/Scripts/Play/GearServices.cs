using System;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Play;

/// <summary>
/// The player's own services on a level the game loads: the gear change's (spec/additions/gear-change.md) and the
/// profile choices of the settings file (objective sounds, the mouse wheel).
/// </summary>
public static class GearServices
{
    /// <summary>
    /// Adds the saved gear, the HUD line for the server's answer and the profile choices. Only the local player reads
    /// them (his kit, his controller's profile), so a server level loaded in this process is not affected.
    /// </summary>
    public static void Install(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.SetService<ILoadoutSource>(new SettingsLoadoutSource());
        level.SetService<IGearChangeFeedback>(new HudGearFeedback(level));
        level.SetService<OpenScct.Game.Devices.IProfileOptions>(new SettingsProfileOptions());
    }
}

/// <summary>
/// The player's saved gear (<see cref="LoadoutSettings"/>) as the level's <see cref="ILoadoutSource"/>
/// (spec/additions/gear-change.md): read each time the server asks this machine for its kit.
/// </summary>
public sealed class SettingsLoadoutSource : ILoadoutSource
{
    public ClassRef[] Gadgets(bool Spy) =>
        Array.ConvertAll(SettingsStore.Current.Loadout.GadgetsOf(Spy), p => p.Length == 0 ? default : new ClassRef(p));

    public ClassRef Weapon(bool Spy)
    {
        if (Spy)
        {
            return Loadouts.SpyWeapon;
        }

        string merc = SettingsStore.Current.Loadout.MercWeapon;
        return merc.Length == 0 ? Loadouts.MercWeapon : new ClassRef(merc);
    }
}

/// <summary>The server's answer to a gear change, shown in the local player's HUD message area.</summary>
public sealed class HudGearFeedback : IGearChangeFeedback
{
    private const int ShowSeconds = 4;

    private readonly Level _level;

    public HudGearFeedback(Level level)
    {
        _level = level;
    }

    public void GearChangeAnswered(GearChangeResult Result, float WaitSeconds)
    {
        if (_level.Info.LocalPlayerController?.myHUD is SHud hud)
        {
            HudMessages.AddMessageToStack(hud, GearMenuModel.ResultText(Result, WaitSeconds), ShowSeconds, 0);
        }
    }
}
