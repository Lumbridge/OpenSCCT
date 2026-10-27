using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadCloak: the spy's camo suit (spec/equipment/spy-energy.md rules 4 to 6). It needs a full energy bar and
/// drains it while on; the ramp, flicker and automatic switch-off belong to the cloak spec.
/// </summary>
public partial class SGadCloak
{
    /// <summary>
    /// The switch-on test: not shocking, past the refire delay, not in rain, weapon holstered, at most 240 uu/s, a full
    /// energy bar and a state that allows the cloak.
    /// </summary>
    [Uc("SBase.SGadCloak.CanTurnOn", Sha1 = "bd72e99c9a3e16991e9878f6659f776a4298eb9e")]
    [PortOf("uc:SBase.SGadCloak:CanTurnOn", Spec = "spec/equipment/spy-energy.md#rules")]
    public bool CanTurnOn()
    {
        if (OwnerPawn is not SPawnAttaque spy)
        {
            return false;
        }

        return !spy.bStickyShocking && !(Level.TimeSeconds - LastUsed < RefireRate) && spy.CurrentRainVolume is null &&
            spy.WeaponLoaded == 0 && !(spy.Velocity.Size() > K.CloakMaxSpeed) && !(spy.EnergyLevel < spy.EnergyLevelMax) &&
            (spy.Controller as SPlayerController)?.eventCanActivateCloak() != false;
    }

    /// <summary>The switch-off test: a ramp and a cloak amount.</summary>
    [Uc("SBase.SGadCloak.CanTurnOff", Sha1 = "1f680d19e02972ce916e226fc89cccd841fcc80f")]
    [PortOf("uc:SBase.SGadCloak:CanTurnOff", Spec = "spec/equipment/spy-energy.md#rules")]
    public bool CanTurnOff() => OwnerPawn is { } pawn && pawn.SlopeGlassEffect > 0f && pawn.GlassEffect != 0;

    /// <summary>The toggle (rule 5): off when on, on when allowed, else the "unavailable" message.</summary>
    [Uc("SBase.SGadCloak.QuickUse", Sha1 = "a4776ad42ea355cc90f1c859501826070ddabd84")]
    [PortOf("uc:SBase.SGadCloak:QuickUse", Spec = "spec/equipment/spy-energy.md#rules")]
    public override void QuickUse()
    {
        if (OwnerPawn is not { } pawn || Level.TimeSeconds - LastUsed < K.CloakToggleGuard)
        {
            return;
        }

        if (CanTurnOff())
        {
            PawnVision.SetDesiredGlassValue(pawn, 0);
        }
        else if (CanTurnOn())
        {
            LastUsed = Level.TimeSeconds;
            PawnVision.SetDesiredGlassValue(pawn, K.FullGlass);
        }
        else
        {
            LocalMessage(TextCantUseCloak, K.CloakMessageTime);
        }
    }

    /// <summary>The hint when the suit is selected.</summary>
    [Uc("SBase.SGadCloak.Load", Sha1 = "31a813577c65ce9839c5e717b5a41fd7fb2aed3f")]
    [PortOf("uc:SBase.SGadCloak:Load", Spec = "spec/equipment/spy-energy.md#rules")]
    public override void Load() => LocalMessage(TextLoadCloak, K.VisionMessageTime);

    [Uc("SBase.SGadCloak.Use", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGadCloak:Use", Spec = "spec/equipment/spy-energy.md#rules")]
    public override void Use(float Value)
    {
    }

    /// <summary>Turns the suit off when it is on.</summary>
    [Uc("SBase.SGadCloak.ForceDesactivation", Sha1 = "16ddba4c8702abc5472fd42e7e50e48cd1ed2f49")]
    [PortOf("uc:SBase.SGadCloak:ForceDesactivation", Spec = "spec/equipment/spy-energy.md#rules")]
    public void ForceDesactivation()
    {
        if (CanTurnOff() && OwnerPawn is { } pawn)
        {
            PawnVision.SetDesiredGlassValue(pawn, 0);
        }
    }
}
