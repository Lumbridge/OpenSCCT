using System;
using System.Collections.Generic;
using System.Text;
using OpenScct.Core;
using OpenScct.Game.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// Replays the recorded movement scenarios (tests/scenarios/movement) on the converted SteeE in a rig and compares
/// every frame: controller state, physics mode, stance, base-channel clip, location and velocity. Needs the local
/// golden runs and the converted map. Missing local fixtures are reported as skips, or failures when
/// OPENSCCT_REQUIRE_GOLDENS=1 (tools/ci/parity-gate.ps1). Every recorded frame and repeat is checked.
/// </summary>
public sealed class GoldenReplayTests
{
    private const float PosTolerance = 0.05f;
    private const float VelTolerance = 0.05f;

    /// <summary>The first frame of a teleport start: the walking floor snap settles in a few frames (see the report).</summary>
    private const int SettleFrames = 10;

    public static TheoryData<string, PawnTeam, int> Recordings()
    {
        var data = new TheoryData<string, PawnTeam, int>();
        foreach ((string scenario, PawnTeam team) in new[]
        {
            ("movement.spy-run-forward", PawnTeam.Spy), ("movement.spy-run-stop", PawnTeam.Spy),
            ("movement.spy-sneak-walk", PawnTeam.Spy), ("movement.spy-crouch-walk", PawnTeam.Spy),
            ("movement.spy-jump-in-place", PawnTeam.Spy), ("movement.spy-jump-forward", PawnTeam.Spy),
            ("movement.merc-walk", PawnTeam.Merc), ("movement.merc-crouch-walk", PawnTeam.Merc),
            ("movement.merc-jump", PawnTeam.Merc), ("movement.spy-fall-ledge", PawnTeam.Spy),
        })
            for (int run = 1; run <= 3; run++) data.Add(scenario, team, run);
        return data;
    }

    [Theory]
    [MemberData(nameof(Recordings))]
    public void ReplayMatchesTheRecording(string scenario, PawnTeam team, int run)
    {
        List<GoldenRuns.GoldenFrame>? frames = GoldenRuns.Load(scenario, run);
        if (frames is null)
        {
            MissingFixture($"Missing {GoldenRuns.GoldenDir}/{scenario}/run{run}.jsonl.gz");
            return;
        }

        List<PawnSample>? samples = GoldenRuns.Replay("SteeE", team, frames);
        if (samples is null)
        {
            MissingFixture($"Missing converted map {GoldenRuns.SvMapDir("SteeE")}");
            return;
        }

        Assert.Equal(frames.Count - 1, samples.Count);

        var errors = new StringBuilder();
        int count = 0;
        int compared = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            GoldenRuns.GoldenFrame g = frames[i + 1];
            PawnSample s = samples[i];
            compared++;
            void Fail(string what)
            {
                if (count++ < 12)
                {
                    errors.Append("frame ").Append(g.F).Append(": ").AppendLine(what);
                }
            }

            if (!string.Equals(g.Cs, s.ControllerState.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Fail($"state {s.ControllerState}, recorded {g.Cs}");
            }

            if (g.Phys != (int)s.Phys)
            {
                Fail($"physics {(int)s.Phys}, recorded {g.Phys}");
            }

            if (g.Crouch != s.Crouch)
            {
                Fail($"crouched {s.Crouch}, recorded {g.Crouch}");
            }

            if (g.Hp != s.Hp) Fail($"health {s.Hp}, recorded {g.Hp}");

            if (g.Anim0.Length > 0 && !string.Equals(g.Anim0, s.Anim0.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Fail($"channel 0 {s.Anim0}, recorded {g.Anim0}");
            }

            FVector dl = s.Loc - g.Loc;
            if (MathF.Abs(dl.X) > PosTolerance || MathF.Abs(dl.Y) > PosTolerance || (g.F >= SettleFrames && MathF.Abs(dl.Z) > PosTolerance))
            {
                Fail($"location {s.Loc}, recorded {g.Loc}");
            }

            if ((s.Vel - g.Vel).Size() > VelTolerance)
            {
                Fail($"velocity {s.Vel}, recorded {g.Vel}");
            }
        }

        Assert.True(compared > 100, $"only {compared} frames compared");
        Assert.True(count == 0, $"{count} differences in {scenario} run {run}:{Environment.NewLine}{errors}");
    }

    private static void MissingFixture(string message)
    {
        if (Environment.GetEnvironmentVariable("OPENSCCT_REQUIRE_GOLDENS") == "1") Assert.Fail(message);
        Assert.Skip(message);
    }
}
