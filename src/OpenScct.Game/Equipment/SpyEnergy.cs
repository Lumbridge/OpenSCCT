using OpenScct.Core;
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
    /// ComputeEnergyLevel (native, decoded game 0x10af5fa0; rule 2): only with a controller on its controlling machine
    /// (RemoteRole not AutonomousProxy) and once the cloak flicker window is over (strictly after Start + Duration),
    /// an active or fading cloak drains GlassEffectEnergyUsePerTick per second (forced off over 1 s at 0 or below);
    /// otherwise, when the whole part of the accumulated idle time goes up and the bar is below EnergyLevelMax, one
    /// EnergyRefillAmount step is added, with no clamp (a step from 95 ends at 102).
    /// </summary>
    [UcNative("ASPawnAttaque::execComputeEnergyLevel", Exe = 0x10af5fa0)]
    [PortOf("nat:SBase.SPawnAttaque.ComputeEnergyLevel", Spec = "spec/equipment/spy-energy.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af5fa0 (REA disassembly 2026-10-26): Controller (0x38c) set and its RemoteRole (0x2dd) != 3; TimeSeconds - PerturbationStartTime (0xac8) > PerturbationDuration (0xac4); GlassEffect (0x2e7) != 0: EnergyLevel (0xb94) -= dt * 0xac0, <= 0 -> 0 and SetDesiredGlassValue(0, 1.0); else EnergyRefill (0xb64) += dt, trunc(new) > trunc(old) and float(EnergyLevelMax) > EnergyLevel -> EnergyLevel += float(EnergyRefillAmount), no clamp")]
    public static void ComputeEnergyLevel(SPawnAttaque Spy, float DeltaTime)
    {
        if (Spy.Controller is not { } controller || controller.RemoteRole == ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        float now = Spy.Level.TimeSeconds;
        if (!(now - Spy.PerturbationStartTime > Spy.PerturbationDuration))
        {
            return;
        }

        if (Spy.GlassEffect != 0)
        {
            Spy.EnergyLevel -= DeltaTime * Spy.GlassEffectEnergyUsePerTick;
            if (Spy.EnergyLevel <= 0f)
            {
                Spy.EnergyLevel = 0f;
                PawnVision.SetDesiredGlassValue(Spy, 0, K.CloakForcedOff);
            }

            return;
        }

        int before = SvmMath.Trunc(Spy.EnergyRefill);
        Spy.EnergyRefill += DeltaTime;
        int after = SvmMath.Trunc(Spy.EnergyRefill);
        if (after > before && Spy.EnergyLevelMax > Spy.EnergyLevel)
        {
            Spy.EnergyLevel += Spy.EnergyRefillAmount;
        }
    }
}
