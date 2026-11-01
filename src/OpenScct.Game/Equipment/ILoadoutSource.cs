using OpenScct.Core.Uc;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The player's own chosen gear, read on the player's machine when the server asks it for its kit (an OpenSCCT
/// addition, spec/additions/gear-change.md). The game client registers one on its level; a machine without one gives
/// the fixed side kit (loadouts.md rule 2.1). It is not a player profile, so the profile's other switches (zone tips,
/// help paths, mouse-wheel gadgets, objective sounds) keep their no-profile behaviour.
/// </summary>
public interface ILoadoutSource
{
    /// <summary>The side's gadget slots (spy when <paramref name="Spy"/>), None for an empty slot.</summary>
    ClassRef[] Gadgets(bool Spy);

    /// <summary>The side's weapon.</summary>
    ClassRef Weapon(bool Spy);

    /// <summary>The independent merc sidearm, or None.</summary>
    ClassRef Sidearm() => default;
}
