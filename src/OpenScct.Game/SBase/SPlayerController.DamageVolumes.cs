using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SPlayerController
{
    private static readonly Name DamageVolumeGrabbed = new("s_GrabbedFromLedge_Begin");
    private static readonly Name DamageVolumeFalling = new("s_FallAfterGrabbedFromLedge");

    /// <summary>Damage volumes ignore ledge-grab victims, dead players and players whose round has ended.</summary>
    [Uc("SBase.SPlayerController.bIsDamageVolumeSensitive", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
    [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.bIsDamageVolumeSensitive", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [Uc("SBase.SPlayerController.s_FallAfterGrabbedFromLedge.bIsDamageVolumeSensitive", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [Uc("SBase.SPlayerController.s_Dead.bIsDamageVolumeSensitive", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [Uc("SBase.SPlayerController.GameEnded.bIsDamageVolumeSensitive", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:bIsDamageVolumeSensitive", Spec = "spec/devices/damage-volume.md#native-damage-tick-decoded-natives2-damagemd-rule-6")]
    public virtual bool bIsDamageVolumeSensitive() =>
        !IsInState(CNAME_s_Dead) && !IsInState(CNAME_GameEnded) &&
        !IsInState(DamageVolumeGrabbed) && !IsInState(DamageVolumeFalling);
}
