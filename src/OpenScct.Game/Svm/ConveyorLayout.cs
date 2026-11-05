using OpenScct.Core;
using OpenScct.Physics.Conveyors;
using OpenScct.Provenance;

namespace OpenScct.Game.Svm;

/// <summary>
/// One item riding a conveyor at some moment: where it is on the surface, the belt's direction there (yaw of travel
/// and pitch of the climb), the item's own seeded turn about the belt's up, its size (0 when faded out), and its mesh:
/// an index into the <see cref="Conveyor.ItemMeshes"/> of <paramref name="Origin"/>, the conveyor it came from.
/// </summary>
public readonly record struct ConveyorItemPlace(FVector Location, FRotator Belt, int Spin, float Scale, int Mesh, Conveyor Origin);

/// <summary>
/// The parts of a conveyor's look the map builder and the game both need to agree on
/// (spec/additions/conveyors.md#look): the names and texture scales of the moving parts the editor builds into
/// render/world.glb, and how many items a conveyor starts with. <see cref="ConveyorItemFlow"/> moves the items and
/// <see cref="ConveyorSteps"/> the escalator steps, for the game, every client and the editor's preview alike.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: design values, spec/additions/conveyors.md#look")]
public static class ConveyorLayout
{
    /// <summary>Instance name suffix of the belt surface (frame and treadmill looks); its texture V runs along the path.</summary>
    public const string BeltPart = ".Belt";

    /// <summary>Instance name suffix of the steps (escalator look); V runs along the path.</summary>
    public const string StepsPart = ".Steps";

    /// <summary>Instance name suffix of the drums at a frame's open ends; U runs once round each drum.</summary>
    public const string RollersPart = ".Rollers";

    /// <summary>uu of belt one texture repeat covers along the path.</summary>
    public const float BeltTile = 128f;

    /// <summary>Length of one escalator step (and of one texture repeat on the steps).</summary>
    public const float StepLength = 40f;

    /// <summary>
    /// Highest rise of one escalator step the editor's map check accepts without a warning (a 30 degree slope). Steps
    /// are always <see cref="StepLength"/> long, so a steeper escalator gets taller steps.
    /// </summary>
    public const float StepRise = 20f;

    /// <summary>Radius of a frame's end drums: half of <see cref="FrameDepth"/>, so a drum closes the frame's end.</summary>
    public const float RollerRadius = 16f;

    /// <summary>Height of the frame's side walls above the belt.</summary>
    public const float WallHeight = 24f;

    public const float WallThickness = 8f;

    /// <summary>Depth of the frame under the belt surface.</summary>
    public const float FrameDepth = 32f;

    /// <summary>Height of the treadmill's low edges.</summary>
    public const float EdgeHeight = 4f;

    /// <summary>Height of an escalator's balustrades.</summary>
    public const float BalustradeHeight = 96f;

    /// <summary>The number of items a conveyor starts with, spread along it (none without meshes).</summary>
    public static int ItemCount(Conveyor conveyor, ConveyorPath path)
    {
        if (conveyor.ItemMeshes.Count == 0 || conveyor.ItemSpacing <= 0f || path.Length <= 0f)
        {
            return 0;
        }

        return SvmMath.Max(1, SvmMath.Floor(path.Length / conveyor.ItemSpacing));
    }

    // A small integer hash (no RNG state): the same items on every machine and in the editor.
    internal static uint Hash(uint a, uint b)
    {
        uint x = (a * 0x9E3779B1u) ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
        x ^= x >> 16;
        x *= 0x85EBCA6Bu;
        x ^= x >> 13;
        x *= 0xC2B2AE35u;
        x ^= x >> 16;
        return x;
    }

    internal static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
}
