using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SSmokeEmitter: the smoke cloud (smoke spec rules 3 to 5). It lives 10 s; every tick, on each machine with
/// local players, a player whose pawn is inside RadiusEffect plus its collision radius goes in smoke.
/// </summary>
public partial class SSmokeEmitter
{
    [Uc("SBase.SSmokeEmitter.PostBeginPlay", Sha1 = "92d178a71c19bac54fa9760320ae884a1f1ffce5")]
    [PortOf("uc:SBase.SSmokeEmitter:PostBeginPlay", Spec = "spec/equipment/smoke-grenade.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (SoundOn)
        {
            ToggleSoundOn();
        }
    }

    /// <summary>The cloud's looping sound on.</summary>
    [Uc("SBase.SSmokeEmitter.ToggleSoundOn", Sha1 = "7dd8bfa392cd7621b466546eeeab3e552cd2efe1")]
    [PortOf("uc:SBase.SSmokeEmitter:ToggleSoundOn", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public void ToggleSoundOn() => AmbientSound = SoundEffect;

    /// <summary>The cloud's looping sound off.</summary>
    [Uc("SBase.SSmokeEmitter.ToggleSoundOff", Sha1 = "1dfdfa78f86e0e056e486e2dfb5e5bfb5a76e800")]
    [PortOf("uc:SBase.SSmokeEmitter:ToggleSoundOff", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public void ToggleSoundOff() => AmbientSound = AssetRef.None;

    /// <summary>A player inside the cloud: GoInSmoke with the cloud's cough, faint and blur values.</summary>
    [Uc("SBase.SSmokeEmitter.DetectedSPlayerController", Sha1 = "1634735716b2a349d506f6fea4753702d4ff2ce0")]
    [PortOf("uc:SBase.SSmokeEmitter:DetectedSPlayerController", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public void DetectedSPlayerController(SPlayerController PC) =>
        PC.eventGoInSmoke(TimeBaseToCought, TimeBaseToFaint, TimeBaseToJamming, ForceBaseToJamming);

    /// <summary>
    /// The emitter's native tick (prior decode 0x10a9a130): every player handled on this machine whose pawn is
    /// within RadiusEffect plus its collision radius (3D) is detected. A dedicated server has no local players.
    /// </summary>
    [UcNative("ASSmokeEmitter::Tick", Exe = 0x10a9a130)]
    [PortOf("eng:ASSmokeEmitter::Tick", Spec = "spec/equipment/smoke-grenade.md#rules")]
    [PortOf("eng:ASSmokeEmitter::Tick", Spec = "spec/equipment/gas-mask.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Detection runs for every controller not on another machine (the local players of the original)")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (pc.Pawn is not { } pawn)
            {
                continue;
            }

            if (FVector.Dist(pawn.Location, Location) <= RadiusEffect + pawn.CollisionRadius)
            {
                if (Role == ENetRole.ROLE_Authority)
                {
                    pc.ObserveGasActivity(this, TimeBaseToCought, TimeBaseToFaint, stickyCamera: false);
                }

                // Observation also invalidates a pending episode while the mask protects the victim.
                if (pawn is SBasePawn { bGasMask: true })
                {
                    continue;
                }

                if (!pc.IsRemotePlayer && Level.NetMode != ENetMode.NM_DedicatedServer)
                {
                    DetectedSPlayerController(pc);
                }
            }
        }
    }
}
