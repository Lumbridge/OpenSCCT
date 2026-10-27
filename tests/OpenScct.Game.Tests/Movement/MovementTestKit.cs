using System;
using System.IO;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Tests.Movement;

/// <summary>Synthetic levels and rigs for the movement tests.</summary>
internal static class MovementTestKit
{
    public static FVector V(float x, float y, float z) => new(x, y, z);

    /// <summary>A floor slab whose top is at z = 0.</summary>
    public static CollisionDataBuilder Floor(float half = 6000f)
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(-half, -half, -64f), V(half, half, 0f));
        return b;
    }

    /// <summary>A rig on <paramref name="level"/> (a flat floor by default), settled standing at the origin facing +Y.</summary>
    public static PawnRig Rig(PawnTeam team, CollisionDataBuilder? level = null, bool crouched = false, int yaw = 16384,
        FVector? at = null, PawnRigOptions? options = null)
    {
        PawnRig rig = PawnRig.Create(team, (level ?? Floor()).Build(), null, options);
        FVector floor = at ?? default;
        float h = crouched ? rig.Pawn.CrouchHeight : 87.5f;
        rig.Place(V(floor.X, floor.Y, floor.Z + h + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), crouched);
        if (team == PawnTeam.Spy)
        {
            rig.SetFastSpeed(true);
        }

        rig.WaitStill(120);
        return rig;
    }

    /// <summary>Steps <paramref name="frames"/> frames with the same input.</summary>
    public static void Hold(this PawnRig rig, PlayerInputFrame input, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(input);
        }
    }

    public static readonly PlayerInputFrame Forward = new() { BT_Up = 1 };

    /// <summary>The repository root (the folder with OpenSCCT.slnx).</summary>
    public static string RepoRoot
    {
        get
        {
            for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
            {
                if (File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
                {
                    return dir;
                }
            }

            throw new InvalidOperationException("The repository root (OpenSCCT.slnx) was not found above the test binaries.");
        }
    }
}
