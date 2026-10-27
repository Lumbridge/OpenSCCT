using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Particles;

/// <summary>
/// The map's UE2 Emitter actors (render/fx.json): one <see cref="ParticleSystemSim"/> per actor over its sub-emitters
/// (stored values over the shared class defaults), drawn by a <see cref="ParticleSystemView"/> facing the viewport's
/// camera (spec/native/emitters.md). <see cref="Trigger"/> is Emitter.Trigger for an event of the actor's Tag. The scene
/// time drives them: <see cref="SetTime"/> steps every simulation at the fixed rate up to Level.TimeSeconds (going back
/// restarts them) and redraws. A mesh emitter whose mesh the map content does not carry draws nothing.
/// docs/RENDERING.md#emitters, docs/EFFECTS.md.
/// </summary>
[PortOf("eng:Render.Emitters", Spec = "spec/native/emitters.md#rules")]
public sealed class Emitters : IParticleAssets
{
    private readonly List<Entry> _entries = [];
    private readonly RenderMap _map;
    private float _time;

    /// <summary>Shorter rests wait for the next frame (float noise of the scene time).</summary>
    private const float MinStep = 1e-5f;

    private sealed record Entry(FxEmitter Emitter, ParticleSystemSim Sim, ParticleSystemView View);

    public Emitters(RenderMap map, Node3D parent)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(parent);
        Root = new Node3D { Name = "Emitters" };
        parent.AddChild(Root);
        FxDoc fx = FxDoc.Load(Path.Combine(map.Folder, "render", "fx.json"), map.SharedRoot);
        foreach (FxEmitter e in fx.Emitters)
        {
            var subs = new List<ParticleParams>();
            foreach (FxSubEmitter sub in e.Emitters)
            {
                var p = ParticleParams.From(sub.Class, sub.ObjectPath, sub.Props, sub.Defaults, sub.Texture);
                if (p.MaxParticles <= 0)
                {
                    continue;
                }

                if (p.Kind == ParticleKind.Mesh)
                {
                    Skipped++;
                }

                subs.Add(p);
                SubEmitters++;
            }

            if (subs.Count == 0)
            {
                continue;
            }

            var settings = new ParticleSystemParams(Bool(e.Props, "AutoDestroy"), Bool(e.Props, "AutoReset"), default, default);
            var sim = new ParticleSystemSim(subs, settings, FxRandom.Seed(e.Id));
            var view = new ParticleSystemView(sim, this, Root, e.Id, e.Float("HeatOpacity", RenderK.DefaultHeatOpacity));
            _entries.Add(new Entry(e, sim, view));
        }
    }

    public Node3D Root { get; }

    /// <summary>Sub-emitters simulated and drawn.</summary>
    public int Count => SubEmitters;

    public int SubEmitters { get; }

    /// <summary>Emitter actors.</summary>
    public int Actors => _entries.Count;

    /// <summary>Mesh sub-emitters (their meshes are not in the map content).</summary>
    public int Skipped { get; }

    /// <summary>The scene time the emitters are simulated to.</summary>
    public float SimulatedTime => _time;

    /// <summary>Particles drawn by the last <see cref="SetTime"/>.</summary>
    public int Particles { get; private set; }

    public Texture2D? Texture(string? png) => png is null ? null : _map.Textures.Get(png);

    public Mesh? Mesh(string? objectPath) => null;

    /// <summary>Emitter.Trigger for every Emitter actor whose Tag is <paramref name="tag"/> (ignoring case).</summary>
    public int Trigger(string tag)
    {
        int n = 0;
        foreach (Entry e in _entries)
        {
            if (string.Equals(e.Emitter.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                e.Sim.Trigger();
                n++;
            }
        }

        return n;
    }

    /// <summary>Emitter.ReplicateTrigger of the Emitter actor whose map id is <paramref name="id"/> (ignoring case).</summary>
    public bool TriggerActor(string id)
    {
        foreach (Entry e in _entries)
        {
            if (string.Equals(e.Emitter.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                e.Sim.Trigger();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Simulates every emitter up to scene time <paramref name="seconds"/> and redraws for the current camera. The
    /// engine updates each emitter once per rendered frame by the frame's time (spec/native/emitters.md): a frame
    /// shorter than the 60 Hz step is one step of its own length, so the particles move every frame at any frame rate;
    /// a longer stretch runs in 60 Hz steps and then the rest.
    /// </summary>
    public void SetTime(float seconds)
    {
        if (seconds < _time)
        {
            foreach (Entry e in _entries)
            {
                e.Sim.Reset();
            }

            _time = 0f;
        }

        float step = RenderK.EmitterStep;
        int steps = (int)MathF.Floor((seconds - _time) / step);
        float rest = seconds - _time - (steps * step);
        if (steps > 0 || rest > MinStep)
        {
            foreach (Entry e in _entries)
            {
                for (int i = 0; i < steps; i++)
                {
                    e.Sim.Step(step, e.Emitter.Location, e.Emitter.Rotation, default);
                }

                if (rest > MinStep)
                {
                    e.Sim.Step(rest, e.Emitter.Location, e.Emitter.Rotation, default);
                }
            }

            _time = rest > MinStep ? seconds : _time + (steps * step);
        }

        Refresh();
    }

    private void Refresh()
    {
        Camera3D? camera = Root.IsInsideTree() ? Root.GetViewport().GetCamera3D() : null;
        ParticleCamera view = camera is null
            ? new ParticleCamera(default, new FVector(0f, 1f, 0f), new FVector(0f, 0f, 1f), new FVector(1f, 0f, 0f))
            : CameraOf(camera);
        int total = 0;
        foreach (Entry e in _entries)
        {
            var owner = new ParticleOwner(e.Emitter.Location, e.Emitter.Rotation, default, e.Sim.GlobalOffset);
            e.View.Draw(owner, view);
            total += e.View.Drawn;
        }

        Particles = total;
    }

    /// <summary>A Godot camera as the particle camera (UE space).</summary>
    public static ParticleCamera CameraOf(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Basis b = camera.GlobalTransform.Basis;
        return new ParticleCamera(UeSpace.ToUe(camera.GlobalPosition), UeSpace.DirectionToUe(b.X.Normalized()),
            UeSpace.DirectionToUe(b.Y.Normalized()), UeSpace.DirectionToUe(-b.Z.Normalized()));
    }

    private static bool Bool(JsonElement props, string name) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}
