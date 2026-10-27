using System;
using System.IO;
using System.Reflection;
using OpenScct.Core;

namespace OpenScctGame.Smoke;

/// <summary>
/// Checks that the boot scene comes up, that the simulation libraries load in the game process, and that a
/// <see cref="World"/> steps its fixed ticks.
/// </summary>
public sealed class BootSmoke : SmokeTest
{
    public const string TestName = "boot";

    // The boot scene is added right after the autoloads, so a few frames is plenty.
    private const int MaxFramesForBootScene = 30;

    private const int SimulationTicks = 600;

    private static readonly string[] SimulationAssemblies =
        ["OpenScct.Provenance", "OpenScct.Core", "OpenScct.Physics", "OpenScct.Game", "OpenScct.Net", "OpenScct.Content", "OpenScct.Server"];

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (context.Tree.CurrentScene is not Boot boot || !boot.IsReady)
        {
            return context.Frame < MaxFramesForBootScene
                ? SmokeResult.Running
                : SmokeResult.Fail("boot scene did not become ready");
        }

        string? failure = CheckSimulation();
        return failure is null
            ? SmokeResult.Pass($"runtime={Environment.Version} ticks={SimulationTicks}")
            : SmokeResult.Fail(failure);
    }

    private static string? CheckSimulation()
    {
        foreach (string assembly in SimulationAssemblies)
        {
            try
            {
                Assembly.Load(assembly);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                return $"could not load {assembly}: {ex.Message}";
            }
        }

        var world = new World(1);
        world.Step(SimulationTicks);
        return world.TickCount == SimulationTicks ? null : $"world ran {world.TickCount} ticks, expected {SimulationTicks}";
    }
}
