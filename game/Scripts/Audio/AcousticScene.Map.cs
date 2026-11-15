using System.Collections.Generic;
using OpenScct.Core;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Audio;

public static class MapAcoustics
{
    public static AcousticScene FromLighting(LightingDoc lighting)
    {
        var portals = new List<AcousticPortal>();
        var zones = new Dictionary<string, int>();
        foreach (ZoneEntry zone in lighting.Zones) zones[zone.Name] = zone.ZoneIndex;
        foreach (var portal in lighting.FogPortals)
        {
            var points = new FVector[portal.Vertices.Length];
            for (int i = 0; i < points.Length; i++)
            { var v = portal.Vertices[i]; points[i] = new FVector(v.X, v.Y, v.Z); }
            portals.Add(new(portal.Back, portal.Front, new FVector(portal.Plane.X, portal.Plane.Y, portal.Plane.Z), points));
        }
        return new AcousticScene(portals.ToArray(), zones);
    }
}
