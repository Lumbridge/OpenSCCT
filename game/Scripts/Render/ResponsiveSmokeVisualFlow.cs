using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Game.Equipment;
using OpenScctGame.Options;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

internal readonly record struct SmokeFlowInput(SmokeRenderCloud Cloud, float CurrentTime);
internal readonly record struct SmokeFlowReadback(int GridSize, int Capacity, byte[] RgbaHalf, long Dispatches, long Impulses);

/// <summary>
/// Persistent smoke-local visual transport. This does not supply extinction or any gameplay query: the renderer
/// uses its advected detail for scattering only, then applies the authoritative density and carve masks.
/// All RenderingDevice resources belong to the global render thread. Main-thread packets contain immutable data.
/// </summary>
internal static class ResponsiveSmokeVisualFlow
{
    public const int Capacity = 8;
    private const int LogicalCells = ResponsiveSmokeField.CellCount;
    private const int MaxRetainedImpulseBytes = 1024 * 1024;
    private const int OrdinaryImpulseBytes = 64 * 1024;
    private static readonly Dictionary<object, Entry> Entries = new(ReferenceEqualityComparer.Instance);
    private static readonly Entry?[] Slots = new Entry?[Capacity];
    private static readonly HashSet<object> Live = new(ReferenceEqualityComparer.Instance);
    private static readonly List<object> Gone = [];
    private static ImageTexture3D? s_empty;
    private static Texture3Drd? s_export;
    private static string? s_source;
    private static bool s_globals;
    private static RenderingDevice? s_rd;
    private static Rid s_shader, s_pipeline, s_sampler, s_cells, s_impulses, s_exportImage;
    private static readonly Rid[] State = new Rid[2];
    private static UniformSlots? s_uniforms;
    private static int s_read, s_gpuGrid, s_impulseBytes;
    private static long s_dispatches, s_impulseCount;

    private sealed class Entry(int slot)
    {
        public readonly int Slot = slot;
        public readonly SmokeFlowTimeline Timeline = new();
        public readonly HashSet<(uint Sequence, float CreatedAt, ResponsiveSmokeCarveKind Kind)> Applied = [];
        public (uint, uint) Stamp;
        public bool HasStamp;
        public SmokeRenderCloud? MaskSource;
    }
    private sealed record Work(int Slot, SmokeRenderCloud Cloud, float Now, SmokeFlowStep Step,
        byte[]? Cells, int EventOffset, int EventCount);

    public static int GridSize { get; private set; } = 32;
    public static int ActiveSlots => Entries.Count;
    public static long AppliedImpulses => s_impulseCount;
    public static long Dispatches => s_dispatches;
    /// <summary>Cumulative flow texture capacity allocations, excluding ordinary simulation dispatches.</summary>
    public static long AllocationCount { get; private set; }
    public static long AllocatedBytes { get; private set; }
    public static double LastGpuMilliseconds { get; private set; }
    public static string? Error { get; private set; }
    internal static bool HasResources => Entries.Count != 0 || s_gpuGrid != 0 || State[0].IsValid || State[1].IsValid ||
        s_pipeline.IsValid || s_shader.IsValid || s_sampler.IsValid || s_cells.IsValid || s_impulses.IsValid || s_exportImage.IsValid;
    public static Texture3D StateTexture => s_gpuGrid > 0 && s_export is not null ? s_export : s_empty!;
    internal static ImageTexture3D EmptyTexture => s_empty!;
    /// <summary>Render-thread only; resolved after this frame's queued simulation dispatch.</summary>
    public static Rid StateRid => s_exportImage.IsValid ? s_exportImage : RenderingServer.TextureGetRdTexture(s_empty!.GetRid());
    public static int GetSlot(object identity) => Entries.TryGetValue(identity, out Entry? entry) ? entry.Slot : -1;

    public static void EnsureGlobals()
    {
        if (s_globals) return;
        s_globals = true;
        using var pixel = Image.CreateEmpty(1,1,false,Image.Format.Rgbah);
        pixel.Fill(Colors.Transparent);
        s_empty = new ImageTexture3D();
        s_empty.Create(Image.Format.Rgbah,1,1,1,false,[pixel]);
        ShaderGlobals.Add("svm_smoke_flow",RenderingServer.GlobalShaderParameterType.Sampler3D,s_empty);
        ShaderGlobals.Add("svm_smoke_flow_ready",RenderingServer.GlobalShaderParameterType.Bool,false);
    }

    public static void Prewarm()
    {
        EnsureGlobals();
        PrepareSource();
        RenderingServer.CallOnRenderThread(Callable.From(Initialize));
    }

    private static void PrepareSource()
    {
        if (s_source is not null) return;
        const string path = "res://Shaders/svm_smoke_flow.glsl";
        s_source = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(s_source)) Error = $"Smoke flow shader {path} could not be read.";
    }

    /// <summary>Assigns stable slots immediately, then queues simulation before the view-volume integration.</summary>
    public static void Queue(IReadOnlyList<SmokeFlowInput> clouds, ResponsiveSmokeQuality quality)
    {
        EnsureGlobals();
        if (clouds.Count == 0) { Release(); return; }
        PrepareSource();
        int grid = quality == ResponsiveSmokeQuality.Low ? 16 : quality == ResponsiveSmokeQuality.High ? 48 : 32;
        bool qualityChanged = grid != GridSize;
        GridSize = grid;
        Live.Clear();
        foreach (SmokeFlowInput input in clouds) Live.Add(input.Cloud.Identity);
        foreach (object identity in Entries.Keys) if (!Live.Contains(identity)) Gone.Add(identity);
        foreach (object identity in Gone) { Slots[Entries[identity].Slot] = null; Entries.Remove(identity); }
        Gone.Clear();
        var work = new List<Work>(Math.Min(clouds.Count,Capacity));
        var events = new List<float>();
        Live.Clear();
        bool any = false;
        foreach (SmokeFlowInput input in clouds)
        {
            SmokeRenderCloud cloud = input.Cloud;
            if (!Live.Add(cloud.Identity)) continue;
            if (!Entries.TryGetValue(cloud.Identity,out Entry? entry))
            {
                int slot = Array.FindIndex(Slots,static value => value is null);
                if (slot < 0) continue; // Authoritative smoke still renders; only this visual detail falls back.
                entry = new(slot); Slots[slot] = entry; Entries.Add(cloud.Identity,entry);
            }
            SmokeFlowStep step = entry.Timeline.Plan(input.CurrentTime,cloud.CreatedAt,grid,qualityChanged);
            if (step.Reset) entry.Applied.Clear();
            byte[]? cells = null;
            if (qualityChanged || !entry.HasStamp || entry.Stamp != cloud.ContentStamp)
            {
                if (qualityChanged || entry.MaskSource is not { } previous ||
                    !cloud.Cells.SequenceEqual(previous.Cells) || !cloud.ActivationTicks.SequenceEqual(previous.ActivationTicks))
                {
                    cells = PackCells(cloud);
                    entry.MaskSource = cloud;
                }
                entry.Stamp = cloud.ContentStamp; entry.HasStamp = true;
            }
            var fresh = new List<SmokeRenderCarve>();
            foreach (SmokeRenderCarve carve in cloud.Carves)
            {
                if (input.CurrentTime < carve.CreatedAt || input.CurrentTime >= carve.CreatedAt+carve.Hold+carve.Refill) continue;
                if (entry.Applied.Add((carve.Sequence,carve.CreatedAt,carve.Kind))) fresh.Add(carve);
            }
            int begin = events.Count;
            foreach (SmokeRenderCarve carve in fresh)
            {
                Add(events,carve.Start.X,carve.Start.Y,carve.Start.Z,carve.Radius);
                Add(events,carve.End.X,carve.End.Y,carve.End.Z,carve.Feather);
                Add(events,carve.CreatedAt,carve.Hold,carve.Refill,(int)carve.Kind);
                Add(events,0,0,0,0);
            }
            for (int i = 0; i < fresh.Count; i++)
                if (fresh[i].AffectedCells is { } mask)
                {
                    events[begin+i*16+12] = events.Count;
                    foreach (byte cell in mask.Span) events.Add(cell == 0 ? 0 : 1);
                }
            s_impulseCount += fresh.Count;
            any |= step.Reset || step.Steps != 0 || cells is not null || fresh.Count != 0;
            work.Add(new(entry.Slot,cloud,input.CurrentTime,step,cells,begin,fresh.Count));
        }
        if (!any) return;
        byte[] eventBytes = events.Count == 0 ? new byte[16] : MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(events)).ToArray();
        Work[] packet = work.ToArray();
        RenderingServer.CallOnRenderThread(Callable.From(() => Draw(packet,eventBytes,grid)));
    }

    private static byte[] PackCells(SmokeRenderCloud cloud)
    {
        var bytes = new byte[LogicalCells*sizeof(uint)];
        Span<uint> packed = MemoryMarshal.Cast<byte,uint>(bytes.AsSpan());
        ReadOnlySpan<byte> cells = cloud.Cells;
        ReadOnlySpan<ushort> activation = cloud.ActivationTicks;
        for (int i = 0; i < packed.Length; i++) packed[i] = cells[i] == 0 ? 0u : (uint)activation[i]+1u;
        return bytes;
    }

    private static void Add(List<float> values,float x,float y,float z,float w)
    { values.Add(x); values.Add(y); values.Add(z); values.Add(w); }

    private static void Initialize()
    {
        if (s_pipeline.IsValid || Error is not null) return;
        s_rd = RenderingServer.GetRenderingDevice();
        if (s_rd is not { } rd) { Error = "Smoke flow requires a RenderingDevice."; return; }
        using var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = s_source! };
        using RDShaderSpirV? spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv is null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            Error = spirv?.CompileErrorCompute ?? "Smoke flow shader compilation returned no SPIR-V.";
            GD.PushError(Error);
            return;
        }
        s_shader = rd.ShaderCreateFromSpirV(spirv,"responsive_smoke_flow");
        s_pipeline = rd.ComputePipelineCreate(s_shader);
        s_sampler = rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatW = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        s_uniforms = new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.Image,
            RenderingDevice.UniformType.StorageBuffer,RenderingDevice.UniformType.StorageBuffer);
        if (!s_pipeline.IsValid || !s_sampler.IsValid) { Error = "Could not initialize smoke flow resources."; GD.PushError(Error); }
    }

    private static void Allocate(RenderingDevice rd,int grid)
    {
        if (s_gpuGrid == grid && State[0].IsValid) return;
        ReleaseTextures(rd);
        using var format = new RDTextureFormat { TextureType = RenderingDevice.TextureType.Type3D,
            Width = (uint)grid, Height = (uint)grid, Depth = (uint)(grid*Capacity),
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit
                | RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanCopyToBit
                | RenderingDevice.TextureUsageBits.CanUpdateBit };
        using var view = new RDTextureView();
        for (int i = 0; i < 2; i++)
        {
            State[i] = rd.TextureCreate(format,view);
            if (!State[i].IsValid) { Error = "Could not allocate smoke flow textures."; GD.PushError(Error); return; }
            AllocationCount++;
            rd.TextureClear(State[i],Colors.Transparent,0,1,0,1);
        }
        s_cells = rd.StorageBufferCreate((uint)(Capacity*LogicalCells*sizeof(uint)),new byte[Capacity*LogicalCells*sizeof(uint)]);
        s_exportImage = rd.TextureCreate(format,view);
        if (!s_exportImage.IsValid) { Error = "Could not allocate the stable smoke flow export."; GD.PushError(Error); return; }
        AllocationCount++;
        rd.TextureClear(s_exportImage,Colors.Transparent,0,1,0,1);
        s_export ??= new Texture3Drd();
        s_export.TextureRdRid = s_exportImage;
        ShaderGlobals.Set("svm_smoke_flow",s_export);
        s_gpuGrid = grid; s_read = 0;
        AllocatedBytes = (long)grid*grid*grid*Capacity*8*3 + Capacity*LogicalCells*sizeof(uint);
    }

    /// <summary>Sample the completed GPU frame even when this frame does not advance the 30 Hz flow.</summary>
    internal static void SampleGpuTiming(RenderingDevice rd)
    {
        LastGpuMilliseconds = 0;
        ulong begin = 0;
        for (uint i = 0; i < rd.GetCapturedTimestampsCount(); i++)
        {
            string name = rd.GetCapturedTimestampName(i);
            if (name == "SmokeFlowBegin") begin = rd.GetCapturedTimestampGpuTime(i);
            else if (name == "SmokeFlowEnd" && begin != 0)
            {
                LastGpuMilliseconds += (rd.GetCapturedTimestampGpuTime(i)-begin)/1_000_000.0;
                begin = 0;
            }
        }
    }

    private static void Draw(Work[] work,byte[] events,int grid)
    {
        Initialize();
        if (s_rd is not { } rd || Error is not null) return;
        Allocate(rd,grid);
        if (Error is not null) return;
        foreach (Work item in work) if (item.Cells is { } cells)
            rd.BufferUpdate(s_cells,(uint)(item.Slot*LogicalCells*sizeof(uint)),(uint)cells.Length,cells);
        if (s_impulseBytes < events.Length || !s_impulses.IsValid)
        {
            if (s_impulses.IsValid) rd.FreeRid(s_impulses);
            AllocatedBytes -= s_impulseBytes;
            s_impulseBytes = Math.Max(OrdinaryImpulseBytes,events.Length);
            s_impulses = rd.StorageBufferCreate((uint)s_impulseBytes);
            AllocatedBytes += s_impulseBytes;
        }
        rd.BufferUpdate(s_impulses,0,(uint)events.Length,events);
        int passes = 1;
        foreach (Work item in work) passes = Math.Max(passes,item.Step.Steps);
        rd.CaptureTimestamp("SmokeFlowBegin");
        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list,s_pipeline);
        for (int pass = 0; pass < passes; pass++)
        {
            s_uniforms!.Set(0,s_sampler,State[s_read]); s_uniforms.Set(1,State[1-s_read]);
            s_uniforms.Set(2,s_cells); s_uniforms.Set(3,s_impulses);
            rd.ComputeListBindUniformSet(list,UniformSetCacheRD.GetCache(s_shader,0,s_uniforms.Array),0);
            foreach (Work item in work)
            {
                float dt = pass < item.Step.Steps ? item.Step.DeltaTime : 0;
                float now = dt > 0 ? item.Step.StartTime+(pass+1)*item.Step.DeltaTime : item.Now;
                float seed = item.Cloud.Center.X*.0137f+item.Cloud.Center.Y*.0171f+item.Cloud.Center.Z*.0193f+item.Cloud.CreatedAt*.031f;
                int[] values = [item.Slot,grid,Capacity,pass == 0 && item.Cells is not null ? 1 : 0,
                    BitConverter.SingleToInt32Bits(dt),BitConverter.SingleToInt32Bits(now-item.Cloud.CreatedAt),
                    BitConverter.SingleToInt32Bits(seed),BitConverter.SingleToInt32Bits(pass == 0 && item.Step.Reset ? 1f : 0f),
                    BitConverter.SingleToInt32Bits(item.Cloud.Origin.X),BitConverter.SingleToInt32Bits(item.Cloud.Origin.Y),
                    BitConverter.SingleToInt32Bits(item.Cloud.Origin.Z),BitConverter.SingleToInt32Bits(item.Cloud.CreatedAt),
                    item.EventOffset,pass == 0 ? item.EventCount : 0,0,0];
                byte[] push = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
                rd.ComputeListSetPushConstant(list,push,(uint)push.Length);
                rd.ComputeListDispatch(list,(uint)((grid+3)/4),(uint)((grid+3)/4),(uint)((grid+3)/4));
                s_dispatches++;
            }
            rd.ComputeListAddBarrier(list);
            s_read = 1-s_read;
        }
        rd.ComputeListEnd();
        // The simulation keeps ping-ponging, but map materials keep one stable RenderingServer texture.
        // TextureRdRid reassignment recreates that texture and dirties every material using the global sampler.
        Godot.Error copied = rd.TextureCopy(State[s_read],s_exportImage,Vector3.Zero,Vector3.Zero,new(grid,grid,grid*Capacity),0,0,0,0);
        rd.CaptureTimestamp("SmokeFlowEnd");
        if (copied != Godot.Error.Ok)
        {
            Error = $"Could not publish responsive smoke flow: {copied}";
            ShaderGlobals.Set("svm_smoke_flow_ready",false);
            GD.PushError(Error);
            return;
        }
        ShaderGlobals.Set("svm_smoke_flow_ready",true);
    }

    /// <summary>Diagnostic readback only; never called during ordinary rendering or performance measurements.</summary>
    public static void Capture(Action<SmokeFlowReadback> receive)
    {
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            // Validate the published image that both surface and prefix shaders consume, including its copy.
            byte[] data = s_rd is { } rd && s_exportImage.IsValid ? rd.TextureGetData(s_exportImage,0) : [];
            receive(new(s_gpuGrid,Capacity,data,s_dispatches,s_impulseCount));
        }));
    }

    /// <summary>Forget cloud identities while keeping the enabled match's bounded eight-slot atlas idle.</summary>
    public static void Idle()
    {
        Entries.Clear(); Array.Clear(Slots); Live.Clear(); Gone.Clear();
        if (!s_globals) return;
        ShaderGlobals.Set("svm_smoke_flow_ready",false);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            ShaderGlobals.Set("svm_smoke_flow_ready",false);
            if (s_rd is { } rd && s_impulseBytes > MaxRetainedImpulseBytes)
            {
                rd.FreeRid(s_impulses);
                AllocatedBytes -= s_impulseBytes;
                s_impulseBytes = OrdinaryImpulseBytes;
                s_impulses = rd.StorageBufferCreate((uint)s_impulseBytes);
                AllocatedBytes += s_impulseBytes;
            }
            LastGpuMilliseconds = 0;
        }));
    }

    public static void Release()
    {
        // A pending warmup/Draw can allocate after this call, so cleanup must always follow it in the queue.
        Entries.Clear(); Array.Clear(Slots); Live.Clear(); Gone.Clear();
        if (!s_globals) return;
        ShaderGlobals.Set("svm_smoke_flow_ready",false);
        ShaderGlobals.Set("svm_smoke_flow",s_empty!);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            // A previously queued Draw may have rebound its atlas after the main-thread reset above.
            ShaderGlobals.Set("svm_smoke_flow_ready",false);
            ShaderGlobals.Set("svm_smoke_flow",s_empty!);
            if (s_rd is { } rd) ReleaseTextures(rd);
            LastGpuMilliseconds = 0;
        }));
    }

    public static void Shutdown()
    {
        Release();
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            if (s_rd is not { } rd) return;
            foreach (Rid resource in new[] { s_pipeline,s_shader,s_sampler }) if (resource.IsValid) rd.FreeRid(resource);
            s_pipeline=s_shader=s_sampler=default; s_uniforms=null; s_rd=null;
        }));
    }

    private static void ReleaseTextures(RenderingDevice rd)
    {
        if (s_export is not null) s_export.TextureRdRid = default;
        for (int i = 0; i < State.Length; i++) { if (State[i].IsValid) rd.FreeRid(State[i]); State[i] = default; }
        if (s_exportImage.IsValid) rd.FreeRid(s_exportImage);
        s_exportImage = default;
        if (s_cells.IsValid) rd.FreeRid(s_cells);
        if (s_impulses.IsValid) rd.FreeRid(s_impulses);
        s_cells = s_impulses = default; s_gpuGrid = 0; s_impulseBytes = 0; s_read = 0;
        AllocatedBytes = 0;
    }
}
