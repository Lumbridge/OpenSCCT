using OpenScct.Core;
using OpenScct.Physics.Conveyors;
using OpenScct.Provenance;

namespace OpenScct.Game.Svm;

/// <summary>One item riding a conveyor at some moment: where it is, its turn and its size (0 when faded out).</summary>
public readonly record struct ConveyorItemPlace(FVector Location, int Yaw, float Scale, int Mesh);

/// <summary>
/// The parts of a conveyor's look the map builder and the game both need to agree on
/// (spec/additions/conveyors.md#look): the names and texture scales of the moving parts the editor builds into
/// render/world.glb, and where each item is at a given belt phase. The game, every client and the editor's preview
/// place items with the same function, so they all show the same thing.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: design values, spec/additions/conveyors.md#look")]
public static class ConveyorLayout
{
    /// <summary>Instance name suffix of the belt surface (frame and treadmill looks); its texture V runs along the path.</summary>
    public const string BeltPart = ".Belt";

    /// <summary>Instance name suffix of the steps (escalator look); V runs along the path.</summary>
    public const string StepsPart = ".Steps";

    /// <summary>Instance name suffix of the rollers (frame look); U runs once round each roller.</summary>
    public const string RollersPart = ".Rollers";

    /// <summary>uu of belt one texture repeat covers along the path.</summary>
    public const float BeltTile = 128f;

    /// <summary>Length of one escalator step (and of one texture repeat on the steps).</summary>
    public const float StepLength = 40f;

    /// <summary>Rise of one escalator step at most; steeper runs get shorter steps.</summary>
    public const float StepRise = 20f;

    public const float RollerRadius = 6f;

    /// <summary>Distance between roller centres along the path.</summary>
    public const float RollerPitch = 24f;

    /// <summary>Height of the frame's side walls above the belt.</summary>
    public const float WallHeight = 24f;

    public const float WallThickness = 8f;

    /// <summary>Depth of the frame under the belt surface.</summary>
    public const float FrameDepth = 32f;

    /// <summary>Height of the treadmill's low edges.</summary>
    public const float EdgeHeight = 4f;

    /// <summary>Height of an escalator's balustrades.</summary>
    public const float BalustradeHeight = 96f;

    /// <summary>The number of items riding the conveyor (none without meshes).</summary>
    public static int ItemCount(Conveyor conveyor, ConveyorPath path)
    {
        if (conveyor.ItemMeshes.Count == 0 || conveyor.ItemSpacing <= 0f || path.Length <= 0f)
        {
            return 0;
        }

        return SvmMath.Max(1, SvmMath.Floor(path.Length / conveyor.ItemSpacing));
    }

    /// <summary>
    /// Item <paramref name="index"/> at belt <paramref name="phase"/> (<see cref="Conveyor.PhaseAt"/>). Items are evenly
    /// spread along the path and keep their order; on an open path they grow in at the start and shrink away at the end
    /// over <see cref="Conveyor.ItemFade"/>, then come round again.
    /// </summary>
    public static ConveyorItemPlace PlaceItem(Conveyor conveyor, ConveyorPath path, int index, int count, double phase)
    {
        float length = path.Length;
        float spacing = length / count;
        uint h = Hash((uint)conveyor.ItemSeed, (uint)index);
        float jitter = (Unit(h) - 0.5f) * spacing * 0.5f;
        double along = index * spacing + jitter + phase;
        float at = (float)(along - length * System.Math.Floor(along / length));
        path.Evaluate(at, out FVector location, out FVector tangent);
        int yaw = SvmMath.Round(SvmMath.Atan2(tangent.Y, tangent.X) * SvmMath.RadToUnr) +
                  (int)(Unit(Hash(h, 1u)) * conveyor.ItemYawJitter);
        int mesh = (int)(Hash(h, 2u) % (uint)conveyor.ItemMeshes.Count);
        float scale = conveyor.ItemScale;
        if (!path.IsLoop && conveyor.ItemFade > 0f)
        {
            scale *= SvmMath.Clamp(SvmMath.Min(at, length - at) / conveyor.ItemFade, 0f, 1f);
        }

        return new ConveyorItemPlace(location, yaw & 0xFFFF, scale, mesh);
    }

    // A small integer hash (no RNG state): the same items on every machine and in the editor.
    private static uint Hash(uint a, uint b)
    {
        uint x = (a * 0x9E3779B1u) ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
        x ^= x >> 16;
        x *= 0x85EBCA6Bu;
        x ^= x >> 13;
        x *= 0xC2B2AE35u;
        x ^= x >> 16;
        return x;
    }

    private static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
}
