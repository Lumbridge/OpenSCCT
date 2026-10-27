using System;

namespace OpenScctGame.Views;

/// <summary>
/// When a gameplay mesh node must change what it draws (Godot-free, so the logic tests run it): whenever the actor's
/// StaticMesh differs from the one the node shows, a change to None included (a broken SVitre pane hides,
/// spec/devices/vitre.md rule 2). Asset paths compare without case, as package paths do.
/// </summary>
public static class MeshSwap
{
    /// <summary>True when <paramref name="actorMesh"/> (null for None) is not what the node shows.</summary>
    public static bool Changed(string? actorMesh, string? shownMesh) =>
        !string.Equals(actorMesh, shownMesh, StringComparison.OrdinalIgnoreCase);
}
