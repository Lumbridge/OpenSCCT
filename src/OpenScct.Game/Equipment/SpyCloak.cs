using System;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Equipment;

/// <summary>The camo suit is an innate spy ability, independent of inventory and selected gadgets.</summary>
public static class SpyCloak
{
    public const string LegacyGadgetClass = "SBase.SGadCloak";
    public const int LegacyGadgetOption = 3;

    public static bool IsLegacyGadget(string? path) =>
        string.Equals(path, LegacyGadgetClass, StringComparison.OrdinalIgnoreCase);

    public static bool CanActivate(SPawnAttaque spy) =>
        spy.Health > 0 && !spy.bStickyShocking && spy.CurrentRainVolume is null &&
        spy.WeaponLoaded == 0 && !spy.bWeaponTransit && spy.Velocity.Size() <= K.CloakMaxSpeed &&
        spy.EnergyLevel >= spy.EnergyLevelMax &&
        (spy.Controller as SPlayerController)?.eventCanActivateCloak() != false;

    public static void Toggle(SPawnAttaque spy) => SetActive(spy, spy.DesiredGlassEffect != K.FullGlass);

    public static void SetActive(SPawnAttaque spy, bool active)
    {
        if (active && !CanActivate(spy))
        {
            CombatFx.Notify(spy.Controller ?? (Actor)spy, CombatFx.LocalMessage, K.CloakMessageTime);
            return;
        }

        byte target = active ? K.FullGlass : (byte)0;
        PawnVision.SetDesiredGlassValue(spy, target);
        // A second press must reverse a request even before its server reply arrives.
        if (spy.Role < ENetRole.ROLE_Authority) spy.DesiredGlassEffect = target;
    }
}
