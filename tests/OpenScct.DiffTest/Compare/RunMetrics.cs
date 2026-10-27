using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Compare;

/// <summary>
/// How far one run is from another. Both runs are put on a common clock (time since each run's frame 0), the
/// candidate is interpolated linearly at the reference's frame times, up to the last time both cover. The
/// recorder's noise envelope (re/tools/difftest/trace.py) uses the same definitions.
/// </summary>
/// <param name="LocRms">RMS of the location distance, uu.</param>
/// <param name="LocMax">Largest location distance, uu.</param>
/// <param name="LocEnd">Location distance at the last common time, uu.</param>
/// <param name="VelRms">RMS of the velocity difference (3D), uu/s.</param>
/// <param name="Samples">Reference frames compared.</param>
/// <param name="Seconds">Length of the common span, s.</param>
public sealed record RunMetrics(double LocRms, double LocMax, double LocEnd, double VelRms, int Samples, double Seconds)
{
    public static readonly RunMetrics Zero = new(0, 0, 0, 0, 0, 0);

    public double this[MetricKind kind] => kind switch
    {
        MetricKind.LocRms => LocRms,
        MetricKind.LocMax => LocMax,
        MetricKind.LocEnd => LocEnd,
        MetricKind.VelRms => VelRms,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Candidate against reference (see the type's summary).</summary>
    public static RunMetrics Compare(TraceRun reference, TraceRun candidate)
    {
        List<TraceFrame> a = reference.Played.ToList();
        List<TraceFrame> b = candidate.Played.ToList();
        if (a.Count < 2 || b.Count < 2)
        {
            throw new ArgumentException("both runs need at least two frames from frame 0");
        }

        double a0 = a[0].T;
        double b0 = b[0].T;
        var bt = b.Select(f => f.T - b0).ToArray();
        double end = Math.Min(a[^1].T - a0, bt[^1]);
        double sumLoc = 0;
        double sumVel = 0;
        double maxLoc = 0;
        int n = 0;
        foreach (TraceFrame fa in a)
        {
            double t = fa.T - a0;
            if (t > end + 1e-9)
            {
                break;
            }

            (Vec3 loc, Vec3 vel) = Sample(b, bt, t);
            double dl = Vec3.Distance(fa.Loc, loc);
            double dv = Vec3.Distance(fa.Vel, vel);
            sumLoc += dl * dl;
            sumVel += dv * dv;
            maxLoc = Math.Max(maxLoc, dl);
            n++;
        }

        var at = a.Select(f => f.T - a0).ToArray();
        double locEnd = Vec3.Distance(Sample(a, at, end).Loc, Sample(b, bt, end).Loc);
        return new RunMetrics(Math.Sqrt(sumLoc / n), maxLoc, locEnd, Math.Sqrt(sumVel / n), n, end);
    }

    /// <summary>Location and velocity of a run at time t (linear between frames, clamped at the ends).</summary>
    public static (Vec3 Loc, Vec3 Vel) Sample(IReadOnlyList<TraceFrame> frames, IReadOnlyList<double> times, double t)
    {
        if (t <= times[0])
        {
            return (frames[0].Loc, frames[0].Vel);
        }

        if (t >= times[^1])
        {
            return (frames[^1].Loc, frames[^1].Vel);
        }

        int lo = 0;
        int hi = times.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (times[mid] <= t)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        double span = times[hi] - times[lo];
        double w = span <= 0 ? 0 : (t - times[lo]) / span;
        return (Vec3.Lerp(frames[lo].Loc, frames[hi].Loc, w), Vec3.Lerp(frames[lo].Vel, frames[hi].Vel, w));
    }
}

public enum MetricKind
{
    LocRms,
    LocMax,
    LocEnd,
    VelRms,
}
