using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

/// <summary>
/// Uploads authoritative smoke extinction and coordinates visual flow/lighting. Surface materials integrate
/// the atlas at their own fragment depth, so glass, cloaks and particles compose like ordinary fog.
/// Camera entries use SurfaceFogRegistry's slots; a security camera cannot borrow the player's origin or mode.
/// </summary>
public static class ResponsiveSmokeRenderer
{
    /// <summary>Temporary editor/command-line preference; never saved into the player's display settings.</summary>
    public static ResponsiveSmokeQuality? LaunchQuality { get; set; }
    public static ResponsiveSmokeQuality Quality => LaunchQuality ?? SettingsStore.Current.SmokeQuality;
    public static bool SceneShadows { get; set; }
    private static bool s_sceneShadows;
    private const int Width = 1024;
    private const int ViewRows = 14;
    private const int CloudRows = 14;
    internal const int SceneShadowGrid = 24, SceneShadowCapacity = 8, SceneShadowRows = (SceneShadowGrid + 7) / 8;
    private sealed record SceneShadowStamp(float Time, ulong Density, ulong Lights, int Offset);
    private static readonly Dictionary<object, SceneShadowStamp> SceneHistory = new(ReferenceEqualityComparer.Instance);
    private static readonly ConditionalWeakTable<RenderMap, WorldBinding> Maps = new();
    private static readonly ViewBinding?[] Views = new ViewBinding?[SurfaceFogRegistry.SlotCount];
    private static readonly List<float> FieldData = [];
    private static readonly List<float> FrameData = [];
    private static readonly Dictionary<object, FieldBinding> Fields = new(ReferenceEqualityComparer.Instance);
    private static readonly List<SmokeRenderCloud> Live = [];
    private static readonly List<SmokeFlowInput> FlowInputs = [];
    private static ImageTexture? s_empty, s_fields, s_frame, s_growth, s_probes;
    private static ImageTexture3D? s_shape;
    private static byte[] s_fieldBytes = [], s_frameBytes = [], s_growthBytes = [];
    private static readonly AtlasPool FieldPool = new(Width, 16, 256, Image.Format.Rgbaf, 16);
    private static readonly AtlasPool FramePool = new(Width, 4, 64, Image.Format.Rgbaf, 16);
    private static readonly AtlasPool GrowthPool = new(675, 27, 216, Image.Format.Rgf, 8);
    private static readonly AtlasPool ProbePool = new(6, 4, 64, Image.Format.Rgbaf, 16);
    private static bool s_initialized;
    private static bool s_hadClouds;
    private static bool s_active;
    private static readonly SmokeTemporalHistory History = new();
    private static readonly SmokeGrowthCache GrowthCache = new();
    private static readonly bool Profile = System.Environment.GetEnvironmentVariable("SVM_RESPONSIVE_SMOKE_PROFILE") == "1";

    private sealed class WorldBinding(SmokeRenderFrame frame) { public SmokeRenderFrame Frame = frame; }
    private sealed record ViewBinding(Camera3D Camera, RenderMap? Map, SmokeRenderFrame? ExplicitFrame, int Mode);
    private sealed class FieldBinding((uint, uint) stamp) { public (uint, uint) ContentStamp = stamp; public int Address; }

    // Keep a common one-cloud upload size and one larger size, rather than uploading an eight-cloud reserve
    // for every single grenade. Rare exceptionally large packets are released at the next idle boundary.
    private sealed class AtlasPool(int width, int baseHeight, int retainedHeight, Image.Format format, int bytesPerPixel)
    {
        internal sealed class Entry(ImageTexture texture, byte[] bytes, int height)
        { public readonly ImageTexture Texture = texture; public readonly byte[] Bytes = bytes; public readonly int Height = height; }
        private Entry? _base, _large;
        public long Bytes => (_base?.Bytes.Length ?? 0L) + (_large?.Bytes.Length ?? 0L);
        public Entry Get(int neededHeight)
        {
            if (neededHeight <= baseHeight) return _base ??= Create(baseHeight);
            if (_large is { } existing && existing.Height >= neededHeight) return existing;
            int height = baseHeight;
            while (height < neededHeight) height *= 2;
            _large?.Texture.Dispose();
            return _large = Create(height);
        }
        private Entry Create(int height)
        {
            var bytes = new byte[checked(width * height * bytesPerPixel)];
            using Image image = Image.CreateFromData(width,height,false,format,bytes);
            FieldAllocationCount++;
            return new(ImageTexture.CreateFromImage(image),bytes,height);
        }
        public void TrimIdle()
        { if (_large is { Height: var height } && height > retainedHeight) { _large.Texture.Dispose(); _large = null; } }
        public void Release()
        { _base?.Texture.Dispose(); _large?.Texture.Dispose(); _base = _large = null; }
    }

    public static int CloudsDrawn { get; private set; }
    public static int LastFieldUploadBytes { get; private set; }
    public static int LastFrameUploadBytes { get; private set; }
    public static int LastGrowthUploadBytes { get; private set; }
    public static long GrowthUploadCount { get; private set; }
    public static double LastUploadMilliseconds { get; private set; }
    public static double LastFlowQueueMilliseconds { get; private set; }
    public static double LastFieldMilliseconds { get; private set; }
    public static double LastGrowthMilliseconds { get; private set; }
    public static double LastLightingMilliseconds { get; private set; }
    public static double LastFramePackingMilliseconds { get; private set; }
    public static double LastFrameUploadMilliseconds { get; private set; }
    public static double LastPrefixQueueMilliseconds { get; private set; }
    public static double LastProfiledMilliseconds => LastFlowQueueMilliseconds+LastFieldMilliseconds+LastGrowthMilliseconds
        +LastLightingMilliseconds+LastFramePackingMilliseconds+LastFrameUploadMilliseconds+LastPrefixQueueMilliseconds;
    /// <summary>Cumulative field/shape texture creations or capacity changes; ordinary updates do not count.</summary>
    public static long FieldAllocationCount { get; private set; }
    public static int LastBoundViewCount { get; private set; }
    /// <summary>Current cameras whose viewport is not explicitly disabled/hidden; WhenVisible may still be unsampled.</summary>
    public static int LastActiveViewCount { get; private set; }
    public static int LastCacheViewCount { get; private set; }
    /// <summary>Sum of dispatched view-grid width x height x depth slices, not inner integration steps.</summary>
    public static long LastPrefixVoxelCount { get; private set; }
    public static double LastVolumeGpuMilliseconds => ResponsiveSmokeVolumeCache.LastGpuMilliseconds + ResponsiveSmokeVisualFlow.LastGpuMilliseconds;
    public static long AllocatedFieldBytes => FieldPool.Bytes+FramePool.Bytes+GrowthPool.Bytes+ProbePool.Bytes+ResponsiveSmokeVisualFlow.AllocatedBytes
        + (s_shape is null ? 0 : ResponsiveSmokeNoise.Dimension*ResponsiveSmokeNoise.Dimension*ResponsiveSmokeNoise.Dimension);
    public static string? VolumeError => ResponsiveSmokeVolumeCache.Error ?? ResponsiveSmokeVisualFlow.Error;
    public static long HistoryAcceptedViews => History.AcceptedViews;
    public static long HistoryRejectedViews => History.RejectedViews;
    internal static bool IsPrepared => s_shape is not null && ResponsiveSmokeVisualFlow.AllocatedBytes != 0
        && ResponsiveSmokeVolumeCache.AllocatedBytes != 0;
    internal static bool Preparing { get; set; }

    /// <summary>Availability is explicit: clients must not silently substitute legacy smoke in a responsive match.</summary>
    public static string? UnsupportedReason => DisplayServer.GetName() == "headless" ? null
        : RenderingServer.GetCurrentRenderingMethod() != "forward_plus" ? "Responsive smoke requires the Forward+ renderer."
        : RenderingServer.GetCurrentRenderingDriverName() is not ("vulkan" or "d3d12")
            ? "Responsive smoke requires the Vulkan or Direct3D 12 rendering driver."
        : VolumeError is { } error ? "Responsive smoke could not initialize: " + error : null;

    public static void EnsureGlobals()
    {
        if (s_initialized) return;
        s_initialized = true;
        ResponsiveSmokeVisualFlow.EnsureGlobals();
        SpotShadowLight.EnsureGlobals();
        using var empty = Image.CreateEmpty(1, 1, false, Image.Format.Rgbaf);
        empty.Fill(Colors.Transparent);
        s_empty = ImageTexture.CreateFromImage(empty);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_fields", RenderingServer.GlobalShaderParameterType.Sampler2D, s_empty);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_frame", RenderingServer.GlobalShaderParameterType.Sampler2D, s_empty);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_growth", RenderingServer.GlobalShaderParameterType.Sampler2D, s_empty);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_probes", RenderingServer.GlobalShaderParameterType.Sampler2D, s_empty);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_shape", RenderingServer.GlobalShaderParameterType.Sampler3D, ResponsiveSmokeVisualFlow.EmptyTexture);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_prefix", RenderingServer.GlobalShaderParameterType.Sampler2D, s_empty);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_active", RenderingServer.GlobalShaderParameterType.Bool, false);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_scene_shadows", RenderingServer.GlobalShaderParameterType.Bool, false);
        RenderingServer.GlobalShaderParameterAdd("svm_smoke_prefix_ready", RenderingServer.GlobalShaderParameterType.Bool, false);
        RenderingServer.FramePreDraw += Flush;
    }

    public static void Prewarm(Level level)
    {
        if (!ResponsiveSmokeWorld.IsEnabled(level) || DisplayServer.GetName() == "headless") return;
        EnsureGlobals();
        ResponsiveSmokeWorld.Warmup(level);
        ResponsiveSmokeVisualFlow.Prewarm();
        ResponsiveSmokeVolumeCache.Prewarm();
    }

    private static void AllocateFields()
    {
        if (s_fields is not null) return;
        s_fields = FieldPool.Get(1).Texture;
        s_frame = FramePool.Get(1).Texture;
        s_growth = GrowthPool.Get(1).Texture;
        s_probes = ProbePool.Get(1).Texture;
        int side = ResponsiveSmokeNoise.Dimension;
        var slices = new Godot.Collections.Array<Image>();
        for (int z = 0; z < side; z++)
            slices.Add(Image.CreateFromData(side,side,false,Image.Format.R8,ResponsiveSmokeNoise.Lattice.Slice(z*side*side,side*side).ToArray()));
        s_shape = new ImageTexture3D();
        Error created = s_shape.Create(Image.Format.R8,side,side,side,false,slices);
        foreach (Image slice in slices) slice.Dispose();
        if (created != Error.Ok) throw new InvalidOperationException($"Could not allocate responsive smoke shape texture: {created}");
        FieldAllocationCount++;
        RenderingServer.GlobalShaderParameterSet("svm_smoke_fields",s_fields);
        RenderingServer.GlobalShaderParameterSet("svm_smoke_frame",s_frame);
        RenderingServer.GlobalShaderParameterSet("svm_smoke_growth",s_growth);
        RenderingServer.GlobalShaderParameterSet("svm_smoke_probes",s_probes);
        RenderingServer.GlobalShaderParameterSet("svm_smoke_shape",s_shape);
    }

    private static void ReleaseFields()
    {
        SetActive(false);
        foreach (string name in new[] { "svm_smoke_fields", "svm_smoke_frame", "svm_smoke_growth", "svm_smoke_prefix", "svm_smoke_probes" })
            RenderingServer.GlobalShaderParameterSet(name,s_empty!);
        ResponsiveSmokeVolumeCache.Release();
        RenderingServer.GlobalShaderParameterSet("svm_smoke_shape",ResponsiveSmokeVisualFlow.EmptyTexture);
        ResponsiveSmokeVisualFlow.Shutdown();
        FieldPool.Release(); FramePool.Release(); GrowthPool.Release(); ProbePool.Release();
        s_shape?.Dispose(); s_shape = null;
        s_fields = s_frame = s_growth = s_probes = null;
        Fields.Clear(); FieldData.Clear(); FrameData.Clear(); FlowInputs.Clear(); SceneHistory.Clear();
        History.Clear();
        GrowthCache.Clear();
        s_fieldBytes = s_frameBytes = s_growthBytes = [];
    }

    private static void IdleFields()
    {
        SetActive(false);
        ResponsiveSmokeVolumeCache.Idle();
        ResponsiveSmokeVisualFlow.Idle();
        Fields.Clear(); FieldData.Clear(); FrameData.Clear(); FlowInputs.Clear(); History.Clear(); SceneHistory.Clear();
        GrowthCache.Clear();
        FieldPool.TrimIdle(); FramePool.TrimIdle(); GrowthPool.TrimIdle(); ProbePool.TrimIdle();
        // Rebind the persistent small uploads after trimming a rare oversized packet.
        BindTexture(ref s_fields,FieldPool.Get(1).Texture,"svm_smoke_fields");
        BindTexture(ref s_frame,FramePool.Get(1).Texture,"svm_smoke_frame");
        BindTexture(ref s_growth,GrowthPool.Get(1).Texture,"svm_smoke_growth");
        BindTexture(ref s_probes,ProbePool.Get(1).Texture,"svm_smoke_probes");
        s_fieldBytes = s_frameBytes = s_growthBytes = [];
    }

    private static void SetActive(bool active)
    {
        bool shadows = active && SceneShadows;
        if (s_sceneShadows != shadows)
        {
            s_sceneShadows = shadows;
            RenderingServer.GlobalShaderParameterSet("svm_smoke_scene_shadows", shadows);
        }
        if (s_active == active) return;
        s_active = active; RenderingServer.GlobalShaderParameterSet("svm_smoke_active",active);
    }

    private static void BindTexture(ref ImageTexture? current, ImageTexture next, string parameter)
    {
        if (ReferenceEquals(current,next)) return;
        current = next; RenderingServer.GlobalShaderParameterSet(parameter,next);
    }

    public static void Publish(RenderMap? map, Level level)
    {
        if (map is null) return;
        SmokeRenderFrame frame = SmokeRenderCache.Capture(level);
        if (Maps.TryGetValue(map, out WorldBinding? binding)) binding.Frame = frame;
        else Maps.Add(map, new WorldBinding(frame));
    }

    public static void BindView(int slot, Camera3D camera, RenderMap? map, int mode, Level? explicitLevel = null)
    {
        if (slot <= 0 || slot >= Views.Length - 1) return;
        EnsureGlobals();
        // Fixtures without a map publish here; normal gameplay publishes once after each client tick.
        SmokeRenderFrame? frame = explicitLevel is null ? null : SmokeRenderCache.Capture(explicitLevel);
        if (frame is null && Views[slot] is { ExplicitFrame: null } previous && previous.Camera == camera && previous.Map == map
            && previous.Mode == mode) return;
        Views[slot] = new(camera, map, frame, mode);
    }

    internal static void BindWarmupView(int slot, Camera3D camera, SmokeRenderFrame frame)
    {
        EnsureGlobals();
        Views[slot] = new(camera,null,frame,RenderK.RendMapNormal);
    }

    public static bool HasSmoke(RenderMap? map) => map is not null && Maps.TryGetValue(map, out WorldBinding? binding)
        && binding.Frame.Enabled && binding.Frame.Clouds.Count != 0;

    /// <summary>Only draws where no opaque surface wrote depth, after the sky backdrop and before other transparency.</summary>
    public static MeshInstance3D CreateBackground(Camera3D camera)
    {
        EnsureGlobals();
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/svm_smoke_background.gdshader"), RenderPriority = -127 };
        var mesh = new MeshInstance3D { Name = "ResponsiveSmokeBackground", Mesh = new QuadMesh { Size = new(2, 2) },
            MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, IgnoreOcclusionCulling = true };
        camera.AddChild(mesh);
        return mesh;
    }

    public static void ReleaseView(int slot)
    {
        if ((uint)slot < Views.Length) Views[slot] = null;
        History.Remove(slot);
        if (Array.TrueForAll(Views, static view => view is null))
        {
            if (s_fields is not null) { ReleaseFields(); s_hadClouds = false; }
            ResponsiveSmokeVolumeCache.Release();
            ResponsiveSmokeVisualFlow.Shutdown();
        }
    }

    private static SmokeRenderFrame? FrameOf(ViewBinding view) => view.ExplicitFrame
        ?? (view.Map is not null && Maps.TryGetValue(view.Map, out WorldBinding? binding) ? binding.Frame : null);

    private static int LayerOf(SmokeRenderCloud field) => Live.FindIndex(cloud => ReferenceEquals(cloud.Identity, field.Identity));

    private static void Add(List<float> values, float x, float y, float z, float w)
    { values.Add(x); values.Add(y); values.Add(z); values.Add(w); }
    private static void Add(List<float> values, Vector4 v) => Add(values, v.X, v.Y, v.Z, v.W);
    private static void Set(List<float> values, int row, float x, float y, float z, float w)
    { int p = row * 4; values[p] = x; values[p + 1] = y; values[p + 2] = z; values[p + 3] = w; }
    private static void Pad(List<float> values) { while ((values.Count & 3) != 0) values.Add(0); }

    /// <summary>Queues the real draw path for a teardown race regression before the next frame is presented.</summary>
    internal static void FlushForDiagnostics() => Flush();

    internal static string DescribeViews()
    {
        var descriptions = new List<string>();
        for (int slot = 1; slot < Views.Length - 1; slot++)
        {
            if (Views[slot] is not { } view || !GodotObject.IsInstanceValid(view.Camera)) continue;
            Viewport? viewport = view.Camera.GetViewport();
            string activity = viewport is SubViewport sub ? sub.RenderTargetUpdateMode.ToString()
                : viewport is Window window ? window.Visible ? "Visible" : "Hidden" : "None";
            descriptions.Add($"slot={slot},map={view.Map?.MapId ?? "fixture"},camera={view.Camera.Name},current={view.Camera.Current},viewport={viewport?.Name},update={activity},size={viewport?.GetVisibleRect().Size},mode={view.Mode},clouds={FrameOf(view)?.Clouds.Count ?? 0}");
        }
        return string.Join("; ", descriptions);
    }

    private static bool ViewAppearsActive(ViewBinding view)
    {
        if (!view.Camera.Current || !view.Camera.IsInsideTree()) return false;
        return view.Camera.GetViewport() switch
        {
            SubViewport sub => sub.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled,
            Window window => window.Visible,
            _ => false,
        };
    }

    private static void Flush()
    {
        // The play scene is explicitly covered by its loading UI until the private warm view is ready.
        // Do not make this frame perform the cold lattice initialization on the presentation thread.
        if (Preparing && s_shape is null) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Live.Clear();
        FlowInputs.Clear();
        LastBoundViewCount = LastActiveViewCount = LastCacheViewCount = 0;
        LastPrefixVoxelCount = 0;
        LastGrowthUploadBytes = 0;
        LastFlowQueueMilliseconds = LastFieldMilliseconds = LastGrowthMilliseconds = LastLightingMilliseconds
            = LastFramePackingMilliseconds = LastFrameUploadMilliseconds = LastPrefixQueueMilliseconds = 0;
        bool changed = false;
        bool enabled = false;
        foreach (ViewBinding? view in Views)
        {
            if (view is null || !GodotObject.IsInstanceValid(view.Camera)) continue;
            LastBoundViewCount++;
            if (ViewAppearsActive(view)) LastActiveViewCount++;
            if (FrameOf(view) is not { Enabled: true } frame) continue;
            enabled = true;
            foreach (SmokeRenderCloud field in frame.Clouds)
            {
                if (LayerOf(field) >= 0) continue;
                Live.Add(field);
                FlowInputs.Add(new(field,frame.CurrentTime));
                if (!Fields.TryGetValue(field.Identity, out FieldBinding? old) || old.ContentStamp != field.ContentStamp) changed = true;
            }
        }
        changed |= Live.Count != Fields.Count;
        CloudsDrawn = Live.Count;
        LastFieldUploadBytes = 0;
        if (Live.Count == 0)
        {
            if (!enabled && s_fields is not null) ReleaseFields();
            else if (s_hadClouds) IdleFields();
            s_hadClouds = false;
            LastFrameUploadBytes = 0; LastUploadMilliseconds = 0;
            return;
        }
        s_hadClouds = true;
        long stage = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ResponsiveSmokeVisualFlow.Queue(FlowInputs,Quality);
        if (Profile) { LastFlowQueueMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds; stage = System.Diagnostics.Stopwatch.GetTimestamp(); }
        AllocateFields();
        SetActive(true);
        if (changed)
        {
            Fields.Clear(); FieldData.Clear();
            // Address zero is deliberately empty, so a retired/corrupt index never points into a real field.
            Add(FieldData, 0, 0, 0, 0);
            foreach (SmokeRenderCloud field in Live)
            {
                var binding = new FieldBinding(field.ContentStamp) { Address = FieldData.Count / 4 };
                Fields.Add(field.Identity, binding);
                PackField(field, Fields.Count - 1);
            }
            LastFieldUploadBytes = Upload(FieldPool, ref s_fields, FieldData, ref s_fieldBytes, "svm_smoke_fields");
        }
        if (Profile) { LastFieldMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds; stage = System.Diagnostics.Stopwatch.GetTimestamp(); }

        FrameData.Clear();
        // Two bilinear samples reconstruct trilinear growth in the shader. This atlas contains exactly the
        // analytic per-cell activation ramp; radial falloff, occupancy and carves still use shared field rules.
        AtlasPool.Entry growthAtlas = GrowthPool.Get(Math.Max(27, Live.Count * 27));
        int growthHeight = growthAtlas.Height;
        BindTexture(ref s_growth,growthAtlas.Texture,"svm_smoke_growth"); s_growthBytes = growthAtlas.Bytes;
        // Keep each layer's owning frame clock. A replay/auxiliary view can have a different time; the last
        // binding of a shared identity retains the same selection order as the original atlas writer.
        foreach (ViewBinding? view in Views)
        {
            if (view is null || FrameOf(view) is not { Enabled: true } frame) continue;
            foreach (SmokeRenderCloud field in frame.Clouds)
            {
                int layer = LayerOf(field);
                if (layer < 0) continue;
                FlowInputs[layer] = new(field,frame.CurrentTime);
            }
        }
        GrowthCache.Begin(Live.Count,growthAtlas,(int)Quality);
        bool uploadGrowth = false;
        for (int layer = 0; layer < FlowInputs.Count; layer++)
            uploadGrowth |= GrowthCache.Update(layer,FlowInputs[layer].Cloud,FlowInputs[layer].CurrentTime);
        if (uploadGrowth)
        {
            s_growthBytes.AsSpan().Clear();
            Span<float> growthValues = MemoryMarshal.Cast<byte, float>(s_growthBytes.AsSpan());
            for (int layer = 0; layer < FlowInputs.Count; layer++)
            {
                SmokeRenderCloud field = FlowInputs[layer].Cloud;
                float age = FlowInputs[layer].CurrentTime - field.CreatedAt;
                ReadOnlySpan<byte> cells = field.Cells;
                ReadOnlySpan<ushort> activation = field.ActivationTicks;
                for (int z = 0; z < 25; z++) for (int y = 0; y < 25; y++) for (int x = 0; x < 25; x++)
                {
                    int index = x + 25 * (y + 25 * z);
                    float value = cells[index] == 0 || activation[index] == ushort.MaxValue ? 0
                        : Math.Clamp((age - activation[index] / ResponsiveSmokeField.TickRate) / ResponsiveSmokeField.GrowthFade, 0, 1);
                    int at = ((layer * 27 + y + 1) * 675 + z * 27 + x + 1) * 2;
                    growthValues[at] = value;
                    growthValues[at + 1] = cells[index] == 0 ? 0 : 1;
                }
            }
            using var growthImage = Image.CreateFromData(675, growthHeight, false, Image.Format.Rgf, s_growthBytes);
            s_growth!.Update(growthImage);
            LastGrowthUploadBytes = s_growthBytes.Length;
            GrowthUploadCount++;
        }
        FlowInputs.Clear();
        if (Profile) { LastGrowthMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds; stage = System.Diagnostics.Stopwatch.GetTimestamp(); }
        for (int i = 0; i < Views.Length * ViewRows * 4; i++) FrameData.Add(0);
        var cacheSlots = new List<int>();
        var probeSets = new List<Vector4[]>();
        var sceneSlots = new List<int>();
        var sceneFields = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (object identity in new List<object>(SceneHistory.Keys))
            if (!Live.Exists(cloud => ReferenceEquals(cloud.Identity, identity))) SceneHistory.Remove(identity);
        // Reserve shared light-transmission tiles before the per-camera fog slices. A cloud's result can be
        // reused by every camera and by receivers outside its box, without tracing at every surface pixel.
        int sceneCount = SceneShadows ? Math.Min(Live.Count, SceneShadowCapacity) : 0;
        int prefixWidth = sceneCount > 0 ? SceneShadowGrid * 8 : 1, prefixHeight = sceneCount * SceneShadowGrid * SceneShadowRows;
        for (int slot = 1; slot < Views.Length - 1; slot++)
        {
            if (Views[slot] is not { } view || !GodotObject.IsInstanceValid(view.Camera) || FrameOf(view) is not { Enabled: true } frame) continue;
            int cloudBase = FrameData.Count / 4, count = 0;
            int firstProbe = probeSets.Count;
            bool hasFlow = false;
            foreach (SmokeRenderCloud field in frame.Clouds)
            {
                if (!Fields.TryGetValue(field.Identity, out FieldBinding? binding)) continue;
                long lightingStart = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                ActorLightSet? light = view.Map?.LightActor(field.Center, 300, 0);
                if (Profile) LastLightingMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(lightingStart).TotalMilliseconds;
                int flowSlot = ResponsiveSmokeVisualFlow.GetSlot(field.Identity);
                hasFlow |= flowSlot >= 0;
                int cloudRow = FrameData.Count / 4;
                Add(FrameData, binding.Address, light?.Count ?? 0, flowSlot + 1, ResponsiveSmokeVisualFlow.GridSize);
                Vector3 ambient = light?.Ambient ?? new Vector3(.35f, .35f, .35f);
                Add(FrameData, ambient.X, ambient.Y, ambient.Z, 0);
                for (int l = 0; l < 4; l++)
                {
                    PackedLight p = light is not null && l < light.Count ? light.Lights[l] : default;
                    Add(FrameData, p.Pos); Add(FrameData, p.Col); Add(FrameData, p.Spot);
                }
                count++;
                if (Profile) lightingStart = System.Diagnostics.Stopwatch.GetTimestamp();
                Vector4[] lighting = ResponsiveSmokeLighting.Capture(view.Map,field);
                if (Profile) LastLightingMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(lightingStart).TotalMilliseconds;
                if (lighting.Length != 0)
                {
                    FrameData[(cloudRow+1)*4+3] = probeSets.Count+1;
                    probeSets.Add(lighting);
                }
            }
            if (count == 0) continue;
            bool sharedLights = !hasFlow && probeSets.Count == firstProbe;
            for (int c = 1; c < count && sharedLights; c++)
                for (int component = 4; component < CloudRows*4; component++)
                    if (FrameData[(cloudBase+c*CloudRows)*4+component] != FrameData[cloudBase*4+component])
                    { sharedLights = false; break; }
            Camera3D camera = view.Camera;
            FVector eye = UeSpace.ToUe(camera.GlobalPosition);
            FVector right = UeSpace.DirectionToUe(camera.GlobalBasis.X);
            FVector up = UeSpace.DirectionToUe(camera.GlobalBasis.Y);
            FVector forward = UeSpace.DirectionToUe(-camera.GlobalBasis.Z);
            int row = slot * ViewRows;
            Set(FrameData, row, cloudBase, count, frame.CurrentTime, (int)Quality);
            Set(FrameData, row + 1, eye.X, eye.Y, eye.Z, view.Mode);
            Set(FrameData, row + 2, right.X, right.Y, right.Z, camera.GetCameraProjection().X.X);
            Set(FrameData, row + 3, up.X, up.Y, up.Z, camera.GetCameraProjection().Y.Y);
            Set(FrameData, row + 4, forward.X, forward.Y, forward.Z, camera.Far * UeSpace.UnitsPerMetre);
            Vector2I pixels = (Vector2I)camera.GetViewport().GetVisibleRect().Size;
            int quality = (int)Quality;
            // Filter scattering at this internal resolution; bullet apertures still resolve at full pixel
            // resolution, and surface-depth correction integrates just the remaining depth interval.
            int scale = quality == 0 ? 12 : 8;
            int gridWidth = Math.Max(1, (pixels.X + scale - 1) / scale), gridHeight = Math.Max(1, (pixels.Y + scale - 1) / scale);
            int slices = quality == 0 ? 24 : quality == 1 ? 32 : 48;
            float near = float.PositiveInfinity, far = 0;
            foreach (SmokeRenderCloud field in frame.Clouds)
                for (int corner = 0; corner < 8; corner++)
                {
                    FVector point = field.Origin + new FVector((corner & 1) == 0 ? 0 : 625,(corner & 2) == 0 ? 0 : 625,(corner & 4) == 0 ? 0 : 625);
                    float depth = (point - eye) | forward;
                    near = Math.Min(near, depth); far = Math.Max(far, depth);
                }
            near = Math.Max(0, near); far = Math.Min(camera.Far * 100, far);
            Set(FrameData, row + 5, prefixHeight, gridWidth, gridHeight, slices);
            Set(FrameData, row + 6, near, Math.Max(near + 1, far), sharedLights ? 2 : 1, 0);
            ulong geometry = 14695981039346656037UL;
            foreach (SmokeRenderCloud field in frame.Clouds)
            {
                geometry = (geometry ^ (uint)RuntimeHelpers.GetHashCode(field.Identity)) * 1099511628211UL;
                geometry = (geometry ^ field.SnapshotRevision) * 1099511628211UL;
            }
            if (SceneShadows && view.Mode is not (36 or 38))
            {
                ulong density = geometry;
                foreach (SmokeRenderCloud cloud in frame.Clouds) density = (density ^ cloud.Revision) * 1099511628211UL;
                for (int c = 0; c < frame.Clouds.Count; c++)
                {
                    SmokeRenderCloud cloud = frame.Clouds[c];
                    int layer = LayerOf(cloud);
                    if (layer < 0 || layer >= SceneShadowCapacity || !sceneFields.Add(cloud.Identity)) continue;
                    int offset = layer * SceneShadowGrid * SceneShadowRows;
                    ulong lights = 14695981039346656037UL;
                    int lightRow = cloudBase + c * CloudRows;
                    lights ^= (uint)FrameData[lightRow*4+1];
                    for (int light = 0; light < (int)FrameData[lightRow*4+1]; light++)
                        for (int component = 0; component < 4; component++)
                        {
                            float value = FrameData[(lightRow+2+light*3)*4+component];
                            if (component == 3) value = value < 0 ? -1 : 1;
                            lights = (lights ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL;
                        }
                    bool update = !SceneHistory.TryGetValue(cloud.Identity, out var old) || old.Density != density
                        || old.Lights != lights || old.Offset != offset || frame.CurrentTime < old.Time || frame.CurrentTime - old.Time >= 1f / 30;
                    if (update) SceneHistory[cloud.Identity] = new(frame.CurrentTime, density, lights, offset);
                    sceneSlots.Add(slot); sceneSlots.Add(c); sceneSlots.Add(update ? 1 : 0);
                }
            }
            var temporal = new SmokeTemporalView(camera.GetInstanceId(), eye, right, up, forward,
                camera.GetCameraProjection().X.X, camera.GetCameraProjection().Y.Y, frame.CurrentTime, near, Math.Max(near + 1, far),
                prefixHeight, gridWidth, gridHeight, slices, view.Mode, quality, geometry);
            bool history = History.Update(slot, temporal, out SmokeTemporalView previous);
            Set(FrameData, row + 8, previous.Eye.X, previous.Eye.Y, previous.Eye.Z, history ? 1 : 0);
            Set(FrameData, row + 9, previous.Right.X, previous.Right.Y, previous.Right.Z, previous.ProjectionX);
            Set(FrameData, row + 10, previous.Up.X, previous.Up.Y, previous.Up.Z, previous.ProjectionY);
            Set(FrameData, row + 11, previous.Forward.X, previous.Forward.Y, previous.Forward.Z, previous.Time);
            Set(FrameData, row + 12, previous.Offset, previous.Width, previous.Height, previous.Slices);
            Set(FrameData, row + 13, previous.Near, previous.Far, 0, 0);
            // A shot can cross several clouds. Its analytic footprint is view-wide, not tested once per cloud.
            int bulletBase = FrameData.Count / 4;
            var bullets = new Dictionary<(FVector Start,FVector End,float Radius,float Feather,float CreatedAt,float Hold,float Refill),SmokeRenderCarve>();
            foreach (SmokeRenderCloud field in frame.Clouds)
                foreach (SmokeRenderCarve carve in field.Carves)
                    if (carve.Kind == ResponsiveSmokeCarveKind.Bullet && frame.CurrentTime < carve.CreatedAt + carve.Hold + carve.Refill)
                        bullets.TryAdd((carve.Start,carve.End,carve.Radius,carve.Feather,carve.CreatedAt,carve.Hold,carve.Refill),carve);
            bool sharedBullets = true;
            foreach (SmokeRenderCloud field in frame.Clouds)
            {
                int active = 0;
                var unique = new HashSet<(FVector,FVector,float,float,float,float,float)>();
                foreach (SmokeRenderCarve carve in field.Carves)
                    if (carve.Kind == ResponsiveSmokeCarveKind.Bullet && frame.CurrentTime < carve.CreatedAt + carve.Hold + carve.Refill)
                    {
                        active++;
                        unique.Add((carve.Start,carve.End,carve.Radius,carve.Feather,carve.CreatedAt,carve.Hold,carve.Refill));
                    }
                if (active != bullets.Count || unique.Count != active) { sharedBullets = false; break; }
            }
            // Factoring is valid only for identical received event sets. New clouds and delayed updates retain
            // their individual carve lists; a shot never retroactively clears a newly-created cloud.
            FrameData[(row + 6) * 4 + 3] = sharedBullets ? 1 : 0;
            foreach (SmokeRenderCarve carve in bullets.Values)
            {
                Add(FrameData,carve.Start.X,carve.Start.Y,carve.Start.Z,carve.Radius);
                Add(FrameData,carve.End.X,carve.End.Y,carve.End.Z,carve.Feather);
                float refill = Math.Clamp((frame.CurrentTime-carve.CreatedAt-carve.Hold)/Math.Max(carve.Refill,.001f),0,1);
                float strength = frame.CurrentTime < carve.CreatedAt ? 0 : 1-refill*refill*(3-2*refill);
                FVector segment = carve.End-carve.Start;
                Add(FrameData,strength,1/Math.Max(segment|segment,.00001f),1/Math.Max(carve.Feather,.001f),0);
                Add(FrameData, ProjectCapsule(carve,eye,right,up,forward,camera.GetCameraProjection(),near,far));
            }
            Set(FrameData, row + 7, pixels.X, pixels.Y, bulletBase, bullets.Count);
            if (view.Mode != 38 && far > near)
            {
                cacheSlots.Add(slot); cacheSlots.Add(gridWidth); cacheSlots.Add(gridHeight);
                LastCacheViewCount++;
                LastPrefixVoxelCount += (long)gridWidth*gridHeight*slices;
                prefixWidth = Math.Max(prefixWidth, gridWidth * 8);
                prefixHeight += gridHeight * ((slices + 7) / 8);
            }
        }
        // One 6x4 tile holds ambient, direct/distance and direction as three pairs of bilinear XY planes.
        // Z blends the two planes in the shader; filtering never crosses into an adjacent cloud's tile.
        AtlasPool.Entry probeAtlas = ProbePool.Get(Math.Max(4,probeSets.Count*4));
        probeAtlas.Bytes.AsSpan().Clear();
        Span<float> probeValues = MemoryMarshal.Cast<byte,float>(probeAtlas.Bytes.AsSpan());
        for (int set = 0; set < probeSets.Count; set++) for (int probe = 0; probe < 8; probe++) for (int component = 0; component < 3; component++)
        {
            int at = ((set*4+(probe/4)*2+((probe/2)%2))*6+component*2+probe%2)*4;
            Vector4 value = probeSets[set][probe*3+component];
            probeValues[at] = value.X; probeValues[at+1] = value.Y; probeValues[at+2] = value.Z; probeValues[at+3] = value.W;
        }
        BindTexture(ref s_probes,probeAtlas.Texture,"svm_smoke_probes");
        using (var probeImage = Image.CreateFromData(6,probeAtlas.Height,false,Image.Format.Rgbaf,probeAtlas.Bytes)) s_probes!.Update(probeImage);
        if (Profile) { LastFramePackingMilliseconds = Math.Max(0,System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds-LastLightingMilliseconds); stage = System.Diagnostics.Stopwatch.GetTimestamp(); }
        LastFrameUploadBytes = Upload(FramePool, ref s_frame, FrameData, ref s_frameBytes, "svm_smoke_frame");
        if (Profile) { LastFrameUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds; stage = System.Diagnostics.Stopwatch.GetTimestamp(); }
        ResponsiveSmokeVolumeCache.Queue(s_fields!, s_frame!, s_growth!, s_shape!, s_probes!, cacheSlots.ToArray(), new(prefixWidth,Math.Max(1,prefixHeight)), sceneSlots.ToArray());
        if (Profile) LastPrefixQueueMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        LastUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static Vector4 ProjectCapsule(SmokeRenderCarve carve, FVector eye, FVector right, FVector up, FVector forward,
        Projection projection, float near, float far)
    {
        FVector a = carve.Start-eye, b = carve.End-eye;
        var start = new Vector3(a|right,a|up,a|forward);
        var end = new Vector3(b|right,b|up,b|forward);
        float radius = carve.Radius+carve.Feather+1;
        float lo = Math.Max(.01f,near-radius), hi = far+radius;
        if (Math.Max(start.Z,end.Z) < lo || Math.Min(start.Z,end.Z) > hi) return new(2,2,-1,-1);
        Vector3 delta = end-start;
        if (MathF.Abs(delta.Z) > .00001f)
        {
            float t0 = (lo-start.Z)/delta.Z, t1 = (hi-start.Z)/delta.Z;
            float begin = Math.Clamp(Math.Min(t0,t1),0,1), stop = Math.Clamp(Math.Max(t0,t1),0,1);
            end = start+delta*stop; start += delta*begin;
        }
        if (Math.Min(start.Z,end.Z) <= radius) return new(0,0,1,1);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (Vector3 point in new[] { start,end }) for (int corner = 0; corner < 8; corner++)
        {
            Vector3 p = point+new Vector3((corner&1) == 0 ? -radius : radius,(corner&2) == 0 ? -radius : radius,(corner&4) == 0 ? -radius : radius);
            float x = (1+p.X*projection.X.X/p.Z)*.5f, y = (1-p.Y*projection.Y.Y/p.Z)*.5f;
            minX = Math.Min(minX,x); minY = Math.Min(minY,y); maxX = Math.Max(maxX,x); maxY = Math.Max(maxY,y);
        }
        return new(minX,minY,maxX,maxY);
    }

    private static void PackField(SmokeRenderCloud field, int growthLayer)
    {
        var carves = new List<SmokeRenderCarve>(field.Carves);
        carves.Sort(static (a,b) => b.Kind.CompareTo(a.Kind)); // broad blasts first; prefix pass excludes tiny bullet holes.
        int blasts = carves.FindLastIndex(static c => c.Kind == ResponsiveSmokeCarveKind.Blast) + 1;
        int begin = FieldData.Count / 4;
        Add(FieldData, field.Origin.X, field.Origin.Y, field.Origin.Z, blasts);
        Add(FieldData, field.CreatedAt, begin + 5, 0, field.Carves.Count);
        Add(FieldData, field.Center.X, field.Center.Y, field.Center.Z, growthLayer);
        FVector extents = ResponsiveSmokeField.GeometryShapeExtents(field.Cells, field.ActivationTicks, field.Origin, field.Center);
        Add(FieldData, 0, extents.X, extents.Y, extents.Z); // Boundary lookup and geometry-directed shape.
        Add(FieldData, growthLayer * SceneShadowGrid * SceneShadowRows, growthLayer < SceneShadowCapacity ? SceneShadowGrid : 0, 0, 0);
        for (int i = 0; i < field.Cells.Length; i++)
            FieldData.Add(field.Cells[i] == 0 ? 0 : field.ActivationTicks[i] == ushort.MaxValue ? -1 : field.ActivationTicks[i] + 1);
        Pad(FieldData);
        if (field.Boundaries.Count != 0)
        {
            int lookup = FieldData.Count / 4;
            FieldData[(begin + 3) * 4] = lookup;
            for (int i = 0; i < field.Cells.Length; i++) FieldData.Add(0);
            Pad(FieldData);
            foreach (SmokeRenderBoundary boundary in field.Boundaries)
            {
                FieldData[lookup * 4 + boundary.Cell] = FieldData.Count / 4;
                Add(FieldData, boundary.Planes.Length, 0, 0, 0);
                foreach (ResponsiveSmokePlane plane in boundary.Planes)
                    Add(FieldData, plane.Normal.X, plane.Normal.Y, plane.Normal.Z, plane.Distance);
            }
        }
        int carveBase = FieldData.Count / 4;
        FieldData[(begin + 1) * 4 + 2] = carveBase;
        foreach (SmokeRenderCarve carve in carves)
        {
            Add(FieldData, carve.Start.X, carve.Start.Y, carve.Start.Z, carve.Radius);
            Add(FieldData, carve.End.X, carve.End.Y, carve.End.Z, carve.Feather);
            Add(FieldData, carve.CreatedAt, carve.Hold, carve.Refill, (int)carve.Kind);
            Add(FieldData, 0, 0, 0, 0);
        }
        for (int c = 0; c < carves.Count; c++)
        {
            if (carves[c].AffectedCells is not { } mask) continue;
            FieldData[(carveBase + c * 4 + 3) * 4] = FieldData.Count / 4;
            foreach (byte cell in mask.Span) FieldData.Add(cell == 0 ? 0 : 1);
            Pad(FieldData);
        }
    }

    private static int Upload(AtlasPool pool, ref ImageTexture? texture, List<float> values, ref byte[] bytes, string parameter)
    {
        int height = Math.Max(1, (values.Count + Width * 4 - 1) / (Width * 4));
        AtlasPool.Entry atlas = pool.Get(height);
        BindTexture(ref texture,atlas.Texture,parameter); bytes = atlas.Bytes; height = atlas.Height;
        bytes.AsSpan().Clear();
        CollectionsMarshal.AsSpan(values).CopyTo(MemoryMarshal.Cast<byte, float>(bytes.AsSpan()));
        using Image image = Image.CreateFromData(Width, height, false, Image.Format.Rgbaf, bytes);
        texture!.Update(image);
        return bytes.Length;
    }
}
