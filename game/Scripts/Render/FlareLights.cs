using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The burning flares' light with cube shadows (an OpenSCCT addition, docs/RENDERING.md#flare-lights). The original
/// lights a flare as an unshadowed point light kept to its own zone (bAffectOwnZoneOnly), so it stops in a hard line at
/// a zone portal and shines through walls inside its zone. Here a flare lights every surface its radius reaches that
/// it can see: six cameras around each of the nearest flares render the world's depth (one cube face each, pawns
/// included) into a shared atlas every frame, and the world template tests each pixel against the face it falls in.
/// The light's colour, radius and falloff stay those of the flare's dynamic light (the world's dynamic lights on BSP
/// and static meshes, the actor model on actors), so only what the flare can see changes. The light source wavers a
/// little with the flame, so the shadows move as a flare's do.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: flares light across zones (Ryan, AquaD Entrance / Blue Room) and are shadowed by the world; not in the original")]
public sealed partial class FlareLights : Node3D
{
    public const string ParamRows = "svm_flares";
    public const string ParamCount = "svm_flare_count";
    public const string ParamDepth = "svm_flare_depth";

    /// <summary>Flares lit at once (the nearest to the camera); more are left dark.</summary>
    public const int MaxFlares = 8;

    /// <summary>Flares with a cube shadow (the nearest that are lit); the others light unshadowed.</summary>
    public const int MaxShadowed = 4;

    /// <summary>Texels per flare row of the row texture (svm_world.gdshaderinc, svm_flare_light).</summary>
    private const int RowTexels = 5;

    /// <summary>Near plane of the shadow cameras (uu).</summary>
    private const float NearUnits = 2f;

    /// <summary>Far plane of the shadow cameras (uu): past the flare's reach, 25 x (LightRadius 25 + 1) = 650 uu.</summary>
    private const float FarUnits = 1000f;

    /// <summary>Depth bias (uu) and bias per uu of depth.</summary>
    private const float BiasUnits = 3f;
    private const float BiasSlope = 0.01f;

    /// <summary>The surface point moves this far along its normal before the shadow test (uu), against acne.</summary>
    private const float NormalOffsetUnits = 3f;

    /// <summary>Disc radius (texels) softening the shadow edges.</summary>
    private const float SoftRadius = 1.5f;

    /// <summary>How far the light source wavers with the flame (uu, each axis).</summary>
    private const float WaverUnits = 1.5f;

    /// <summary>Frames a newly placed cube waits for its first depth before it lights (its tile holds old depth).</summary>
    private const int WarmFrames = 2;

    // The faces' forward and up axes (Godot space); the shader picks the same face for a direction.
    private static readonly Vector3[] FaceForward =
    [
        new(1f, 0f, 0f), new(-1f, 0f, 0f), new(0f, 1f, 0f), new(0f, -1f, 0f), new(0f, 0f, 1f), new(0f, 0f, -1f),
    ];

    private static readonly Vector3[] FaceUp =
    [
        new(0f, 1f, 0f), new(0f, 1f, 0f), new(0f, 0f, -1f), new(0f, 0f, 1f), new(0f, 1f, 0f), new(0f, 1f, 0f),
    ];

    private static readonly StringName s_rowsName = ParamRows;
    private static readonly StringName s_countName = ParamCount;
    private static readonly StringName s_depthName = ParamDepth;
    private static bool s_globalsAdded;
    private static Image? s_rows;
    private static ImageTexture? s_rowsTexture;
    private static ImageTexture? s_noDepth;

    private readonly List<Flare> _frame = [];
    private readonly Cube?[] _cubes = new Cube?[MaxShadowed];
    private readonly Texture2Drd _atlasTexture = new();
    private int _faceSize;
    private Rid _atlas;
    private bool _atlasPending;
    private bool _bound;

    private readonly record struct Flare(object Key, LightEntry Light, float Distance2);

    /// <summary>Flares lit last frame, and how many of them had a shadow.</summary>
    public int Lit { get; private set; }

    public int Shadowed { get; private set; }

    /// <summary>Registers the template's flare parameters (once per process).</summary>
    public static void EnsureGlobals()
    {
        if (s_globalsAdded)
        {
            return;
        }

        s_rows = Image.CreateEmpty(RowTexels, MaxFlares, false, Image.Format.Rgbaf);
        s_rowsTexture = ImageTexture.CreateFromImage(s_rows);
        s_noDepth = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rf));
        RenderingServer.GlobalShaderParameterAdd(ParamRows, RenderingServer.GlobalShaderParameterType.Sampler2D, s_rowsTexture);
        RenderingServer.GlobalShaderParameterAdd(ParamCount, RenderingServer.GlobalShaderParameterType.Int, 0);
        RenderingServer.GlobalShaderParameterAdd(ParamDepth, RenderingServer.GlobalShaderParameterType.Sampler2D, s_noDepth);
        s_globalsAdded = true;
    }

    /// <summary>
    /// A flare burning this frame: <paramref name="key"/> keeps its shadow from frame to frame (the flare actor),
    /// <paramref name="light"/> its light as a world light (location, colour, brightness, radius, effect).
    /// </summary>
    public void Add(object key, LightEntry light, FVector camera)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(light);
        _frame.Add(new Flare(key, light, (light.Location - camera).SizeSquared()));
    }

    /// <summary>Writes this frame's flares to the template, places their shadow cameras, and starts a new frame.</summary>
    public void Flush(float time)
    {
        EnsureGlobals();
        _frame.Sort(static (a, b) => a.Distance2.CompareTo(b.Distance2));
        int lit = Math.Min(_frame.Count, MaxFlares);
        AssignCubes(lit);
        int shadowed = 0;
        int rows = 0;
        for (int i = 0; i < lit; i++)
        {
            Flare f = _frame[i];
            Cube? cube = CubeOf(f.Key);
            FVector at = f.Light.Location + Waver(time, f.Light.Seed);
            cube?.Place(UeSpace.ToGodot(at));
            if (cube is not null && !cube.Lights)
            {
                // Its tile still holds another light's depth: dark until its own depth is in.
                continue;
            }

            PackedLight p = ActorLighting.PackWorld(f.Light with { Location = at }, 1f);
            SetRow(rows, 0, p.Pos);
            SetRow(rows, 1, p.Col);
            SetRow(rows, 2, p.Spot);
            SetRow(rows, 3, new Vector4(cube?.Row ?? -1, UeSpace.LengthToGodot(BiasUnits), UeSpace.LengthToGodot(NormalOffsetUnits), SoftRadius));
            SetRow(rows, 4, new Vector4(1f / Cube.Widen(_faceSize), _faceSize, BiasSlope, 0f));
            rows++;
            shadowed += cube is null ? 0 : 1;
        }

        foreach (Cube? cube in _cubes)
        {
            cube?.Tick();
        }

        BindAtlas();
        s_rowsTexture!.Update(s_rows);
        RenderingServer.GlobalShaderParameterSet(s_countName, rows);
        Lit = rows;
        Shadowed = shadowed;
        _frame.Clear();
    }

    /// <summary>The flame's waver: a few uu, smooth, different for each flare.</summary>
    private static FVector Waver(float time, int seed)
    {
        float s = (seed & 0xffff) * 0.001f;
        return new FVector(
            WaverUnits * (MathF.Sin((time * 11.3f) + s) * 0.6f + MathF.Sin((time * 23.7f) + (s * 2.1f)) * 0.4f),
            WaverUnits * (MathF.Sin((time * 13.1f) + (s * 1.3f)) * 0.6f + MathF.Sin((time * 19.9f) + (s * 0.7f)) * 0.4f),
            WaverUnits * 0.7f * MathF.Sin((time * 17.3f) + (s * 1.7f)));
    }

    private Cube? CubeOf(object key)
    {
        foreach (Cube? c in _cubes)
        {
            if (c is not null && ReferenceEquals(c.Key, key))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the cubes of flares still among the nearest <paramref name="lit"/>, frees the others, and gives free cubes
    /// to the nearest flares without one. A flare keeps its cube while it stays lit, so shadows do not hop between
    /// flares as the camera moves.
    /// </summary>
    private void AssignCubes(int lit)
    {
        foreach (Cube? c in _cubes)
        {
            if (c?.Key is not { } key)
            {
                continue;
            }

            bool kept = false;
            for (int i = 0; i < lit && !kept; i++)
            {
                kept = ReferenceEquals(_frame[i].Key, key);
            }

            if (!kept)
            {
                c.Release();
            }
        }

        for (int i = 0; i < lit; i++)
        {
            object key = _frame[i].Key;
            if (CubeOf(key) is not null)
            {
                continue;
            }

            int free = Array.FindIndex(_cubes, c => c is null || c.Key is null);
            if (free < 0)
            {
                break;
            }

            EnsureAtlas();
            Cube cube = _cubes[free] ??= new Cube(this, free, _faceSize, () => _atlas);
            cube.Take(key);
        }
    }

    private static void SetRow(int row, int texel, Vector4 v) => s_rows!.SetPixel(texel, row, new Color(v.X, v.Y, v.Z, v.W));

    /// <summary>Asks the render thread for the cube atlas (six faces across, one cube per row).</summary>
    private void EnsureAtlas()
    {
        if (_faceSize == 0)
        {
            // Half the spot shadow map: a flare's reach is short and its shadows are soft.
            _faceSize = Math.Clamp(SpotShadowLight.MapSize / 2, 128, 512);
        }

        if (_atlas.IsValid || _atlasPending)
        {
            return;
        }

        _atlasPending = true;
        int width = _faceSize * 6;
        int height = _faceSize * MaxShadowed;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            RenderingDevice? rd = RenderingServer.GetRenderingDevice();
            if (rd is not null)
            {
                _atlas = rd.TextureCreate(new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R32Sfloat,
                    Width = (uint)width,
                    Height = (uint)height,
                    UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
                }, new RDTextureView());
            }

            _atlasPending = false;
        }));
    }

    private void BindAtlas()
    {
        Rid atlas = _atlas;
        if (atlas.IsValid && (!_bound || _atlasTexture.TextureRdRid != atlas))
        {
            _atlasTexture.TextureRdRid = atlas;
            RenderingServer.GlobalShaderParameterSet(s_depthName, _atlasTexture);
            _bound = true;
        }
    }

    public override void _ExitTree()
    {
        foreach (Cube? c in _cubes)
        {
            c?.Dispose();
        }

        Array.Clear(_cubes);
        if (s_globalsAdded)
        {
            RenderingServer.GlobalShaderParameterSet(s_countName, 0);
            if (_bound && s_noDepth is not null)
            {
                RenderingServer.GlobalShaderParameterSet(s_depthName, s_noDepth);
            }
        }

        _bound = false;
        _atlasTexture.TextureRdRid = default;
        Rid atlas = _atlas;
        _atlas = default;
        if (atlas.IsValid)
        {
            RenderingServer.CallOnRenderThread(Callable.From(() => RenderingServer.GetRenderingDevice()?.FreeRid(atlas)));
        }

        base._ExitTree();
    }

    /// <summary>The six shadow cameras of one flare, writing row <see cref="Row"/> of the atlas.</summary>
    private sealed class Cube : IDisposable
    {
        private readonly SubViewport[] _viewports = new SubViewport[6];
        private readonly Camera3D[] _cameras = new Camera3D[6];
        private readonly ShadowDepthCapture[] _captures = new ShadowDepthCapture[6];
        private int _age;

        public Cube(Node parent, int row, int size, Func<Rid> atlas)
        {
            Row = row;
            float near = UeSpace.LengthToGodot(NearUnits);
            float far = UeSpace.LengthToGodot(FarUnits);
            float fov = Mathf.RadToDeg(2f * MathF.Atan(Widen(size)));
            for (int f = 0; f < 6; f++)
            {
                _captures[f] = new ShadowDepthCapture(new Vector2I(size, size), near, far, new Vector2I(f * size, row * size), false, atlas);
                _viewports[f] = new SubViewport
                {
                    Name = $"FlareShadow{row}_{f}",
                    Size = new Vector2I(size, size),
                    Msaa3D = Godot.Viewport.Msaa.Disabled,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                };
                _cameras[f] = new Camera3D
                {
                    Name = "FlareCamera",
                    KeepAspect = Camera3D.KeepAspectEnum.Height,
                    Projection = Camera3D.ProjectionType.Perspective,
                    Fov = fov,
                    Near = near,
                    Far = far,
                    CullMask = RenderLayers.World,
                    Environment = FaithfulView.CreateEnvironment(),
                    Attributes = FaithfulView.CreateAttributes(),
                    Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _captures[f] } },
                    Current = true,
                };
                _viewports[f].AddChild(_cameras[f]);
                parent.AddChild(_viewports[f]);
            }
        }

        /// <summary>The cube's row of the atlas.</summary>
        public int Row { get; }

        /// <summary>The flare the cube follows, or null while free.</summary>
        public object? Key { get; private set; }

        /// <summary>The cube's depth is its own flare's: the flare may light with it.</summary>
        public bool Lights => _age >= WarmFrames && Array.TrueForAll(_captures, c => c.Ready);

        /// <summary>The faces see a little past 90 degrees, so a lookup at a face's edge stays inside its tile.</summary>
        public static float Widen(int size) => 1f + (4f / Math.Max(1, size));

        public void Take(object key)
        {
            Key = key;
            _age = 0;
            foreach (SubViewport v in _viewports)
            {
                v.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            }
        }

        public void Release()
        {
            Key = null;
            foreach (SubViewport v in _viewports)
            {
                v.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            }
        }

        /// <summary>Puts the six cameras at <paramref name="at"/> (Godot space).</summary>
        public void Place(Vector3 at)
        {
            for (int f = 0; f < 6; f++)
            {
                Vector3 forward = FaceForward[f];
                Vector3 up = FaceUp[f];
                _cameras[f].GlobalTransform = new Transform3D(new Basis(forward.Cross(up), up, -forward), at);
            }
        }

        /// <summary>Counts the frames the cube has rendered for its flare.</summary>
        public void Tick()
        {
            if (Key is not null && _age < WarmFrames)
            {
                _age++;
            }
        }

        public void Dispose()
        {
            Release();
            foreach (ShadowDepthCapture c in _captures)
            {
                c.Release();
            }

            foreach (SubViewport v in _viewports)
            {
                v.QueueFree();
            }
        }
    }
}
