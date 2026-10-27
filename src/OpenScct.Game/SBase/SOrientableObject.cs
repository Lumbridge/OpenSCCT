using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SOrientableObject: a prop a player can turn and zoom (sticky and network cameras), spec/devices/cam-network.md.</summary>
public partial class SOrientableObject
{
    /// <summary>The turn limits centre on the placed rotation; the view starts there; the activation sound plays.</summary>
    [Uc("SBase.SOrientableObject.PostNetBeginPlay", Sha1 = "ba74e06ce64f4178ecb08c061fa50023d13d354e")]
    [PortOf("uc:SBase.SOrientableObject:PostNetBeginPlay", Spec = "spec/devices/cam-network.md#turning-and-limits")]
    protected override void PostNetBeginPlay()
    {
        PitchConstraint.X = Rotation.Pitch;
        RollConstraint.X = Rotation.Roll;
        YawConstraint.X = Rotation.Yaw;
        iReplicatedPitch = Rotation.Pitch;
        iReplicatedYaw = Rotation.Yaw;
        iPitch = Rotation.Pitch;
        iYaw = Rotation.Yaw;
        PlaySound(ActivationSound, ESoundSlot.SLOT_None, 1f, false, DeviceK.CameraActivationRadius);
        LastSoundUpdate = 0f;
        base.PostNetBeginPlay();
    }

    /// <summary>Nothing sticks to a camera.</summary>
    [Uc("SBase.SOrientableObject.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SOrientableObject:AcceptStickableGPO", Spec = "spec/devices/cam-network.md#ring-and-entry")]
    public override bool AcceptStickableGPO() => false;
}
