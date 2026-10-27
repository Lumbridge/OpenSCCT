using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using OpenScct.Core;
using OpenScct.DiffTest.Model;
using OpenScct.Game.Movement;

namespace OpenScct.DiffTest.Sim;

/// <summary>Where a sim run starts.</summary>
public enum SimStart
{
    /// <summary>
    /// The reference run's settled frame -1: location, pawn and controller rotation, stance, health, speed mode,
    /// fZMaxFalling and bCrouchWithRoll, placed in one step with <see cref="PawnRig.Place"/>. The gate: the start
    /// state is taken out of the comparison.
    /// </summary>
    Recorded,

    /// <summary>
    /// The scenario's own setup steps (teleport, stance, speed mode, writes, view, waitStill) and settle frames, as
    /// the recorder played them in the original. Checks the spawn and the settle as well.
    /// </summary>
    Setup,
}

/// <summary>Options of <see cref="PawnRigSim"/>.</summary>
public sealed record PawnRigSimOptions
{
    public SimStart Start { get; init; } = SimStart.Recorded;

    /// <summary>
    /// Idle frames played at the start spot before the final placement, so the level clock is past the movement
    /// cooldowns (stance 0.5 s, zip grab 2 s, back-to-wall corner 2 s) as it is in a running match, and the rig is
    /// past its first frames (a cold rig leaves 0.09 uu of location RMS in the crouched scenarios). The original's
    /// clock reads 8 to 95 s at frame -1; a fresh level reads 0.
    /// </summary>
    public int WarmupFrames { get; init; } = 180;

    /// <summary>
    /// With <see cref="SimStart.Recorded"/>: the time since the setup's last stance press, when the recorder pressed
    /// one, as the settle and still frames it waited after it. Stamps <c>m_LastSpecialMoveTime</c> so the stance
    /// cooldown runs as it did in the original.
    /// </summary>
    public bool StampStancePress { get; init; } = true;

    /// <summary>Called once the start state is placed, before frame -1 is sampled (the animation difftest seeds the
    /// recorded channel state here).</summary>
    public Action<PawnRig, SimRequest>? Started { get; init; }

    /// <summary>Called after each frame is sampled, frame -1 included, with the frame index.</summary>
    public Action<PawnRig, int>? Sampled { get; init; }
}

/// <summary>
/// The rewrite's runner for the differential tests: a <see cref="PawnRig"/> (OpenScct.Game's SBasePawn and
/// SPlayerController on OpenScct.Physics) on the converted map, started at the scenario's start, fed the scenario's
/// inputs frame by frame (<see cref="SimRequest.Steps"/>) and sampled after every tick into an svm.trace/1 run with
/// the recorder's fields.
/// </summary>
public sealed class PawnRigSim : IScenarioSim
{
    /// <summary>Frames of an input-free settle the recorder's waitStill counts.</summary>
    private const int StillFrames = 10;

    private readonly Func<Scenario, PawnRig> _rigs;
    private readonly Func<Scenario, bool> _hasWorld;

    /// <summary>A runner on the converted maps of <paramref name="content"/>.</summary>
    public PawnRigSim(SimContent content, PawnRigSimOptions? options = null)
        : this(s => PawnRig.Create(TeamOf(s), content.Map(s.Map), new PawnRigOptions { Animations = content.Animations }),
            s => content.HasMap(s.Map), options)
    {
        ArgumentNullException.ThrowIfNull(content);
    }

    /// <summary>A runner whose rigs come from <paramref name="rigs"/> (synthetic levels in tests).</summary>
    public PawnRigSim(Func<Scenario, PawnRig> rigs, Func<Scenario, bool> hasWorld, PawnRigSimOptions? options = null)
    {
        _rigs = rigs ?? throw new ArgumentNullException(nameof(rigs));
        _hasWorld = hasWorld ?? throw new ArgumentNullException(nameof(hasWorld));
        Options = options ?? new PawnRigSimOptions();
    }

    public PawnRigSimOptions Options { get; }

    public string Name => Options.Start == SimStart.Recorded ? "pawnrig" : "pawnrig-setup";

    public bool CanRun(Scenario scenario) =>
        scenario.Doc["players"]?.AsArray().Count == 1 &&
        scenario.Team is "spy" or "merc" &&
        (scenario.Doc["mode"]?.GetValue<string>() ?? "playlevel") == "playlevel" &&
        _hasWorld(scenario);

    /// <summary>A rig for the scenario's team on its map, before any start step.</summary>
    public PawnRig CreateRig(Scenario scenario) => _rigs(scenario);

    public TraceRun Run(SimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Scenario scenario = request.Scenario;
        PawnRig rig = CreateRig(scenario);
        var names = SimTrace.NameCanon.From(request.Reference);
        var frames = new List<TraceFrame>(scenario.DurationFrames + 1);
        JsonObject setup = Options.Start == SimStart.Recorded ? StartRecorded(rig, request) : StartSetup(rig, request);
        Options.Started?.Invoke(rig, request);
        frames.Add(SimTrace.Frame(rig, -1, FrameInput.None, names));
        Options.Sampled?.Invoke(rig, -1);

        IReadOnlyDictionary<int, TraceFrame> byFrame = request.Reference.Played.ToDictionary(f => f.F);
        FrameInput held = FrameInput.None;
        double prevT = request.Start.T;
        foreach ((int frame, double dt, FrameInput input) in request.Steps())
        {
            // Frames the recorder missed: their time is known only in sum, their inputs were those held before.
            if (request.Timing == SimTiming.DtReplay && byFrame.TryGetValue(frame, out TraceFrame? reference) && reference.Gap > 0)
            {
                double missing = (reference.T - prevT - reference.Dt) / reference.Gap;
                var carry = new FrameInput(held.Buttons, false, null, null);
                for (int g = reference.Gap; g > 0; g--)
                {
                    rig.Step(ToRig(carry), (float)Math.Max(missing, 1e-4));
                    frames.Add(SimTrace.Frame(rig, frame - g, carry, names));
                    Options.Sampled?.Invoke(rig, frame - g);
                }
            }

            if (request.Timing == SimTiming.DtReplay)
            {
                rig.Step(ToRig(input), (float)dt);
            }
            else
            {
                rig.Step(ToRig(input));
            }

            frames.Add(SimTrace.Frame(rig, frame, input, names));
            Options.Sampled?.Invoke(rig, frame);
            held = input;
            if (byFrame.TryGetValue(frame, out TraceFrame? r))
            {
                prevT = r.T;
            }
        }

        JsonObject header = SimTrace.Header(this, request, rig, setup);
        return new TraceRun(header, frames, SimTrace.Footer(frames));
    }

    /// <summary>The scenario input in the rig's terms.</summary>
    public static PlayerInputFrame ToRig(FrameInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return PlayerInputFrame.FromButtons(input.Buttons, input.Jump,
            input.View is Rot3 v ? (v.Pitch, v.Yaw) : null, input.FastSpeed);
    }

    public static PawnTeam TeamOf(Scenario scenario) => scenario.Team == "merc" ? PawnTeam.Merc : PawnTeam.Spy;

    private JsonObject StartRecorded(PawnRig rig, SimRequest request)
    {
        TraceFrame start = request.Start;
        FVector loc = SimTrace.ToF(start.Loc);
        var rot = new FRotator(start.Rot.Pitch, start.Rot.Yaw, start.Rot.Roll);
        var crot = new FRotator(start.ControllerRot.Pitch, start.ControllerRot.Yaw, start.ControllerRot.Roll);
        rig.Place(loc, rot, crot, start.Crouched);
        rig.SetFastSpeed(start.FastSpeed);
        for (int i = 0; i < Options.WarmupFrames; i++)
        {
            rig.Step(default);
        }

        rig.Place(loc, rot, crot, start.Crouched);
        rig.SetHealth(start.Health);
        rig.SetFastSpeed(start.FastSpeed);
        rig.Controller.fZMaxFalling = (float)start.ZMaxFalling;
        rig.Controller.bCrouchWithRoll = start.CrouchWithRoll;
        var setup = new JsonObject
        {
            ["start"] = "recorded",
            ["warmupFrames"] = Options.WarmupFrames,
        };

        // The setup's stance presses happened just before its waitStill and the settle frames.
        int presses = request.Reference.Header["setup"]?["stance"]?["presses"]?.GetValue<int>() ?? 0;
        if (Options.StampStancePress && presses > 0)
        {
            int still = request.Reference.Header["setup"]?["still"]?["frames"]?.GetValue<int>() ?? StillFrames;
            float since = (still + request.Scenario.SettleFrames) * (float)start.Dt;
            rig.Controller.m_LastSpecialMoveTime = rig.Level.TimeSeconds - since;
            setup["stancePressAgo"] = since;
        }

        return setup;
    }

    private JsonObject StartSetup(PawnRig rig, SimRequest request)
    {
        Scenario scenario = request.Scenario;
        var done = new JsonObject { ["start"] = "setup", ["warmupFrames"] = Options.WarmupFrames };

        // The original's pawn comes from the previous scenario of its game, not from a spawn: it presses crouch only
        // when its stance differs (the reference header says how often it pressed). The rig starts in its team's
        // spawn stance, so it is first put in the stance the original had before the setup, then idles on the spot
        // so the level clock is past the movement cooldowns.
        double clearance = scenario.Doc["spawn"]?["clearance"]?.GetValue<double>() ?? 2.0;
        SetupOp? stanceOp = scenario.Setup.FirstOrDefault(o => o.Op == "stance");
        bool target = stanceOp?.Raw["crouched"]?.GetValue<bool>() ?? rig.Pawn.bIsCrouched;
        int presses = request.Reference.Header["setup"]?["stance"]?["presses"]?.GetValue<int>() ?? 0;
        bool crouchedBefore = presses % 2 == 0 ? target : !target;
        var spot = new FVector((float)scenario.SpawnFloor.X, (float)scenario.SpawnFloor.Y,
            (float)(scenario.SpawnFloor.Z + clearance) + Math.Max(rig.Pawn.CollisionHeight, rig.Pawn.CrouchHeight));
        var yaw = new FRotator(0, scenario.SpawnYaw, 0);
        rig.Place(spot, yaw, yaw, crouchedBefore);
        for (int i = 0; i < Options.WarmupFrames; i++)
        {
            rig.Step(default);
        }

        done["stanceBefore"] = crouchedBefore ? "crouched" : "standing";
        foreach (SetupOp op in scenario.Setup)
        {
            JsonObject o = op.Raw;
            switch (op.Op)
            {
                case "leaveGE":
                    done["leaveGE"] = new JsonObject { ["frames"] = 0 };
                    break;
                case "teleport":
                    rig.Teleport(SimTrace.ToF(scenario.SpawnFloor), scenario.SpawnYaw, (float)clearance);
                    done["teleport"] = new JsonObject
                    {
                        ["location"] = SimTrace.Json(rig.Pawn.Location),
                        ["yaw"] = scenario.SpawnYaw,
                        ["collisionHeight"] = rig.Pawn.CollisionHeight,
                    };
                    break;
                case "stance":
                    bool crouched = o["crouched"]!.GetValue<bool>();
                    int before = rig.Frame;
                    bool ok = rig.SetCrouched(crouched);
                    done["stance"] = new JsonObject { ["ok"] = ok, ["frames"] = rig.Frame - before };
                    break;
                case "fastSpeed":
                    rig.SetFastSpeed(o["value"]!.GetValue<bool>());
                    break;
                case "view":
                    rig.SetView(o["pitch"]!.GetValue<int>(), o["yaw"]!.GetValue<int>());
                    break;
                case "write":
                    string field = o["field"]!.GetValue<string>();
                    if (o["target"]?.GetValue<string>() != "pawn" || field != "Health")
                    {
                        throw new NotSupportedException($"{scenario.Id}: setup write {o["target"]}.{field} is not played by the sim");
                    }

                    rig.SetHealth(o["value"]!.GetValue<int>());
                    break;
                case "wait":
                    for (int i = 0; i < o["frames"]!.GetValue<int>(); i++)
                    {
                        rig.Step(default);
                    }

                    break;
                case "waitStill":
                    int from = rig.Frame;
                    bool still = rig.WaitStill(o["maxFrames"]!.GetValue<int>());
                    done["still"] = new JsonObject { ["ok"] = still, ["frames"] = rig.Frame - from };
                    break;
                default:
                    throw new NotSupportedException($"{scenario.Id}: setup op {op.Op} is not played by the sim");
            }
        }

        for (int i = 0; i < scenario.SettleFrames; i++)
        {
            rig.Step(default);
        }

        done["settleFrames"] = scenario.SettleFrames;
        done["clock"] = rig.Level.TimeSeconds.ToString("R", CultureInfo.InvariantCulture);
        return done;
    }
}
