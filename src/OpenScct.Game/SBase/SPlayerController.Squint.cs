using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The merc's squint (spec/equipment/merc-eye-adaptation.md rule 2): a value the original computes every tick and
// whose only reader, the RendMap 5 light byte of the pre-render, shows nothing on screen. Kept for fidelity.
public partial class SPlayerController
{
    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafd40: still below 5.0 uu/s (0x10c10b4c)")]
    private const float SquintStillSpeed = 5f;

    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafd5d: |int(aLookUp)| and |int(aTurn)| at most 10")]
    private const int SquintStillLook = 10;

    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafd89: DurationStatic above 0.5 (0x10c10af8)")]
    private const float SquintStillDelay = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafda9: a move below 80.0 uu/s (0x10c10cb4) is slow")]
    private const float SquintSlowSpeed = 80f;

    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafdc4: slow moves take 0.3 dt, floor 1.2")]
    private const float SquintSlowRate = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "as SquintSlowRate")]
    private const float SquintSlowFloor = 1.2f;

    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafdfc: DurationForSquinting clamped to 0..2")]
    private const float SquintMaxDuration = 2f;

    [Provenance(ProvenanceKind.Decoded, "execNativeComputeSquinting game 0x10aafe2d: 0.15 per second below 1 s, then 0.25 (doubles 0x10c10ca8, 0x10c10ca0)")]
    private const float SquintFirstRate = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "as SquintFirstRate")]
    private const float SquintSecondRate = 0.25f;

    /// <summary>
    /// NativeComputeSquinting (rule 2): standing still and not looking around for 0.5 s with the torch off, the squint
    /// time grows by dt; the torch or a fast move takes dt off it, a slow move 0.3 dt (and lifts it to at least 1.2).
    /// The time stays within 0..2 s and SquintingFactor is 0.15 per second up to 1 s, then 0.25 per second (0.4 at 2 s).
    /// In the scope or without a torch the factor is 0.
    /// </summary>
    [UcNative("ASPlayerController::execNativeComputeSquinting", Exe = 0x10aafc80, Editor = 0x111e7280)]
    [PortOf("nat:SBase.SPlayerController.NativeComputeSquinting", Spec = "spec/equipment/merc-eye-adaptation.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10aafc80: bBinocularVision (bit 25 of +0xc38), SPAWNDEFENSE.Torche (+0xb74) and its bInUse, DurationStatic (+0xa94), DurationForSquinting (+0xa98), SquintingFactor (+0x518)")]
    public void NativeComputeSquinting(float dt)
    {
        if (bBinocularVision || Pawn is not SPAWNDEFENSE { Torche: { } torch } merc)
        {
            SquintingFactor = 0f;
            return;
        }

        float d = DurationForSquinting;
        if (torch.bInUse)
        {
            DurationStatic = 0f;
            d -= dt;
        }
        else
        {
            float speed = merc.Velocity.Size();
            bool still = speed < SquintStillSpeed && SvmMath.Abs(SvmMath.Trunc(aLookUp)) <= SquintStillLook &&
                SvmMath.Abs(SvmMath.Trunc(aTurn)) <= SquintStillLook;
            if (still)
            {
                DurationStatic += dt;
                if (DurationStatic > SquintStillDelay)
                {
                    d += dt;
                }
            }
            else
            {
                DurationStatic = 0f;
                if (speed < SquintSlowSpeed)
                {
                    d -= dt * SquintSlowRate;
                    if (d < SquintSlowFloor)
                    {
                        d = SquintSlowFloor;
                    }
                }
                else
                {
                    d -= dt;
                }
            }
        }

        DurationForSquinting = d < 0f ? 0f : d > SquintMaxDuration ? SquintMaxDuration : d;
        SquintingFactor = DurationForSquinting < 1f
            ? DurationForSquinting * SquintFirstRate
            : ((DurationForSquinting - 1f) * SquintSecondRate) + SquintFirstRate;
    }
}
