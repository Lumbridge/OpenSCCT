using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Tests.AI;

/// <summary>Synthetic levels, worlds and a follower harness for the bot tests.</summary>
internal static class AiTestKit
{
    public static FVector V(float x, float y, float z) => new(x, y, z);

    /// <summary>A floor slab whose top is at z = 0.</summary>
    public static CollisionDataBuilder Floor(float half = 3000f)
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(-half, -half, -64f), V(half, half, 0f));
        return b;
    }

    public static GERecord Ge(int index, GEKind kind, params FVector[] points) =>
        new(index, $"GE{index}", kind, points, 0, GEPlatform.Common, GESource.Synthetic("ai test"));

    public static TraversalDoc Doc(params GERecord[] ges) => new("1.0", "ai-test", ges);

    /// <summary>A spy (or merc) rig on the level, standing at <paramref name="floor"/> facing +Y.</summary>
    public static PawnRig Rig(CollisionDataBuilder level, TraversalDoc? ges, FVector floor, PawnTeam team = PawnTeam.Spy, int yaw = 16384)
    {
        PawnRig rig = PawnRig.Create(team, level.Build(), ges);
        rig.Place(V(floor.X, floor.Y, floor.Z + 87.5f + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), crouched: false);
        if (team == PawnTeam.Spy)
        {
            rig.SetFastSpeed(true);
        }

        rig.WaitStill(120);
        return rig;
    }

    /// <summary>The bot world of a rig: both sides' graphs over <paramref name="min"/>..<paramref name="max"/>.</summary>
    public static AiWorld World(PawnRig rig, FVector min, FVector max, TraversalDoc? ges = null, AiLightField? light = null)
    {
        CollisionWorld collision = rig.Collision ?? throw new InvalidOperationException("The rig has no collision.");
        NavGraph spy = NavBuilder.Build(collision, min, max, NavProfile.Spy, ges?.Ges);
        NavGraph merc = NavBuilder.Build(collision, min, max, NavProfile.Merc, ges?.Ges);
        var world = new AiWorld(rig.Level, collision, spy, merc, light);
        AiWorld.Install(world);
        return world;
    }

    /// <summary>
    /// Drives <paramref name="rig"/> with <paramref name="follower"/> toward <paramref name="goal"/> for up to
    /// <paramref name="seconds"/>: the view turns at once, the crouch key toggles as the follower asks. True on arrival.
    /// </summary>
    public static bool Follow(PawnRig rig, NavFollower follower, FVector goal, float seconds, List<string>? trace = null)
    {
        if (!follower.SetGoal(rig.Pawn, goal))
        {
            trace?.Add("no path");
            return false;
        }

        int crouchFrames = 0;
        float crouchAt = -10f;
        int frames = (int)(seconds * Sim.Hz);
        string last = string.Empty;
        for (int i = 0; i < frames; i++)
        {
            float now = rig.Level.TimeSeconds;
            NavCommand c = follower.Steer(rig.Pawn, now, rig.Controller.Rotation.Yaw & 0xFFFF, sneak: false);
            if (follower.Arrived)
            {
                return true;
            }

            if (follower.Failed)
            {
                trace?.Add("failed");
                return false;
            }

            byte crouch = 0;
            if (crouchFrames > 0)
            {
                crouch = 1;
                crouchFrames--;
            }
            else if (c.Crouched is { } want && want != rig.Pawn.bIsCrouched && rig.Pawn.Physics == OpenScct.Core.Engine.EPhysics.PHYS_Walking &&
                     now - crouchAt > 0.6f)
            {
                crouch = 1;
                crouchFrames = 2;
                crouchAt = now;
            }

            if (c.Keys.bCrouch != 0)
            {
                crouch = 1;
            }

            var view = c.Yaw is { } yaw ? new FRotator(0, yaw, 0) : (FRotator?)null;
            rig.Step(new PlayerInputFrame
            {
                BT_Up = c.Keys.BT_Up,
                BT_Down = c.Keys.BT_Down,
                BT_Left = c.Keys.BT_Left,
                BT_Right = c.Keys.BT_Right,
                bCrouch = crouch,
                bUsing = c.Keys.bUsing,
                Jump = c.Keys.Command1 == AiCommand.Jump || c.Keys.Command2 == AiCommand.Jump,
                View = view,
                FastSpeed = c.Keys.FastSpeed || rig.Team == PawnTeam.Spy,
            });

            if (trace is not null)
            {
                string s = $"{rig.Controller.GetStateName()} {rig.Pawn.Physics} step {follower.NextIndex}/{follower.Path.Count} {follower.CurrentKind}";
                if (s != last)
                {
                    trace.Add($"{now:F2} {s} at {rig.Pawn.Location}");
                    last = s;
                }
            }
        }

        trace?.Add("timeout");
        return false;
    }

    /// <summary>The SteeE folder of a local conversion, or null.</summary>
    public static string? SteeEFolder
    {
        get
        {
            for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
            {
                if (File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
                {
                    string map = Path.Combine(dir, ".content", "svmap", "SteeE");
                    return File.Exists(Path.Combine(map, "manifest.json")) ? map : null;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Keeps a test's report in <c>local/test-results/ai/&lt;name&gt;.txt</c> of the checkout (gitignored), as the test
    /// runner shows the output of failing tests only.
    /// </summary>
    public static void SaveReport(string name, string text)
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
            {
                string folder = Path.Combine(dir, "local", "test-results", "ai");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, name + ".txt"), text);
                return;
            }
        }
    }

    /// <summary>True when the long runs (every difficulty, the slow SteeE checks) are asked for.</summary>
    public static bool LongRuns => Environment.GetEnvironmentVariable("SVM_AI_MATCHES") == "1";
}
