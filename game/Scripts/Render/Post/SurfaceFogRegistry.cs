using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>
/// Fog data for every live camera, selected by its reserved visibility bits. A material can be shared by main,
/// sky and auxiliary views without borrowing whichever camera last set a global uniform. No geometry uses these
/// bits. Slot zero is deliberately unregistered, so independent diagnostic views keep their compositor fallback.
/// The main view's rows are also global vec4 uniforms (svm_fog.gdshaderinc), so its pixels skip the fetches.
/// </summary>
public sealed class SurfaceFogRegistry : IDisposable
{
    public const string Parameter = "svm_surface_fog";
    public const string MainSlotParameter = "svm_fog_main_slot";
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
    private static SurfaceFogRegistry? s_main;
    private static readonly StringName MainSlotName = MainSlotParameter;
    // The main view's rows in this order: its table row, simple fog colour, frame rows 0-5, camera zone ramp pair.
    private static readonly StringName[] MainRowNames = [
        "svm_fog_main_view", "svm_fog_main_color", "svm_fog_main_eye", "svm_fog_main_right", "svm_fog_main_up",
        "svm_fog_main_forward", "svm_fog_main_grid", "svm_fog_main_layout", "svm_fog_main_ramp", "svm_fog_main_ramp_color"];
    private readonly Camera3D? _camera;
    private readonly int _slot;
    public int Slot => _slot;
    private FogState _state;
    private bool _disposed;

    public static int LastUploadBytes { get; private set; }
    public static long LastUploadAllocatedBytes { get; private set; }
    public static double LastUploadMilliseconds { get; private set; }

    private readonly record struct FogState(int Mode, Color Color, float Start, float End, PortalFogFrame? Portal);

    /// <param name="camera">The view's camera; its reserved visibility bits are set to the view's slot.</param>
    /// <param name="main">The gameplay view: its rows also go to uniforms (the latest main view takes them).</param>
    public SurfaceFogRegistry(Camera3D camera, bool main = false)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _camera = camera;
        _slot = Register();
        if (main) s_main = this;
        camera.CullMask = (camera.CullMask & ~RenderLayers.FogViewMask) | Layers;
    }

    /// <summary>
    /// A slot without a camera: materials that shade as a camera no longer drawn (the sky zone in the main view,
    /// FaithfulView) take its <see cref="Layers"/> from a global parameter instead.
    /// </summary>
    public SurfaceFogRegistry()
    {
        _slot = Register();
    }

    private int Register()
    {
        EnsureGlobals();
        // 127 is also unregistered: Godot's default all-layers camera has every reserved bit set.
        for (int i = 1; i < Views.Length - 1; i++)
        {
            if (Views[i] is not null) continue;
            Views[i] = this;
            s_dirty = true;
            return i;
        }

        throw new InvalidOperationException("All 126 per-camera fog slots are in use.");
    }

    /// <summary>The slot's reserved visibility bits, as a registered camera carries them in its cull mask.</summary>
    public uint Layers => (uint)_slot << RenderLayers.FogViewShift;

    /// <summary>Registers the sampler and the main view's uniforms before a shader referencing them is compiled.</summary>
    public static void EnsureGlobals()
    {
        ResponsiveSmokeRenderer.EnsureGlobals();
        if (s_initialized) return;
        using var empty = Image.CreateEmpty(TextureWidth, 1, false, Image.Format.Rgbaf);
        empty.Fill(Colors.Transparent);
        s_texture = ImageTexture.CreateFromImage(empty);
        RenderingServer.GlobalShaderParameterAdd(Parameter, RenderingServer.GlobalShaderParameterType.Sampler2D, s_texture);
        RenderingServer.GlobalShaderParameterAdd(MainSlotName, RenderingServer.GlobalShaderParameterType.Int, 0);
        foreach (StringName name in MainRowNames)
            RenderingServer.GlobalShaderParameterAdd(name, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        RenderingServer.FramePreDraw += Flush;
        s_initialized = true;
    }

    public void Update(DisplayPostSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool takesFog = DisplaySpaceEffect.TakesFog(settings.RendMap);
        Update(takesFog && settings.Fog, settings.FogColor, settings.FogStart, settings.FogEnd, takesFog ? settings.PortalFog : null);
    }

    public void Update(bool fog, Color color, float start, float end, PortalFogFrame? portal = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // An empty portal frame fogs no pixel, the same as mode 0. Flush writes nothing for mode 0, so its state is
        // left at default: a moving camera then neither re-uploads the table nor runs the portal shader path.
        int mode = portal is { Empty: false } ? 2 : portal is null && fog ? 1 : 0;
        FogState next = mode == 0 ? default : new FogState(mode, color, start, end, portal);
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
        SetMainRows(values);
        LastUploadBytes = byteCount;
        LastUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LastUploadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Unusually large multi-view scenes are still supported without retaining unbounded managed scratch.
        if (values.Capacity > MaxRetainedScratchBytes / sizeof(float)) s_values = new(TableFloats);
        s_dirty = false;
    }

    // The main view's rows as uniforms, copied from the floats just uploaded: the texture is 32-bit float, so the
    // shader reads the same values either way. Set together with the texture, they always describe the table the
    // frame draws with; no other view's slot matches the main slot, and slot 0 (no main view) matches no view.
    private static void SetMainRows(List<float> values)
    {
        int slot = s_main is { _disposed: false } main ? main._slot : 0;
        int block = (int)values[slot * 4];
        float mode = values[slot * 4 + 1];
        int frame = mode == 2f ? block + 1 : -1;
        // The shader's camera zone ramp row: 6 + clamp(int(eye.w), 0, 63) * 2 in the frame.
        int ramp = frame < 0 ? -1 : frame + 6 + Math.Clamp((int)values[frame * 4 + 3], 0, 63) * 2;
        RenderingServer.GlobalShaderParameterSet(MainSlotName, slot);
        Set(0, slot);
        Set(1, mode >= 1f ? block : -1);
        for (int row = 0; row < 6; row++) Set(2 + row, frame < 0 ? -1 : frame + row);
        Set(8, ramp);
        Set(9, ramp < 0 ? -1 : ramp + 1);

        void Set(int parameter, int row) => RenderingServer.GlobalShaderParameterSet(MainRowNames[parameter],
            row >= 0 && row * 4 + 3 < values.Count
                ? new Vector4(values[row * 4], values[row * 4 + 1], values[row * 4 + 2], values[row * 4 + 3])
                : Vector4.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Views[_slot] = null;
        if (s_main == this) s_main = null;
        ResponsiveSmokeRenderer.ReleaseView(_slot);
        if (_camera is not null && GodotObject.IsInstanceValid(_camera) && ((_camera.CullMask & RenderLayers.FogViewMask) >> RenderLayers.FogViewShift) == (uint)_slot)
            _camera.CullMask &= ~RenderLayers.FogViewMask;
        s_dirty = true;
    }
}
