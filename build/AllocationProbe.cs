using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OpenScct.Testing;

/// <summary>Runs the complete allocation contract, including setup and warmup, away from runner execution context.</summary>
internal static class AllocationProbe
{
    public static void Run(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        });
        if (ExecutionContext.IsFlowSuppressed()) thread.Start();
        else
        {
            using (ExecutionContext.SuppressFlow()) thread.Start();
        }
        thread.Join();
        failure?.Throw();
    }
}
