using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Server;

/// <summary>Hub cycle refreshes correct speed without rewinding moving collision, riders or attached GEs.</summary>
internal sealed class HubMoverSmoothing
{
    private readonly HashSet<Mover> _initialized = [];

    /// <returns>True when the current interpolation is retained; false for native first-snapshot/new-leg setup.</returns>
    public bool Receive(Mover mover)
    {
        if (!_initialized.Add(mover))
        {
            int keys = (int)mover.SimInterpolate.Z;
            int previous = keys >> 8, target = keys & 255;
            if (target == mover.KeyNum)
            {
                // Completion snapshots can arrive just before the client's final tick, or a delayed
                // in-flight sample just after it. Neither should restart an already completed leg.
                if (!mover.bInterpolating && mover.PrevKeyNum == target) return true;
                if (mover.bInterpolating && previous == target) return true;
                if (mover.bInterpolating && previous == mover.PrevKeyNum && mover.SimOldPos == mover.OldPos)
                {
                    float seconds = mover.Legs?.LegTime(mover.PrevKeyNum, mover.KeyNum) ?? mover.MoveTime;
                    float nominal = 1f / Math.Max(.01f, seconds);
                    float error = mover.SimInterpolate.X * .01f - mover.PhysAlpha;
                    // The wire phase is rounded to 1%. Ignore that noise and converge over half a
                    // second. Always move forward, even when a delayed snapshot is behind us.
                    mover.PhysRate = Math.Clamp(nominal + (Math.Abs(error) > .015f ? error / .5f : 0), nominal * .5f, nominal * 1.5f);
                    return true;
                }
            }
        }
        return false;
    }
}
