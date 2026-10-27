using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn script Tick and the health part of its native NativeTick (native 1403): the spy's health refill
// from the pool and the poison drain. Written from spec/combat/health.md rules 3 and 4 and
// spec/native/natives2-damage.md rule 8.
public partial class SBasePawn
{
    /// <summary>
    /// SBasePawn.Tick: NativeTick first. The rest of the script tick (the server's spy bullet expiry) is not ported
    /// here.
    /// </summary>
    [Uc("SBase.SBasePawn.Tick", Sha1 = "8604928c46b3e97d1540d25c1dad27e6d65d6925")]
    [PortOf("uc:SBase.SBasePawn:Tick", Spec = "spec/combat/health.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        NativeTickHealth(DeltaTime);
    }

    /// <summary>
    /// The health part of ASBasePawn::execNativeTick (authority only). Poisoned and alive: the drain of
    /// <c>L = dt * fPoisonned_HealthLostPerSecond</c> while <c>Health &gt;= int(bHealthDecrease - 2L)</c>, the killing
    /// PawnTakeDamage(1500) at zero; other damage that lowered Health resets bHealthDecrease. Otherwise
    /// bHealthDecrease follows Health and a spy with 0 &lt; Health &lt; 100 that stands still and is not lying counts
    /// HealthRefill; past MinTimeHealthRefill, each whole second moves min(NbPointsRefilledPerSecond, pool) into
    /// Health (cap 100).
    /// </summary>
    [UcNative("ASBasePawn::execNativeTick", Exe = 0x10ae3c20, Editor = 0x1120ff60)]
    [PortOf("nat:SBase.SBasePawn.NativeTick", Spec = "spec/combat/health.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "execNativeTick editor 0x1120ff60..0x112102e9: poison drain, refill gate |V|^2 < 0.01 and not lying, int() is the _ftol at 0x1123fb80, cap 100")]
    public void NativeTickHealth(float DeltaTime)
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        if (bPoisonned && Health > 0)
        {
            int health = Health;
            float lost = DeltaTime * fPoisonned_HealthLostPerSecond;
            if (health >= SvmMath.Trunc(bHealthDecrease - (lost + lost)))
            {
                bHealthDecrease -= lost;
                Health = SvmMath.Trunc(bHealthDecrease);
                if (Health <= 0)
                {
                    bPoisonned = false;
                    Health = 1;
                    Pawn? by = PoisonnedBy ?? Instigator;
                    Controller?.PawnTakeDamage(K.PoisonKillDamage, by, Location, new FVector(0f, 0f, -1f), SDamageType.SDTBase,
                        K.PoisonKillPill, 0);
                }
            }
            else if (health < SvmMath.Trunc(bHealthDecrease))
            {
                bHealthDecrease = health;
            }

            return;
        }

        bHealthDecrease = Health;
        if (this is not SPawnAttaque spy || Health <= 0 || Health >= K.RefillHealthCap)
        {
            return;
        }

        if (Velocity.SizeSquared() >= K.RefillStillSpeedSquared || bLyingOnGround)
        {
            spy.HealthRefill = 0f;
            return;
        }

        float before = spy.HealthRefill;
        spy.HealthRefill = DeltaTime + spy.HealthRefill;
        if (!(spy.HealthRefill > spy.MinTimeHealthRefill) || SvmMath.Trunc(spy.HealthRefill) <= SvmMath.Trunc(before))
        {
            return;
        }

        int n = SvmMath.Min(spy.NbPointsRefilledPerSecond, spy.NbPointsRefillable);
        Health += n;
        if (Health > K.RefillHealthCap)
        {
            Health = K.RefillHealthCap;
        }

        spy.NbPointsRefillable -= n;
        spy.BloodDropStartTime = 0f;
    }
}
