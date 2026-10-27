using System;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>The shape and colour of an LE_ESpotShadow light (Echelon spot: the merc torch, window and sun projectors).</summary>
/// <param name="Range">EchelonRange in uu: the light reaches this far along its axis.</param>
/// <param name="SpotWidth">SpotWidth in uu: the lit width at <paramref name="Range"/>.</param>
/// <param name="SpotHeight">SpotHeight in uu.</param>
/// <param name="NearPlaneRatio">NearPlaneRatio: the near plane is Range x this.</param>
/// <param name="Brightness">LightBrightness.</param>
/// <param name="Hue">LightHue.</param>
/// <param name="Saturation">LightSaturation (255 is white).</param>
/// <param name="NormalInfluence">EchelonNormalInfluence: 0 lights every surface fully, 1 by N.L.</param>
/// <param name="Attenuation">EchelonLightingMaterial.AttenuationType: 0 none, 1 linear, 2 late.</param>
/// <param name="Cookie">Object path of the projected texture in the render pack.</param>
public sealed record SpotShadowShape(float Range, float SpotWidth, float SpotHeight, float NearPlaneRatio, float Brightness, byte Hue,
    byte Saturation, float NormalInfluence, int Attenuation, string Cookie)
{
    /// <summary>The merc torch, SGadTorche (spec/equipment/merc-flashlight.md): Echelon.Torche cookie, linear attenuation, no normal influence.</summary>
    public static SpotShadowShape Torch { get; } = new(RenderK.TorchRange, RenderK.TorchSpotSize, RenderK.TorchSpotSize,
        RenderK.TorchNearPlaneRatio, RenderK.TorchBrightness, RenderK.TorchHue, RenderK.TorchSaturation, 0f, RenderK.TorchAttenuation,
        RenderPack.TorchCookie);
}

/// <summary>
/// An LE_ESpotShadow light drawn the Faithful way (docs/RENDERING.md#spot-shadow-lights): its own shadow pass (a camera
/// at the light renders the world's depth into a 512 x 512 target, kept as linear distance), and a projected term the
/// world template adds per pixel to the light stage of every surface it reaches: colour x cookie(uv) x attenuation(d /
/// Range) x shadow, the frustum being SpotWidth x SpotHeight at EchelonRange. One spot shadow light is drawn at a time
/// (the template has one slot); <see cref="Place"/> moves it, <see cref="On"/> switches it.
/// </summary>
[PortOf("eng:Render.SpotShadow", Spec = "docs/RENDERING.md#spot-shadow-lights")]
public sealed partial class SpotShadowLight : IDisposable
{
    public const string ParamSpot = "svm_spot";
    public const string ParamColour = "svm_spot_colour";
    public const string ParamView = "svm_spot_view";
    public const string ParamFrustum = "svm_spot_frustum";
    public const string ParamPosition = "svm_spot_pos";
    public const string ParamDepth = "svm_spot_depth";
    public const string ParamCookie = "svm_spot_cookie";
    public const string ParamAttenuation = "svm_spot_atten";

    /// <summary>The parameter names Push writes every frame, made once (a name passed as a string makes a StringName per call).</summary>
    private static readonly (StringName Spot, StringName Colour, StringName View, StringName Frustum, StringName Position, StringName Depth, StringName Cookie) s_names =
        (ParamSpot, ParamColour, ParamView, ParamFrustum, ParamPosition, ParamDepth, ParamCookie);

    private static bool s_globalsAdded;

    /// <summary>The depth slot's empty texture (no spot shadow light draws).</summary>
    private static ImageTexture? s_noDepth;

    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly SpotDepthCapture _capture;
    private readonly Texture2Drd _depth = new();
    private readonly Texture2D? _cookie;
    private readonly Vector3 _colour;
    private bool _on;

    public SpotShadowLight(Node parent, RenderPack? pack, SpotShadowShape shape)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(shape);
        Shape = shape;
        EnsureGlobals(pack);
        int height = RenderK.SpotShadowMapSize;
        int width = Math.Max(1, (int)MathF.Round(height * shape.SpotWidth / MathF.Max(1f, shape.SpotHeight)));
        float near = UeSpace.LengthToGodot(MathF.Max(1f, shape.Range * shape.NearPlaneRatio));
        _capture = new SpotDepthCapture(new Vector2I(width, height), near, UeSpace.LengthToGodot(shape.Range));
        _viewport = new SubViewport
        {
            Name = "SpotShadow",
            Size = new Vector2I(width, height),
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _camera = new Camera3D
        {
            Name = "LightCamera",
            Projection = Camera3D.ProjectionType.Perspective,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Fov = Mathf.RadToDeg(2f * MathF.Atan(shape.SpotHeight * 0.5f / shape.Range)),
            Near = near,
            Far = UeSpace.LengthToGodot(shape.Range),
            CullMask = RenderLayers.World,
            Environment = FaithfulView.CreateEnvironment(),
            Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _capture } },
            Current = true,
        };
        _viewport.AddChild(_camera);
        parent.AddChild(_viewport);
        _cookie = pack?.Texture(shape.Cookie);
        _colour = Colour(shape) * RenderK.SpotShadowGain;
    }

    public SpotShadowShape Shape { get; }

    /// <summary>Where the light is and where it points (UE), as last placed.</summary>
    public (FVector Location, FRotator Rotation) Placement { get; private set; }

    /// <summary>The light's shadow viewport (rendered only while <see cref="On"/>).</summary>
    public SubViewport Viewport => _viewport;

    /// <summary>LightType != LT_None: the shadow pass runs and the template adds the light.</summary>
    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            _viewport.RenderTargetUpdateMode = value ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            Push();
        }
    }

    /// <summary>The light colour of the 2x stage: FGetHSV(hue, saturation) x brightness / 255.</summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0 (as actor lighting)")]
    public static Vector3 Colour(SpotShadowShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var entry = new LightEntry("spot", default, default, shape.Brightness, shape.Hue, shape.Saturation, 0f, 0, RenderK.LE_ESpotShadow,
            1, true, false, true, true, null, 32f, 0f, false);
        return ActorLighting.Colour(entry);
    }

    /// <summary>Moves the light to a UE location and rotation (its axis is the rotation's forward).</summary>
    public void Place(FVector location, FRotator rotation)
    {
        Placement = (location, rotation);
        _camera.Transform = FaithfulView.CameraTransform(location, rotation);
        Push();
    }

    // The cookie the global slot holds now (the slot is shared by every spot light).
    private static Texture2D? s_cookieSet;

    /// <summary>Writes the light into the template's spot slot (or clears it when off).</summary>
    public void Push()
    {
        if (_capture.Texture.IsValid && _depth.TextureRdRid != _capture.Texture)
        {
            _depth.TextureRdRid = _capture.Texture;
            RenderingServer.GlobalShaderParameterSet(s_names.Depth, _depth);
        }

        bool live = _on && _capture.Texture.IsValid;
        float range = UeSpace.LengthToGodot(Shape.Range);
        if (!_camera.IsInsideTree())
        {
            // Off the tree (the view is leaving the session): the slot is cleared; there is no transform to place.
            RenderingServer.GlobalShaderParameterSet(s_names.Spot, new Vector4(0f, range, Shape.NormalInfluence, Shape.Attenuation));
            return;
        }

        RenderingServer.GlobalShaderParameterSet(s_names.Spot, new Vector4(live ? 1f : 0f, range, Shape.NormalInfluence, Shape.Attenuation));
        RenderingServer.GlobalShaderParameterSet(s_names.Colour, new Vector4(_colour.X, _colour.Y, _colour.Z, _camera.Near));
        Transform3D view = _camera.GlobalTransform.AffineInverse();
        using (var projection = Variant.From(new Projection(view)))
        {
            // A projection variant lives on the heap: disposed now rather than by its finalizer.
            RenderingServer.GlobalShaderParameterSet(s_names.View, projection);
        }
        float tanW = Shape.SpotWidth * 0.5f / Shape.Range;
        float tanH = Shape.SpotHeight * 0.5f / Shape.Range;
        RenderingServer.GlobalShaderParameterSet(s_names.Frustum, new Vector4(1f / tanW, 1f / tanH, UeSpace.LengthToGodot(2f), 0.01f));
        Vector3 p = _camera.GlobalPosition;
        RenderingServer.GlobalShaderParameterSet(s_names.Position, new Vector4(p.X, p.Y, p.Z, 0f));
        if (_cookie is not null && !ReferenceEquals(s_cookieSet, _cookie))
        {
            // Set when it changes: an object variant is a finalizable wrapper per call.
            s_cookieSet = _cookie;
            RenderingServer.GlobalShaderParameterSet(s_names.Cookie, _cookie);
        }
    }

    /// <summary>Registers the template's spot slot (once per process), with the attenuation ramps of the pack.</summary>
    public static void EnsureGlobals(RenderPack? pack = null)
    {
        if (!s_globalsAdded)
        {
            Image black = Image.CreateEmpty(1, 1, false, Image.Format.Rf);
            Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            white.Fill(new Color(1f, 1f, 1f, 1f));
            RenderingServer.GlobalShaderParameterAdd(ParamSpot, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
            RenderingServer.GlobalShaderParameterAdd(ParamColour, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
            RenderingServer.GlobalShaderParameterAdd(ParamView, RenderingServer.GlobalShaderParameterType.Mat4, Projection.Identity);
            RenderingServer.GlobalShaderParameterAdd(ParamFrustum, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.One);
            RenderingServer.GlobalShaderParameterAdd(ParamPosition, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
            s_noDepth = ImageTexture.CreateFromImage(black);
            RenderingServer.GlobalShaderParameterAdd(ParamDepth, RenderingServer.GlobalShaderParameterType.Sampler2D, s_noDepth);
            RenderingServer.GlobalShaderParameterAdd(ParamCookie, RenderingServer.GlobalShaderParameterType.Sampler2D, ImageTexture.CreateFromImage(white));
            RenderingServer.GlobalShaderParameterAdd(ParamAttenuation, RenderingServer.GlobalShaderParameterType.Sampler2D, Ramps(null));
            s_globalsAdded = true;
        }

        if (pack is not null)
        {
            RenderingServer.GlobalShaderParameterSet(ParamAttenuation, Ramps(pack));
        }
    }

    /// <summary>
    /// The Echelon attenuation ramps as one 16 x 2 texture: row 0 Engine.linear_attenuation, row 1
    /// Engine.late_attenuation (16 texels along the light's range), or analytic stand-ins without the pack.
    /// </summary>
    private static ImageTexture Ramps(RenderPack? pack)
    {
        Image ramps = Image.CreateEmpty(16, 2, false, Image.Format.Rgba8);
        Image? linear = pack?.Image(RenderPack.LinearAttenuation);
        Image? late = pack?.Image(RenderPack.LateAttenuation);
        for (int x = 0; x < 16; x++)
        {
            float t = x / 15f;
            float l = linear is not null && linear.GetWidth() == 16 ? linear.GetPixel(x, 0).R : 1f - t;
            float k = late is not null && late.GetWidth() == 16 ? late.GetPixel(x, 0).R : (t < 0.9f ? 1f : 0f);
            ramps.SetPixel(x, 0, new Color(l, l, l, 1f));
            ramps.SetPixel(x, 1, new Color(k, k, k, 1f));
        }

        return ImageTexture.CreateFromImage(ramps);
    }

    public void Dispose()
    {
        On = false;
        _viewport.QueueFree();
        // The template must stop sampling the depth copy before it goes.
        if (_depth.TextureRdRid == _capture.Texture && s_noDepth is not null)
        {
            RenderingServer.GlobalShaderParameterSet(ParamDepth, s_noDepth);
        }

        // The effect is a resource the managed side keeps alive: without the garbage collector running before the
        // process quits its predelete never comes, so its render device objects are freed here.
        _capture.Release();
    }

    /// <summary>
    /// The light's depth copy: after its camera's opaque pass, the depth buffer becomes linear distance along the axis in
    /// an R32F texture the template samples.
    /// </summary>
    private sealed partial class SpotDepthCapture : CompositorEffect
    {
        public const string ShaderPath = "res://Shaders/svm_spot_depth.glsl";

        private readonly Vector2I _size;
        private readonly float _near;
        private readonly float _far;
        private RenderingDevice? _rd;
        private Rid _shader;
        private Rid _pipeline;
        private Rid _sampler;

        public SpotDepthCapture(Vector2I size, float near, float far)
        {
            _size = size;
            _near = near;
            _far = far;
            EffectCallbackType = EffectCallbackTypeEnum.PostOpaque;
            AccessResolvedDepth = true;
            RenderingServer.CallOnRenderThread(Callable.From(Init));
        }

        /// <summary>The R32F distance texture (render device RID), valid once the render thread made it.</summary>
        public Rid Texture { get; private set; }

        private volatile bool _released;

        private void Init()
        {
            if (_released)
            {
                return;
            }

            _rd = RenderingServer.GetRenderingDevice();
            if (_rd is null)
            {
                return;
            }

            _shader = ComputeShaderSource.CreateShader(_rd, ShaderPath, "svm_spot_depth", out string? error);
            if (!_shader.IsValid)
            {
                GD.PushError("svm_spot_depth: " + error);
                return;
            }

            _pipeline = _rd.ComputePipelineCreate(_shader);
            _sampler = _rd.SamplerCreate(new RDSamplerState
            {
                MinFilter = RenderingDevice.SamplerFilter.Nearest,
                MagFilter = RenderingDevice.SamplerFilter.Nearest,
            });
            Texture = _rd.TextureCreate(new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R32Sfloat,
                Width = (uint)_size.X,
                Height = (uint)_size.Y,
                UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
            }, new RDTextureView());
        }

        /// <summary>Frees the render device objects on the render thread (once); the effect draws nothing after.</summary>
        public void Release()
        {
            _released = true;
            if (_rd is not { } rd)
            {
                return;
            }

            Rid[] rids = [_pipeline, _shader, _sampler, Texture];
            _rd = null;
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                foreach (Rid rid in rids)
                {
                    if (rid.IsValid)
                    {
                        rd.FreeRid(rid);
                    }
                }
            }));
        }

        public override void _Notification(int what)
        {
            if (what == NotificationPredelete)
            {
                Release();
            }

            base._Notification(what);
        }

        private Post.UniformSlots? _slots;
        private readonly byte[] _push = new byte[16];

        public override void _RenderCallback(int effectCallbackType, RenderData renderData)
        {
            // Release the scene buffers' managed wrapper here, on the render thread. Left to the garbage collector it can
            // hold the last reference to a freed viewport's buffers, whose destructor then frees GPU resources on the
            // finalizer thread (free_rid errors) and crashes the process at exit after a play session was left.
            // The frame's render data gets a new managed wrapper every call: released here it is not left to the finalizer
            // (a finalizable object and a tracked handle per frame for the garbage collector).
            using RenderData? frameData = renderData;
            using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
            if (_rd is null || !_pipeline.IsValid || sceneBuffers is not RenderSceneBuffersRD buffers)
            {
                return;
            }

            Vector2I size = buffers.GetInternalSize();
            if (size != _size)
            {
                return;
            }

            // The uniform list and the constant bytes are kept from frame to frame (no wrappers made per frame).
            Post.UniformSlots slots = _slots ??= new Post.UniformSlots(RenderingDevice.UniformType.SamplerWithTexture,
                RenderingDevice.UniformType.Image);
            slots.Set(0, _sampler, buffers.GetDepthLayer(0));
            slots.Set(1, Texture);
            Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
            Span<float> push = [_near, _far, size.X, size.Y];
            MemoryMarshal.AsBytes(push).CopyTo(_push);
            byte[] bytes = _push;
            long list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, set, 0);
            _rd.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
            _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
            _rd.ComputeListEnd();
        }
    }
}
