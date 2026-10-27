using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>The yaw and pitch window of a camera mode (SPlayerCam GetMinYaw, GetMaxYaw, GetMinPitch, GetMaxPitch).</summary>
public readonly record struct CameraWindow(int MinYaw, int MaxYaw, int MinPitch, int MaxPitch);

/// <summary>
/// The view windows of the 35 camera modes, which the limited-view states (ledge, pipe, zip line, back to wall)
/// read through the controller's camera. The camera itself is local to each client; the simulation keeps the last
/// mode the movement asked for and reads its window from this table.
/// </summary>
public static class CameraWindows
{
    [Provenance(ProvenanceKind.Decoded, "spec/native/camera.md mode table (InitCamParam, editor 0x111f71d0), yaw and pitch limits of entry 0")]
    private static readonly CameraWindow[] s_windows =
    [
        new(-65536, 65536, -13000, 12000), // 0 Walking
        new(-65536, 65536, -13000, 12000), // 1 WalkingCr
        new(-65536, 65536, -13000, 12000), // 2 Running
        new(-65536, 65536, -13000, 12000), // 3 RunningCr
        new(-65536, 65536, -15000, 15000), // 4 SecondPerson
        new(-65536, 65536, -15000, 15000), // 5 SecondPersonCr
        new(-65536, 65536, -13000, 12000), // 6 Grab
        new(-65536, 65536, -5000, 7500), // 7 GrabFP
        new(-65536, 65536, -13000, 12000), // 8 Carry
        new(-65536, 65536, -13000, 12000), // 9 CarryCr
        new(-65536, 65536, -14000, 14000), // 10 Throw
        new(-65536, 65536, -14000, 14000), // 11 ThrowCr
        new(-65536, 65536, -15000, 15000), // 12 Sniping
        new(-20000, 20000, -13000, 12000), // 13 HSphere
        new(-65536, 65536, -13000, 10000), // 14 FSphere
        new(-65536, 65536, -13000, 10000), // 15 HOH
        new(-65536, 65536, -13000, 12000), // 16 HOHFU
        new(-10000, 10000, -12000, 5000), // 17 HOHFP
        new(-20000, 20000, -13000, 13000), // 18 BTW
        new(-20000, 20000, -13000, 13000), // 19 BTWPeakLeft
        new(-20000, 20000, -13000, 13000), // 20 BTWPeakRight
        new(-12000, 2000, -12000, 12000), // 21 BTWLeftFP
        new(-2000, 12000, -12000, 12000), // 22 BTWRightFP
        new(-20000, 20000, -13000, 12000), // 23 Rapel
        new(-12000, 12000, -8000, 13000), // 24 RapelFP
        new(0, 16000, -12000, 12000), // 25 DoorPeekRight
        new(-16000, 0, -12000, 12000), // 26 DoorPeekLeft
        new(-65536, 65536, -13000, 10000), // 27 SplitJump
        new(-65536, 65536, -15000, 15000), // 28 SplitJumpFP
        new(-65536, 65536, -15000, 15000), // 29 FirstPerson
        new(-65535, 65535, -15000, 15000), // 30 OnLedge
        new(-12000, 2000, -12000, 12000), // 31 HOHFPHanged
        new(-32767, 32768, -15000, 14000), // 32 OnLadder
        new(-32767, 32768, -15000, 14000), // 33 NoTargetCol
        new(-65536, 65536, -13000, 12000), // 34 SmoothTransitionCentered
    ];

    /// <summary>The window of <paramref name="mode"/> (the Walking window for an unknown value).</summary>
    public static CameraWindow WindowOf(ECamMode mode) => (int)mode < s_windows.Length ? s_windows[(int)mode] : s_windows[0];
}
