using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.Render;

/// <summary>
/// The bounding spheres of a map's static meshes that carry baked vertex colours, for their dynamic world lights. The
/// original lights such a mesh through the D3D vertex lights like any actor: a light is a candidate only while its
/// centre is within its radius plus the mesh's sphere radius of the sphere's centre, the four strongest are kept, and
/// each one's attenuation is fitted over that sphere (docs/RENDERING.md#dynamic-lights). Merged map geometry has no
/// values per instance, so each vertex carries its instance's row here (UV2.x, row + 1; 0: no sphere, the light is
/// evaluated at the pixel alone) and the template reads the sphere in its vertex stage: xyz the centre in the mesh's
/// own space (the world's for merged geometry) in metres, w the radius in metres.
/// </summary>
[PortOf("eng:Render.DynamicLights", Spec = "docs/RENDERING.md#dynamic-lights")]
public sealed class MeshLightSpheres
{
    public const string TextureParameter = "svm_mesh_spheres";

    /// <summary>Texels a row of the texture (the template splits an index by it: svm_world.gdshaderinc vertex()).</summary>
    public const int Width = 256;

    private static readonly StringName s_textureName = TextureParameter;
    private static bool s_globalAdded;

    // Index 0 is "no sphere".
    private readonly List<Vector4> _spheres = [default];
    private Image? _image;
    private ImageTexture? _texture;
    private int _written;

    public MeshLightSpheres()
    {
        EnsureGlobals();
        Flush();
    }

    /// <summary>Spheres added so far.</summary>
    public int Count => _spheres.Count - 1;

    /// <summary>The largest radius added (metres).</summary>
    public float LargestRadius { get; private set; }

    /// <summary>
    /// Adds a sphere (centre and radius in metres, in the space of the mesh that will carry it) and returns the value its
    /// vertices carry. Takes effect in the shader at the next <see cref="Flush"/>.
    /// </summary>
    public int Add(Vector3 centre, float radius)
    {
        _spheres.Add(new Vector4(centre.X, centre.Y, centre.Z, MathF.Max(radius, 0f)));
        LargestRadius = MathF.Max(LargestRadius, radius);
        return _spheres.Count - 1;
    }

    /// <summary>The sphere a vertex value names (w 0 for none).</summary>
    public Vector4 Get(int index) => index > 0 && index < _spheres.Count ? _spheres[index] : default;

    /// <summary>Writes the spheres added since the last call into the texture and makes it current.</summary>
    public void Flush()
    {
        if (_texture is not null && _written == _spheres.Count)
        {
            return;
        }

        int rows = Math.Max(1, (_spheres.Count + Width - 1) / Width);
        bool grown = _image is null || _image.GetHeight() < rows;
        if (grown)
        {
            // Whole rows of headroom, so a few late additions do not make a new texture each.
            _image = Image.CreateEmpty(Width, Mathf.NearestPo2(rows), false, Image.Format.Rgbaf);
            _written = 0;
        }

        for (int i = _written; i < _spheres.Count; i++)
        {
            Vector4 s = _spheres[i];
            _image!.SetPixel(i % Width, i / Width, new Color(s.X, s.Y, s.Z, s.W));
        }

        _written = _spheres.Count;
        if (grown || _texture is null)
        {
            _texture = ImageTexture.CreateFromImage(_image!);
            ShaderGlobals.Set(s_textureName, _texture);
        }
        else
        {
            _texture.Update(_image!);
        }
    }

    public static void EnsureGlobals()
    {
        if (s_globalAdded)
        {
            return;
        }

        ShaderGlobals.Add(s_textureName, RenderingServer.GlobalShaderParameterType.Sampler2D,
            ImageTexture.CreateFromImage(Image.CreateEmpty(Width, 1, false, Image.Format.Rgbaf)));
        s_globalAdded = true;
    }
}
