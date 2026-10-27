using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Tests;

/// <summary>Synthetic runs for the tests: a walker that speeds up at a fixed rate, and a ballistic jump.</summary>
internal static class Synthetic
{
    public static JsonObject Header(string id, int run) => new()
    {
        ["scenario"] = new JsonObject { ["id"] = id },
        ["run"] = run,
        ["team"] = "spy",
    };

    /// <summary>
    /// A pawn walking along +Y: speed rises by accel x dt per frame up to top speed, held until frame release, then
    /// zero. Frame k is at time t0 + sum of dts; dts come from <paramref name="dt"/>(k).
    /// </summary>
    public static TraceRun Walk(string id, int run, int frames, double top, double accel, int release, Func<int, double> dt,
        double yOffset = 0, double t0 = 10.0)
    {
        var list = new List<TraceFrame>();
        double t = t0;
        double y = yOffset;
        double v = 0;
        list.Add(Frame(-1, t, dt(-1), y, 0, "s_PlayerWalkingSt0H", FrameInput.None));
        for (int f = 0; f < frames; f++)
        {
            double step = dt(f);
            t += step;
            bool held = f < release;
            v = held ? Math.Min(top, v + (accel * step)) : 0;
            y += v * step;
            var input = held ? new FrameInput(new Dictionary<string, int> { ["BT_Up"] = 1 }, false, null, null) : FrameInput.None;
            list.Add(Frame(f, t, step, y, v, "s_PlayerWalkingSt0H", input));
        }

        return new TraceRun(Header(id, run), list, null);
    }

    /// <summary>A jump in place at frame 'at': launch speed jumpZ, gravity g (negative), ground at z 0.</summary>
    public static TraceRun Jump(string id, int frames, int at, double jumpZ, double g, double dt)
    {
        var list = new List<TraceFrame>();
        double t = 10.0;
        double z = 0;
        double vz = 0;
        bool air = false;
        list.Add(Frame(-1, t, dt, 0, 0, "s_PlayerWalkingSt0H", FrameInput.None));
        for (int f = 0; f < frames; f++)
        {
            t += dt;
            if (f == at)
            {
                air = true;
                vz = jumpZ;
            }

            if (air)
            {
                // The average of the old and new velocity, as the engine's falling physics moves (exact under
                // constant gravity: a 610 uu/s jump peaks at 124.0 in the recorded traces).
                double v0 = vz;
                vz += g * dt;
                z += (v0 + vz) * 0.5 * dt;
                if (z <= 0)
                {
                    z = 0;
                    vz = 0;
                    air = false;
                }
            }

            list.Add(new TraceFrame
            {
                F = f,
                T = t,
                Dt = dt,
                Loc = new Vec3(0, 0, z),
                Vel = new Vec3(0, 0, vz),
                Physics = air ? 2 : 1,
                ControllerState = air ? "s_PlayerJump" : "s_PlayerWalkingSt0H",
                Health = 100,
                Input = f == at ? new FrameInput(new Dictionary<string, int>(), true, null, null) : FrameInput.None,
            });
        }

        return new TraceRun(Header(id, 1), list, null);
    }

    private static TraceFrame Frame(int f, double t, double dt, double y, double v, string state, FrameInput input) => new()
    {
        F = f,
        T = t,
        Dt = dt,
        Loc = new Vec3(0, y, 0),
        Vel = new Vec3(0, v, 0),
        Physics = 1,
        ControllerState = state,
        Health = 100,
        GroundSpeed = v > 0 ? 423.5 : 0,
        Input = input,
    };
}
