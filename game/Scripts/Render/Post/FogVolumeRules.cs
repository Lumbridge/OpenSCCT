using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace OpenScctGame.Render.Post;

/// <summary>A convex world-space brush piece: outward unit planes contain points where dot(n,p) &lt;= w.</summary>
public sealed record FogVolumePiece(Vector3 Color, float Extinction, float Floor, float HeightFalloff,
    float EdgeFade, int Zone, Vector4[] Planes, int OpenPlanes)
{
    public Vector3? Minimum { get; init; }
    public Vector3? Maximum { get; init; }

    public static FogVolumePiece Read(JsonElement json)
    {
        JsonElement color = json.GetProperty("color");
        var planes = new List<Vector4>();
        foreach (JsonElement plane in json.GetProperty("planes").EnumerateArray())
            planes.Add(new(plane[0].GetSingle(), plane[1].GetSingle(), plane[2].GetSingle(), plane[3].GetSingle()));
        var piece = new FogVolumePiece(new(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle()),
            json.GetProperty("extinction").GetSingle(), json.GetProperty("floor").GetSingle(),
            json.GetProperty("heightFalloff").GetSingle(), json.GetProperty("edgeFade").GetSingle(),
            json.GetProperty("zone").GetInt32(), planes.ToArray(), json.GetProperty("openPlanes").GetInt32())
        {
            Minimum = Bound("min"), Maximum = Bound("max"),
        };
        if (!piece.IsValid) throw new JsonException("Invalid fog volume piece.");
        return piece;

        Vector3? Bound(string name) => json.TryGetProperty(name, out JsonElement value)
            ? new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle()) : null;
    }

    public bool IsValid
    {
        get
        {
            if (!float.IsFinite(Extinction) || Extinction <= 0f || !float.IsFinite(Floor) ||
                !float.IsFinite(HeightFalloff) || !float.IsFinite(EdgeFade) || Zone is < -1 or > 254 ||
                Planes.Length is < 4 or > 24 || OpenPlanes < 0 || OpenPlanes > Planes.Length ||
                !float.IsFinite(Color.X) || !float.IsFinite(Color.Y) || !float.IsFinite(Color.Z)) return false;
            if (Minimum.HasValue != Maximum.HasValue) return false;
            if (Minimum is { } low && Maximum is { } high &&
                (!float.IsFinite(low.X + low.Y + low.Z + high.X + high.Y + high.Z) ||
                 low.X > high.X || low.Y > high.Y || low.Z > high.Z)) return false;
            foreach (Vector4 p in Planes)
            {
                float length = new Vector3(p.X, p.Y, p.Z).LengthSquared();
                if (!float.IsFinite(length) || !float.IsFinite(p.W) || MathF.Abs(length - 1f) > .001f) return false;
            }
            return true;
        }
    }
}

/// <summary>Reference integration of Enhanced's volume density and open-face ramp; see enhanced-zone-fog.md.</summary>
public static class FogVolumeRules
{
    private const float InverseLn2 = 1.4426950408889634f;

    public static float OpticalDepth(FogVolumePiece piece, Vector3 origin, Vector3 direction, float distance, float start = 0f)
    {
        float entry = 0f, exit = distance, clearStart = 0f, clearEnd = distance;
        for (int i = 0; i < piece.Planes.Length; i++)
        {
            Vector4 plane = piece.Planes[i];
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            float remaining = plane.W - Vector3.Dot(normal, origin), speed = Vector3.Dot(normal, direction);
            float fade = i < piece.OpenPlanes ? Math.Max(piece.EdgeFade, 0f) : 0f;
            if (MathF.Abs(speed) < 1e-7f)
            {
                if (remaining < 0f) return 0f;
                if (remaining < fade) { clearStart = distance; clearEnd = 0f; }
                continue;
            }
            float crossing = remaining / speed, clear = (remaining - fade) / speed;
            if (speed > 0f) { exit = Math.Min(exit, crossing); clearEnd = Math.Min(clearEnd, clear); }
            else { entry = Math.Max(entry, crossing); clearStart = Math.Max(clearStart, clear); }
        }
        entry = Math.Max(entry, start);
        if (exit <= entry) return 0f;
        clearStart = Math.Clamp(Math.Max(start, clearStart), entry, exit);
        clearEnd = Math.Clamp(clearEnd, entry, exit);
        if (clearEnd <= clearStart) clearStart = clearEnd = (entry + exit) * .5f;
        float integrated = DensityIntegral(clearStart, clearEnd);
        if (piece.EdgeFade > 0f && piece.OpenPlanes > 0)
        {
            integrated += FadedIntegral(entry, clearStart) + FadedIntegral(clearEnd, exit);
        }
        return piece.Extinction * integrated;

        float DensityIntegral(float from, float to)
        {
            float low = origin.Z - piece.Floor + direction.Z * from;
            float high = origin.Z - piece.Floor + direction.Z * to;
            if (piece.HeightFalloff <= 0f) return to - from;
            float Density(float z) => MathF.Pow(2f, -Math.Max(z, 0f) / piece.HeightFalloff);
            float Primitive(float z) => Math.Min(z, 0f) - piece.HeightFalloff * InverseLn2 * Density(z);
            return (to - from) * (MathF.Abs(high - low) > .001f
                ? (Primitive(high) - Primitive(low)) / (high - low) : Density(low));
        }

        float FadedIntegral(float from, float to)
        {
            float Near(float at)
            {
                float nearest = float.MaxValue;
                Vector3 point = origin + at * direction;
                for (int i = 0; i < piece.OpenPlanes; i++)
                {
                    Vector4 plane = piece.Planes[i];
                    nearest = Math.Min(nearest, plane.W - Vector3.Dot(new(plane.X, plane.Y, plane.Z), point));
                }
                return nearest / piece.EdgeFade;
            }
            float a = Near(from), b = Near(to), low = Math.Min(a, b), high = Math.Max(a, b);
            static float RampIntegral(float x) => x <= 0f ? 0f : x < 1f ? x * x * .5f : x - .5f;
            float mean = high - low < .00001f ? Math.Clamp(low, 0f, 1f) : (RampIntegral(high) - RampIntegral(low)) / (high - low);
            return mean * DensityIntegral(from, to);
        }
    }

    public static Vector4 Evaluate(IReadOnlyList<FogVolumePiece> pieces, IReadOnlyList<FogPortal> portals,
        int cameraZone, Vector3 origin, Vector3 target, Vector4 zoneFog)
    {
        float distance = Vector3.Distance(origin, target);
        if (distance <= 0f) return zoneFog;
        Vector3 direction = (target - origin) / distance, weighted = Vector3.Zero;
        float total = 0f;
        var crossings = new List<(float At, int From, int To)>();
        foreach (FogPortal portal in portals)
            if (portal.Crossing(origin, direction, distance, out float at, out int from, out int to) &&
                !crossings.Exists(c => c.From == from && c.To == to && MathF.Abs(c.At - at) < .001f * Math.Max(at, 1f)))
                crossings.Add((at, from, to));
        foreach (FogVolumePiece piece in pieces)
        {
            float depth = piece.Zone < 0 || piece.Zone == cameraZone ? OpticalDepth(piece, origin, direction, distance) : 0f;
            if (piece.Zone >= 0)
                foreach (var crossing in crossings)
                {
                    int sign = (crossing.To == piece.Zone ? 1 : 0) - (crossing.From == piece.Zone ? 1 : 0);
                    if (sign != 0) depth += sign * OpticalDepth(piece, origin, direction, distance, crossing.At);
                }
            depth = Math.Max(depth, 0f);
            total += depth;
            weighted += depth * piece.Color;
        }
        float opacity = 1f - MathF.Exp(-total), zoneOpacity = 1f - zoneFog.W;
        float through = zoneFog.W * (1f - opacity), sum = zoneOpacity + opacity;
        Vector3 color = new(zoneFog.X, zoneFog.Y, zoneFog.Z);
        if (total > 0f) color += opacity / total * weighted;
        return new(sum > 0f ? color * ((1f - through) / sum) : Vector3.Zero, through);
    }
}
