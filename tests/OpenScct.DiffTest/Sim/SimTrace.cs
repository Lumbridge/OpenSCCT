using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using OpenScct.Core;
using OpenScct.DiffTest.Model;
using OpenScct.Game.Movement;

namespace OpenScct.DiffTest.Sim;

/// <summary>Turns a rig's state into svm.trace/1 frames, header and footer.</summary>
public static class SimTrace
{
    public const string Recorder = "tests/OpenScct.DiffTest/Sim/PawnRigSim.cs";

    public static Vec3 ToVec(FVector v) => new(v.X, v.Y, v.Z);

    public static FVector ToF(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Rot3 ToRot(FRotator r) => new(r.Pitch, r.Yaw, r.Roll);

    public static JsonArray Json(FVector v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// The rig's state after its last tick as frame <paramref name="f"/>, with the inputs applied before that tick.
    /// Animation: channel 0 only (the movement port keeps no blend weights), alpha 1.
    /// </summary>
    public static TraceFrame Frame(PawnRig rig, int f, FrameInput input, NameCanon names)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(names);
        PawnSample s = rig.Sample();
        PawnAnimChannel ch0 = rig.Pawn.PawnAnim[0];
        IReadOnlyList<AnimChannelSample> anim = ch0.Sequence.IsNone
            ? []
            : [new AnimChannelSample(0, names.Map(ch0.Sequence.ToString()), ch0.Frame, ch0.Rate, 1.0, null)];
        return new TraceFrame
        {
            F = f,
            T = s.T,
            Dt = s.Dt,
            Loc = ToVec(s.Loc),
            Vel = ToVec(s.Vel),
            Acc = ToVec(s.Acc),
            Rot = ToRot(s.Rot),
            ControllerRot = ToRot(s.CRot),
            Physics = (int)s.Phys,
            PawnState = names.Map(s.PawnState.ToString()),
            ControllerState = names.Map(s.ControllerState.ToString()),
            Crouched = s.Crouch,
            Rolling = s.Roll,
            Health = s.Hp,
            GroundSpeed = s.Gs,
            CollisionHeight = s.Ch,
            ZMaxFalling = s.ZMax,
            JumpNumber = s.Jn,
            AForward = s.Fwd,
            AStrafe = s.Str,
            NormePadMove = s.Npm,
            IMove = s.IMove,
            FastSpeed = s.Fast,
            CrouchWithRoll = s.Cwr,
            PressedJump = s.Pj,
            Input = input,
            Anim = anim,
        };
    }

    /// <summary>The header of a sim run: the reference's scenario block, team and map, and the runner's settings.</summary>
    public static JsonObject Header(IScenarioSim sim, SimRequest request, PawnRig rig, JsonObject setup)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rig);
        Scenario s = request.Scenario;
        return new JsonObject
        {
            ["format"] = TraceRun.Format,
            ["scenario"] = new JsonObject
            {
                ["id"] = s.Id,
                ["path"] = System.IO.Path.GetRelativePath(Golden.RepoPaths.Root, s.Path).Replace('\\', '/'),
                ["doc"] = s.Doc.DeepClone(),
            },
            ["run"] = 0,
            ["runs"] = 1,
            ["team"] = s.Team,
            ["map"] = new JsonObject { ["name"] = s.Map },
            ["pawnClass"] = rig.Pawn.ClassName.ToString(),
            ["controllerClass"] = rig.Controller.ClassName.ToString(),
            ["recorder"] = new JsonObject { ["tool"] = Recorder, ["sim"] = sim.Name, ["version"] = 1 },
            ["sim"] = new JsonObject
            {
                ["timing"] = request.Timing.ToString(),
                ["reference"] = request.Reference.Name,
                ["referenceStartT"] = request.Start.T,
                ["anim"] = "channel 0 only",
            },
            ["setup"] = setup,
            ["timeDilation"] = 1.0,
        };
    }

    public static JsonObject Footer(IReadOnlyList<TraceFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var played = frames.Where(f => f.F >= 0).ToList();
        return new JsonObject
        {
            ["end"] = true,
            ["frames"] = frames.Count,
            ["torn"] = 0,
            ["gaps"] = 0,
            ["late"] = 0,
            ["unstable"] = 0,
            ["gameSeconds"] = played.Count == 0 ? 0 : played[^1].T - frames[0].T,
            ["dtMean"] = played.Count == 0 ? 0 : played.Average(f => f.Dt),
            ["dtMin"] = played.Count == 0 ? 0 : played.Min(f => f.Dt),
            ["dtMax"] = played.Count == 0 ? 0 : played.Max(f => f.Dt),
        };
    }

    /// <summary>
    /// UE2 names compare without case; the sim's spelling of a state or sequence can differ from the original's
    /// (a name is entered in the table by its first use). Names are written with the reference's spelling, so the
    /// comparator's sequence check compares names, not spellings.
    /// </summary>
    public sealed class NameCanon
    {
        private readonly Dictionary<string, string> _byLower = new(StringComparer.OrdinalIgnoreCase);

        public static NameCanon From(params TraceRun[] runs)
        {
            var canon = new NameCanon();
            foreach (TraceFrame f in runs.SelectMany(r => r.Frames))
            {
                canon.Add(f.ControllerState);
                canon.Add(f.PawnState);
                foreach (AnimChannelSample c in f.Anim)
                {
                    canon.Add(c.Sequence);
                }
            }

            return canon;
        }

        public string Map(string name) => _byLower.TryGetValue(name, out string? spelled) ? spelled : name;

        private void Add(string? name)
        {
            if (name is { Length: > 0 })
            {
                _byLower.TryAdd(name, name);
            }
        }
    }
}
