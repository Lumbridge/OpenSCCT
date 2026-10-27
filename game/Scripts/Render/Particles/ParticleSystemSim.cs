using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render.Particles;

/// <summary>The actor-level settings of an Emitter (Engine.Emitter properties).</summary>
public sealed record ParticleSystemParams(bool AutoDestroy, bool AutoReset, FxRangeVector GlobalOffsetRange, FxRange TimeTillResetRange);

/// <summary>
/// One Emitter actor's particles: AEmitter::Tick over its sub-emitters (spec/native/emitters.md rules 1 to 4). The
/// owner's place is given every step. Godot-free.
/// </summary>
[PortOf("eng:AEmitter::Tick", Spec = "spec/native/emitters.md#rules")]
public sealed class ParticleSystemSim
{
    private readonly ParticleEmitterSim[] _emitters;
    private FxRandom _rng;
    private float _timeTillReset;

    public ParticleSystemSim(IReadOnlyList<ParticleParams> emitters, ParticleSystemParams settings, uint seed)
    {
        ArgumentNullException.ThrowIfNull(emitters);
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _rng = new FxRandom(seed);
        _emitters = new ParticleEmitterSim[emitters.Count];
        for (int i = 0; i < emitters.Count; i++)
        {
            _emitters[i] = new ParticleEmitterSim(emitters[i], (seed * 2654435761u) + (uint)(i * 40503) + 1u);
        }

        foreach (ParticleEmitterSim e in _emitters)
        {
            e.Siblings = _emitters;
        }

        _timeTillReset = settings.TimeTillResetRange.Pick(ref _rng);
    }

    public ParticleSystemParams Settings { get; }

    public IReadOnlyList<ParticleEmitterSim> Emitters => _emitters;

    /// <summary>Seconds simulated.</summary>
    public float Time { get; private set; }

    public FVector GlobalOffset { get; private set; }

    /// <summary>Every sub-emitter's particles are dead and none will spawn more (the Emitter's AutoDestroy point).</summary>
    public bool AllDead { get; private set; }

    /// <summary>Particles alive now in all sub-emitters.</summary>
    public int Alive
    {
        get
        {
            int n = 0;
            foreach (ParticleEmitterSim e in _emitters)
            {
                n += e.Alive;
            }

            return n;
        }
    }

    public ParticleTrace? Trace
    {
        set
        {
            foreach (ParticleEmitterSim e in _emitters)
            {
                e.Trace = value;
            }
        }
    }

    /// <summary>Emitter.Trigger: each sub-emitter's Trigger.</summary>
    public void Trigger()
    {
        foreach (ParticleEmitterSim e in _emitters)
        {
            e.Trigger();
        }
    }

    /// <summary>Emitter.Kill.</summary>
    public void Kill()
    {
        foreach (ParticleEmitterSim e in _emitters)
        {
            e.Kill();
        }
    }

    /// <summary>Back to the start (every sub-emitter reset, the stored Disabled flags back).</summary>
    public void Reset()
    {
        foreach (ParticleEmitterSim e in _emitters)
        {
            e.Disabled = e.Params.Disabled;
            e.Reset();
        }

        Time = 0f;
        AllDead = false;
    }

    /// <summary>One AEmitter::Tick of <paramref name="dt"/> seconds with the actor at <paramref name="location"/>.</summary>
    [Provenance(ProvenanceKind.Decoded, "AEmitter::Tick 0x110db890 (editor twin): disabled skipped, initial delay, warm-up, update, auto reset and destroy per sub-emitter, then the actor's auto reset")]
    public void Step(float dt, FVector location, FRotator rotation, FVector velocity)
    {
        Time += dt;
        bool allDead = true;
        var owner = new ParticleOwner(location, rotation, velocity, GlobalOffset);
        foreach (ParticleEmitterSim e in _emitters)
        {
            if (e.Disabled)
            {
                continue;
            }

            if (e.InitialDelay == 0f)
            {
                e.WarmUp(owner);
                if (!e.AllParticlesDead)
                {
                    e.Update(dt, owner);
                    allDead = false;
                }
                else if (e.Params.AutoReset && !Settings.AutoReset)
                {
                    if (e.TimeTillReset - dt <= 0f)
                    {
                        e.Reset();
                        e.TimeTillReset = e.Params.AutoResetTimeRange.Pick(ref _rng);
                    }
                    else
                    {
                        e.TimeTillReset -= dt;
                    }
                }
                else if (e.Params.AutoDestroy && !Settings.AutoDestroy)
                {
                    e.Disabled = true;
                }
            }
            else
            {
                allDead = false;
            }

            e.InitialDelay = MathF.Max(0f, e.InitialDelay - dt);
        }

        AllDead = allDead;
        if (allDead && Settings.AutoReset && !Settings.AutoDestroy)
        {
            if (_timeTillReset - dt <= 0f)
            {
                GlobalOffset = Settings.GlobalOffsetRange.Pick(ref _rng);
                _timeTillReset = Settings.TimeTillResetRange.Pick(ref _rng);
                foreach (ParticleEmitterSim e in _emitters)
                {
                    e.Reset();
                }
            }
            else
            {
                _timeTillReset -= dt;
            }
        }
    }
}
