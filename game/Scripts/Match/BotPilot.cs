using OpenScct.Core;
using OpenScct.Server;

namespace OpenScctGame.Match;

/// <summary>
/// Scripted input for test walkers: a wide circle, a circle the other way, a weave and a strafing zig-zag.
/// Pure functions of the pilot number and the client tick, so every run walks the same paths.
/// </summary>
public static class BotPilot
{
    public const int PatternCount = 4;

    public static CubeInput For(int pilot, long tick) => (pilot % PatternCount) switch
    {
        0 => new CubeInput(127, 0, (ushort)(tick * 200)),
        1 => new CubeInput(127, 0, (ushort)(16384 - tick * 150)),
        2 => new CubeInput(127, 0, (ushort)(32768 + (int)(8000f * SvmMath.Sin((int)(tick * 273))))),
        _ => new CubeInput(80, (sbyte)(SvmMath.Sin((int)(tick * 364)) >= 0f ? 127 : -127), 49152),
    };
}
