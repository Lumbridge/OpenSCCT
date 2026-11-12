using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// OpenSCCT: keeping a client's copy of a mover in step with the server's (docs/NETCODE.md, "Movers on clients").
public partial class Mover
{
    /// <summary>
    /// The server's Rotation of a spinning mover (PHYS_Rotating with bFixedRotationDir), sampled about once a second
    /// and sent to clients. The original sends nothing: every machine turns its own copy from its own ticks, so a
    /// client that started later or dropped ticks in a long frame stays out of phase for the rest of the round, and
    /// a player walking on the deck is corrected to a spot turned by that phase.
    /// </summary>
    public FRotator SpinSample;

    private float _spinSampleAt;
    private bool _spinSampled;

    private bool Spins => bFixedRotationDir && Physics == EPhysics.PHYS_Rotating;

    /// <summary>Server: samples the rotation while the mover spins, and once more after it stops.</summary>
    private void SampleSpin()
    {
        if (Level.NetMode == ENetMode.NM_Client || bDeleteMe)
        {
            return;
        }

        float now = Level.TimeSeconds;
        if (Spins)
        {
            // A clock that went back (a new round) samples at once.
            if (_spinSampled && now >= _spinSampleAt - MoverK.SpinSampleSeconds && now < _spinSampleAt)
            {
                return;
            }

            _spinSampleAt = now + MoverK.SpinSampleSeconds;
            _spinSampled = true;
        }
        else if (_spinSampled)
        {
            _spinSampled = false;
        }
        else
        {
            return;
        }

        SpinSample = new FRotator(Rotation.Pitch & 0xffff, Rotation.Yaw & 0xffff, Rotation.Roll & 0xffff);
    }

    /// <summary>
    /// Client: a new <see cref="SpinSample"/> arrived. An axis further from it than a few ticks of spin is turned onto
    /// it, with a mover move, so whoever stands on the deck is carried round with it. The sample is as old as the
    /// network delay, so the client's copy ends up that much behind the server's, as its keyed movers are.
    /// A sample that was lost and sent again arrives later still and reads as a deck far behind (10 ticks of spin on
    /// a link with 120 ms ping and 2% loss, 36 uu for a rider at the rim, undone by the next sample a second later).
    /// So a spinning copy is turned only when two samples in a row ask for the same turn.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: the original never corrects a spinning mover on a client")]
    public void ClientSyncSpin()
    {
        FRotator r = Rotation;
        var error = new FRotator(
            SpinError(SpinSample.Pitch, r.Pitch, RotationRate.Pitch),
            SpinError(SpinSample.Yaw, r.Yaw, RotationRate.Yaw),
            SpinError(SpinSample.Roll, r.Roll, RotationRate.Roll));
        if (error == default)
        {
            _spinErrorSeen = false;
            return;
        }

        bool confirmed = _spinErrorSeen &&
            SpinError(error.Pitch, _spinError.Pitch, RotationRate.Pitch) == 0 &&
            SpinError(error.Yaw, _spinError.Yaw, RotationRate.Yaw) == 0 &&
            SpinError(error.Roll, _spinError.Roll, RotationRate.Roll) == 0;
        if (Spins && !confirmed)
        {
            _spinError = error;
            _spinErrorSeen = true;
            return;
        }

        _spinErrorSeen = false;
        MoveBrush(default, new FRotator((r.Pitch + error.Pitch) & 0xffff, (r.Yaw + error.Yaw) & 0xffff, (r.Roll + error.Roll) & 0xffff));
    }

    /// <summary>Client: the turn the last sample asked for, kept until the next one confirms it.</summary>
    private FRotator _spinError;
    private bool _spinErrorSeen;

    private static int SpinError(int sample, int current, int rate)
    {
        int error = FRotator.NormalizeAxis(sample - current);
        float slack = SvmMath.Max(MoverK.SpinSlack, SvmMath.Abs(rate) * MoverK.SpinSlackSeconds);
        return SvmMath.Abs(error) > slack ? error : 0;
    }

    /// <summary>
    /// Client: whether the leg the server just announced, from key <paramref name="previous"/> to key
    /// <paramref name="target"/>, is one this mover already runs: it reached the key before on its own and carried
    /// on (KeyFrameReached), and the server's start of the same leg arrives a network delay later. Starting it again
    /// from SimOldPos would pull the mover, and its riders, back by that delay every leg. The leg is kept when it has
    /// the same start and rate and is no more than <see cref="MoverK.ClientLegLeadSeconds"/> ahead of the announced
    /// alpha; a client behind the server, or on another leg, starts the server's.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: the original restarts the leg on every changed SimInterpolate (editor 0x110b3020)")]
    public bool ClientAlreadyOnLeg(int previous, int target)
    {
        if (!bInterpolating || previous == target || previous != PreviousKey || target != CurrentKey || PhysRate <= 0f)
        {
            return false;
        }

        if ((SimOldPos - OldPos).Size() > MoverK.ClientSnapDistance || WireRate(PhysRate) != SvmMath.Floor(SimInterpolate.Y + 0.5f))
        {
            return false;
        }

        float ahead = PhysAlpha - (SimInterpolate.X * 0.01f);
        return ahead >= -0.005f && ahead <= PhysRate * MoverK.ClientLegLeadSeconds;
    }
}
