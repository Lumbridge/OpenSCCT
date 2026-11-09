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

    /// <summary>
    /// True when no tile has a candidate, the camera zone has no fog and no fog volume is visible: svm_fog_evaluate
    /// then returns no fog for every pixel, exactly as a view without fog does.
    /// </summary>
    public bool Empty { get; init; }

    /// <summary>
    /// True when a view of the map can draw portal fog: it has fog volumes, or a portal table and a fogged zone (a
    /// view's own fog only comes from a fogged zone, FaithfulView.ZoneFog). Decided once per map before its materials
    /// are built: only then do the shaders compile portal fog (ResponsiveSmokeShaders.UsePortalFog).
    /// </summary>
    public static bool MapUses(LightingDoc lighting) =>
        lighting.FogVolumes.Count > 0 || (lighting.HasFogPortals && lighting.Zones.Exists(static zone => zone.Fog));

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
        Add(size.X, size.Y, columns, 0f); // w is the tile-table address, assigned below.
        Add(PortalBase, cameraFog ? 1f : 0f, 0f, 0f);
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
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            bool behind = false;
            foreach (NVector3 vertex in portal.CandidateVertices)
            {
                NVector3 delta = vertex - eye;
                float depth = NVector3.Dot(delta, forward);
                if (depth <= 0f) { behind = true; continue; }
                float x = NVector3.Dot(delta, right) * projection.X.X / depth;
                float y = -NVector3.Dot(delta, up) * projection.Y.Y / depth;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            if (float.IsPositiveInfinity(minX)) continue;

            // A portal in front of the eye can affect fog even if it is closer than the near plane.
            // For polygons straddling the eye plane, keep each projected bound only if all the
            // behind-eye vertices are also inside that bound's plane through the eye.
            if (behind)
            {
                const float planeSlack = 0.001f;
                foreach (NVector3 vertex in portal.CandidateVertices)
                {
                    NVector3 delta = vertex - eye;
                    float depth = NVector3.Dot(delta, forward);
                    if (depth > 0f) continue;
                    float x = NVector3.Dot(delta, right) * projection.X.X;
                    float y = -NVector3.Dot(delta, up) * projection.Y.Y;
                    if (x - (minX * depth) < planeSlack) minX = float.NegativeInfinity;
                    if (x - (maxX * depth) > -planeSlack) maxX = float.PositiveInfinity;
                    if (y - (minY * depth) < planeSlack) minY = float.NegativeInfinity;
                    if (y - (maxY * depth) > -planeSlack) maxY = float.PositiveInfinity;
                }
            }
            minX = Math.Max(0f, (1f + minX) * .5f * size.X - 1f);
            minY = Math.Max(0f, (1f + minY) * .5f * size.Y - 1f);
            maxX = Math.Min(size.X, (1f + maxX) * .5f * size.X + 1f);
            maxY = Math.Min(size.Y, (1f + maxY) * .5f * size.Y + 1f);
            if (maxX < 0 || maxY < 0 || minX >= size.X || minY >= size.Y) continue;
            int x0 = Math.Clamp((int)MathF.Floor(minX / TileSize), 0, columns - 1), x1 = Math.Clamp((int)MathF.Floor(maxX / TileSize), 0, columns - 1);
            int y0 = Math.Clamp((int)MathF.Floor(minY / TileSize), 0, rows - 1), y1 = Math.Clamp((int)MathF.Floor(maxY / TileSize), 0, rows - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) (cells[y * columns + x] ??= []).Add(index);
        }
        foreach (FogPortal portal in lighting.FogPortals)
        foreach (System.Numerics.Vector4 edge in portal.Edges) Add(edge.X, edge.Y, edge.Z, edge.W);
        data[19] = data.Count / 4;
        int candidateCount = 0;
        foreach (List<int>? cell in cells) candidateCount += cell?.Count ?? 0;
        int candidateAt = data.Count / 4 + cells.Length, partnerAt = candidateAt + candidateCount;
        foreach (List<int>? cell in cells)
        {
            Add(candidateAt, cell?.Count ?? 0, 0, 0);
            candidateAt += cell?.Count ?? 0;
        }
        // Candidate rows are (portal, first partner row, partner count, 0). A crossing can only repeat an earlier
        // candidate's when both join the same two zones, so the shader tests just those partners.
        var partners = new List<int>();
        foreach (List<int>? cell in cells)
        {
            if (cell is null) continue;
            for (int k = 0; k < cell.Count; k++)
            {
                FogPortal portal = lighting.FogPortals[cell[k]];
                int first = partners.Count;
                for (int earlier = 0; earlier < k; earlier++)
                {
                    FogPortal other = lighting.FogPortals[cell[earlier]];
                    if ((other.Back == portal.Back && other.Front == portal.Front) || (other.Back == portal.Front && other.Front == portal.Back))
                        partners.Add(cell[earlier]);
                }
                Add(cell[k], partnerAt + first, partners.Count - first, 0);
            }
        }
        foreach (int partner in partners) Add(partner, 0, 0, 0);
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
        return new(data.ToArray()) { Empty = candidateCount == 0 && !cameraFog && volumes.Count == 0 };

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
