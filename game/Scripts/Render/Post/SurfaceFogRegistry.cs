using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>
/// Fog data for every live camera, selected by its reserved visibility bits. A material can be shared by main,
/// sky and auxiliary views without borrowing whichever camera last set a global uniform. No geometry uses these
/// bits. Slot zero is deliberately unregistered, so independent diagnostic views keep their compositor fallback.
/// </summary>
public sealed class SurfaceFogRegistry : IDisposable
{
    public const string Parameter = "svm_surface_fog";
    public const int TextureWidth = 1024;
    public const int SlotCount = 128;
    private const int TableFloats = SlotCount * 4;
    private const int MaxRetainedScratchBytes = 4 * 1024 * 1024;
    private static readonly SurfaceFogRegistry?[] Views = new SurfaceFogRegistry?[SlotCount];
    private static ImageTexture? s_texture;
    private static bool s_initialized;
    private static bool s_dirty;
    private static List<float> s_values = new(TableFloats);
    private static byte[] s_upload = [];
    private readonly Camera3D _camera;
    private readonly int _slot;
    private FogState _state;
    private bool _disposed;

    public static int LastUploadBytes { get; private set; }
    public static long LastUploadAllocatedBytes { get; private set; }
    public static double LastUploadMilliseconds { get; private set; }

    private readonly record struct FogState(int Mode, Color Color, float Start, float End, PortalFogFrame? Portal);

    public SurfaceFogRegistry(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        EnsureGlobals();
        _camera = camera;
        // 127 is also unregistered: Godot's default all-layers camera has every reserved bit set.
        for (int i = 1; i < Views.Length - 1; i++)
        {
            if (Views[i] is not null) continue;
            _slot = i;
            Views[i] = this;
            camera.CullMask = (camera.CullMask & ~RenderLayers.FogViewMask) | ((uint)i << RenderLayers.FogViewShift);
            s_dirty = true;
            return;
        }

        throw new InvalidOperationException("All 126 per-camera fog slots are in use.");
    }

    /// <summary>Registers the sampler before a shader referencing it is compiled.</summary>
    public static void EnsureGlobals()
    {
        if (s_initialized) return;
        using var empty = Image.CreateEmpty(TextureWidth, 1, false, Image.Format.Rgbaf);
        empty.Fill(Colors.Transparent);
        s_texture = ImageTexture.CreateFromImage(empty);
        RenderingServer.GlobalShaderParameterAdd(Parameter, RenderingServer.GlobalShaderParameterType.Sampler2D, s_texture);
        RenderingServer.FramePreDraw += Flush;
        s_initialized = true;
    }

    public void Update(DisplayPostSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        int mode = !DisplaySpaceEffect.TakesFog(settings.RendMap) ? 0 : settings.PortalFog is not null ? 2 : settings.Fog ? 1 : 0;
        var next = new FogState(mode, settings.FogColor, settings.FogStart, settings.FogEnd, settings.PortalFog);
        if (_state == next) return;
        _state = next;
        s_dirty = true;
    }

    // Called once immediately before drawing, after every camera has supplied this frame's data. All addresses are
    // vec4 indices. Table rows are (block, mode, start, end); a block begins with its simple fog colour followed by
    // the unchanged PortalFogFrame layout. Relative offsets inside a portal frame remain relative to that frame.
    private static void Flush()
    {
        if (!s_dirty || s_texture is null) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        List<float> values = s_values;
        values.Clear();
        for (int i = 0; i < TableFloats; i++) values.Add(0f);
        for (int i = 1; i < Views.Length; i++)
        {
            if (Views[i] is not { _disposed: false } view || view._state.Mode == 0) continue;
            FogState state = view._state;
            values[i * 4] = values.Count / 4;
            values[i * 4 + 1] = state.Mode;
            values[i * 4 + 2] = state.Start;
            values[i * 4 + 3] = state.End;
            values.Add(state.Color.R); values.Add(state.Color.G); values.Add(state.Color.B); values.Add(0f);
            if (state.Mode == 2) values.AddRange(state.Portal!.Data);
        }

        // Camera movement changes candidate counts. Keep texture capacity instead of recreating the GPU image
        // whenever its last occupied row changes; the table only addresses current data.
        int height = Math.Max(s_texture.GetHeight(), (values.Count + TextureWidth * 4 - 1) / (TextureWidth * 4));
        int byteCount = checked(TextureWidth * height * 4 * sizeof(float));
        byte[] upload;
        if (byteCount <= MaxRetainedScratchBytes)
        {
            if (s_upload.Length != byteCount) s_upload = new byte[byteCount];
            upload = s_upload;
        }
        else upload = new byte[byteCount];
        // The cleared table makes retired slots and unregistered views transparent. Clear the tail too, so a
        // smaller current frame cannot expose stale data from the retained capacity.
        upload.AsSpan().Clear();
        CollectionsMarshal.AsSpan(values).CopyTo(MemoryMarshal.Cast<byte, float>(upload.AsSpan()));
        using var image = Image.CreateFromData(TextureWidth, height, false, Image.Format.Rgbaf, upload);
        if (s_texture.GetWidth() == TextureWidth && s_texture.GetHeight() == height) s_texture.Update(image);
        else s_texture.SetImage(image);
        LastUploadBytes = byteCount;
        LastUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LastUploadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Unusually large multi-view scenes are still supported without retaining unbounded managed scratch.
        if (values.Capacity > MaxRetainedScratchBytes / sizeof(float)) s_values = new(TableFloats);
        s_dirty = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Views[_slot] = null;
        if (GodotObject.IsInstanceValid(_camera) && ((_camera.CullMask & RenderLayers.FogViewMask) >> RenderLayers.FogViewShift) == (uint)_slot)
            _camera.CullMask &= ~RenderLayers.FogViewMask;
        s_dirty = true;
    }
}
