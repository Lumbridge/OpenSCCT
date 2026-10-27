using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>
/// The 35 camera modes as InitCamParam leaves them (spec/native/camera.md, Data: the resolved table, each mode
/// copied from its parent and then overridden). Only entry 0 of offset and Distance is decoded; the solver resets
/// m_volSize to 0 before every solve, so entries 1 to 3 are never read and repeat entry 0 here.
/// </summary>
public static class CamParamTable
{
    [Flags]
    private enum F
    {
        None = 0,
        C = 1, // useColl
        Y = 2, // useCylColl
        T = 4, // useCollTarget
        P = 8, // usePitchCurve
        W = 16, // useTwig
        A = 32, // alignPawn
        G = 64, // useAngles
    }

    private readonly record struct Row(
        float Dist, float Ox, float Oy, float Oz, float Inter, float Coll, float CollCB, float OffsetSpeed, float TargetZ,
        F Flags, float TwigX, float TwigZ, int MinYaw, int MaxYaw, int MinPitch, int MaxPitch);

    private const int Full = 65536;

    [Provenance(ProvenanceKind.Decoded, "spec/native/camera.md mode table (InitCamParam, editor 0x111f71d0; matches the Bridge camparams dump)")]
    private static readonly Row[] s_rows =
    [
        new(250, 0, 0, 0, 400, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 0 Walking
        new(250, 0, 0, 0, 400, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 1 WalkingCr
        new(250, 0, 0, 0, 500, 500, 0, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 2 Running
        new(250, 0, 0, 0, 500, 500, 0, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 3 RunningCr
        new(30, 0, 27, 0, 400, 400, 0, 400, 100, F.C | F.T | F.W | F.A | F.G, 30, 30, -Full, Full, -15000, 15000), // 4 SecondPerson
        new(10, 0, 27, 20, 400, 400, 0, 400, 100, F.C | F.T | F.W | F.A | F.G, 30, 30, -Full, Full, -15000, 15000), // 5 SecondPersonCr
        new(250, 0, 0, 0, 400, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 6 Grab
        new(90, 0, 15, -5, 400, 400, 0, 400, 100, F.C | F.T | F.W | F.A | F.G, 30, 30, -Full, Full, -5000, 7500), // 7 GrabFP
        new(250, 0, 0, 0, 400, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 8 Carry
        new(250, 0, 0, 0, 400, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 9 CarryCr
        new(70, 0, 30, -10, 400, 400, 400, 400, 300, F.C | F.Y | F.T | F.W | F.A | F.G, 20, 20, -Full, Full, -14000, 14000), // 10 Throw
        new(70, 0, 30, -10, 400, 400, 400, 400, 300, F.C | F.Y | F.T | F.W | F.A | F.G, 20, 20, -Full, Full, -14000, 14000), // 11 ThrowCr
        new(0, 0, 25, 0, 800, 400, 0, 400, 200, F.C | F.T | F.A | F.G, 30, 30, -Full, Full, -15000, 15000), // 12 Sniping
        new(250, 0, 0, 0, 400, 400, 400, 400, 300, F.C | F.Y | F.T | F.P | F.W | F.G, 10, 30, -20000, 20000, -13000, 12000), // 13 HSphere
        new(250, 0, 0, 0, 400, 400, 400, 400, 300, F.C | F.Y | F.T | F.G, 0, 0, -Full, Full, -13000, 10000), // 14 FSphere
        new(250, 0, 0, 0, 400, 400, 400, 20, 300, F.C | F.Y | F.G, 40, 30, -Full, Full, -13000, 10000), // 15 HOH
        new(250, -40, 0, 0, 400, 400, 400, 20, 300, F.C | F.Y | F.P | F.G, 0, 0, -Full, Full, -13000, 12000), // 16 HOHFU
        new(27, 5, 0, 40, 200, 0, 0, 40, 30, F.W | F.G, 30, 30, -10000, 10000, -12000, 5000), // 17 HOHFP
        new(150, 15, 0, 0, 200, 400, 400, 125, 250, F.C | F.Y | F.P | F.G, 0, 0, -20000, 20000, -13000, 13000), // 18 BTW
        new(100, 15, -60, -10, 200, 400, 400, 125, 250, F.C | F.Y | F.P | F.G, 0, 0, -20000, 20000, -13000, 13000), // 19 BTWPeakLeft
        new(100, 15, 60, -10, 200, 400, 400, 125, 250, F.C | F.Y | F.P | F.G, 0, 0, -20000, 20000, -13000, 13000), // 20 BTWPeakRight
        new(50, 15, -60, -10, 200, 400, 400, 125, 250, F.C | F.Y | F.P | F.G, 0, 0, -12000, 2000, -12000, 12000), // 21 BTWLeftFP
        new(50, 15, 60, -10, 200, 400, 400, 125, 250, F.C | F.Y | F.P | F.G, 0, 0, -2000, 12000, -12000, 12000), // 22 BTWRightFP
        new(250, -30, 0, -20, 400, 400, 400, 400, 300, F.C | F.Y | F.T | F.P | F.W | F.G, 10, 30, -20000, 20000, -13000, 12000), // 23 Rapel
        new(45, -60, 25, -35, 800, 0, 0, 400, 300, F.W | F.G, 30, 30, -12000, 12000, -8000, 13000), // 24 RapelFP
        new(50, 45, 25, 5, 400, 400, 400, 400, 300, F.W | F.G, 20, 40, 0, 16000, -12000, 12000), // 25 DoorPeekRight
        new(50, 45, -25, 5, 400, 400, 400, 400, 300, F.W | F.G, 20, 40, -16000, 0, -12000, 12000), // 26 DoorPeekLeft
        new(250, 0, 0, -40, 50, 400, 400, 40, 300, F.C | F.Y | F.G, 0, 0, -Full, Full, -13000, 10000), // 27 SplitJump
        new(27, 5, 12, 0, 50, 400, 0, 40, 100, F.C | F.A | F.G, 30, 30, -Full, Full, -15000, 15000), // 28 SplitJumpFP
        new(0, 0, 0, 0, 800, 400, 0, 75, 200, F.C | F.W | F.A | F.G, 30, 30, -Full, Full, -15000, 15000), // 29 FirstPerson
        new(250, -40, 0, 10, 400, 400, 400, 20, 300, F.C | F.Y | F.P | F.W | F.G, 10, 30, -65535, 65535, -15000, 15000), // 30 OnLedge
        new(125, -30, -15, -120, 200, 400, 400, 40, 250, F.C | F.Y | F.P | F.G, 0, 0, -12000, 2000, -12000, 12000), // 31 HOHFPHanged
        new(250, -20, 0, 0, 400, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -32767, 32768, -15000, 14000), // 32 OnLadder
        new(250, 0, 0, 0, 200, 400, 400, 75, 300, F.C | F.Y | F.P | F.W | F.G, 20, 10, -32767, 32768, -15000, 14000), // 33 NoTargetCol
        new(250, 0, 0, 0, 50, 400, 400, 75, 50, F.C | F.Y | F.P | F.W | F.G, 20, 10, -Full, Full, -13000, 12000), // 34 SmoothTransitionCentered
    ];

    /// <summary>The parameters of <paramref name="mode"/> (all four volume entries filled from entry 0).</summary>
    public static FECamParam Get(ECamMode mode)
    {
        int index = (int)mode;
        if ((uint)index >= (uint)s_rows.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not one of the 35 camera modes.");
        }

        Row r = s_rows[index];
        var p = new FECamParam
        {
            minYaw = r.MinYaw,
            maxYaw = r.MaxYaw,
            minPitch = r.MinPitch,
            maxPitch = r.MaxPitch,
            interSpeed = r.Inter,
            collInterSpeed = r.Coll,
            collCBInterSpeed = r.CollCB,
            targetZSpeed = r.TargetZ,
            twigX = r.TwigX,
            twigZ = r.TwigZ,
            offsetSpeed = r.OffsetSpeed,
            useColl = Bit(r.Flags, F.C),
            useCylColl = Bit(r.Flags, F.Y),
            useCollTarget = Bit(r.Flags, F.T),
            usePitchCurve = Bit(r.Flags, F.P),
            useTwig = Bit(r.Flags, F.W),
            alignPawn = Bit(r.Flags, F.A),
            useAngles = Bit(r.Flags, F.G),
        };
        var offset = new FVector(r.Ox, r.Oy, r.Oz);
        for (int v = 0; v < 4; v++)
        {
            p.offset[v] = offset;
            p.Distance[v] = r.Dist;
        }

        return p;
    }

    /// <summary>InitCamParam: fills every mode of <paramref name="table"/> (m_camParam[35]).</summary>
    [UcNative("ASPlayerCam::InitCamParam", Editor = 0x111f71d0)]
    [PortOf("nat:SBase.SPlayerCam.InitCameraSettings", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    public static void Fill(FECamParam[] table)
    {
        ArgumentNullException.ThrowIfNull(table);
        int n = Math.Min(table.Length, s_rows.Length);
        for (int i = 0; i < n; i++)
        {
            table[i] = Get((ECamMode)i);
        }
    }

    private static int Bit(F flags, F bit) => (flags & bit) != 0 ? 1 : 0;
}
