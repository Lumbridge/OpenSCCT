namespace OpenScctGame.Render;

/// <summary>Visibility layers of the Faithful views.</summary>
public static class RenderLayers
{
    /// <summary>The world: BSP, static meshes, actors (Godot's default layer 1).</summary>
    public const uint World = 1u;

    /// <summary>The sky zone's geometry: seen only by the sky view, as in UE2's sky pass.</summary>
    public const uint Sky = 1u << 1;

    /// <summary>
    /// The first-person view model (arms and gun at the eye): drawn by the main view only. It floats in front of the
    /// camera, so the shadow cameras leave it out.
    /// </summary>
    public const uint FirstPerson = 1u << 2;

    /// <summary>
    /// The body of the pawn seen from its own eyes: hidden from the main view, still drawn by the flares' shadow
    /// cameras, so the local merc casts his whole shadow (FlareLights).
    /// </summary>
    public const uint ShadowOnly = 1u << 17;

    /// <summary>
    /// No geometry: set in the shadow cameras' cull masks so the world template knows it draws depth only and skips
    /// its lighting (svm_world.gdshaderinc, SVM_SHADOW_PASS).
    /// </summary>
    public const uint ShadowPass = 1u << 18;

    /// <summary>The sky backdrop quad of a main view; the sky camera leaves it out so it never samples itself.</summary>
    public const uint Backdrop = 1u << 19;

    /// <summary>What a main view draws: everything but the sky zone and the shadow-only bodies.</summary>
    public const uint MainView = 0xFFFFFu & ~Sky & ~ShadowOnly & ~ShadowPass;

    /// <summary>What a spot light's shadow camera draws.</summary>
    public const uint SpotShadow = World | ShadowPass;

    /// <summary>What a flare's shadow camera draws: the world and the local pawn's hidden body.</summary>
    public const uint FlareShadow = World | ShadowOnly | ShadowPass;
}
