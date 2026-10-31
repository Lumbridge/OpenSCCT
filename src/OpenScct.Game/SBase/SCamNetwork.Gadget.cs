using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SCamNetwork as the merc's CamNet view: the camera's tick (energy, vision, self view, angles, sounds), its shot
// energy and its (unreachable) shot. Written from spec/equipment/camera-network-gadget.md rules 7 to 11 and
// spec/devices/cam-network.md rule 10. The ring, the limits and the vision skins are in SCamNetwork.cs.
public partial class SCamNetwork
{
    /// <summary>
    /// The camera's tick on every machine: energy refill, a changed replicated vision mode applied, the globe drawn
    /// small for its local users, the replicated angles for viewers who do not steer, the zoom and turn sounds, the
    /// actor rotation. It replaces the sticky cam and orientable ticks (the original does not call them).
    /// </summary>
    [Uc("SBase.SCamNetwork.Tick", Sha1 = "ab5f5d73c41a0b6528b830a0d8a461d86d045b74")]
    [PortOf("uc:SBase.SCamNetwork:Tick", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            DisabledSeconds = System.Math.Max(0, (int)System.MathF.Ceiling(_shotDisabledUntil - Level.TimeSeconds));
        }

        ComputeEnergyLevel(DeltaTime);
        UpdateDisabledLens();
        if (iReplicatedVisionMode != iCurrentVisionMode)
        {
            SwitchToVision(iReplicatedVisionMode);
        }

        var local = Level.GetLocalPlayerController() as SPlayerController;
        bool controllerView = aUsedByPawn?.Controller is SPlayerController user && user.IAmOnClient() &&
            user.IsInState(GadgetStates.s_UseObject_CamNetwork);
        bool passengerView = local is { SBPawn: { } lp } && local.IAmOnClient() && ReferenceEquals(lp.UsedObject, this) &&
            !ReferenceEquals(lp, aUsedByPawn) && local.IsInState(GadgetStates.s_UseObject_CamNetwork);
        SetSelfViewScale(controllerView || passengerView);

        if (local is { SBPawn: { } viewer } && (!ReferenceEquals(viewer.UsedObject, this) || !ReferenceEquals(aUsedByPawn, local.Pawn)))
        {
            AdoptReplicatedAngles();
        }

        float now = Level.TimeSeconds;
        if (fVisionFOV != fOldVisionFOV && !ZoomSoundIn.IsNone && !ZoomSoundOut.IsNone)
        {
            AmbientSound = fVisionFOV > fOldVisionFOV ? ZoomSoundIn : ZoomSoundOut;
            LastSoundUpdate = now;
        }
        else
        {
            if (now > LastSoundUpdate + K.CamSoundLinger)
            {
                AmbientSound = AssetRef.None;
                StopSound(ZoomSoundOut);
                StopSound(ZoomSoundIn);
            }

            if (bPlayRotationSound)
            {
                AmbientSound = RotationSound;
                LastSoundUpdate = now;
                bPlayRotationSound = false;
            }
            else if (now > LastSoundUpdate + K.CamSoundLinger)
            {
                AmbientSound = AssetRef.None;
                StopSound(RotationSound);
            }
        }

        bPlayRotationSound = false;
        fOldVisionFOV = fVisionFOV;
        Rotation = new FRotator(iPitch, iYaw, Rotation.Roll);
    }

    /// <summary>
    /// ComputeEnergyLevel (native, decoded 0x10ae8fb0): the refill clock advances; each whole second it crosses adds
    /// EnergyRefillAmount while the level is below the maximum (not clamped).
    /// </summary>
    [UcNative("ASCamNetwork::execComputeEnergyLevel", Exe = 0x10ae8fb0)]
    [PortOf("nat:SBase.SCamNetwork.ComputeEnergyLevel", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public void ComputeEnergyLevel(float DeltaTime)
    {
        int before = SvmMath.Trunc(EnergyRefill);
        EnergyRefill += DeltaTime;
        if (SvmMath.Trunc(EnergyRefill) > before && EnergyLevel < EnergyLevelMax)
        {
            EnergyLevel += EnergyRefillAmount;
        }
    }

    /// <summary>AuthorizeEnergy (native, decoded 0x10ae9510): spends <paramref name="Amount"/> when there is that much.</summary>
    [UcNative("ASCamNetwork::execAuthorizeEnergy", Exe = 0x10ae9510)]
    [PortOf("nat:SBase.SCamNetwork.AuthorizeEnergy", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public bool AuthorizeEnergy(int Amount)
    {
        if (EnergyLevel < Amount)
        {
            return false;
        }

        EnergyLevel -= Amount;
        return true;
    }

    /// <summary>
    /// The camera's shot (rule 10): with energy, the shot sound and the server's shot; else the empty sound after the
    /// reload time. Nothing in 4.0 calls it (the camera view ignores Fire).
    /// </summary>
    [Uc("SBase.SCamNetwork.Fire", Sha1 = "81730f9dfeee0356a37b033080245dd71b7208a9")]
    [PortOf("uc:SBase.SCamNetwork:Fire", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public void Fire(float F)
    {
        if (AuthorizeEnergy(EnergyNeeded))
        {
            PlaySound(FireSingleShotSound, ESoundSlot.SLOT_None, 1f, false, K.CamShotSoundRadius);
            (aUsedByPawn?.Controller as SPlayerController)?.CamFire(F, this);
        }
        else if (LastShot + ReloadTime < Level.TimeSeconds)
        {
            PlaySound(EmptySound, ESoundSlot.SLOT_None, 1f, false, K.CamEmptySoundRadius);
        }

        LastShot = Level.TimeSeconds;
    }

    /// <summary>
    /// ServerFire (native, decoded 0x10ae9030; unreachable): a sticky shot along the camera's forward axis from 20 uu
    /// ahead; the first pawn or gameplay object hit takes BaseDamage of the sticky shocker type.
    /// </summary>
    [UcNative("ASCamNetwork::execServerFire", Exe = 0x10ae9030)]
    [PortOf("nat:SBase.SCamNetwork.ServerFire", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The bone trace, the offset start of a pawn flag and the impact effect are reduced to a line and the damage")]
    public void ServerFire(float F)
    {
        Rotation.GetAxes(out FVector x, out _, out _);
        FVector start = Location + x * K.CamShotStart;
        if (!GadgetWorld.Line(this, start + x * K.CamShotLength, start, ETraceFlags.TRACE_AllColliding, out FCheckResult hit) ||
            hit.Actor is null or LevelInfo)
        {
            return;
        }

        Pawn? by = aUsedByPawn;
        FVector momentum = (hit.Location - start) * 1000f;
        if (hit.Actor is SBasePawn pawn)
        {
            pawn.Controller?.PawnTakeDamage(BaseDamage, by, hit.Location, momentum, SDamageType.SDTStickyShocker, 0, 0);
        }
        else if (hit.Actor is SBasicGamePlayObject gpo)
        {
            gpo.eventTakeDamage(BaseDamage, by, hit.Location, -hit.Normal * 1000f, SDamageType.SDTStickyShocker);
        }
    }
}
