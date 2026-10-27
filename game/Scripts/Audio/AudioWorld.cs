using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;

namespace OpenScctGame.Audio;

/// <summary>An actor's ambient loop as the sound system sees it this frame (spec/native/audio.md rule 19).</summary>
public readonly record struct AmbientSource(
    int ActorId,
    string Id,
    FVector Location,
    string Sound,
    byte SoundVolume,
    float SoundRadius,
    byte SoundPitch,
    bool IsZone,
    int LightType,
    float LightBrightness);

/// <summary>What the sound system reads from the client's simulation. It never writes to it.</summary>
public interface IAudioWorld
{
    /// <summary>The current location of an actor the simulation knows, for sources that follow their actor.</summary>
    bool TryGetActorLocation(int actorId, out FVector location);

    /// <summary>Every actor whose AmbientSound is set now.</summary>
    void CollectAmbient(List<AmbientSource> into);

    /// <summary>The map id of the ZoneInfo whose zone holds the point, or null.</summary>
    string? ZoneIdAt(FVector location);

    /// <summary>The surface type of what a line from <paramref name="start"/> to <paramref name="end"/> hits first, or -1.</summary>
    int SurfaceOf(FVector start, FVector end);
}

/// <summary>
/// <see cref="IAudioWorld"/> over a <see cref="Level"/> (the client's world) and its collision. Actor ids for zones
/// come from <paramref name="mapIdOf"/> (MapLoadResult.RecordOf(actor).Id); actors the map did not place have none.
/// </summary>
public sealed class LevelAudioWorld(Level level, ICollisionWorld? collision, Func<Actor, string?>? mapIdOf = null) : IAudioWorld
{
    public Level Level { get; } = level ?? throw new ArgumentNullException(nameof(level));

    public bool TryGetActorLocation(int actorId, out FVector location)
    {
        Actor? a = Level.FindActor(actorId);
        location = a?.Location ?? default;
        return a is not null && !a.bDeleteMe;
    }

    public void CollectAmbient(List<AmbientSource> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        // Indexed, not foreach over the interface (a boxed enumerator every frame).
        IReadOnlyList<Actor> actors = Level.Actors;
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (a is null || a.bDeleteMe || a.AmbientSound.IsNone)
            {
                continue;
            }

            into.Add(new AmbientSource(a.ActorId, mapIdOf?.Invoke(a) ?? SpawnedId(a), a.Location,
                a.AmbientSound.Path!, a.SoundVolume, a.SoundRadius, a.SoundPitch, a is ZoneInfo, (int)a.LightType,
                a.LightBrightness));
        }
    }

    private readonly Dictionary<int, string> _spawnedIds = [];

    /// <summary>The id of an ambient actor the map did not place (class name and actor id), made once per actor.</summary>
    private string SpawnedId(Actor a)
    {
        if (!_spawnedIds.TryGetValue(a.ActorId, out string? id))
        {
            id = a.ClassName.ToString() + a.ActorId;
            _spawnedIds[a.ActorId] = id;
        }

        return id;
    }

    public string? ZoneIdAt(FVector location)
    {
        ZoneInfo? z = collision?.GetZoneInfo(location);
        return z is null ? null : mapIdOf?.Invoke(z);
    }

    public int SurfaceOf(FVector start, FVector end)
    {
        if (collision is null)
        {
            return -1;
        }

        return collision.LineCheck(out FCheckResult hit, null, end, start, default, ETraceFlags.TRACE_World)
            ? hit.SurfaceType
            : -1;
    }
}
