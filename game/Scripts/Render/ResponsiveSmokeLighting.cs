using System;
using System.Runtime.CompilerServices;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render;

/// <summary>Spatial incident-light probes for smoke. Reads published cells, never a live physics world.</summary>
internal static class ResponsiveSmokeLighting
{
    public const int ProbeCount = 8;
    public const int RowsPerProbe = 3;
    public const float ProbeHalfSpan = 150;
    private const float IndirectRadius = 300;
    private const float IndirectGain = .5f;
    private const float IndirectMaximum = .22f;
    private static readonly bool LogProbes = System.Environment.GetEnvironmentVariable("SVM_RESPONSIVE_SMOKE_LIGHT_PROBES") == "1";
    private static readonly ConditionalWeakTable<object, ProbePositions> Positions = new();
    private sealed class ProbePositions
    {
        public uint SnapshotRevision;
        public FVector Origin, Center;
        public FVector[]? Points;
        public bool DiagnosticLogged;
    }
    internal static long ProjectionBuilds { get; private set; }

    /// <summary>Each probe is ambient RGB, direct RGB/distance uu, and incident direction in UE coordinates.</summary>
    public static Vector4[] Capture(RenderMap? map, SmokeRenderCloud cloud)
    {
        if (map is null) return [];
        ProbePositions cached = Positions.GetValue(cloud.Identity, static _ => new());
        // Fast bullet updates replace immutable frames but do not change reachable geometry. A newer reliable
        // baseline can change cells even with an older common revision, so key this cache by SnapshotRevision.
        if (cached.Points is null || cached.SnapshotRevision != cloud.SnapshotRevision ||
            cached.Origin != cloud.Origin || cached.Center != cloud.Center)
        {
            cached.Points = ProjectProbes(cloud); cached.SnapshotRevision = cloud.SnapshotRevision;
            cached.Origin = cloud.Origin; cached.Center = cloud.Center; ProjectionBuilds++;
        }
        FVector[] positions = cached.Points;
        var rows = new Vector4[ProbeCount * RowsPerProbe];
        Span<Vector3> broadSamples = stackalloc Vector3[ProbeCount];
        for (int probe = 0; probe < ProbeCount; probe++)
        {
            FVector point = positions[probe];
            // A point probe chooses its own zone and nearby lights. Sampling a cloud-sized sphere here would
            // admit lights which do not reach this region, defeating the spatial lighting field.
            ActorLightSet lights = map.LightSmoke(point, 0);
            Vector3 direct = Vector3.Zero, moment = Vector3.Zero;
            float weightedDistance = 0, energy = 0;
            for (int i = 0; i < lights.Count; i++)
            {
                var sample = Evaluate(lights.Lights[i], UeSpace.ToGodot(point));
                direct += sample.Radiance;
                float weight = sample.Radiance.Dot(new Vector3(.2126f, .7152f, .0722f));
                moment += sample.Direction * weight;
                weightedDistance += sample.Distance * weight;
                energy += weight;
            }
            Vector3 incident = energy > .00001f ? moment / energy : Vector3.Zero;
            // Authored SCCT rooms can have zero zone ambient and use nearby baked lights instead. The existing
            // actor-volume fit supplies bounded neighboring irradiance for indirect support; preserve the exact
            // point selection above for direct light. Only excess neighboring light contributes, avoiding a
            // second copy of bright local light, and the cap preserves color without flooding a bright region.
            ActorLightSet nearby = map.LightSmoke(point,IndirectRadius);
            Vector3 broad = Vector3.Zero;
            for (int i = 0; i < nearby.Count; i++) broad += Evaluate(nearby.Lights[i],UeSpace.ToGodot(point)).Radiance;
            broadSamples[probe] = broad;
            float localPeak = Math.Max(direct.X,Math.Max(direct.Y,direct.Z));
            float broadPeak = Math.Max(broad.X,Math.Max(broad.Y,broad.Z));
            // Subtract a scalar energy budget, not RGB channels independently: channel subtraction can turn
            // red local illumination into an artificial blue diffuse term from a neighboring blue light.
            Vector3 indirect = broad*(Math.Max(0,broadPeak-localPeak)/Math.Max(broadPeak,.00001f)*IndirectGain);
            float peak = Math.Max(indirect.X,Math.Max(indirect.Y,indirect.Z));
            if (peak > IndirectMaximum) indirect *= IndirectMaximum/peak;
            float localWeight = Math.Clamp((localPeak-.10f)/.25f,0,1);
            indirect *= 1-localWeight*localWeight*(3-2*localWeight);
            Vector3 ambient = lights.Ambient+indirect;
            rows[probe * RowsPerProbe] = new(ambient.X, ambient.Y, ambient.Z, 0);
            rows[probe * RowsPerProbe + 1] = new(direct.X, direct.Y, direct.Z,
                energy > .00001f ? weightedDistance / energy : 625);
            // The vector length retains directional coherence: opposing lights approach isotropic scattering.
            rows[probe * RowsPerProbe + 2] = new(incident.X, incident.Z, incident.Y, 0);
        }
        if (LogProbes && !cached.DiagnosticLogged && map.ActorLighting.Time-cloud.CreatedAt >= 1)
        {
            cached.DiagnosticLogged = true;
            GD.Print($"SMOKE_LIGHT_PROBES map={map.MapId} center={cloud.Center}");
            for (int probe = 0; probe < ProbeCount; probe++)
            {
                GD.Print($"SMOKE_LIGHT_PROBE index={probe} position={positions[probe]} ambient={rows[probe*3]} direct={rows[probe*3+1]} broad={broadSamples[probe]} direction={rows[probe*3+2]}");
            }
        }
        return rows;
    }

    internal static (Vector3 Radiance, Vector3 Direction, float Distance) Evaluate(PackedLight light, Vector3 point)
    {
        Vector3 vector = new(light.Pos.X, light.Pos.Y, light.Pos.Z);
        bool directional = light.Pos.W < 0;
        if (!directional) vector -= point;
        float distance = vector.Length();
        Vector3 direction = distance > .00001f ? vector / distance : Vector3.Up;
        float attenuation = 1;
        if (!directional)
        {
            if (light.Spot.W > -3.5f && light.Spot.W < -2.5f)
            {
                float ratio = distance / Math.Max(light.Pos.W, .001f);
                attenuation = ratio >= 1 ? 0 : 1 / Math.Max(8 * ratio * ratio, .001f);
            }
            else attenuation = distance > (256 - light.Col.W) / Math.Max(1, light.Pos.W)
                ? 0 : 1 / Math.Max(light.Col.W + light.Pos.W * distance, .01f);
            if (light.Spot.W >= 0)
            {
                float cosine = -direction.Dot(new(light.Spot.X, light.Spot.Y, light.Spot.Z));
                float edge = Math.Min(1, light.Spot.W + .1f);
                float t = Math.Clamp((cosine - light.Spot.W) / Math.Max(edge - light.Spot.W, .00001f), 0, 1);
                attenuation *= t * t * (3 - 2 * t);
            }
        }
        Vector3 radiance = new Vector3(light.Col.X, light.Col.Y, light.Col.Z) * attenuation;
        return (radiance, direction, directional ? 625 : distance * UeSpace.UnitsPerMetre);
    }

    private static FVector[] ProjectProbes(SmokeRenderCloud cloud)
    {
        var points = new FVector[ProbeCount];
        ReadOnlySpan<byte> cells = cloud.Cells;
        ReadOnlySpan<ushort> activation = cloud.ActivationTicks;
        for (int probe = 0; probe < ProbeCount; probe++)
        {
            FVector target = cloud.Center + new FVector((probe & 1) == 0 ? -ProbeHalfSpan : ProbeHalfSpan,
                (probe & 2) == 0 ? -ProbeHalfSpan : ProbeHalfSpan, (probe & 4) == 0 ? -ProbeHalfSpan : ProbeHalfSpan);
            FVector local = (target - cloud.Origin) / 25;
            int x = (int)MathF.Floor(local.X), y = (int)MathF.Floor(local.Y), z = (int)MathF.Floor(local.Z);
            int index = x + 25 * (y + 25 * z);
            if (x is >= 0 and < 25 && y is >= 0 and < 25 && z is >= 0 and < 25 &&
                cells[index] != 0 && activation[index] != ushort.MaxValue)
            { points[probe] = target; continue; }
            // A probe in a wall must not pick that wall's unrelated room ambient. Project into the nearest
            // reachable cell from the immutable authoritative snapshot; no render-thread collision queries.
            float nearest = float.PositiveInfinity;
            FVector chosen = cloud.Center;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == 0 || activation[i] == ushort.MaxValue) continue;
                FVector point = cloud.Origin + new FVector((i % 25 + .5f) * 25,
                    (i / 25 % 25 + .5f) * 25, (i / 625 + .5f) * 25);
                float distance = FVector.DistSquared(target, point);
                if (distance >= nearest) continue;
                nearest = distance; chosen = point;
            }
            points[probe] = chosen;
        }
        return points;
    }
}
