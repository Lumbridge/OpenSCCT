using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;

namespace OpenScctGame.Fx;

/// <summary>
/// The detector laser beams on this machine, drawn through <see cref="Lasers"/> each frame from the client world:
/// <list type="bullet">
/// <item>the map's tripwires (spec/devices/laser-trigger.md#drawing): every SLaserTrigger whose beam actor (SLaserBeam)
/// is shown, from the beam's place along its rotation for its drawn length (DrawScale3D.X x DrawScale, cut at a spy by
/// CheckPresence);</item>
/// <item>the mines' beams (SLaserBeam_MineLaser of the laser mine, the spy trap and the poison mine,
/// spec/equipment/mine-launcher.md rules 16, 17 and 23): from the beam's place (the mine) along its rotation (towards
/// the wall it measured) for the replicated beam length fTailleFaisceau.</item>
/// </list>
/// Each with the beam's bHideInNormalVision and HeatIntensity (0.4 for a mine's beam, so thermal only and EMF). A
/// hidden beam (switched off, chaffed, an absent tripwire) or a gone one is removed. Reads the client world and never
/// writes it.
/// </summary>
[PortOf("eng:Render.LaserTriggerBeams", Spec = "spec/devices/laser-trigger.md#drawing")]
public sealed class LaserTriggerView
{
    private readonly Lasers _lasers;
    private readonly Dictionary<Actor, string> _ids = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _seen = new(System.StringComparer.Ordinal);
    private readonly List<string> _gone = [];
    private readonly List<Actor> _dead = [];
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

            Place(trigger, "LaserTrigger", beam, beam.DrawScale3D.X * beam.DrawScale);
        }

        foreach (SLaserBeam_MineLaser beam in level.AllActors<SLaserBeam_MineLaser>())
        {
            // A beam with no length (a trap with nothing ahead) draws nothing.
            if (!beam.bDeleteMe && !beam.bHidden && beam.fTailleFaisceau > 0f)
            {
                Place(beam, "MineBeam", beam, beam.fTailleFaisceau);
            }
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

        _dead.Clear();
        foreach (Actor a in _ids.Keys)
        {
            if (a.bDeleteMe)
            {
                _dead.Add(a);
            }
        }

        foreach (Actor a in _dead)
        {
            _ids.Remove(a);
        }
    }

    /// <summary>Places the beam of <paramref name="key"/> (its tripwire, or the mine beam itself) for <paramref name="length"/> uu.</summary>
    private void Place(Actor key, string prefix, Actor beam, float length)
    {
        if (!_ids.TryGetValue(key, out string? id))
        {
            id = prefix + _next++;
            _ids[key] = id;
        }

        FVector end = beam.Location + (beam.Rotation.Vector() * length);
        _lasers.Set(id, beam.Location, end, beam.bHideInNormalVision, beam.HeatIntensity);
        _seen.Add(id);
    }
}
