using System;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render.Particles;

/// <summary>Where the owning Emitter actor is this frame.</summary>
public readonly record struct ParticleOwner(FVector Location, FRotator Rotation, FVector Velocity, FVector GlobalOffset);

/// <summary>Adjusts a new particle once its start place and velocity are set.</summary>
public delegate void ParticleSpawnHook(ref Particle particle);

/// <summary>A world trace for particle collisions: from <paramref name="start"/> to <paramref name="end"/>.</summary>
public delegate bool ParticleTrace(FVector start, FVector end, out FVector hitLocation, out FVector hitNormal);

/// <summary>One particle of a sub-emitter (the engine's 200-byte record).</summary>
public struct Particle
{
    public FVector Location;
    public FVector OldLocation;
    public FVector Velocity;
    public FVector StartSize;
    public FVector SpinsPerSecond;
    public FVector StartSpin;
    public FVector RevolutionCenter;
    public FVector RevolutionsPerSecond;
    public FVector RevolutionsMultiplier;
    public FVector Size;
    public FVector StartLocation;
    public FVector ColorMultiplier;
    public FVector VelocityMultiplier;
    public FxColor Color;
    public float Time;
    public float MaxLifetime;
    public float Mass;
    public int HitCount;
    public int Flags;
    public int Subdivision;

    // Spark data (USparkEmitter): the spawn location and velocity, the time between trail segments.
    public FVector SparkLocation;
    public FVector SparkVelocity;
    public float TimeBeforeVisible;
    public float TimeBetweenSegments;

    public readonly bool Alive => (Flags & ParticleEmitterSim.FlagAlive) != 0;
}

/// <summary>
/// One UE2 sub-emitter (ParticleEmitter and its draw kinds) simulated as the engine does: SpawnParticles,
/// SpawnParticle and UpdateParticles of spec/native/emitters.md rules 3 to 16, with the spark data of rule 22. The
/// owner's place comes in each update; collisions use the trace the caller gives. Godot-free: the view reads
/// <see cref="Particles"/> and draws them (<see cref="ParticleGeometry"/>).
/// </summary>
[PortOf("eng:UParticleEmitter::UpdateParticles", Spec = "spec/native/emitters.md#rules")]
public sealed class ParticleEmitterSim
{
    public const int FlagAlive = 1;
    public const int FlagResting = 2;
    public const int FlagSpawnedThisTick = 4;

    private const int SpawnRate = 4;
    private const int SpawnNoGlobalOffset = 1;
    private const int SpawnNoOwnerLocation = 2;

    private readonly Particle[] _particles;
    private FxRandom _rng;
    private ParticleOwner _owner;
    private float _ppsFraction;
    private int _particleIndex;
    private int _otherIndex;
    private int _deferred;
    private int _currentSpawnOnTrigger;
    private bool _killPending;

    public ParticleEmitterSim(ParticleParams p, uint seed)
    {
        Params = p ?? throw new ArgumentNullException(nameof(p));
        _particles = new Particle[Math.Max(0, p.MaxParticles)];
        _rng = new FxRandom(seed);
        Disabled = p.Disabled;
        Reset();
    }

    public ParticleParams Params { get; }

    /// <summary>The particle records (alive ones have <see cref="Particle.Alive"/>).</summary>
    public ReadOnlySpan<Particle> Particles => _particles;

    /// <summary>Slots used at least once (ActiveParticles).</summary>
    public int ActiveParticles { get; private set; }

    public int MaxActiveParticles => _particles.Length;

    public bool Disabled { get; set; }

    public bool AllParticlesDead { get; private set; }

    public bool WarmedUp { get; private set; }

    public float InitialDelay { get; set; }

    public float TimeTillReset { get; set; }

    /// <summary>The other sub-emitters of the same actor (for SpawnFromOtherEmitter and the Add*FromOtherEmitter links).</summary>
    public ParticleEmitterSim[]? Siblings { get; set; }

    /// <summary>The collision trace (null: particles never collide).</summary>
    public ParticleTrace? Trace { get; set; }

    /// <summary>
    /// A rate script set at run time (Emitters[i].InitialParticlesPerSecond and ParticlesPerSecond, both the same), or
    /// null for the sub-emitter's own.
    /// </summary>
    public float? RateOverride { get; set; }

    /// <summary>RespawnDeadParticles as script set it at run time, or null for the sub-emitter's own.</summary>
    public bool? RespawnOverride { get; set; }

    /// <summary>
    /// FlicFlocEmitter.VariableMaxParticles: while not 0, the most particles the rate may still spawn (each spawn uses
    /// one; once used up it is 0 and the rate spawns freely again, UFlicFlocEmitter::SpawnParticles).
    /// </summary>
    public int SpawnBudget { get; set; }

    /// <summary>Called for every new particle once its start place is set (a FlicFlocEmitter's body splash, for one).</summary>
    public ParticleSpawnHook? OnSpawn { get; set; }

    private bool Respawns => RespawnOverride ?? Params.RespawnDeadParticles;

    /// <summary>Script's <c>AllParticlesDead = false; WarmedUp = false</c>: the sub-emitter runs again.</summary>
    public void Restart()
    {
        AllParticlesDead = false;
        WarmedUp = false;
    }

    /// <summary>Particles alive now.</summary>
    public int Alive
    {
        get
        {
            int n = 0;
            for (int i = 0; i < ActiveParticles; i++)
            {
                if (_particles[i].Alive)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Reset: nothing used, not all dead, a new initial delay and reset time (rule 3).</summary>
    [PortOf("eng:UParticleEmitter::Reset", Spec = "spec/native/emitters.md#rules")]
    public void Reset()
    {
        ActiveParticles = 0;
        _particleIndex = 0;
        _otherIndex = 0;
        _ppsFraction = 0f;
        AllParticlesDead = false;
        Array.Clear(_particles);
        InitialDelay = Params.InitialDelayRange.Pick(ref _rng);
        TimeTillReset = Params.AutoResetTimeRange.Pick(ref _rng);
    }

    /// <summary>ParticleEmitter.Trigger (rule 4).</summary>
    [PortOf("nat:Engine.ParticleEmitter.Trigger", Spec = "spec/native/emitters.md#rules")]
    public void Trigger()
    {
        if (Params.TriggerDisabled)
        {
            Disabled = !Disabled;
        }

        if (Params.ResetOnTrigger)
        {
            Reset();
        }

        _currentSpawnOnTrigger = (int)MathF.Round(Params.SpawnOnTriggerRange.Pick(ref _rng));
        if (_currentSpawnOnTrigger != 0)
        {
            AllParticlesDead = false;
        }
    }

    /// <summary>Emitter.Kill: no more spawns.</summary>
    public void Kill() => _killPending = true;

    /// <summary>The one-time warm-up of rule 2.2.</summary>
    public void WarmUp(ParticleOwner owner)
    {
        if (WarmedUp)
        {
            return;
        }

        WarmedUp = true;
        ParticleParams p = Params;
        if (p.WarmupTicksPerSecond == 0f || p.RelativeWarmupTime == 0f)
        {
            return;
        }

        int ticks = (int)MathF.Round(p.LifetimeRange.Average * p.RelativeWarmupTime * p.WarmupTicksPerSecond);
        float dt = 1f / p.WarmupTicksPerSecond;
        for (int i = 0; i < ticks; i++)
        {
            Update(dt, owner);
        }
    }

    // ---- spawning ------------------------------------------------------------------------------------------------

    [PortOf("eng:UParticleEmitter::SpawnParticles", Spec = "spec/native/emitters.md#rules")]
    private float SpawnParticles(float fraction, float rate, float dt)
    {
        if (!(rate > 0f) || MaxActiveParticles == 0)
        {
            return fraction;
        }

        float leftover = fraction + (rate * dt);
        int n = Math.Clamp((int)MathF.Floor(leftover), 0, MaxActiveParticles);
        if (_currentSpawnOnTrigger != 0)
        {
            n = Math.Clamp(n, 0, _currentSpawnOnTrigger);
            _currentSpawnOnTrigger -= n;
        }

        if (SpawnBudget != 0)
        {
            n = Math.Clamp(n, 0, SpawnBudget);
            SpawnBudget -= n;
        }

        float increment = 1f / rate;
        float start = dt + (fraction * increment) - increment;
        for (int k = 0; k < n; k++)
        {
            SpawnParticle(_particleIndex, start - (k * increment), SpawnRate, 0, default);
            ActiveParticles = Math.Max(ActiveParticles, _particleIndex + 1);
            _particleIndex = (_particleIndex + 1) % MaxActiveParticles;
        }

        return leftover - (int)MathF.Floor(leftover);
    }

    /// <summary>Queues a particle as script's SpawnParticle does (spawned at the next update).</summary>
    public void DeferSpawn(int count) => _deferred += Math.Max(0, count);

    /// <summary>SpawnParticle (rule 6).</summary>
    [PortOf("eng:UParticleEmitter::SpawnParticle", Spec = "spec/native/emitters.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "UParticleEmitter::SpawnParticle 0x110ebc10 (editor twin), USparkEmitter::SpawnParticle 0x110f2d70")]
    public void SpawnParticle(int slot, float offset, int flags, int spawnFlags, FVector at)
    {
        ParticleParams p = Params;
        if (_particles.Length == 0 || _killPending || p.LifetimeRange.Max <= 0f || slot < 0 || slot >= _particles.Length)
        {
            return;
        }

        ref Particle q = ref _particles[slot];
        q = default;
        q.Location = p.StartLocationOffset;
        q.Velocity = p.StartVelocityRange.Pick(ref _rng);
        q.Location += p.StartLocationRange.Pick(ref _rng);
        q.ColorMultiplier = new FVector(1f, 1f, 1f);
        _otherIndex++;
        if (Sibling(p.AddLocationFromOtherEmitter) is { ActiveParticles: > 0 } other)
        {
            q.Location += other._particles[_otherIndex % other.ActiveParticles].Location - _owner.Location;
        }

        q.Location = Turn(q.Location);
        q.RevolutionCenter = p.RevolutionCenterOffsetRange.Pick(ref _rng);
        q.RevolutionsPerSecond = p.RevolutionsPerSecondRange.Pick(ref _rng);
        if (p.CoordinateSystem == 0)
        {
            if ((spawnFlags & SpawnNoOwnerLocation) == 0)
            {
                q.Location += _owner.Location + (_owner.Velocity * offset);
            }

            q.RevolutionCenter += _owner.Location;
        }

        if ((spawnFlags & SpawnNoGlobalOffset) == 0)
        {
            q.Location += _owner.GlobalOffset;
        }

        q.Location += at;
        OnSpawn?.Invoke(ref q);
        q.OldLocation = q.Location;
        q.StartLocation = q.Location;
        q.ColorMultiplier *= p.ColorMultiplierRange.Pick(ref _rng);
        q.MaxLifetime = p.LifetimeRange.Pick(ref _rng);
        q.HitCount = 0;
        q.Flags = flags | FlagAlive;
        q.Time = p.InitialTimeRange.Pick(ref _rng) + offset;
        q.Mass = p.StartMassRange.Pick(ref _rng);
        q.StartSize = p.StartSizeRange.Pick(ref _rng);
        if (p.UniformSize)
        {
            q.StartSize = new FVector(q.StartSize.X, q.StartSize.X, q.StartSize.X);
        }

        q.Size = q.StartSize;
        q.Velocity += p.Acceleration * offset;
        q.VelocityMultiplier = new FVector(1f, 1f, 1f);
        q.RevolutionsMultiplier = new FVector(1f, 1f, 1f);
        q.Velocity = Turn(q.Velocity);
        if (p.GetVelocityDirectionFrom != 0)
        {
            FVector dir = (p.CoordinateSystem == 1 ? q.Location : q.Location - _owner.Location).SafeNormal();
            q.Velocity = p.GetVelocityDirectionFrom switch
            {
                1 => -(q.Velocity * dir),
                2 => q.Velocity * dir,
                3 => q.Velocity + (dir * p.StartVelocityRadialRange.Pick(ref _rng)),
                _ => q.Velocity,
            };
        }

        if (Sibling(p.AddVelocityFromOtherEmitter) is { ActiveParticles: > 0 } vel)
        {
            q.Velocity += p.AddVelocityMultiplierRange.Pick(ref _rng) * vel._particles[_otherIndex % vel.ActiveParticles].Velocity;
        }

        q.Location += q.Velocity * offset;
        if (q.Mass != 0f)
        {
            q.Mass = 1f / q.Mass;
        }

        q.StartSpin = p.StartSpinRange.Pick(ref _rng);
        q.SpinsPerSecond = p.SpinsPerSecondRange.Pick(ref _rng);
        if (_rng.Next() < p.SpinCCWorCW.X)
        {
            q.SpinsPerSecond.X = -q.SpinsPerSecond.X;
        }

        if (_rng.Next() < p.SpinCCWorCW.Y)
        {
            q.SpinsPerSecond.Y = -q.SpinsPerSecond.Y;
        }

        if (_rng.Next() < p.SpinCCWorCW.Z)
        {
            q.SpinsPerSecond.Z = -q.SpinsPerSecond.Z;
        }

        q.StartSpin *= ParticleK.RotatorTurn;
        q.SpinsPerSecond *= ParticleK.RotatorTurn;
        if (p.UseRandomSubdivision)
        {
            q.Subdivision = p.SubdivisionEnd != 0
                ? (int)(p.SubdivisionStart + (_rng.Next() * (p.SubdivisionEnd - p.SubdivisionStart)))
                : (int)(_rng.Next() * p.Cells);
        }
        else
        {
            q.Subdivision = -1;
        }

        if (p.Kind == ParticleKind.Spark)
        {
            q.TimeBeforeVisible = p.TimeBeforeVisibleRange.Pick(ref _rng);
            q.TimeBetweenSegments = p.TimeBetweenSegmentsRange.Pick(ref _rng);
            q.SparkLocation = q.Location;
            q.SparkVelocity = q.Velocity;
        }

        if (q.Time > q.MaxLifetime && q.MaxLifetime != 0f)
        {
            float again = q.Time % q.MaxLifetime;
            SpawnParticle(slot, again, 0, 0, default);
            return;
        }

        AllParticlesDead = false;
    }

    private ParticleEmitterSim? Sibling(int index) =>
        index >= 0 && Siblings is { } s && index < s.Length && !ReferenceEquals(s[index], this) ? s[index] : null;

    /// <summary>UseRotationFrom applied to a start location or velocity (rules 6.4 and 6.11).</summary>
    private FVector Turn(FVector v)
    {
        ParticleParams p = Params;
        return p.UseRotationFrom switch
        {
            1 => Rotate(Rotate(v, p.RotationOffset), _owner.Rotation),
            2 => Rotate(v, p.RotationOffset),
            3 => Rotate(v, NormalRotation(p)),
            _ => v,
        };
    }

    private static FRotator NormalRotation(ParticleParams p)
    {
        FRotator r = p.RotationNormal.Rotation();
        return p.EffectAxis == 1 ? new FRotator(r.Pitch - 16384, r.Yaw, r.Roll) : r;
    }

    public static FVector Rotate(FVector v, FRotator r)
    {
        r.GetAxes(out FVector x, out FVector y, out FVector z);
        return (x * v.X) + (y * v.Y) + (z * v.Z);
    }

    // ---- update --------------------------------------------------------------------------------------------------

    /// <summary>One UpdateParticles of <paramref name="dt"/> seconds with the owner where it is now (rules 5 to 16).</summary>
    [Provenance(ProvenanceKind.Decoded, "UParticleEmitter update 0x110ec950 (editor twin): rate, spawn, age, move, revolution, collision, curves, colour and fade, velocity clamp and loss")]
    public void Update(float dt, ParticleOwner owner)
    {
        _owner = owner;
        ParticleParams p = Params;
        float rate;
        if (ActiveParticles < MaxActiveParticles)
        {
            rate = p.AutomaticInitialSpawning
                ? MaxActiveParticles / ((p.LifetimeRange.Max + p.LifetimeRange.Min) * 0.5f)
                : RateOverride ?? p.InitialParticlesPerSecond;
        }
        else
        {
            rate = RateOverride ?? p.ParticlesPerSecond;
        }

        if (_currentSpawnOnTrigger != 0)
        {
            rate += p.SpawnOnTriggerPPS;
        }

        if (rate > 0f && !_killPending)
        {
            _ppsFraction = SpawnParticles(_ppsFraction, rate, dt);
        }

        if (!_killPending && _deferred > 0 && MaxActiveParticles > 0)
        {
            int n = Math.Min(_deferred, MaxActiveParticles);
            for (int i = 0; i < n; i++)
            {
                SpawnParticle(_particleIndex, 0f, 0, 0, default);
                ActiveParticles = Math.Max(ActiveParticles, _particleIndex + 1);
                _particleIndex = (_particleIndex + 1) % MaxActiveParticles;
            }
        }

        _deferred = 0;
        int dead = 0;
        int used = Math.Min(ActiveParticles, MaxActiveParticles);
        for (int i = 0; i < used; i++)
        {
            ref Particle q = ref _particles[i];
            if (!q.Alive)
            {
                dead++;
                continue;
            }

            bool moves = (q.Flags & FlagResting) == 0 || p.CoordinateSystem == 1;
            if ((q.Flags & FlagSpawnedThisTick) != 0)
            {
                moves = false;
                q.Flags &= ~FlagSpawnedThisTick;
            }
            else
            {
                q.Time += dt;
            }

            if (q.Time > q.MaxLifetime)
            {
                if (!Respawns || _killPending)
                {
                    dead++;
                    q.Flags &= ~FlagAlive;
                    continue;
                }

                SpawnParticle(i, q.MaxLifetime == 0f ? 0f : q.Time % q.MaxLifetime, 0, 0, default);
            }
            else if (moves)
            {
                q.Velocity += p.Acceleration * dt;
                q.OldLocation = q.Location;
                q.Location += q.Velocity * q.VelocityMultiplier * dt;
                if (p.UseRevolution)
                {
                    q.Location = Revolve(q, dt);
                }
            }

            bool bounced = false;
            if (moves && p.CoordinateSystem != 1 && p.UseCollision && Trace is { } trace && !Collide(ref q, i, dt, trace, ref dead, ref bounced))
            {
                continue;
            }

            Evaluate(ref q);
            ClampAndLose(ref q, dt, bounced);
        }

        // Emitter.Kill: no rate and no respawn once killed, so the sub-emitter is all dead when its last particle goes.
        bool spawning = (dead < MaxActiveParticles && ActiveParticles != dead) || (!_killPending && (rate != 0f || Respawns));
        AllParticlesDead = !spawning;
    }

    /// <summary>UseRevolution: the location turned about the revolution centre (rule 9).</summary>
    private static FVector Revolve(in Particle q, float dt)
    {
        FVector turns = q.RevolutionsPerSecond * q.RevolutionsMultiplier * dt;
        FVector v = q.Location - q.RevolutionCenter;
        v = RotateAngleAxis(v, turns.X * MathF.Tau, new FVector(1f, 0f, 0f));
        v = RotateAngleAxis(v, turns.Y * MathF.Tau, new FVector(0f, 1f, 0f));
        v = RotateAngleAxis(v, turns.Z * MathF.Tau, new FVector(0f, 0f, 1f));
        return v + q.RevolutionCenter;
    }

    /// <summary>FVector::RotateAngleAxis (decoded 0x10fe9070) with the angle in radians.</summary>
    public static FVector RotateAngleAxis(FVector v, float angle, FVector axis)
    {
        float s = MathF.Sin(angle);
        float c = MathF.Cos(angle);
        float omc = 1f - c;
        float xy = axis.X * axis.Y * omc;
        float yz = axis.Y * axis.Z * omc;
        float zx = axis.Z * axis.X * omc;
        return new FVector(
            ((axis.X * axis.X * omc) + c) * v.X + (xy - (s * axis.Z)) * v.Y + (zx + (s * axis.Y)) * v.Z,
            (xy + (s * axis.Z)) * v.X + ((axis.Y * axis.Y * omc) + c) * v.Y + (yz - (s * axis.X)) * v.Z,
            (zx - (s * axis.Y)) * v.X + (yz + (s * axis.X)) * v.Y + ((axis.Z * axis.Z * omc) + c) * v.Z);
    }

    /// <summary>Rule 10; false when the particle died (or respawned) and is done for this tick.</summary>
    private bool Collide(ref Particle q, int slot, float dt, ParticleTrace trace, ref int dead, ref bool bounced)
    {
        ParticleParams p = Params;
        FVector dir = (q.Location - q.OldLocation).SafeNormal();
        FVector extent = dir * p.ExtentMultiplier * q.Size;
        if (!trace(q.Location + extent, q.OldLocation, out FVector hit, out FVector normal))
        {
            return true;
        }

        FVector at = hit - extent;
        if (p.UseMaxCollisions && q.HitCount + 1 >= (int)MathF.Round(p.MaxCollisions.Pick(ref _rng)))
        {
            if (!Respawns || _killPending)
            {
                dead++;
                q.Flags &= ~FlagAlive;
                return false;
            }

            SpawnParticle(slot, dt * 0.5f, 0, 0, default);
            return false;
        }

        q.Velocity -= p.Acceleration * (dt * 0.5f);
        q.Velocity = q.Velocity.MirrorByVector(normal);
        q.Velocity *= p.DampingFactorRange.Pick(ref _rng);
        if (p.DampRotation)
        {
            q.SpinsPerSecond *= p.RotationDampingFactorRange.Pick(ref _rng);
        }

        FVector rest = at + (normal * ParticleK.BouncePushOut);
        q.Location = rest;
        q.OldLocation = rest;
        q.HitCount++;
        bounced = true;
        if (Sibling(p.SpawnFromOtherEmitter) is { } other && dt != 0f && p.SpawnAmount > 0 && other.MaxActiveParticles > 0)
        {
            for (int k = 0; k < p.SpawnAmount; k++)
            {
                other._owner = _owner;
                other.SpawnParticle(other._particleIndex, dt, 0, SpawnNoGlobalOffset | SpawnNoOwnerLocation, rest);
                if (p.UseSpawnedVelocityScale)
                {
                    other._particles[other._particleIndex].Velocity += q.Velocity * p.SpawnedVelocityScaleRange.Pick(ref _rng);
                }

                other.ActiveParticles = Math.Max(other.ActiveParticles, other._particleIndex + 1);
                other._particleIndex = (other._particleIndex + 1) % other.MaxActiveParticles;
            }
        }

        return true;
    }

    /// <summary>Rules 11 to 14: the size, velocity and revolution multipliers and the colour for the particle's age.</summary>
    private void Evaluate(ref Particle q)
    {
        ParticleParams p = Params;
        float t = q.MaxLifetime == 0f ? 0f : Math.Clamp(q.Time / q.MaxLifetime, 0f, 1f);
        float scale = 1f;
        if (p.UseSizeScale)
        {
            if (p.UseRegularSizeScale)
            {
                scale = 1f / (q.Time + 1f);
            }
            else if (q.MaxLifetime != 0f)
            {
                scale = SizeAt(p.SizeScale, Frac((p.SizeScaleRepeats + 1f) * t));
            }
        }

        q.Size = q.StartSize * scale;
        if (p.UseVelocityScale && q.MaxLifetime != 0f && VectorAt(p.VelocityScale, Frac((p.VelocityScaleRepeats + 1f) * t)) is { } v)
        {
            q.VelocityMultiplier = v;
        }

        if (p.UseRevolutionScale && q.MaxLifetime != 0f && VectorAt(p.RevolutionScale, Frac((p.RevolutionScaleRepeats + 1f) * t)) is { } r)
        {
            q.RevolutionsMultiplier = r;
        }

        q.Color = ColourAt(p, q, t);
    }

    /// <summary>Rule 14: the colour curve, the colour multiplier and the fade.</summary>
    [Provenance(ProvenanceKind.Decoded, "UParticleEmitter update 0x110ec950: colour scale bytes rounded, alpha only for AlphaBlend, RGB x ColorMultiplier, then the fade per draw style (Modulated grey 0x7f7f7f)")]
    public static FxColor ColourAt(ParticleParams p, in Particle q, float t)
    {
        int r = 255, g = 255, b = 255, a = 255;
        if (p.UseColorScale && q.MaxLifetime != 0f && CurveColour(p.ColorScale, Frac((p.ColorScaleRepeats + 1f) * t)) is { } c)
        {
            r = c.R;
            g = c.G;
            b = c.B;
            a = p.DrawStyle == 1 ? c.A : 255;
        }

        r = RoundByte(r * q.ColorMultiplier.X);
        g = RoundByte(g * q.ColorMultiplier.Y);
        b = RoundByte(b * q.ColorMultiplier.Z);
        bool fadeOut = p.FadeOut && q.Time > p.FadeOutStartTime && q.MaxLifetime != p.FadeOutStartTime;
        bool fadeIn = p.FadeIn && q.Time < p.FadeInEndTime && p.FadeInEndTime != 0f;
        if (fadeOut || fadeIn)
        {
            float f;
            (float X, float Y, float Z, float W) k;
            if (fadeOut)
            {
                f = (q.Time - p.FadeOutStartTime) / (q.MaxLifetime - p.FadeOutStartTime);
                k = p.FadeOutFactor;
            }
            else
            {
                f = (p.FadeInEndTime - q.Time) / p.FadeInEndTime;
                k = p.FadeInFactor;
            }

            f *= 255f;
            switch (p.DrawStyle)
            {
                case 2:
                    return new FxColor(127, 127, 127, (byte)(255 - (Math.Clamp((int)MathF.Round(k.W * f), 0, 255))));
                case 1:
                    a = ClampByte(a - (k.W * f));
                    break;
                default:
                    r = ClampByte(r - (k.X * f));
                    g = ClampByte(g - (k.Y * f));
                    b = ClampByte(b - (k.Z * f));
                    a = ClampByte(a - (k.W * f));
                    break;
            }
        }

        return new FxColor((byte)r, (byte)g, (byte)b, (byte)a);
    }

    /// <summary>Rule 15.</summary>
    private void ClampAndLose(ref Particle q, float dt, bool bounced)
    {
        ParticleParams p = Params;
        FVector max = p.MaxAbsVelocity;
        if (max.X != 0f)
        {
            q.Velocity.X = Math.Clamp(q.Velocity.X, -max.X, max.X);
        }

        if (max.Y != 0f)
        {
            q.Velocity.Y = Math.Clamp(q.Velocity.Y, -max.Y, max.Y);
        }

        if (max.Z != 0f)
        {
            q.Velocity.Z = Math.Clamp(q.Velocity.Z, -max.Z, max.Z);
        }

        FVector loss = p.VelocityLossRange.Pick(ref _rng);
        q.Velocity = new FVector(
            q.Velocity.X - (loss.X * q.Velocity.X * dt),
            q.Velocity.Y - (loss.Y * q.Velocity.Y * dt),
            q.Velocity.Z - (loss.Z * q.Velocity.Z * dt));
        if (bounced && p.MinSquaredVelocity != 0f && q.Velocity.SizeSquared() < p.MinSquaredVelocity)
        {
            q.Flags |= FlagResting;
        }
    }

    // ---- curves --------------------------------------------------------------------------------------------------

    public static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>The size curve at x (rule 11): from (0, 1) before the first point; 1 past the last.</summary>
    public static float SizeAt((float Time, float Size)[] points, float x)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (x <= points[i].Time)
            {
                (float t0, float s0) = i == 0 ? (0f, 1f) : points[i - 1];
                float f = points[i].Time == 0f ? 1f : (x - t0) / (points[i].Time - t0);
                return s0 + ((points[i].Size - s0) * f);
            }
        }

        return 1f;
    }

    private static FVector? VectorAt((float Time, FVector Value)[] points, float x)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (x <= points[i].Time)
            {
                (float t0, FVector v0) = i == 0 ? (0f, new FVector(1f, 1f, 1f)) : points[i - 1];
                float f = points[i].Time == 0f ? 1f : (x - t0) / (points[i].Time - t0);
                return v0 + ((points[i].Value - v0) * f);
            }
        }

        return null;
    }

    private static FxColor? CurveColour((float Time, FxColor Color)[] points, float x)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (x <= points[i].Time)
            {
                (float t0, FxColor c0) = i == 0 ? (0f, FxColor.White) : points[i - 1];
                float f = points[i].Time == 0f ? 1f : (x - t0) / (points[i].Time - t0);
                FxColor c1 = points[i].Color;
                return new FxColor(Lerp(c0.R, c1.R, f), Lerp(c0.G, c1.G, f), Lerp(c0.B, c1.B, f), Lerp(c0.A, c1.A, f));
            }
        }

        return null;
    }

    private static byte Lerp(byte a, byte b, float f) => (byte)Math.Clamp((int)MathF.Round(a + ((b - a) * f)), 0, 255);

    private static int RoundByte(float v) => Math.Clamp((int)MathF.Round(v), 0, 255);

    private static int ClampByte(float v) => Math.Clamp((int)MathF.Round(Math.Clamp(v, 0f, 255f)), 0, 255);
}

/// <summary>Constants of the particle natives (spec/native/emitters.md, Constants).</summary>
public static class ParticleK
{
    [Provenance(ProvenanceKind.Decoded, "SpawnParticle scales StartSpin and SpinsPerSecond by 65535.0 (rotator units per turn)")]
    public const float RotatorTurn = 65535f;

    [Provenance(ProvenanceKind.Decoded, "UParticleEmitter update: the bounce puts the particle 0.01 uu out along the hit normal")]
    public const float BouncePushOut = 0.01f;

    [Provenance(ProvenanceKind.Decoded, "the sprite fill's best-axis helper 0x110eea10")]
    public const float BestAxisThreshold = 0.57f;

    [Provenance(ProvenanceKind.Decoded, "the particle collision trace flags (SingleLineCheck 0x84: TRACE_Level and TRACE_LevelGeometry)")]
    public const int TraceFlags = 0x84;
}
