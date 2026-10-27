namespace OpenScctGame.Render;

/// <summary>Visibility layers of the Faithful views.</summary>
public static class RenderLayers
{
    /// <summary>The world: BSP, static meshes, actors (Godot's default layer 1).</summary>
    public const uint World = 1u;

    /// <summary>The sky zone's geometry: seen only by the sky view, as in UE2's sky pass.</summary>
    public const uint Sky = 1u << 1;

    /// <summary>The sky backdrop quad of a main view; the sky camera leaves it out so it never samples itself.</summary>
    public const uint Backdrop = 1u << 19;

    /// <summary>What a main view draws: everything but the sky zone.</summary>
    public const uint MainView = 0xFFFFFu & ~Sky;
}
