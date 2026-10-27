using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadHeartBeatSensor: the spy's heartbeat sensor (spec/equipment/heartbeat-sensor.md). While it is the
/// selected gadget and the Shocker is drawn, every pawn within 2200 uu in a 60 degree cone of the view is marked
/// (bDetectedByHBS) for the radar.
/// </summary>
public partial class SGadHeartBeatSensor
{
    /// <summary>No hint on selection.</summary>
    [Uc("SBase.SGadHeartBeatSensor.Load", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGadHeartBeatSensor:Load", Spec = "spec/equipment/heartbeat-sensor.md#rules")]
    public override void Load()
    {
    }

    /// <summary>The scan every tick, on every machine that has the sensor.</summary>
    [Uc("SBase.SGadHeartBeatSensor.Tick", Sha1 = "a716e9dd6edc7ae14303b05be2acb316e179a61b")]
    [PortOf("uc:SBase.SGadHeartBeatSensor:Tick", Spec = "spec/equipment/heartbeat-sensor.md#rules")]
    protected override void Tick(float DeltaTime) => CheckForPawn();

    /// <summary>
    /// CheckForPawn (native 1821, decoded 0x10abd0e0): with the sensor selected and the weapon drawn, marks the pawns
    /// in range and in the view cone, clears the others; StateOfDetect 1 when something was found, else 2. False when
    /// it did not scan (the marks stay).
    /// </summary>
    [UcNative("ASGadHeartBeatSensor::execCheckForPawn", Exe = 0x10abd0e0)]
    [PortOf("nat:SBase.SGadHeartBeatSensor.CheckForPawn", Spec = "spec/equipment/heartbeat-sensor.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "heartbeat-sensor.md rule 2: squared distance under fDetectionDistance^2, cosine above fDemiAngle, no team or sight test")]
    public bool CheckForPawn()
    {
        if (OwnerPawn is not { } owner || !ReferenceEquals(owner.SelectedGadget, this) || owner.WeaponLoaded == 0)
        {
            return false;
        }

        StateOfDetect = K.HbsIdle;
        FVector view = (owner.Controller?.Rotation ?? owner.Rotation).Vector();
        float range2 = fDetectionDistance * fDetectionDistance;
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (ReferenceEquals(p, owner))
            {
                continue;
            }

            FVector d = p.Location - owner.Location;
            bool found = d.SizeSquared() < range2 && (view | d.SafeNormal()) > fDemiAngle;
            p.bDetectedByHBS = found;
            if (found)
            {
                StateOfDetect = K.HbsDetected;
            }
        }

        if (StateOfDetect != K.HbsDetected)
        {
            StateOfDetect = K.HbsNothing;
        }

        return true;
    }
}
