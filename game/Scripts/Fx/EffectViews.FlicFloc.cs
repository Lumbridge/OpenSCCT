using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Fx;

// The rain splashing on a pawn (SFlicFlocEmitter, spec/equipment/rain.md rule 4): the simulation's emitter says what its
// script set (rates, respawn, the particle budget, restarts, the puff trigger); this applies it to the drawn copy, and
// places each new splash on the drawn body as UFlicFlocEmitter::SpawnParticle does.
public sealed partial class EffectViews
{
    private const int FlicSplash = 0;
    private const int FlicDrip = 1;
    private const int FlicPuff = 2;

    /// <summary>
    /// One body segment of the rain-mode spawn table (11 entries): two bones of the mesh's skeleton, each raised by a
    /// height, and the spread across the pawn's forward and right axes.
    /// </summary>
    private readonly record struct SplashSegment(int BoneA, float RiseA, int BoneB, float RiseB, float SpreadForward, float SpreadRight);

    [Provenance(ProvenanceKind.Decoded, "UFlicFlocEmitter::SpawnParticle editor 0x110f0750: the table at 0x116159f0 (11 entries of bone, rise, bone, rise, two spreads)")]
    private static readonly SplashSegment[] s_splashSegments =
    [
        new(14, 2f, 11, 3f, 2f, 5f), new(11, 5f, 10, 5f, 2f, 5f), new(10, 5f, 9, 5f, 2f, 5f), new(9, 8f, 5, 11f, 10f, 2f),
        new(26, 2f, 23, 3f, 2f, 5f), new(23, 5f, 22, 5f, 2f, 5f), new(22, 5f, 21, 5f, 2f, 5f), new(21, 8f, 5, 11f, 10f, 2f),
        new(6, 20f, 6, 20f, 9f, 9f), new(4, 20f, 5, 9f, 5f, 20f), new(4, 25f, 3, 25f, 0f, 20f),
    ];

    // The table names bones by index in the mesh's skeleton; these are the bones of those indices in the converted
    // meshes (spy ATT_01 and merc DEF_01 differ from index 7 on, so the spy's left arm entries fall a bone further out).
    private static readonly Dictionary<int, Name> s_spyBones = Bones(
        (3, "B Spine1"), (4, "B Spine2"), (5, "B Neck"), (6, "B Head"), (9, "B L UpperArm"), (10, "B L ForeArm"),
        (11, "B L Hand"), (14, "B L Finger1"), (21, "B R Clavicle"), (22, "B R UpperArm"), (23, "B R ForeArm"), (26, "B R Finger01"));

    private static readonly Dictionary<int, Name> s_mercBones = Bones(
        (3, "B Spine1"), (4, "B Spine2"), (5, "B Neck"), (6, "B Head"), (9, "B L Clavicle"), (10, "B L UpperArm"),
        (11, "B L ForeArm"), (14, "B L Finger01"), (21, "B R Clavicle"), (22, "B R UpperArm"), (23, "B R ForeArm"), (26, "B R Finger01"));

    private readonly Random _splashRandom = new(7);

    private static Dictionary<int, Name> Bones(params (int Index, string Name)[] bones)
    {
        var map = new Dictionary<int, Name>();
        foreach ((int index, string name) in bones)
        {
            map[index] = new Name(name);
        }

        return map;
    }

    /// <summary>The spawn places of the drawn copy: splashes on the body, drips from the head with the pawn's motion.</summary>
    private void HookFlicFloc(Tracked t, SFlicFlocEmitter flicFloc, ParticleSystemView view)
    {
        IReadOnlyList<ParticleEmitterSim> subs = view.Sim.Emitters;
        if (subs.Count > FlicSplash)
        {
            subs[FlicSplash].OnSpawn = (ref Particle q) => PlaceSplash(flicFloc, ref q);
        }

        if (subs.Count > FlicDrip)
        {
            subs[FlicDrip].OnSpawn = (ref Particle q) => PlaceDrip(t, flicFloc, ref q);
        }
    }

    /// <summary>The script's sub-emitter values on the drawn copy, each restart and trigger once.</summary>
    private static void ApplyFlicFloc(Tracked t, SFlicFlocEmitter flicFloc, ParticleSystemView view)
    {
        IReadOnlyList<ParticleEmitterSim> subs = view.Sim.Emitters;
        if (subs.Count <= FlicPuff)
        {
            return;
        }

        ParticleEmitterSim splash = subs[FlicSplash];
        splash.RateOverride = flicFloc.SplashRate;
        splash.RespawnOverride = flicFloc.SplashRespawn;
        if (t.FlicSplashStarts != flicFloc.SplashStarts)
        {
            t.FlicSplashStarts = flicFloc.SplashStarts;
            splash.SpawnBudget = flicFloc.SplashBudget;
            splash.Restart();
        }

        ParticleEmitterSim drip = subs[FlicDrip];
        drip.RateOverride = flicFloc.DripRate;
        drip.RespawnOverride = flicFloc.DripRespawn;
        if (t.FlicDripStarts != flicFloc.DripStarts)
        {
            t.FlicDripStarts = flicFloc.DripStarts;
            drip.SpawnBudget = flicFloc.DripBudget;
            drip.Restart();
        }

        while (t.FlicPuffTriggers != flicFloc.PuffTriggers)
        {
            t.FlicPuffTriggers++;
            subs[FlicPuff].Trigger();
        }
    }

    /// <summary>
    /// A rain-mode spawn: a random segment of the table, a random point between its two raised bones, spread across
    /// the pawn's forward and right axes by up to half of each spread either way.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UFlicFlocEmitter::SpawnParticle editor 0x110f0750 (bInRainMode, owner with a skeletal mesh)")]
    [Provenance(ProvenanceKind.Estimated, "The point replaces the sub-emitter's own start place (the native's coordinate-system step after it is not decoded); bones are matched by name, from the drawn skeleton")]
    private void PlaceSplash(SFlicFlocEmitter flicFloc, ref Particle q)
    {
        if (flicFloc.Pawn is not { } pawn || Pawns is null)
        {
            return;
        }

        SplashSegment s = s_splashSegments[_splashRandom.Next(s_splashSegments.Length)];
        Dictionary<int, Name> bones = pawn is SPawnAttaque ? s_spyBones : s_mercBones;
        if (!Pawns.TryGetBone(pawn, bones[s.BoneA], out Transform3D a) || !Pawns.TryGetBone(pawn, bones[s.BoneB], out Transform3D b))
        {
            return;
        }

        FVector from = UeSpace.ToUe(a.Origin) + new FVector(0f, 0f, s.RiseA);
        FVector to = UeSpace.ToUe(b.Origin) + new FVector(0f, 0f, s.RiseB);
        pawn.Rotation.GetAxes(out FVector forward, out FVector right, out _);
        float f = (_splashRandom.NextSingle() * s.SpreadForward) - (s.SpreadForward * 0.5f);
        float r = (_splashRandom.NextSingle() * s.SpreadRight) - (s.SpreadRight * 0.5f);
        FVector at = from + ((to - from) * _splashRandom.NextSingle()) + (forward * f) + (right * r);
        q.Location = at;
        q.OldLocation = at;
    }

    /// <summary>A drip: from the emitter on the head, with the pawn's horizontal speed.</summary>
    [Provenance(ProvenanceKind.Decoded, "UFlicFlocEmitter::SpawnParticle editor 0x110f0750: outside rain mode the start velocity takes the owner pawn's Velocity X and Y")]
    [Provenance(ProvenanceKind.Estimated, "The absolute-coordinate start is taken as the emitter's own place")]
    private static void PlaceDrip(Tracked t, SFlicFlocEmitter flicFloc, ref Particle q)
    {
        q.Location += t.Place;
        if (flicFloc.Pawn is { } pawn)
        {
            q.Velocity = new FVector(q.Velocity.X + pawn.Velocity.X, q.Velocity.Y + pawn.Velocity.Y, q.Velocity.Z);
        }
    }
}
