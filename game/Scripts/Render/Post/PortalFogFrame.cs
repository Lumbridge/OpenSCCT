using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render.Data;
using NVector3 = System.Numerics.Vector3;

namespace OpenScctGame.Render.Post;

/// <summary>Conservative portal candidates and per-view zone settings for the display-space fog pass.</summary>
public sealed record PortalFogFrame(float[] Data)
{
    // Tile size affects only work selection; no portal candidates are discarded.
    private const int TileSize = 32;
    private const int ZoneBase = 6;
    private const int PortalBase = ZoneBase + 64 * 2;

    public static PortalFogFrame Build(LightingDoc lighting, Camera3D camera, FVector origin, int cameraZone,
        bool cameraFog, Color cameraColor, float cameraStart, float cameraEnd)
    {
        Vector2I size = (Vector2I)camera.GetViewport().GetVisibleRect().Size;
        int columns = Math.Max(1, (size.X + TileSize - 1) / TileSize), rows = Math.Max(1, (size.Y + TileSize - 1) / TileSize);
        Projection projection = camera.GetCameraProjection();
        Basis basis = camera.GlobalBasis;
        static NVector3 Direction(Vector3 value)
        {
            FVector ue = UeSpace.ToUe(value);
            return NVector3.Normalize(new(ue.X, ue.Y, ue.Z));
        }
        NVector3 right = Direction(basis.X), up = Direction(basis.Y), forward = Direction(-basis.Z);
        var eye = new NVector3(origin.X, origin.Y, origin.Z);
        var data = new List<float>(PortalBase * 4);
        void Add(float x, float y, float z, float w) { data.Add(x); data.Add(y); data.Add(z); data.Add(w); }
        Add(origin.X, origin.Y, origin.Z, Math.Clamp(cameraZone, 0, 63));
        Add(right.X, right.Y, right.Z, projection.X.X);
        Add(up.X, up.Y, up.Z, projection.Y.Y);
        Add(forward.X, forward.Y, forward.Z, UeSpace.UnitsPerMetre);
        Add(columns, rows, TileSize, 0f); // w is the tile-table address, assigned below.
        Add(PortalBase, lighting.FogPortals.Count, 0f, 0f);
        for (int z = 0; z < 64; z++)
        {
            ZoneEntry? zone = lighting.ZoneByIndex(z);
            bool enabled = z == cameraZone ? cameraFog : zone is { Fog: true };
            float start = z == cameraZone ? cameraStart : zone?.FogStart ?? 0f;
            float end = z == cameraZone ? cameraEnd : zone?.FogEnd ?? 0f;
            Color color = z == cameraZone ? cameraColor : zone?.FogColor ?? default;
            Add(start, end, FogZone.Rate(start, end), enabled ? 1f : 0f);
            Add(color.R, color.G, color.B, 0f);
        }
        var cells = new List<int>?[columns * rows];
        int edgeAt = PortalBase + lighting.FogPortals.Count * 2;
        for (int index = 0; index < lighting.FogPortals.Count; index++)
        {
            FogPortal portal = lighting.FogPortals[index];
            Add(portal.Plane.X, portal.Plane.Y, portal.Plane.Z, portal.Plane.W);
            Add(portal.Back, portal.Front, edgeAt, portal.Edges.Length);
            edgeAt += portal.Edges.Length;
            float minX = size.X, minY = size.Y, maxX = 0, maxY = 0;
            bool ahead = false, near = false, positive = false;
            foreach (NVector3 vertex in portal.CandidateVertices)
            {
                NVector3 delta = vertex - eye;
                float depth = NVector3.Dot(delta, forward);
                positive |= depth > 0f;
                if (depth <= RenderK.NearClipUnits) { near = true; continue; }
                ahead = true;
                float x = (1f + NVector3.Dot(delta, right) * projection.X.X / depth) * .5f * size.X;
                float y = (1f - NVector3.Dot(delta, up) * projection.Y.Y / depth) * .5f * size.Y;
                minX = Math.Min(minX, x - 1f); maxX = Math.Max(maxX, x + 1f);
                minY = Math.Min(minY, y - 1f); maxY = Math.Max(maxY, y + 1f);
            }
            // A polygon intersecting the near plane uses the whole screen as a conservative bound.
            // Fully behind the near plane can still separate the camera from a visible surface.
            if (!positive) continue;
            if (near) { minX = minY = 0; maxX = size.X; maxY = size.Y; }
            else if (!ahead || maxX < 0 || maxY < 0 || minX >= size.X || minY >= size.Y) continue;
            int x0 = Math.Clamp((int)MathF.Floor(minX / TileSize), 0, columns - 1), x1 = Math.Clamp((int)MathF.Floor(maxX / TileSize), 0, columns - 1);
            int y0 = Math.Clamp((int)MathF.Floor(minY / TileSize), 0, rows - 1), y1 = Math.Clamp((int)MathF.Floor(maxY / TileSize), 0, rows - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) (cells[y * columns + x] ??= []).Add(index);
        }
        foreach (FogPortal portal in lighting.FogPortals)
        foreach (System.Numerics.Vector4 edge in portal.Edges) Add(edge.X, edge.Y, edge.Z, edge.W);
        data[19] = data.Count / 4;
        int candidateAt = data.Count / 4 + cells.Length;
        foreach (List<int>? cell in cells)
        {
            Add(candidateAt, cell?.Count ?? 0, 0, 0);
            candidateAt += cell?.Count ?? 0;
        }
        foreach (List<int>? cell in cells)
            if (cell is not null) foreach (int portal in cell) Add(portal, 0, 0, 0);
        var volumes = new List<FogVolumePiece>();
        foreach (FogVolumePiece piece in lighting.FogVolumes)
            if (BoundsVisible(piece)) volumes.Add(piece);
        data[22] = data.Count / 4;
        data[23] = volumes.Count;
        int planeAt = data.Count / 4 + volumes.Count * 3;
        foreach (FogVolumePiece piece in volumes)
        {
            Add(piece.Color.X, piece.Color.Y, piece.Color.Z, piece.Extinction);
            Add(piece.Floor - origin.Z, piece.HeightFalloff, piece.EdgeFade, piece.Zone);
            Add(planeAt, piece.Planes.Length, piece.OpenPlanes, 0f);
            planeAt += piece.Planes.Length;
        }
        foreach (FogVolumePiece piece in volumes)
        foreach (System.Numerics.Vector4 plane in piece.Planes)
            Add(plane.X, plane.Y, plane.Z, plane.W - NVector3.Dot(new(plane.X, plane.Y, plane.Z), eye));
        return new(data.ToArray());

        bool BoundsVisible(FogVolumePiece piece)
        {
            if (piece.Minimum is not { } low || piece.Maximum is not { } high) return true;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool near = false, positive = false;
            for (int corner = 0; corner < 8; corner++)
            {
                NVector3 delta = new NVector3((corner & 1) == 0 ? low.X : high.X,
                    (corner & 2) == 0 ? low.Y : high.Y, (corner & 4) == 0 ? low.Z : high.Z) - eye;
                float depth = NVector3.Dot(delta, forward);
                positive |= depth > 0f;
                if (depth <= RenderK.NearClipUnits) { near = true; continue; }
                float x = NVector3.Dot(delta, right) * projection.X.X / depth;
                float y = NVector3.Dot(delta, up) * projection.Y.Y / depth;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            // Height-clipped bounds only select pieces; their original planes remain the actual fog shape.
            return positive && (near || (maxX >= -1f && minX <= 1f && maxY >= -1f && minY <= 1f));
        }
    }
}
