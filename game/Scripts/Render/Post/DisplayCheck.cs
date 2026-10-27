using System;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>
/// The known-value check of the display-space pipeline: patches of exact display values (0 to 255) drawn by an
/// unshaded material go through the post pass and the Linear tonemapper; read back, each must come out as the same
/// byte within one step. docs/RENDERING.md#display-space.
/// </summary>
public sealed class DisplayCheck : IDisposable
{
    public static readonly int[] Values = [0, 1, 2, 4, 8, 16, 24, 32, 48, 64, 96, 128, 160, 192, 224, 255];

    private const int PatchSize = 8;

    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, fog_disabled, cull_disabled;
        uniform float values[16];
        void fragment() {
            int i = clamp(int(floor(UV.x * 16.0)), 0, 15);
            ALBEDO = vec3(values[i] / 255.0);
        }
        """;

    public DisplayCheck(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Viewport = new SubViewport
        {
            Name = "DisplayCheck",
            Size = new Vector2I(Values.Length * PatchSize, PatchSize),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            ScreenSpaceAA = Godot.Viewport.ScreenSpaceAAEnum.Disabled,
            UseDebanding = false,
        };
        Effect = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1f,
            KeepAspect = Camera3D.KeepAspectEnum.Width,
            Near = 0.01f,
            Far = 10f,
            Environment = FaithfulView.CreateEnvironment(),
            Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { Effect } },
            Position = new Vector3(0f, 0f, 1f),
        };
        var material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        var values = new float[Values.Length];
        for (int i = 0; i < Values.Length; i++)
        {
            values[i] = Values[i];
        }

        material.SetShaderParameter("values", values);
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1f, 1f / Values.Length) },
            MaterialOverride = material,
        };
        Viewport.AddChild(camera);
        Viewport.AddChild(quad);
        parent.AddChild(Viewport);
    }

    public SubViewport Viewport { get; }

    public DisplaySpaceEffect Effect { get; }

    /// <summary>Largest difference, in 8-bit steps, between a patch and its value; -1 when nothing was read.</summary>
    public int MaxError(out string detail)
    {
        Image image = Viewport.GetTexture().GetImage();
        int worst = -1;
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < Values.Length; i++)
        {
            Color c = image.GetPixel((i * PatchSize) + (PatchSize / 2), PatchSize / 2);
            int got = (int)MathF.Round(c.R * 255f);
            int err = Math.Abs(got - Values[i]);
            worst = Math.Max(worst, err);
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{Values[i]}:{got} ");
        }

        detail = text.ToString().TrimEnd();
        return worst;
    }

    public void Dispose() => Viewport.QueueFree();
}
