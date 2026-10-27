using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Devices;

/// <summary>
/// Calls the devices make into the HUD, game-rules and equipment ports (SHud.AddTutoMapMessage,
/// SPlayerController.AddGPOOnHud and RemoveGPOFromHud, SGameInfo.BroadcastTeamLocalizedMessage,
/// SBasePawn.FindGadgetType, SWeapon and SGadget FullReload). Devices find it as the level service
/// <c>Level.GetService&lt;IDeviceHooks&gt;()</c>; without it these calls do nothing.
/// </summary>
public interface IDeviceHooks
{
    /// <summary>A malfunction marker for a gameplay object at <paramref name="Location"/> on a player's HUD.</summary>
    void AddGPOOnHud(SPlayerController Controller, FVector Location);

    /// <summary>Removes the malfunction marker at <paramref name="Location"/>.</summary>
    void RemoveGPOFromHud(SPlayerController Controller, FVector Location);

    /// <summary>Shows (or with an empty text clears) a map hint on a player's HUD.</summary>
    void AddTutoMapMessage(SPlayerController Controller, string Message);

    /// <summary>A localized message to every player of team <paramref name="Team"/> (index into the message class).</summary>
    void BroadcastTeamLocalizedMessage(int Team, ClassRef MessageClass, int Switch, Actor? OptionalObject);

    /// <summary>The pawn's gadget of class <paramref name="GadgetClass"/> (or a subclass), or null.</summary>
    Actor? FindGadgetType(SBasePawn Pawn, ClassRef GadgetClass);

    /// <summary>Refills a weapon or gadget completely.</summary>
    void FullReload(Actor Item);

    /// <summary>The local player's profile (SPersistentData(GetPersistentData()).PlayerProfile), or null.</summary>
    USPlayerProfile? GetPlayerProfile();
}
