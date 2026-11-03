using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// Legacy class retained for asset compatibility. Live loadouts reject it; every spy owns the SpyCloak ability.
/// </summary>
public partial class SGadCloak
{
    /// <summary>
    /// The switch-on test: not shocking, not in rain, weapon holstered, at most 240 uu/s, a full
    /// energy bar and a state that allows the cloak.
    /// </summary>
    [Uc("SBase.SGadCloak.CanTurnOn", Sha1 = "bd72e99c9a3e16991e9878f6659f776a4298eb9e")]
    [PortOf("uc:SBase.SGadCloak:CanTurnOn", Spec = "spec/equipment/spy-energy.md#rules")]
    public bool CanTurnOn() => OwnerPawn is SPawnAttaque spy && SpyCloak.CanActivate(spy);

    /// <summary>The switch-off test includes a cloak that has only just begun fading in.</summary>
    [Uc("SBase.SGadCloak.CanTurnOff", Sha1 = "1f680d19e02972ce916e226fc89cccd841fcc80f")]
    [PortOf("uc:SBase.SGadCloak:CanTurnOff", Spec = "spec/equipment/spy-energy.md#rules")]
    public bool CanTurnOff() => OwnerPawn is { DesiredGlassEffect: K.FullGlass };

    /// <summary>The toggle (rule 5): off when on, on when allowed, else the "unavailable" message.</summary>
    [Uc("SBase.SGadCloak.QuickUse", Sha1 = "a4776ad42ea355cc90f1c859501826070ddabd84")]
    [PortOf("uc:SBase.SGadCloak:QuickUse", Spec = "spec/equipment/spy-energy.md#rules")]
    public override void QuickUse()
    {
        SetActive(!CanTurnOff());
    }

    /// <summary>Requests a state immediately; release must not wait for the fade-in or a refire timer.</summary>
    public void SetActive(bool active)
    {
        if (OwnerPawn is not SPawnAttaque spy) return;
        if (active && SpyCloak.CanActivate(spy)) LastUsed = Level.TimeSeconds;
        SpyCloak.SetActive(spy, active);
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
