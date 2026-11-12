using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Code the world pass leaves out of a draw's shader where its term is exactly zero. A shader that holds less code
/// shades faster on integrated GPUs whether or not the code runs, so every draw gets the smallest shader that draws
/// the same pixels.
/// </summary>
[Flags]
internal enum RdFeatures
{
    None = 0,
    /// <summary>No spot shadow light reaches the draw (SVM_NO_SPOT).</summary>
    NoSpot = 1,
    /// <summary>No dynamic world light reaches the draw (SVM_NO_DYN).</summary>
    NoDynamic = 2,
    /// <summary>The view's fog is not portal fog this frame (SVM_PORTAL_FOG left out).</summary>
    NoPortalFog = 4,
    All = 7,
}

/// <summary>The translated shaders of the process, by Godot shader code and the features left out.</summary>
internal static class RdShaderLibrary
{
    private static readonly Dictionary<(string Code, RdFeatures Features, int Actor), (RdTranslatedShader? Shader, string? Reason)> s_shaders = [];

    /// <summary>
    /// Ends the code of a material drawn in thermal and EMF vision (a comment: the code is the same): its shader keeps
    /// the vision outputs, and merged map geometry reads each instance's heat and EMF values from its vertices.
    /// </summary>
    public const string VisionMark = "\n// svm_rd_vision\n";

    public static bool IsVision(string code) => code.EndsWith(VisionMark, StringComparison.Ordinal);

    /// <summary>The features that change <paramref name="code"/>: leaving out any other gives the same shader.</summary>
    public static RdFeatures Applicable(string code)
    {
        bool baked = code.Contains("#define LIGHT_LIGHTMAP", StringComparison.Ordinal) || code.Contains("#define LIGHT_VERTEX", StringComparison.Ordinal);
        bool lit = baked || code.Contains("#define LIGHT_ACTOR", StringComparison.Ordinal);
        return (lit ? RdFeatures.NoSpot : 0) | (baked ? RdFeatures.NoDynamic : 0)
            | (code.Contains("#define " + ResponsiveSmokeShaders.PortalFogDefine, StringComparison.Ordinal) ? RdFeatures.NoPortalFog : 0);
    }

    /// <summary>The shader of <paramref name="code"/> without <paramref name="features"/>, or null with the reason.</summary>
    public static RdTranslatedShader? Get(string code, RdFeatures features, out string? reason, int actor = 0)
    {
        lock (s_shaders)
        {
            if (s_shaders.TryGetValue((code, features, actor), out (RdTranslatedShader? Shader, string? Reason) cached))
            {
                reason = cached.Reason;
                return cached.Shader;
            }

            RdTranslatedShader? translated = null;
            reason = null;
            try
            {
                var globals = new Dictionary<string, RdGlobal>(StringComparer.Ordinal);
                foreach (ShaderGlobals.Slot slot in ShaderGlobals.Slots())
                {
                    string name = slot.Name.ToString();
                    globals[name] = new RdGlobal(name, slot.Size == 0, slot.Place);
                }

                var define = new List<string>();
                if ((features & RdFeatures.NoSpot) != 0) define.Add("SVM_NO_SPOT");
                if ((features & RdFeatures.NoDynamic) != 0) define.Add("SVM_NO_DYN");
                // An actor's material is the one every vision mode draws with: outside thermal and EMF their code is left out.
                bool vision = IsVision(code);
                if (actor != 0 && !vision) define.Add("SVM_NORMAL_VISION");
                string[] suppress = (features & RdFeatures.NoPortalFog) != 0 ? [ResponsiveSmokeShaders.PortalFogDefine] : [];
                translated = GdShaderTranslator.Translate(code, FileAccess.GetFileAsString, globals, define, suppress, actor, vision);
            }
            catch (NotSupportedException error)
            {
                reason = error.Message;
            }

            s_shaders[(code, features, actor)] = (translated, reason);
            return translated;
        }
    }
}

/// <summary>One Godot material as the world pass draws it: its shader code, value block and textures.</summary>
internal sealed class RdMaterial
{
    /// <summary>The Godot shader's code; the pass draws it with some features left out (<see cref="RdShaderLibrary"/>).</summary>
    public required string Code { get; init; }
    /// <summary>The features that change the code (the others are dropped from a draw's request).</summary>
    public required RdFeatures Applicable { get; init; }
    /// <summary>The shader with every feature: the layout of <see cref="Values"/> and <see cref="Textures"/>.</summary>
    public required RdTranslatedShader Shader { get; init; }
    public required byte[] Values { get; set; }
    /// <summary>One per <see cref="RdTranslatedShader.Textures"/> entry; null draws the hint's default.</summary>
    public required Texture2D?[] Textures { get; set; }
    /// <summary>0: merged map geometry; 1 and 2: an actor's material, for rigid and skinned meshes.</summary>
    public int Actor { get; init; }
    /// <summary>Counted up when the values, or the textures, were read again and had changed (actor materials).</summary>
    public int Version, TextureVersion;
    private int _flags = -1;

    private int Flags
    {
        get
        {
            if (_flags < 0)
            {
                bool Has(string name) => Code.Contains("#define " + name, StringComparison.Ordinal);
                _flags = (Has("BLEND_MASKED") ? 1 : 0)
                    | (Has("BLEND_TRANSLUCENT") || Has("BLEND_ADDITIVE") || Has("BLEND_MODULATE") || Has("BLEND_DARKEN") ? 2 : 0)
                    | (RdObjects.LayersOf(Code).Length > 0 ? 4 : 0) | (Has("SVM_CLOAK") ? 8 : 0) | (Has("LIGHT_VERTEX") ? 16 : 0);
            }

            return _flags;
        }
    }

    public bool Masked => (Flags & 1) != 0;
    /// <summary>Blended into the frame (translucent, additive, multiplied): drawn after the solid scene, far to near, writing no depth.</summary>
    public bool Blended => (Flags & 2) != 0;
    /// <summary>
    /// More than one texture times a light: layers, animated coordinates, a combiner, an environment, an authored
    /// material. The own frame's small shaders do not hold these; such a surface is drawn by the whole template.
    /// </summary>
    public bool Layered => (Flags & 4) != 0;
    /// <summary>Lit by its baked vertex colours (a static mesh).</summary>
    public bool VertexLit => (Flags & 16) != 0;
    /// <summary>A cloaked body: it mixes its colour with the frame behind it (svm_behind).</summary>
    public bool Cloak => (Flags & 8) != 0;
    /// <summary>Drawn in thermal and EMF vision only (<see cref="RdShaderLibrary.VisionMark"/>).</summary>
    public bool Vision => RdShaderLibrary.IsVision(Code);
    /// <summary>Set by RdWorldGpu on first use: its index there.</summary>
    public int GpuIndex = -1;
}

/// <summary>
/// SVM_RD_WORLD=1: the merged map geometry the world pass draws on the RenderingDevice (RdWorldRenderer), copied out
/// as MapBuilder flushes its cells. A bucket is taken when its material is solid or masked and its normal vision twin
/// translates (<see cref="GdShaderTranslator"/>); every other bucket stays with Godot's renderer. Taken buckets keep
/// their Godot mesh too, on a layer of their own: the shadow cameras draw it, and the main view when the world pass is
/// off (thermal, EMF, responsive smoke, a failure).
/// </summary>
internal sealed class RdWorldGeometry
{
    /// <summary>
    /// On with the options' Fast renderer (read after <see cref="RdWorldRenderer.LatchOwnFrame"/>, when a map is
    /// built); SVM_RD_WORLD=1 or 0 overrides that.
    /// </summary>
    public static bool Enabled => s_forced ?? RdWorldRenderer.OwnFrame;

    private static readonly bool? s_forced = System.Environment.GetEnvironmentVariable("SVM_RD_WORLD") switch { "1" => true, "0" => false, _ => null };

    /// <summary>One vertex, 40 bytes: the streams of a Godot mesh surface, packed as Godot packs them.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Vertex
    {
        public Vector3 Position;
        /// <summary>Octahedral, 16 bits a component (Vector3::octahedron_encode).</summary>
        public ushort NormalX, NormalY;
        public Vector2 Uv;
        public Vector2 Uv2;
        /// <summary>RGBA8: the baked light (half the light term) and the zone in alpha.</summary>
        public uint Rgba;
        public float Echelon;
    }

    public sealed class Batch
    {
        public required bool Sky { get; init; }
        public required RdMaterial Material { get; init; }
        /// <summary>
        /// What draws the batch in thermal and EMF vision: the material with its vision outputs, and for partly
        /// see-through actors the blended pass drawn over it. Null: not drawn there (the sky zone).
        /// </summary>
        public RdMaterial? VisionMaterial { get; init; }
        public RdMaterial? VisionOver { get; init; }
        public uint IndexOffset { get; set; }
        public uint IndexCount { get; set; }
        public Aabb Bounds { get; set; }
        /// <summary>
        /// The box a dynamic world light is tested against: <see cref="Bounds"/> merged with the box around the light
        /// spheres of the batch's vertices (a static mesh is lit from as far as the light's radius from its sphere).
        /// </summary>
        public Aabb LightBounds { get; set; }
        /// <summary>The zones of the batch's vertices (the colour's alpha byte), one bit each.</summary>
        public required ulong[] Zones { get; init; }

        public bool HasZone(int zone) => zone is >= 0 and < 256 && (Zones[zone >> 6] & (1UL << (zone & 63))) != 0;
    }

    private sealed class Builder
    {
        public readonly ulong[] Zones = new ulong[4];
        public readonly List<Vertex> Vertices = [];
        public readonly List<Vector4> Vision = [];
        public readonly List<uint> Indices = [];
        public Aabb Bounds;
        public Aabb LightBounds;
        public int LastSphere;
        public bool Any;
    }

    /// <summary>SVM_RD_WORLD_LEAVE=a,b: materials whose name holds one of these stay with Godot (to find a difference).</summary>
    private static readonly string[] s_leave =
        (System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_LEAVE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The names of the materials taken, with the defines of each one's shader (a diagnostic listing).</summary>
    public IEnumerable<string> TakenNames()
    {
        foreach (((string name, LightSource light, bool sky), RdMaterial? material) in _byName)
        {
            if (material is null) continue;
            var defines = new List<string>();
            foreach (string line in material.Code.Split((char)10))
            {
                if (line.StartsWith("#define ", StringComparison.Ordinal)) defines.Add(line[8..].Trim());
            }

            yield return $"{name} [{light}{(sky ? ", sky" : "")}] {string.Join(' ', defines)}";
        }
    }

    private readonly MaterialCompiler _compiler;
    private readonly Dictionary<(string Name, LightSource Light, bool Sky), RdMaterial?> _byName = [];
    private readonly Dictionary<ShaderMaterial, RdMaterial?> _materials = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(Vector3I Block, bool Sky, RdMaterial Material, bool SeeThrough), Builder> _builders = [];
    private readonly Dictionary<(string Name, LightSource Light, bool SeeThrough), (RdMaterial? Solid, RdMaterial? Over)> _vision = [];
    private readonly Dictionary<RdMaterial, (string Name, LightSource Light)> _named = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Builder, int[]> _remap = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The side of a batch's block (m; SVM_RD_WORLD_BLOCK). A draw is one material's triangles in one block of this
    /// grid, by triangle centre: MapBuilder's own cells are 4096 uu (78 m) wide, and a draw that large touches most
    /// lights of a map, so nearly every pixel would pay for light code that lights a few. Small boxes keep the costly
    /// shader variants to the surfaces a light can reach, and cull better.
    /// </summary>
    public static float BlockSize { get; } =
        float.TryParse(System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_BLOCK"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float metres) && metres > 0.5f ? metres : 8f;

    public RdWorldGeometry(MaterialCompiler compiler) => _compiler = compiler;

    public Vertex[] Vertices { get; private set; } = [];
    /// <summary>One for each of <see cref="Vertices"/>: HeatIntensity, HeatOpacity, EMF glow and EMFOpacity + 2 of its instance.</summary>
    public Vector4[] Vision { get; private set; } = [];
    public uint[] Indices { get; private set; } = [];
    public List<Batch> Batches { get; } = [];
    public int Taken { get; private set; }
    public int Left { get; private set; }
    /// <summary>Why buckets were left with Godot, with counts (logged once per map).</summary>
    public SortedDictionary<string, int> Reasons { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes one flushed cell bucket if the world pass can draw it; false leaves it with Godot's renderer.
    /// </summary>
    /// <param name="seeThrough">The bucket of partly see-through actors (MaterialCompiler.GetSeeThrough draws it in thermal and EMF).</param>
    /// <param name="spheres">The map's light spheres, which the UV2 stream of a vertex-lit surface names (SurfaceBuilder.HasSphere).</param>
    public bool TryAdd((int X, int Y, int Sky) cell, string name, LightSource light, MaterialEntry? entry, SurfaceBuilder surface, bool seeThrough = false,
        MeshLightSpheres? spheres = null)
    {
        bool sky = cell.Sky != 0;
        RdMaterial? material = MaterialOf(name, light, sky, entry);
        if (material is null)
        {
            Left++;
            return false;
        }

        (Vector3[] positions, Vector3[] normals, Vector2[] uvs, Vector2[]? uv2s, Color[]? colours, float[]? echelon, int[] indices) = surface.RdStreams();
        float[]? vision = surface.RdVision();
        _named[material] = (name, light);
        seeThrough &= !sky;
        _remap.Clear();
        float perMetre = 1f / BlockSize;
        Span<int> corner = stackalloc int[3];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            // The sky zone is one draw per material: its surfaces are seen from the SkyZoneInfo, far from where they lie.
            Vector3I block = default;
            if (!sky)
            {
                Vector3 centre = (positions[indices[t]] + positions[indices[t + 1]] + positions[indices[t + 2]]) * (perMetre / 3f);
                block = new Vector3I(Mathf.FloorToInt(centre.X), Mathf.FloorToInt(centre.Y), Mathf.FloorToInt(centre.Z));
            }

            var key = (block, sky, material, seeThrough);
            if (!_builders.TryGetValue(key, out Builder? b))
            {
                b = new Builder();
                _builders[key] = b;
            }

            // Each of the surface's vertices is written once per block that uses it.
            if (!_remap.TryGetValue(b, out int[]? remap))
            {
                remap = new int[positions.Length];
                Array.Fill(remap, -1);
                _remap[b] = remap;
            }

            (corner[0], corner[1], corner[2]) = (indices[t], indices[t + 1], indices[t + 2]);
            foreach (int i in corner)
            {
                if (remap[i] < 0)
                {
                    remap[i] = b.Vertices.Count;
                    Vector2 oct = Octahedron(normals[i]);
                    // A surface without colours reads white, as a Godot mesh without the stream.
                    uint rgba = colours is null ? 0xffffffffu : Rgba8(colours[i]);
                    b.Zones[rgba >> 30] |= 1UL << (int)((rgba >> 24) & 63);
                    b.Vertices.Add(new Vertex
                    {
                        Position = positions[i],
                        NormalX = (ushort)Math.Clamp(oct.X * 65535, 0, 65535),
                        NormalY = (ushort)Math.Clamp(oct.Y * 65535, 0, 65535),
                        Uv = uvs[i],
                        Uv2 = uv2s is null ? Vector2.Zero : uv2s[i],
                        Rgba = rgba,
                        Echelon = echelon is null ? 0f : echelon[i],
                    });
                    b.Vision.Add(vision is null
                        ? new Vector4(SurfaceBuilder.DefaultVision.X, SurfaceBuilder.DefaultVision.Y, SurfaceBuilder.DefaultVision.Z, SurfaceBuilder.DefaultVision.W + 2f)
                        : new Vector4(vision[i * 4], vision[(i * 4) + 1], vision[(i * 4) + 2], vision[(i * 4) + 3]));
                    b.Bounds = b.Any ? b.Bounds.Expand(positions[i]) : new Aabb(positions[i], Vector3.Zero);
                    b.LightBounds = b.Any ? b.LightBounds.Expand(positions[i]) : b.Bounds;
                    b.Any = true;
                    if (spheres is not null && surface.HasSphere && uv2s is not null && (int)uv2s[i].X is var sphere && sphere != 0 && sphere != b.LastSphere)
                    {
                        b.LastSphere = sphere;
                        Vector4 s = spheres.Get(sphere);
                        var half = new Vector3(s.W, s.W, s.W);
                        b.LightBounds = b.LightBounds.Merge(new Aabb(new Vector3(s.X, s.Y, s.Z) - half, half * 2f));
                    }
                }

                b.Indices.Add((uint)remap[i]);
            }
        }

        Taken++;
        return true;
    }

    /// <summary>Vector3::octahedron_encode, as the engine packs a mesh normal.</summary>
    private static Vector2 Octahedron(Vector3 n)
    {
        n /= MathF.Abs(n.X) + MathF.Abs(n.Y) + MathF.Abs(n.Z);
        float x, y;
        if (n.Z >= 0f)
        {
            x = n.X;
            y = n.Y;
        }
        else
        {
            x = (1f - MathF.Abs(n.Y)) * (n.X >= 0f ? 1f : -1f);
            y = (1f - MathF.Abs(n.X)) * (n.Y >= 0f ? 1f : -1f);
        }

        return new Vector2((x * 0.5f) + 0.5f, (y * 0.5f) + 0.5f);
    }

    /// <summary>A mesh colour as the engine packs it: each channel x 255, cut to a byte (not rounded).</summary>
    private static uint Rgba8(Color c)
    {
        static uint Byte(float v) => (uint)(byte)Math.Clamp(v * 255.0, 0.0, 255.0);
        return Byte(c.R) | (Byte(c.G) << 8) | (Byte(c.B) << 16) | (Byte(c.A) << 24);
    }

    private RdMaterial? MaterialOf(string name, LightSource light, bool sky, MaterialEntry? entry)
    {
        if (_byName.TryGetValue((name, light, sky), out RdMaterial? material))
        {
            return material;
        }

        string? reason = null;
        if (Array.Exists(s_leave, part => name.Contains(part, StringComparison.OrdinalIgnoreCase))) reason = "left by SVM_RD_WORLD_LEAVE";
        else if (entry is null) reason = "no material entry";
        // The own frame draws blended surfaces itself; inside Godot's frame they stay with Godot's transparent pass.
        else if (entry.Blend is not (MaterialBlend.Opaque or MaterialBlend.Masked) && !RdWorldRenderer.OwnFrame) reason = "blended";
        else if (entry.Authored is not null) reason = "authored";
        else if (entry.Sequences.Count > 0) reason = "sequence";
        else
        {
            // The normal vision twin with every light term (the pass leaves terms out per draw itself); the sky zone has
            // one material for every state.
            Material? source = sky ? _compiler.GetSky(name, light) : _compiler.GetNormalVision(name, light, plain: false) ?? _compiler.Get(name, light);
            if (source is ShaderMaterial shaderMaterial) material = MaterialOf(shaderMaterial, sky, out reason);
            else reason = "no normal vision material";
        }

        if (reason is not null) Reasons[reason] = Reasons.GetValueOrDefault(reason) + 1;
        _byName[(name, light, sky)] = material;
        return material;
    }

    /// <summary>
    /// The materials that draw a batch in thermal and EMF vision: the map material with its vision outputs (the one
    /// Godot's renderer swaps in there, LightReach), and its blended see-through pass when it has one.
    /// </summary>
    private (RdMaterial? Solid, RdMaterial? Over) VisionOf(string name, LightSource light, bool seeThrough)
    {
        if (_vision.TryGetValue((name, light, seeThrough), out (RdMaterial? Solid, RdMaterial? Over) found)) return found;
        Material? source = seeThrough ? _compiler.GetSeeThrough(name, light) : _compiler.Get(name, light);
        if (source is ShaderMaterial solid)
        {
            found = (VisionMaterial(solid), solid.NextPass is ShaderMaterial over ? VisionMaterial(over) : null);
        }

        _vision[(name, light, seeThrough)] = found;
        return found;
    }

    private static RdMaterial? VisionMaterial(ShaderMaterial source)
    {
        if (source.Shader is not { } shader) return null;
        string code = ResponsiveSmokeShaders.BaseCode(shader) + RdShaderLibrary.VisionMark;
        if (RdShaderLibrary.Get(code, RdFeatures.None, out _) is not { } translated) return null;
        try
        {
            return new RdMaterial
            {
                Code = code, Applicable = RdShaderLibrary.Applicable(code), Shader = translated,
                Values = PackValues(source, translated), Textures = TexturesOf(source, translated),
            };
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private RdMaterial? MaterialOf(ShaderMaterial source, bool sky, out string? reason)
    {
        reason = null;
        if (_materials.TryGetValue(source, out RdMaterial? cached))
        {
            if (cached is null) reason = "untranslatable";
            return cached;
        }

        RdMaterial? material = null;
        if (source.NextPass is not null) reason = "next pass";
        else if (source.Shader is not { } shader) reason = "no shader";
        else
        {
            string code = shader.Code;
            if (RdShaderLibrary.Get(code, RdFeatures.None, out reason) is { } translated)
            {
                try
                {
                    material = new RdMaterial
                    {
                        Code = code, Applicable = sky ? RdFeatures.None : RdShaderLibrary.Applicable(code), Shader = translated,
                        Values = PackValues(source, translated), Textures = TexturesOf(source, translated),
                    };
                }
                catch (NotSupportedException error)
                {
                    reason = error.Message;
                }
            }
        }

        _materials[source] = material;
        return material;
    }

    /// <summary>
    /// An actor's material as the own frame draws it with the whole template (main thread), or null when the template
    /// cannot be carried: the caller then draws the surface plainly, or not at all.
    /// </summary>
    /// <param name="vision">The material as thermal and EMF vision draw it (its vision outputs kept).</param>
    public static RdMaterial? ActorMaterial(ShaderMaterial source, int actor, bool vision = false)
    {
        if (source.Shader is not { } shader) return null;
        string code = ResponsiveSmokeShaders.BaseCode(shader) + (vision ? RdShaderLibrary.VisionMark : "");
        if (RdShaderLibrary.Get(code, RdFeatures.None, out _, actor) is not { } translated) return null;
        try
        {
            // Authored materials read every light: they keep the full variant.
            bool authored = code.Contains("#define AUTHORED_MATERIAL", StringComparison.Ordinal);
            return new RdMaterial
            {
                Code = code, Actor = actor, Applicable = authored ? RdFeatures.None : RdShaderLibrary.Applicable(code), Shader = translated,
                Values = PackValues(source, translated), Textures = TexturesOf(source, translated),
            };
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads an actor material's values and textures again (they are set on the Godot material as play goes on).</summary>
    public static void Refresh(ShaderMaterial source, RdMaterial material)
    {
        try
        {
            byte[] values = PackValues(source, material.Shader);
            if (!values.AsSpan().SequenceEqual(material.Values))
            {
                material.Values = values;
                material.Version++;
            }

            Texture2D?[] textures = TexturesOf(source, material.Shader);
            for (int i = 0; i < textures.Length; i++)
            {
                if (ReferenceEquals(textures[i], material.Textures[i])) continue;
                material.Textures = textures;
                material.TextureVersion++;
                break;
            }
        }
        catch (NotSupportedException)
        {
            // A value of a kind the block cannot hold: the material keeps what it had.
        }
    }

    private static byte[] PackValues(ShaderMaterial source, RdTranslatedShader shader)
    {
        var bytes = new byte[shader.ValueBlockSize];
        Span<float> floats = stackalloc float[4];
        Span<int> ints = stackalloc int[4];
        foreach (RdMaterialValue v in shader.Values)
        {
            Variant value = source.GetShaderParameter(v.Name);
            bool integer = v.Type is "int" or "uint" or "bool" or "ivec2" or "ivec3" or "ivec4";
            int n = v.Default.Length;
            for (int i = 0; i < n; i++)
            {
                floats[i] = (float)v.Default[i];
                ints[i] = (int)v.Default[i];
            }

            switch (value.VariantType)
            {
                case Variant.Type.Nil:
                    break;
                case Variant.Type.Bool:
                    ints[0] = value.AsBool() ? 1 : 0;
                    floats[0] = ints[0];
                    break;
                case Variant.Type.Int:
                    ints[0] = value.AsInt32();
                    floats[0] = ints[0];
                    break;
                case Variant.Type.Float:
                    floats[0] = value.AsSingle();
                    ints[0] = (int)floats[0];
                    break;
                case Variant.Type.Vector2:
                {
                    Vector2 x = value.AsVector2();
                    (floats[0], floats[1]) = (x.X, x.Y);
                    break;
                }

                case Variant.Type.Vector3:
                {
                    Vector3 x = value.AsVector3();
                    (floats[0], floats[1], floats[2]) = (x.X, x.Y, x.Z);
                    break;
                }

                case Variant.Type.Vector4:
                {
                    Vector4 x = value.AsVector4();
                    (floats[0], floats[1], floats[2], floats[3]) = (x.X, x.Y, x.Z, x.W);
                    break;
                }

                case Variant.Type.Color:
                {
                    Color x = value.AsColor();
                    (floats[0], floats[1], floats[2], floats[3]) = (x.R, x.G, x.B, x.A);
                    break;
                }

                case Variant.Type.Vector2I:
                {
                    Vector2I x = value.AsVector2I();
                    (ints[0], ints[1]) = (x.X, x.Y);
                    break;
                }

                case Variant.Type.Vector3I:
                {
                    Vector3I x = value.AsVector3I();
                    (ints[0], ints[1], ints[2]) = (x.X, x.Y, x.Z);
                    break;
                }

                case Variant.Type.Vector4I:
                {
                    Vector4I x = value.AsVector4I();
                    (ints[0], ints[1], ints[2], ints[3]) = (x.X, x.Y, x.Z, x.W);
                    break;
                }

                default:
                    throw new NotSupportedException($"material parameter {v.Name} of type {value.VariantType}");
            }

            Span<byte> to = bytes.AsSpan(v.Offset, n * 4);
            if (integer) MemoryMarshal.AsBytes(ints[..n]).CopyTo(to);
            else MemoryMarshal.AsBytes(floats[..n]).CopyTo(to);
        }

        return bytes;
    }

    private static Texture2D?[] TexturesOf(ShaderMaterial source, RdTranslatedShader shader)
    {
        var textures = new Texture2D?[shader.Textures.Count];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = source.GetShaderParameter(shader.Textures[i].Name).Obj as Texture2D;
        }

        return textures;
    }

    /// <summary>Concatenates the batches into one vertex and one index stream (indices absolute in the stream).</summary>
    public void Finish()
    {
        int vertices = 0, indices = 0;
        foreach (Builder b in _builders.Values)
        {
            vertices += b.Vertices.Count;
            indices += b.Indices.Count;
        }

        var vs = new Vertex[vertices];
        var vision = new Vector4[vertices];
        var ix = new uint[indices];
        int v = 0, n = 0;
        foreach (((Vector3I _, bool sky, RdMaterial material, bool seeThrough), Builder b) in _builders)
        {
            if (b.Indices.Count == 0) continue;
            CollectionsMarshal.AsSpan(b.Vertices).CopyTo(vs.AsSpan(v));
            CollectionsMarshal.AsSpan(b.Vision).CopyTo(vision.AsSpan(v));
            (RdMaterial? solid, RdMaterial? over) = sky || !RdWorldRenderer.OwnFrame || !_named.TryGetValue(material, out (string Name, LightSource Light) named)
                ? default : VisionOf(named.Name, named.Light, seeThrough);
            var batch = new Batch
            {
                Sky = sky, Material = material, VisionMaterial = solid, VisionOver = over,
                IndexOffset = (uint)n, IndexCount = (uint)b.Indices.Count, Bounds = b.Bounds, LightBounds = b.LightBounds, Zones = b.Zones,
            };
            foreach (uint index in b.Indices) ix[n++] = index + (uint)v;
            v += b.Vertices.Count;
            Batches.Add(batch);
        }

        Vertices = vs;
        Vision = vision;
        Indices = ix;
        _builders.Clear();
        _remap.Clear();
    }
}
