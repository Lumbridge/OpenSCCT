using System;
using Godot;

namespace OpenScctGame.Render;

/// <summary>Conservative rejection: only a box entirely outside a camera plane is skipped.</summary>
public static class ViewFrustum
{
    /// <summary>Camera planes as value types; avoids a finalizable Godot Array and per-element Variant calls.</summary>
    public static void Read(Camera3D camera, Span<Plane> planes)
    {
        Projection projection = camera.GetCameraProjection();
        Transform3D transform = camera.GetCameraTransform();
        for (int i = 0; i < 6; i++) planes[i] = transform * projection.GetProjectionPlane((Projection.Planes)i);
    }

    public static bool Intersects(Aabb box, ReadOnlySpan<Plane> planes)
    {
        Vector3 center = box.GetCenter();
        Vector3 half = box.Size.Abs() * 0.5f;
        foreach (Plane plane in planes)
            if (plane.DistanceTo(center) > plane.Normal.Abs().Dot(half)) return false;
        return true;
    }
}
