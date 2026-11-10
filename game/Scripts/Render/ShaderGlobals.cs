using System.Collections.Generic;
using System.Threading;
using Godot;

namespace OpenScctGame.Render;

/// <summary>
/// The global shader parameters, set through here so their values can be read back: the engine keeps them for its own
/// shaders only (RenderingServer.GlobalShaderParameterGet works in the editor alone), and the world pass drawn on the
/// RenderingDevice (Rd/RdWorldRenderer) needs the same values. Every parameter gets a place in registration order, so a
/// shader compiled earlier stays valid when later ones are added: a value parameter an offset in one std140 uniform
/// block, a texture parameter a binding number.
/// </summary>
public static class ShaderGlobals
{
    /// <summary>A registered parameter: its type and its place (block offset, or binding index for a texture).</summary>
    public readonly record struct Slot(StringName Name, RenderingServer.GlobalShaderParameterType Type, int Place, int Size);

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<StringName, Variant> s_values = [];
    private static readonly Dictionary<StringName, int> s_index = [];
    private static readonly List<Slot> s_slots = [];
    private static int s_blockSize, s_textures;

    /// <summary>Bumped when a texture parameter is set to another texture (uniform sets are made again).</summary>
    public static int TextureStamp { get; private set; }

    /// <summary>RenderingServer.GlobalShaderParameterAdd, remembering the type, the place and the default.</summary>
    public static void Add(StringName name, RenderingServer.GlobalShaderParameterType type, Variant value)
    {
        RenderingServer.GlobalShaderParameterAdd(name, type, value);
        lock (s_lock)
        {
            if (!s_index.ContainsKey(name))
            {
                (int size, int align) = Std140(type);
                int place;
                if (size == 0)
                {
                    place = s_textures++;
                }
                else
                {
                    place = (s_blockSize + align - 1) / align * align;
                    s_blockSize = place + size;
                }

                s_index[name] = s_slots.Count;
                s_slots.Add(new Slot(name, type, place, size));
            }

            s_values[name] = value;
            if (IsTexture(type)) TextureStamp++;
        }
    }

    /// <summary>RenderingServer.GlobalShaderParameterSet, remembering the value.</summary>
    public static void Set(StringName name, Variant value)
    {
        RenderingServer.GlobalShaderParameterSet(name, value);
        lock (s_lock)
        {
            if (s_index.TryGetValue(name, out int at) && IsTexture(s_slots[at].Type)) TextureStamp++;
            s_values[name] = value;
        }
    }

    /// <summary>The registered parameters so far, in registration order.</summary>
    public static Slot[] Slots()
    {
        lock (s_lock) return [.. s_slots];
    }

    /// <summary>The value last set (the default until then); false when the parameter was never registered here.</summary>
    public static bool TryGet(StringName name, out Variant value)
    {
        lock (s_lock) return s_values.TryGetValue(name, out value);
    }

    /// <summary>The bytes of the value block so far (std140).</summary>
    public static int BlockSize
    {
        get
        {
            lock (s_lock) return s_blockSize;
        }
    }

    public static bool IsTexture(RenderingServer.GlobalShaderParameterType type) => Std140(type).Size == 0;

    /// <summary>std140 size and alignment of a value type; size 0 for textures (and types the world pass cannot carry).</summary>
    public static (int Size, int Align) Std140(RenderingServer.GlobalShaderParameterType type) => type switch
    {
        RenderingServer.GlobalShaderParameterType.Bool or RenderingServer.GlobalShaderParameterType.Int
            or RenderingServer.GlobalShaderParameterType.Uint or RenderingServer.GlobalShaderParameterType.Float => (4, 4),
        RenderingServer.GlobalShaderParameterType.Vec2 or RenderingServer.GlobalShaderParameterType.Ivec2 => (8, 8),
        RenderingServer.GlobalShaderParameterType.Vec3 or RenderingServer.GlobalShaderParameterType.Ivec3 => (12, 16),
        RenderingServer.GlobalShaderParameterType.Vec4 or RenderingServer.GlobalShaderParameterType.Ivec4
            or RenderingServer.GlobalShaderParameterType.Color => (16, 16),
        _ => (0, 1),
    };
}
