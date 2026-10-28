using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFlicFlocEmitter: rain splashing on a pawn (spec/equipment/rain.md rule 4). Every machine spawns one per pawn
/// (SBasePawn.PostBeginPlay) on its head bone. Sub-emitter 0 splashes over the body while the pawn is in a rain volume,
/// sub-emitter 1 drips for 10 s after it leaves, sub-emitter 2 is a puff the timer fires every 2.5 s in the rain.
/// The particles live in the views: this actor keeps what the script writes into its sub-emitters (rates, respawn,
/// the particle budget, restarts and triggers), and the view applies it to the drawn copy.
/// </summary>
public partial class SFlicFlocEmitter
{
    /// <summary>The splash bone and the emitter's place relative to it.</summary>
    public static readonly Name HeadBone = new("B Head");

    [Provenance(ProvenanceKind.Decoded, "SFlicFlocEmitter.PreBeginPlay: SetRelativeLocation(vect(3, -8, 0))")]
    public static readonly FVector HeadOffset = new(3f, -8f, 0f);

    /// <summary>Rates and the particle budget when the rain actor is not an ERainVolume.</summary>
    public const float OtherRainRate = 20f;

    public const int OtherRainBudget = 80;

    /// <summary>The drip after leaving the rain, and the puff timer in the rain.</summary>
    public const float DripTime = 10f;

    public const float PuffPeriod = 2.5f;

    /// <summary>Sub-emitter 0 (body splashes): InitialParticlesPerSecond and ParticlesPerSecond.</summary>
    public float SplashRate { get; private set; }

    /// <summary>Sub-emitter 0: RespawnDeadParticles.</summary>
    public bool SplashRespawn { get; private set; }

    /// <summary>Sub-emitter 0: FlicFlocEmitter.VariableMaxParticles (a budget of spawns; 0 is no limit).</summary>
    public int SplashBudget { get; private set; }

    /// <summary>Sub-emitter 1 (drips after the rain): the rates.</summary>
    public float DripRate { get; private set; }

    /// <summary>Sub-emitter 1: RespawnDeadParticles.</summary>
    public bool DripRespawn { get; private set; }

    /// <summary>Sub-emitter 1: VariableMaxParticles.</summary>
    public int DripBudget { get; private set; }

    /// <summary>Times sub-emitter 0 was restarted (AllParticlesDead and WarmedUp cleared).</summary>
    public int SplashStarts { get; private set; }

    /// <summary>Times sub-emitter 1 was restarted.</summary>
    public int DripStarts { get; private set; }

    /// <summary>Times sub-emitter 2 was triggered (Timer).</summary>
    public int PuffTriggers { get; private set; }

    /// <summary>The pawn the emitter sits on.</summary>
    public SBasePawn? Pawn => Owner as SBasePawn;

    /// <summary>All sub-emitters off, hidden, on the owner's head 3 uu forward and 8 uu aside.</summary>
    [Uc("SBase.SFlicFlocEmitter.PreBeginPlay", Sha1 = "bf5dc860e88db3b76d6ecb761a8b8615d3875dfc")]
    [PortOf("uc:SBase.SFlicFlocEmitter:PreBeginPlay", Spec = "spec/equipment/rain.md#rules")]
    protected override void PreBeginPlay()
    {
        OutOfRainTime = -10f;
        oldrainvolume = null;
        SplashRate = 0f;
        SplashRespawn = false;
        DripRate = 0f;
        DripRespawn = false;
        bHidden = true;
        if (Pawn is { } pawn)
        {
            ObjectiveNatives.AttachToBone(pawn, this, HeadBone);
        }

        RelativeLocation = HeadOffset;
    }

    /// <summary>
    /// When the owner's rain volume changes (always for a spy, for a merc only on machines where he is not the local
    /// player's pawn): shown; into rain the splashes start at m_RainFalloff / 90 a second with a budget of
    /// m_RainFalloff / 10 (20 and 80 for another rain actor), the drips stop and the puff timer runs every 2.5 s; out
    /// of the rain the drips take the splashes' rates and budget for 10 s, the splashes and the timer stop. Past the
    /// drip time the drips stop.
    /// </summary>
    [Uc("SBase.SFlicFlocEmitter.Tick", Sha1 = "ee025511f2862fc708589e7f6169b502377f6324")]
    [PortOf("uc:SBase.SFlicFlocEmitter:Tick", Spec = "spec/equipment/rain.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The emitter goes once its pawn is gone (the original leaves it behind with no owner)")]
    protected override void Tick(float DeltaTime)
    {
        if (Pawn is not { bDeleteMe: false } pawn)
        {
            Destroy();
            return;
        }

        if (oldrainvolume != pawn.CurrentRainVolume &&
            (pawn is SPawnAttaque || !ReferenceEquals(Level.GetLocalPlayerController()?.Pawn, pawn)))
        {
            oldrainvolume = pawn.CurrentRainVolume;
            bHidden = false;
            if (oldrainvolume is not null)
            {
                if (oldrainvolume is ERainVolume rain)
                {
                    SplashRate = rain.m_RainFalloff / 90f;
                    SplashBudget = SvmMath.Trunc(rain.m_RainFalloff / 10f);
                }
                else
                {
                    SplashRate = OtherRainRate;
                    SplashBudget = OtherRainBudget;
                }

                SplashRespawn = true;
                SplashStarts++;
                DripRate = 0f;
                DripRespawn = false;
                SetTimer(PuffPeriod, true);
            }
            else
            {
                OutOfRainTime = Level.TimeSeconds + DripTime;
                DripRate = SplashRate;
                DripBudget = SplashBudget;
                DripRespawn = true;
                DripStarts++;
                SplashRate = 0f;
                SplashRespawn = false;
                SetTimer(0f, false);
            }
        }

        if (Level.TimeSeconds > OutOfRainTime)
        {
            DripRate = 0f;
            DripRespawn = false;
        }
    }

    /// <summary>While the pawn lives, the puff (sub-emitter 2) is triggered.</summary>
    [Uc("SBase.SFlicFlocEmitter.Timer", Sha1 = "c2d82db14b19163e1222c30d3a4b88505220fad5")]
    [PortOf("uc:SBase.SFlicFlocEmitter:Timer", Spec = "spec/equipment/rain.md#rules")]
    protected override void Timer()
    {
        if (Pawn is { Health: > 0 })
        {
            PuffTriggers++;
        }
    }
}
