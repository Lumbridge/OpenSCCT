using System.Collections.Generic;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Sim;

/// <summary>How a sim runner steps time when it replays a scenario.</summary>
public enum SimTiming
{
    /// <summary>
    /// Replay the reference run's frame times: frame k is stepped by the reference frame k's Dt, after applying
    /// that frame's recorded inputs (<see cref="TraceFrame.Input"/>). This is the gate for ports of variable-step
    /// engine code; it removes frame-time jitter from the comparison.
    /// </summary>
    DtReplay,

    /// <summary>
    /// The rewrite's own fixed 60 Hz tick with the scenario's frame-indexed inputs (<see cref="Scenario.Timeline"/>).
    /// What the game runs; compared on the common clock, with the noise envelope absorbing frame-time jitter.
    /// </summary>
    Fixed,
}

/// <summary>
/// What a sim runner is asked to play: the scenario, the golden reference run whose start state and (for
/// <see cref="SimTiming.DtReplay"/>) frame times and applied inputs it should follow, and the timing mode.
/// </summary>
/// <param name="Scenario">The scenario file.</param>
/// <param name="Reference">The golden run picked as reference (the noise envelope's medoid).</param>
/// <param name="Timing">How to step time.</param>
public sealed record SimRequest(Scenario Scenario, TraceRun Reference, SimTiming Timing)
{
    /// <summary>The settled state the run starts from (frame -1 of the reference): location, rotation, stance.</summary>
    public TraceFrame Start => Reference.Start;

    /// <summary>Per frame from 0: the time step and the inputs to apply before that frame's tick.</summary>
    public IEnumerable<(int Frame, double Dt, FrameInput Input)> Steps()
    {
        if (Timing == SimTiming.DtReplay)
        {
            foreach (TraceFrame f in Reference.Played)
            {
                yield return (f.F, f.Dt, f.Input);
            }

            yield break;
        }

        IReadOnlyList<FrameInput> timeline = Scenario.Timeline();
        for (int f = 0; f < timeline.Count; f++)
        {
            yield return (f, 1.0 / 60.0, timeline[f]);
        }
    }
}

/// <summary>
/// A simulation of the rewrite that can play scenarios: the runner built on OpenScct.Physics and OpenScct.Game implements
/// this. It returns its run as a trace (svm.trace/1 fields; frame -1 is the start state), which the
/// <see cref="Compare.Comparator"/> checks against the golden runs.
/// </summary>
public interface IScenarioSim
{
    /// <summary>A short name for reports.</summary>
    string Name { get; }

    /// <summary>Whether this runner can play the scenario (map content, team, features).</summary>
    bool CanRun(Scenario scenario);

    /// <summary>Plays the scenario and returns the run.</summary>
    TraceRun Run(SimRequest request);
}
