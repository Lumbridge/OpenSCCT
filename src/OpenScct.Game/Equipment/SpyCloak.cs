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
        spy.EnergyLevel > 0f &&
        (spy.Controller as SPlayerController)?.eventCanActivateCloak() != false;

    /// <summary>
    /// The suit remains engaged through its fade and flicker, including frames where perturbation samples zero
    /// glass. This state is independent of the viewer's goggles and gates recharge and gauge visibility.
    /// </summary>
    public static bool IsEngaged(SPawnAttaque spy) =>
        spy.DesiredGlassEffect != 0 || spy.GlassEffect != 0 ||
        (spy.BGlassEffect != 0f &&
         spy.Level.TimeSeconds - spy.PerturbationStartTime < spy.PerturbationDuration);

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
    }
}
