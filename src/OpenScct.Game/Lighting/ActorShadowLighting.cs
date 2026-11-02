using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Lighting;

/// <summary>
/// A normal actor light for the original's shadow cache. Present is membership in this frame's candidate set;
/// an off/occluded-zone light can remain alive in the cache while its visibility fades out. Radius is LightRadius.
/// Echelon spot-shadow lights use a separate renderer path and do not enter this cache.
/// </summary>
public readonly record struct ActorShadowLight(object Key, FVector Location, FRotator Rotation, float Brightness,
    float Radius, byte Cone, int Effect, bool ActorShadows, bool Static, bool Dynamic, bool Present = true, bool Alive = true);

/// <summary>
/// Native actor-light visibility cache and own-spy ShadowDarkness. This is distinct from actor surface shading:
/// all twelve cached lights can contribute, independent of MaxLights, normal, hue, ambient and light-type modulation.
/// The client keeps one instance per local pawn; the resulting byte controls the extra sticky-camera view solve.
/// </summary>
[PortOf("eng:Render.ActorShadowLighting", Spec = "spec/native/camera.md#4-other-view-sources")]
public sealed class ActorShadowLighting
{
    [Provenance(ProvenanceKind.Decoded, "Game 0x10a363d0: twelve 24-byte cached light entries")]
    public const int Capacity = 12;

    [Provenance(ProvenanceKind.Decoded, "Game 0x10a37610: at most 256 ordinary light candidates before ranking")]
    public const int MaxCandidates = 256;

    [Provenance(ProvenanceKind.Decoded, "Game 0x10a363d0: visibility recheck/fade interval at 0x10c11200 / 0x10c11004")]
    public const float VisibilitySeconds = 0.35f;

    private sealed class Entry(ActorShadowLight light, int rank)
    {
        public ActorShadowLight Light = light;
        public int Rank = rank;
        public bool Current;
        public bool Previous;
        public float LastCheck = -VisibilitySeconds;
    }

    private readonly List<Entry> _cache = new(Capacity);
    private readonly Dictionary<object, ActorShadowLight> _live = [];
    private readonly List<(ActorShadowLight Light, int Rank)> _candidates = [];
    private bool _initialized;
    private float _lastTime;

    /// <summary>The byte last written by native lighting; initially zero until the first lighting update.</summary>
    public byte Darkness { get; private set; }

    public void Reset()
    {
        _cache.Clear();
        _live.Clear();
        _candidates.Clear();
        _initialized = false;
        _lastTime = 0f;
        Darkness = 0;
    }

    /// <summary>
    /// Updates lighting after the actor tick. The projector tick uses the PREVIOUS Darkness value, so callers must
    /// retain that value for this frame's camera solve before calling Update. Center/radius are the actor's render
    /// bounding sphere; shadowOrigin is skeletal bone zero, or actor location + (0,0,5) without a pose.
    /// Visible performs the original shadow-caster line check from center (only static lights require it).
    /// </summary>
    public byte Update(IReadOnlyList<ActorShadowLight> lights, FVector center, float radius, FVector shadowOrigin,
        float time, float darknessScale, bool lightingVisibility, byte glassEffect,
        Func<ActorShadowLight, FVector, bool> visible)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(visible);
        if (_initialized && time < _lastTime) Reset();
        _lastTime = time;
        // bLightingVisibility bypasses this entire native cache/shadow path, rather than making all lights visible.
        if (!lightingVisibility) return Darkness;

        _live.Clear();
        _candidates.Clear();
        int presentCount = 0;
        foreach (ActorShadowLight light in lights)
        {
            if (light.Effect == ActorLightRules.LE_ESpotShadow || !light.Alive) continue;
            if (_live.ContainsKey(light.Key)) continue;
            bool present = light.Present && presentCount < MaxCandidates;
            _live.Add(light.Key, light with { Present = present });
            if (!present) continue;
            presentCount++;
            int rank = Rank(light, center, radius);
            if (rank > 0) _candidates.Add((light, rank));
        }

        // World-render caller 0x10a37610 skips the native updater when its candidate set is empty. Preserve both
        // the cache and the last byte, including pending fades, instead of inventing a fresh dark sample.
        if (presentCount == 0) return Darkness;

        for (int i = _cache.Count - 1; i >= 0; i--)
        {
            Entry entry = _cache[i];
            if (!_live.TryGetValue(entry.Light.Key, out ActorShadowLight light))
            {
                _cache.RemoveAt(i);
                continue;
            }
            int rank = Rank(light, center, radius);
            if (rank <= 0)
            {
                _cache.RemoveAt(i);
                continue;
            }
            entry.Light = light;
            entry.Rank = rank;
        }

        _candidates.Sort((a, b) => b.Rank.CompareTo(a.Rank));
        foreach ((ActorShadowLight light, int rank) in _candidates)
        {
            bool cached = false;
            foreach (Entry entry in _cache)
            {
                if (!Equals(entry.Light.Key, light.Key)) continue;
                cached = true;
                break;
            }
            if (cached) continue;
            if (_cache.Count < Capacity)
            {
                _cache.Add(new Entry(light, rank));
                continue;
            }
            int weakest = 0;
            for (int i = 1; i < _cache.Count; i++)
                if (_cache[i].Rank < _cache[weakest].Rank) weakest = i;
            if (rank > _cache[weakest].Rank) _cache[weakest] = new Entry(light, rank);
        }
        _cache.Sort((a, b) => b.Rank.CompareTo(a.Rank));

        foreach (Entry entry in _cache)
        {
            if (entry.LastCheck > time - VisibilitySeconds) continue;
            entry.Previous = entry.Current;
            entry.Current = entry.Light.Present && (!entry.Light.Static || visible(entry.Light, center));
            if (entry.Light.Dynamic) entry.Previous = entry.Current;
            // A newly created whole cache starts fully lit; a new entry in an existing cache fades in.
            entry.LastCheck = _initialized ? time : time - VisibilitySeconds;
        }
        _initialized = true;

        if (glassEffect != 0)
        {
            Darkness = 0;
            return Darkness;
        }

        float sum = 0f, squared = 0f;
        foreach (Entry entry in _cache)
        {
            ActorShadowLight light = entry.Light;
            if (!light.ActorShadows || !(entry.Current || entry.Previous)) continue;
            float from = entry.Previous ? 1f : 0f;
            float to = entry.Current ? 1f : 0f;
            float blend = from + (to - from) * ((time - entry.LastCheck) / VisibilitySeconds);
            float gain = Math.Clamp(2f * blend, 0f, 1f);
            float weight = light.Brightness * gain;
            if (light.Effect != ActorLightRules.LE_Sunlight)
            {
                float reach = ActorLightRules.WorldRadius(light.Radius);
                float distance = (light.Location - shadowOrigin).Size();
                if (distance >= reach || reach <= 0f) continue;
                weight *= 1f - distance / reach;
            }
            if (weight <= 0f || !float.IsFinite(weight)) continue;
            sum += weight;
            squared += weight * weight;
        }

        float strength = sum > 0f ? darknessScale * squared / sum : 0f;
        Darkness = float.IsFinite(strength) ? (byte)Math.Clamp(strength, 0f, 255f) : (byte)0;
        return Darkness;
    }

    /// <summary>Native light ranking (0x10a36290), including its narrower spot cone and integer quantization.</summary>
    public static int Rank(ActorShadowLight light, FVector center, float radius)
    {
        if (light.Effect == ActorLightRules.LE_Sunlight) return int.MaxValue - 1;
        FVector relative = center - light.Location;
        if (ActorLightRules.IsSpot(light.Effect))
        {
            float distance = relative.Size();
            float dot = distance > 0f ? (relative * (1f / distance)) | light.Rotation.Vector() : 0f;
            float cone = 1f - light.Cone / 256f;
            if (dot <= 0f || dot <= cone * cone) return 0;
        }
        float reach = ActorLightRules.WorldRadius(light.Radius) + radius;
        if (reach <= 0f || !float.IsFinite(reach)) return 0;
        double score = Math.Floor(1024f * light.Brightness * (1f - relative.SizeSquared() / (reach * reach)) + 0.5f);
        return double.IsFinite(score) ? (int)Math.Clamp(score, 0, int.MaxValue - 2) : 0;
    }
}
