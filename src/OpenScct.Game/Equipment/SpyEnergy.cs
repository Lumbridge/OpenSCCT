using System;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The spy energy bar (spec/equipment/spy-energy.md): the Shocker's ammunition and the camo suit's power. It lives on
/// the spy's own machine only (the server never checks it).
/// </summary>
public static class SpyEnergy
{
    /// <summary>Spends <paramref name="Amount"/> energy when the bar holds that much (rule 3).</summary>
    [Uc("SBase.SPawnAttaque.AuthorizeEnergy", Sha1 = "7284bd071df1fa75d98dc5c87e13ec2186313649")]
    [PortOf("uc:SBase.SPawnAttaque:AuthorizeEnergy", Spec = "spec/equipment/spy-energy.md#rules")]
    public static bool AuthorizeEnergy(SPawnAttaque Spy, int Amount)
    {
        if (Spy.EnergyLevel < Amount)
        {
            return false;
        }

        Spy.EnergyLevel -= Amount;
        return true;
    }

    /// <summary>
    /// OpenSCCT's continuous battery, on the controlling machine only: drain while cloak is requested, including
    /// activation and active flicker. Switching off stops spending immediately; the battery holds during the
    /// remaining fade before refilling smoothly up to its maximum.
    /// The decoded original's whole-second refill and perturbation grace are retained in the reference spec.
    /// </summary>
    [UcNative("ASPawnAttaque::execComputeEnergyLevel", Exe = 0x10af5fa0)]
    [PortOf("nat:SBase.SPawnAttaque.ComputeEnergyLevel", Spec = "spec/equipment/spy-energy.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af5fa0 (REA disassembly 2026-10-26): Controller (0x38c) set and its RemoteRole (0x2dd) != 3; TimeSeconds - PerturbationStartTime (0xac8) > PerturbationDuration (0xac4); GlassEffect (0x2e7) != 0: EnergyLevel (0xb94) -= dt * 0xac0, <= 0 -> 0 and SetDesiredGlassValue(0, 1.0); else EnergyRefill (0xb64) += dt, trunc(new) > trunc(old) and float(EnergyLevelMax) > EnergyLevel -> EnergyLevel += float(EnergyRefillAmount), no clamp")]
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT feel change: spend continuously while cloak is requested, independently of visual perturbation; hold energy during the off fade, then recharge continuously at EnergyRefillAmount per second, clamped to EnergyLevelMax")]
    public static void ComputeEnergyLevel(SPawnAttaque Spy, float DeltaTime)
    {
        if (!(DeltaTime > 0f) || Spy.Controller is not { } controller ||
            controller.RemoteRole == ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        if (Spy.DesiredGlassEffect != 0)
        {
            Spy.EnergyLevel = Math.Clamp(Spy.EnergyLevel - DeltaTime * Spy.GlassEffectEnergyUsePerTick,
                0f, Spy.EnergyLevelMax);
            if (Spy.EnergyLevel == 0f && Spy.DesiredGlassEffect != 0)
            {
                PawnVision.SetDesiredGlassValue(Spy, 0, K.CloakForcedOff);
            }

            return;
        }

        // The off request ends spending immediately. Residual visibility still prevents recharge, so a quick
        // reversal cannot recharge the suit while it is fading and active flicker never grants free on-time.
        if (SpyCloak.IsEngaged(Spy)) return;

        Spy.EnergyLevel = Math.Clamp(Spy.EnergyLevel + DeltaTime * Spy.EnergyRefillAmount,
            0f, Spy.EnergyLevelMax);
    }
}
