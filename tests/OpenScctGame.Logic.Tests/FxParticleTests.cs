using System.Text.Json;
using OpenScct.Core;
using OpenScctGame.Render.Particles;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The particle natives as decoded in spec/native/emitters.md (synthetic emitters, no game data).</summary>
public sealed class FxParticleTests
{
    private static readonly ParticleOwner Origin = new(default, default, default, default);

    private static ParticleParams Sprite(string json) =>
        ParticleParams.From("Engine.SpriteEmitter", "Test", JsonDocument.Parse(json).RootElement, null, null);

    private static int Alive(ParticleEmitterSim e) => e.Alive;

    [Fact]
    public void A_burst_emitter_makes_max_particles_once_then_nothing()
    {
        // The smoke grenade's cloud: no automatic spawning, 200 a second until the 15 slots were used once, no respawn.
        ParticleParams p = Sprite("""{"MaxParticles": 15, "AutomaticInitialSpawning": false, "InitialParticlesPerSecond": 200, "RespawnDeadParticles": false, "LifetimeRange": {"Min": 10, "Max": 10}}""");
        var e = new ParticleEmitterSim(p, 1);
        e.Update(1f / 60f, Origin);
        Assert.Equal(3, Alive(e));
        for (int i = 0; i < 10; i++)
        {
            e.Update(1f / 60f, Origin);
        }

        Assert.Equal(15, Alive(e));
        Assert.Equal(15, e.ActiveParticles);
        for (int i = 0; i < 60 * 11; i++)
        {
            e.Update(1f / 60f, Origin);
        }

        Assert.Equal(0, Alive(e));
        Assert.True(e.AllParticlesDead);
    }

    [Fact]
    public void Particles_spawned_in_a_frame_are_aged_by_their_share_of_it()
    {
        // Rate 10, dt 0.25: two particles at ages dt - 1/rate = 0.15 and 0.05 (rule 5); they do not age further that tick.
        ParticleParams p = Sprite("""{"MaxParticles": 10, "AutomaticInitialSpawning": false, "InitialParticlesPerSecond": 10, "LifetimeRange": {"Min": 4, "Max": 4}}""");
        var e = new ParticleEmitterSim(p, 1);
        e.Update(0.25f, Origin);
        Assert.Equal(2, Alive(e));
        Assert.Equal(0.15f, e.Particles[0].Time, 4);
        Assert.Equal(0.05f, e.Particles[1].Time, 4);
    }

    [Fact]
    public void Automatic_spawning_spreads_max_particles_over_one_average_lifetime()
    {
        ParticleParams p = Sprite("""{"MaxParticles": 20, "LifetimeRange": {"Min": 1, "Max": 3}}""");
        var e = new ParticleEmitterSim(p, 3);
        e.Update(0.5f, Origin);
        Assert.Equal(5, Alive(e));
    }

    [Fact]
    public void The_size_curve_starts_at_one_and_stops_after_its_last_point()
    {
        Assert.Equal(4.5f, ParticleEmitterSim.SizeAt([(1f, 8f)], 0.5f), 4);
        Assert.Equal(17f, ParticleEmitterSim.SizeAt([(0f, 17f), (1f, 30f)], 0f), 4);
        Assert.Equal(23.5f, ParticleEmitterSim.SizeAt([(0f, 17f), (1f, 30f)], 0.5f), 4);
        Assert.Equal(1f, ParticleEmitterSim.SizeAt([(0.5f, 5f)], 0.75f), 4);
    }

    [Fact]
    public void Fade_out_lowers_only_alpha_for_alpha_blend_and_every_channel_otherwise()
    {
        var q = new Particle { Time = 9f, MaxLifetime = 10f, ColorMultiplier = new FVector(1f, 1f, 1f) };
        ParticleParams alpha = Sprite("""{"DrawStyle": 1, "FadeOut": true, "FadeOutStartTime": 8}""");
        FxColor a = ParticleEmitterSim.ColourAt(alpha, q, 0.9f);
        Assert.Equal(new FxColor(255, 255, 255, 128), a);
        ParticleParams add = Sprite("""{"DrawStyle": 3, "FadeOut": true, "FadeOutStartTime": 8}""");
        Assert.Equal(new FxColor(128, 128, 128, 128), ParticleEmitterSim.ColourAt(add, q, 0.9f));
        ParticleParams modulated = Sprite("""{"DrawStyle": 2, "FadeOut": true, "FadeOutStartTime": 8}""");
        Assert.Equal(new FxColor(127, 127, 127, 127), ParticleEmitterSim.ColourAt(modulated, q, 0.9f));
    }

    [Fact]
    public void The_colour_curve_alpha_counts_only_with_alpha_blend()
    {
        const string curve = "\"ColorScale\": [{\"RelativeTime\": 0, \"Color\": {\"R\": 93, \"G\": 93, \"B\": 93, \"A\": 255}}, {\"RelativeTime\": 2, \"Color\": {\"R\": 255, \"G\": 255, \"B\": 255, \"A\": 0}}]";
        var q = new Particle { Time = 5f, MaxLifetime = 10f, ColorMultiplier = new FVector(1f, 1f, 1f) };
        FxColor blended = ParticleEmitterSim.ColourAt(Sprite($$"""{"DrawStyle": 1, "UseColorScale": true, {{curve}}}"""), q, 0.5f);
        Assert.Equal(new FxColor(134, 134, 134, 191), blended);
        FxColor additive = ParticleEmitterSim.ColourAt(Sprite($$"""{"DrawStyle": 3, "UseColorScale": true, {{curve}}}"""), q, 0.5f);
        Assert.Equal(255, additive.A);
    }

    [Fact]
    public void Cells_run_down_a_column_first()
    {
        Assert.Equal((0f, 0.25f, 0.25f, 0.5f), ParticleGeometry.Cell(1, 4, 4));
        Assert.Equal((0.25f, 0f, 0.5f, 0.25f), ParticleGeometry.Cell(4, 4, 4));
        Assert.Equal((0.5f, 0.5f, 1f, 1f), ParticleGeometry.Cell(3, 2, 2));
    }

    [Fact]
    public void Age_picks_the_cell_when_the_subdivision_is_not_random()
    {
        ParticleParams p = Sprite("""{"TextureUSubdivisions": 2, "TextureVSubdivisions": 2}""");
        var q = new Particle { Time = 0.6f, MaxLifetime = 1f, Subdivision = -1, Flags = 1 };
        var quad = new SpriteQuad();
        ParticleGeometry.Cells(p, q, ref quad);
        Assert.Equal(0.5f, quad.U0);
        Assert.Equal(0f, quad.V0);
    }

    [Fact]
    public void Inward_direction_multiplies_the_start_velocity_by_the_direction_and_negates_it()
    {
        ParticleParams p = Sprite("""{"MaxParticles": 1, "AutomaticInitialSpawning": false, "InitialParticlesPerSecond": 100, "StartLocationOffset": {"X": 10, "Y": 0, "Z": 0}, "StartVelocityRange": {"X": {"Min": 50, "Max": 50}, "Y": {"Min": 50, "Max": 50}, "Z": {"Min": 0, "Max": 0}}, "GetVelocityDirectionFrom": 1}""");
        var e = new ParticleEmitterSim(p, 1);
        e.Update(0.01f, Origin);
        Particle q = e.Particles[0];
        Assert.Equal(-50f, q.Velocity.X, 3);
        Assert.Equal(0f, q.Velocity.Y, 3);
    }

    [Fact]
    public void A_negative_spin_turns_the_other_way_from_its_start()
    {
        float forward = ParticleGeometry.SpinAngle(1000f, 6553.5f, 1f);
        float back = ParticleGeometry.SpinAngle(1000f, -6553.5f, 1f);
        Assert.Equal((1000 + 6554) * (System.MathF.Tau / 65536f), forward, 3);
        Assert.Equal((0xffff - ((1000 + 6554) & 0xffff)) * (System.MathF.Tau / 65536f), back, 3);
    }

    [Fact]
    public void A_colliding_particle_bounces_off_the_floor_damped()
    {
        ParticleParams p = Sprite("""{"MaxParticles": 1, "AutomaticInitialSpawning": false, "InitialParticlesPerSecond": 100, "UseCollision": true, "StartLocationOffset": {"X": 0, "Y": 0, "Z": 5}, "StartVelocityRange": {"X": {"Min": 0, "Max": 0}, "Y": {"Min": 0, "Max": 0}, "Z": {"Min": -100, "Max": -100}}, "DampingFactorRange": {"X": {"Min": 0.5, "Max": 0.5}, "Y": {"Min": 0.5, "Max": 0.5}, "Z": {"Min": 0.5, "Max": 0.5}}, "StartSizeRange": {"X": {"Min": 1, "Max": 1}, "Y": {"Min": 1, "Max": 1}, "Z": {"Min": 1, "Max": 1}}, "LifetimeRange": {"Min": 10, "Max": 10}}""");
        var e = new ParticleEmitterSim(p, 1)
        {
            Trace = (FVector start, FVector end, out FVector hit, out FVector normal) =>
            {
                normal = new FVector(0f, 0f, 1f);
                if (start.Z > 0f == end.Z > 0f)
                {
                    hit = default;
                    return false;
                }

                float f = start.Z / (start.Z - end.Z);
                hit = start + ((end - start) * f);
                return true;
            },
        };
        e.Update(0.001f, Origin);
        for (int i = 0; i < 12; i++)
        {
            e.Update(0.01f, Origin);
        }

        Particle q = e.Particles[0];
        Assert.Equal(1, q.HitCount);
        Assert.True(q.Velocity.Z > 0f && q.Velocity.Z <= 50.01f);
        Assert.True(q.Location.Z >= 0f);
    }

    [Fact]
    public void An_initial_delay_holds_the_emitter_back()
    {
        ParticleParams p = Sprite("""{"MaxParticles": 5, "AutomaticInitialSpawning": false, "InitialParticlesPerSecond": 1000, "InitialDelayRange": {"Min": 0.3, "Max": 0.3}}""");
        var sim = new ParticleSystemSim([p], new ParticleSystemParams(false, false, default, default), 9);
        for (int i = 0; i < 18; i++)
        {
            sim.Step(1f / 60f, default, default, default);
        }

        Assert.Equal(0, sim.Alive);
        for (int i = 0; i < 3; i++)
        {
            sim.Step(1f / 60f, default, default, default);
        }

        Assert.Equal(5, sim.Alive);
    }

    [Fact]
    public void Relative_particles_follow_their_owner()
    {
        ParticleParams p = Sprite("""{"MaxParticles": 1, "AutomaticInitialSpawning": false, "InitialParticlesPerSecond": 100, "CoordinateSystem": 1, "StartLocationOffset": {"X": 10, "Y": 0, "Z": 0}}""");
        var e = new ParticleEmitterSim(p, 1);
        var owner = new ParticleOwner(new FVector(100f, 0f, 0f), new FRotator(0, 16384, 0), default, default);
        e.Update(0.01f, owner);
        FVector world = ParticleGeometry.World(p, e.Particles[0].Location, owner);
        Assert.Equal(100f, world.X, 2);
        Assert.Equal(10f, world.Y, 2);
    }
}
