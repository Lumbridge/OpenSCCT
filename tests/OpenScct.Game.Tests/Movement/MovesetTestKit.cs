using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>Synthetic GE levels and stepping helpers for the ladder, hand-over-hand, fence and wall move tests.</summary>
internal static class MovesetTestKit
{
    public static readonly PlayerInputFrame JumpPress = new() { Jump = true };

    public static readonly PlayerInputFrame NoInput = new();

    public static readonly PlayerInputFrame Up = new() { BT_Up = 1 };

    public static readonly PlayerInputFrame Down = new() { BT_Down = 1 };

    public static readonly PlayerInputFrame Crouch = new() { bCrouch = 1 };

    public static GERecord Ge(int index, GEKind kind, params FVector[] points) =>
        new(index, $"GE{index}", kind, points, 0, GEPlatform.Common, GESource.Synthetic("test"));

    public static TraversalDoc Doc(params GERecord[] ges) => new("1.0", "test", ges);

    /// <summary>A rig of <paramref name="team"/> standing at <paramref name="at"/> (floor height) facing <paramref name="yaw"/>.</summary>
    public static PawnRig GeRig(PawnTeam team, CollisionDataBuilder level, TraversalDoc? ges, FVector at, int yaw = 16384,
        bool crouched = false)
    {
        PawnRig rig = PawnRig.Create(team, level.Build(), ges);
        float h = crouched ? rig.Pawn.CrouchHeight : 87.5f;
        rig.Place(V(at.X, at.Y, at.Z + h + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), crouched);
        if (team == PawnTeam.Spy)
        {
            rig.SetFastSpeed(true);
        }

        rig.WaitStill(120);
        return rig;
    }

    /// <summary>
    /// Steps until the controller is in <paramref name="state"/> (or the frames run out); true when it got there. The
    /// first frame takes <paramref name="first"/>, the others <paramref name="hold"/> (by default the first frame
    /// without its jump press).
    /// </summary>
    public static bool StepUntil(PawnRig rig, PlayerInputFrame first, Name state, int frames, List<Name>? seen = null,
        PlayerInputFrame? hold = null)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(i == 0 ? first : hold ?? (first.Jump || first.CoopMove ? first with { Jump = false, CoopMove = false } : first));
            Name s = rig.Controller.GetStateName();
            if (seen is not null && (seen.Count == 0 || seen[^1] != s))
            {
                seen.Add(s);
            }

            if (s == state)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rig options with the converted characters (clip lengths and root tracks), or null without them.</summary>
    public static PawnRigOptions? ContentOptions()
    {
        string dir = System.IO.Path.Combine(RepoRoot, ".content", "shared", "chars");
        return System.IO.Directory.Exists(dir) ? new PawnRigOptions { CharacterContentDir = dir } : null;
    }

    /// <summary>The states seen, for assertion messages.</summary>
    public static string Seen(List<Name> seen) => string.Join(", ", seen);
}
