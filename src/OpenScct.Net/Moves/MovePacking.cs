using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net.Moves;

/// <summary>
/// The packed values of the move protocol: View, the pawn action pair, DiversInfos, the old-move acceleration and
/// the packed words of Core.Object (spec/native/net-moves.md rules 7 to 10, spec/native/net-replication.md rule 27).
/// </summary>
public static class MovePacking
{
    /// <summary>
    /// <c>View = ((Pitch / 2) &amp; 0x7FFF) × 32768 + ((Yaw / 2) &amp; 0x7FFF)</c>, integer division rounding toward
    /// zero (rule 7).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 7, RealReplicateMove game 0x10aad000")]
    public static int PackView(int pitch, int yaw) => ((pitch / 2) & 0x7FFF) * 32768 + ((yaw / 2) & 0x7FFF);

    /// <summary>The server's reading of View: pitch and yaw lose their lowest bit and come back in 0..65534.</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 7, ProcessServerMove game 0x10aac4a0")]
    public static void UnpackView(int view, out int pitch, out int yaw)
    {
        pitch = 2 * (view / 32768);
        yaw = 2 * (view % 32768);
    }

    /// <summary>
    /// CompressActions (native 1823): <c>a1 | a2 &lt;&lt; 9 | a3 &lt;&lt; 18 | count &lt;&lt; 27</c> and
    /// <c>a4 | a5 &lt;&lt; 9 | a6 &lt;&lt; 18</c>; 9-bit codes, a 3-bit count, unused top bits 0 (rule 8).
    /// </summary>
    [UcNative("ASBasePawn::execCompressActions", Exe = 0x10ac20e0, Editor = 0x111fa7f0)]
    public static void CompressActions(ReadOnlySpan<int> actions, int count, out int val0, out int val1)
    {
        Span<int> a = stackalloc int[6];
        for (int i = 0; i < 6 && i < actions.Length; i++)
        {
            a[i] = actions[i] & 0x1FF;
        }

        val0 = a[0] | (a[1] << 9) | (a[2] << 18) | ((count & 7) << 27);
        val1 = a[3] | (a[4] << 9) | (a[5] << 18);
    }

    /// <summary>UnCompressActions (native 1824): the six codes and the count of <see cref="CompressActions"/>.</summary>
    [UcNative("ASBasePawn::execUnCompressActions", Exe = 0x10ac2290, Editor = 0x111fa990)]
    public static int UnCompressActions(int val0, int val1, Span<int> actions)
    {
        if (actions.Length >= 6)
        {
            actions[0] = val0 & 0x1FF;
            actions[1] = (val0 >> 9) & 0x1FF;
            actions[2] = (val0 >> 18) & 0x1FF;
            actions[3] = val1 & 0x1FF;
            actions[4] = (val1 >> 9) & 0x1FF;
            actions[5] = (val1 >> 18) & 0x1FF;
        }

        return (val0 >> 27) & 7;
    }

    /// <summary>
    /// DiversInfos (rule 9): <c>Physics &lt;&lt; 4 | (bPressedJump ? 8 : 0) | speed | ZoneConfig</c>, speed being 4
    /// for a charging merc and for a spy with adrenaline. The original writes the spy's flag as 1, into the zone bits,
    /// so its server never raised the spy's cap and read a wrong zone: every client spy hit while running was pulled
    /// back about 25 uu every 0.3 s for 5 s. A listen host's own spy never met it; here every player of a solo match
    /// is a connected client, so the flag goes in the speed bit the server reads (a fix, spec/native/net-moves.md).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 9")]
    [Provenance(ProvenanceKind.Estimated, "the spy's adrenaline flag in the speed bit (4) instead of the zone bits (1): a fix of the decoded packing")]
    public static byte DiversInfos(EPhysics physics, bool pressedJump, bool merc, bool charging, bool adrenaline, byte zoneConfig)
    {
        int speed = merc ? (charging ? 4 : 0) : (adrenaline ? 4 : 0);
        return (byte)((((int)physics & 0xF) << 4) | (pressedJump ? 8 : 0) | speed | (zoneConfig & 3));
    }

    public static EPhysics DiversPhysics(byte divers) => (EPhysics)(divers >> 4);

    public static int DiversZone(byte divers) => divers & 3;

    public static bool DiversSpeed(byte divers) => (divers & 4) != 0;

    public static bool DiversJump(byte divers) => (divers & 8) != 0;

    /// <summary>
    /// The old-move acceleration (rule 10): <paramref name="accel"/> is the old acceleration itself (0.1 ×
    /// vOldAccel). X and Y as <c>trunc(|a|)</c> with the two low bits cleared and a sign bit above (14-bit fields at
    /// bits 18 and 6), Z as <c>trunc(|a.Z|) / 2</c> with a sign bit (8 bits at bit 0): 4 uu/s² on X and Y over ±8188,
    /// 2 on Z over ±254. The layout is the original's: the client packs <c>x &lt;&lt; 18 | y &lt;&lt; 6 | z</c>
    /// (game 0x10aad000) and the server reads X from bits 20..30 with the sign at 31, Y from bits 8..18 with the sign
    /// at 19, Z from bits 0..6 with the sign at 7 (game 0x10aac4a0 and 0x10aaec30). The original neither clamps an
    /// out of range component (it bleeds into the next field) nor clears the sign of a zero one; the clamp here only
    /// changes values the original would corrupt.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 10; layout of game 0x10aad000 (pack) and 0x10aac4a0 (unpack)")]
    public static int PackOldAccel(FVector accel)
    {
        uint x = Field14(accel.X);
        uint y = Field14(accel.Y);
        uint zMag = (uint)Math.Min(254, SvmMath.Trunc(SvmMath.Abs(accel.Z))) / 2;
        uint z = (zMag & 0x7F) | (accel.Z < 0f && zMag != 0 ? 0x80u : 0u);
        return (int)((x << 18) | (y << 6) | z);
    }

    private static uint Field14(float v)
    {
        uint mag = (uint)Math.Min(8188, SvmMath.Trunc(SvmMath.Abs(v))) & 0x1FFCu;
        return mag | (v < 0f && mag != 0 ? 0x2000u : 0u);
    }

    /// <summary>The acceleration <see cref="PackOldAccel"/> packed.</summary>
    public static FVector UnpackOldAccel(int packed)
    {
        uint p = (uint)packed;
        uint x = (p >> 18) & 0x3FFF;
        uint y = (p >> 6) & 0x3FFF;
        uint z = p & 0xFF;
        float fx = (x & 0x1FFC) * ((x & 0x2000) != 0 ? -1f : 1f);
        float fy = (y & 0x1FFC) * ((y & 0x2000) != 0 ? -1f : 1f);
        float fz = (z & 0x7F) * 2f * ((z & 0x80) != 0 ? -1f : 1f);
        return new FVector(fx, fy, fz);
    }

    /// <summary>The old-move delta in 0.5 ms units, or -1 when it does not fit a byte (rule 3.4).</summary>
    public static int OldTimeDelta(float timeStamp, float lastMovementTime) =>
        lastMovementTime < 0f ? -1 : SvmMath.Trunc((timeStamp - lastMovementTime) * (1f / MoveK.OldTimeDeltaUnit)) is int d and >= 0 and < 256 ? d : -1;

    /// <summary>Core.Object.PackW (native 438): <c>(High &lt;&lt; 16) | (Low &amp; 0xFFFF)</c>.</summary>
    [UcNative("UObject::execPackW", Exe = 0x1094a680, Editor = 0x10ff09a0)]
    public static int PackW(int low, int high) => (high << 16) | (low & 0xFFFF);

    /// <summary>Core.Object.GetPackedLW (native 441): the low 16 bits, sign-extended.</summary>
    [UcNative("UObject::execGetPackedLW", Exe = 0x1094a6f0, Editor = 0x10ff0ac0)]
    public static int GetPackedLW(int packed) => (short)(packed & 0xFFFF);

    /// <summary>Core.Object.GetPackedHW (native 442): the high 16 bits, sign-extended.</summary>
    [UcNative("UObject::execGetPackedHW", Exe = 0x1094a750, Editor = 0x10ff0b10)]
    public static int GetPackedHW(int packed) => (short)((packed >> 16) & 0xFFFF);
}
