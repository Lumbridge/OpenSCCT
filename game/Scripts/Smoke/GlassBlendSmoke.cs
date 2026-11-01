using System;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Native translucent diffuse followed by additive Specular, including zero opacity and a partial mask.</summary>
public sealed class GlassBlendSmoke : SmokeTest
{
    public const string TestName = "glass-blend";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private int _frame;
    private readonly float[] _alpha = [0f, 0.25f, 1f, 0.25f];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_frame++ == 0)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
            _viewport = new SubViewport { Size = new Vector2I(128, 32), OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
            var effect = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
            _viewport.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 4,
                CullMask = 1, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.01f, Far = 10,
                Position = new Vector3(0, 0, 2), Environment = FaithfulView.CreateEnvironment(),
                Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect } } });
            var background = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, fog_disabled; void fragment() { ALBEDO = vec3(0.2,0.3,0.4); }" } };
            _viewport.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(4, 1) }, MaterialOverride = background });
            var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
            for (int i = 0; i < _alpha.Length; i++)
            {
                var material = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = $"glass-{i}",
                    Translucent = true, Unlit = true, EnvColor = Vector3.One, SpecMask = "mask" }, LightSource.None);
                material.SetShaderParameter("tex_diffuse", Pixel(new Color(0.1f, 0.2f, 0.3f, _alpha[i])));
                material.SetShaderParameter("tex_spec_mask", Pixel(new Color(1, 1, 1, i == 3 ? 0.5f : 1f)));
                material.SetShaderParameter("env_color", new Vector3(0.1f, 0.1f, 0.1f));
                _viewport.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = Vector2.One },
                    Position = new Vector3(i - 1.5f, 0, 0.1f), MaterialOverride = material });
            }
            context.Tree.Root.AddChild(_viewport);
        }
        if (_frame < 12) return SmokeResult.Running;
        Image result = _viewport!.GetTexture().GetImage();
        string detail = "";
        float worst = 0;
        for (int i = 0; i < _alpha.Length; i++)
        {
            Color got = result.GetPixel(i * 32 + 16, 16);
            float spec = i == 3 ? 0.05f : 0.1f;
            var expected = new Vector3(0.2f, 0.3f, 0.4f) * (1f - _alpha[i]) + new Vector3(0.1f, 0.2f, 0.3f) * _alpha[i] + Vector3.One * spec;
            worst = Math.Max(worst, Math.Max(Math.Abs(got.R - expected.X), Math.Max(Math.Abs(got.G - expected.Y), Math.Abs(got.B - expected.Z))));
            detail += $"alpha={_alpha[i]}:{got} ";
        }
        _viewport.QueueFree();
        return worst < 3f / 255f ? SmokeResult.Pass(detail) : SmokeResult.Fail($"error={worst}: {detail}");
    }

    private static ImageTexture Pixel(Color color)
    {
        Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }
}

