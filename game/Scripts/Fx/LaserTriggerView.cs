using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;

namespace OpenScctGame.Fx;

/// <summary>
/// The map's laser tripwire beams on this machine (spec/devices/laser-trigger.md#drawing): each frame every
/// SLaserTrigger of the client world whose beam actor (SLaserBeam) is shown draws it through <see cref="Lasers"/>,
/// from the beam's place along its rotation for its drawn length (DrawScale3D.X x DrawScale, cut at a spy by
/// CheckPresence), with the beam's bHideInNormalVision and HeatIntensity. A hidden beam (switched off, chaffed or an
/// absent tripwire) is removed. Reads the client world and never writes it.
/// </summary>
[PortOf("eng:Render.LaserTriggerBeams", Spec = "spec/devices/laser-trigger.md#drawing")]
public sealed class LaserTriggerView
{
    private readonly Lasers _lasers;
    private readonly Dictionary<SLaserTrigger, string> _ids = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _seen = new(System.StringComparer.Ordinal);
    private readonly List<string> _gone = [];
    private int _next;

    public LaserTriggerView(RenderMap map)
    {
        _lasers = new Lasers(map);
    }

    /// <summary>Beams drawn by the last <see cref="Draw"/>.</summary>
    public int Drawn => _lasers.Count;

    public void Draw(Level level)
    {
        if (!_lasers.Available)
        {
            return;
        }

        _seen.Clear();
        foreach (SLaserTrigger trigger in level.AllActors<SLaserTrigger>())
        {
            if (trigger.bDeleteMe || trigger.bHidden || trigger.Laser is not { bDeleteMe: false, bHidden: false } beam)
            {
                continue;
            }

            if (!_ids.TryGetValue(trigger, out string? id))
            {
                id = "LaserTrigger" + _next++;
                _ids[trigger] = id;
            }

            float length = beam.DrawScale3D.X * beam.DrawScale;
            FVector end = beam.Location + (beam.Rotation.Vector() * length);
            _lasers.Set(id, beam.Location, end, beam.bHideInNormalVision, beam.HeatIntensity);
            _seen.Add(id);
        }

        _gone.Clear();
        foreach (string id in _lasers.Ids)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (string id in _gone)
        {
            _lasers.Remove(id);
        }

        if (_ids.Count > 0)
        {
            List<SLaserTrigger>? dead = null;
            foreach (SLaserTrigger t in _ids.Keys)
            {
                if (t.bDeleteMe)
                {
                    (dead ??= []).Add(t);
                }
            }

            if (dead is not null)
            {
                foreach (SLaserTrigger t in dead)
                {
                    _ids.Remove(t);
                }
            }
        }
    }
}
