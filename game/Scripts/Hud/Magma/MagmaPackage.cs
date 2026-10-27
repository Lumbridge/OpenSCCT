using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;

namespace OpenScctGame.Hud.Magma;

/// <summary>Interpolation into a keyframe; it applies to the segment that ends at that keyframe.</summary>
public enum MagmaInterp
{
    None,
    Linear,
    Circle,
    Square,
    Root,
}

/// <summary>MASKMODE: SETMASK widgets are invisible occluders, USEMASK widgets are cut where the latest SETMASK covered.</summary>
public enum MagmaMask
{
    None,
    Set,
    Use,
}

/// <summary>
/// BLENDINGMODE as the D3D path treats it (docs/HUD.md, Magma blending): Dodge, 2X and 4X multiply the screen by
/// (1 + S) with a colour scale of 0.5, 1 and 2; Burn 2X and 4X are alpha blends with a colour scale of 1 and 2; every
/// other mode the HUD pages use is the plain alpha blend.
/// </summary>
public enum MagmaBlend
{
    Normal,
    Dodge,
    Dodge2X,
    Dodge4X,
    Burn2X,
    Burn4X,
}

public enum MagmaAlign
{
    Left,
    Center,
    Right,
    Justify,
}

public enum MagmaWidgetKind
{
    Image,
    Text,
    Instance,
    List,
}

/// <summary>A keyframe action: GotoFrame(area, frame) or Stop(area). Area is an index in the package, or -1.</summary>
public readonly record struct MagmaAction(bool IsGoto, int Area, int Frame);

/// <summary>One keyframe of a widget. Colours are 0..1 per channel of the original byte values (no gamma).</summary>
public sealed class MagmaKey
{
    public int Frame { get; init; }

    public MagmaInterp Interp { get; init; }

    /// <summary>Left, top, right, bottom in the area's page units (images, texts and lists).</summary>
    public Rect4 Rect { get; set; }

    /// <summary>Instance position in the parent area.</summary>
    public Vector2 Pos { get; set; }

    /// <summary>Degrees, clockwise on screen, about the rect centre.</summary>
    public float Rot { get; set; }

    /// <summary>Corner colours TL, TR, BR, BL (COLOR1..4).</summary>
    public Color[] Colors { get; init; } = [Colors4.White, Colors4.White, Colors4.White, Colors4.White];

    public Color State { get; set; } = Colors4.White;

    public Color TextColor { get; set; } = Colors4.White;

    public Color Shadow { get; set; } = new(0f, 0f, 0f, 0f);

    public Vector2 ShadowOffset { get; set; }

    public float Height { get; set; } = 12f;

    /// <summary>Extra space after each glyph, in 1/1000 em.</summary>
    public float Tracking { get; set; }

    public Vector2 Tiling { get; set; } = Vector2.One;

    public Vector2 Offset { get; set; }

    public IReadOnlyList<MagmaAction> Actions { get; init; } = [];

    public MagmaKey Clone() => new()
    {
        Frame = Frame,
        Interp = Interp,
        Rect = Rect,
        Pos = Pos,
        Rot = Rot,
        Colors = [Colors[0], Colors[1], Colors[2], Colors[3]],
        State = State,
        TextColor = TextColor,
        Shadow = Shadow,
        ShadowOffset = ShadowOffset,
        Height = Height,
        Tracking = Tracking,
        Tiling = Tiling,
        Offset = Offset,
    };
}

/// <summary>A rectangle as Magma stores it: left, top, right, bottom.</summary>
public readonly record struct Rect4(float L, float T, float R, float B)
{
    public float Width => R - L;

    public float Height => B - T;

    public Rect4 Offset(Vector2 d) => new(L + d.X, T + d.Y, R + d.X, B + d.Y);

    public static Rect4 Lerp(Rect4 a, Rect4 b, float t) =>
        new(a.L + (b.L - a.L) * t, a.T + (b.T - a.T) * t, a.R + (b.R - a.R) * t, a.B + (b.B - a.B) * t);
}

internal static class Colors4
{
    public static readonly Color White = new(1f, 1f, 1f, 1f);
}

public sealed class MagmaWidget
{
    public MagmaWidgetKind Kind { get; init; }

    public string Name { get; init; } = "";

    public bool Hidden { get; init; }

    public MagmaMask Mask { get; init; }

    public MagmaBlend Blend { get; init; }

    /// <summary>Material index in this package, or -1 (none, or a material of another package).</summary>
    public int Material { get; init; } = -1;

    public string Text { get; set; } = "";

    public string Font { get; init; } = "";

    public MagmaAlign Align { get; init; }

    /// <summary>Instances: the linked area; lists: the row (button) area. -1 when none.</summary>
    public int Area { get; init; } = -1;

    /// <summary>"Synchronized with page": the instance follows its parent's clock.</summary>
    public bool Sync { get; init; }

    /// <summary>Lists: visible rows.</summary>
    public int Rows { get; init; }

    /// <summary>
    /// Text flag AUTOSIZED: the text box takes the width of its text from the rect's left edge, so the alignment
    /// inside the authored rect does not apply (only the HUD timer has it).
    /// </summary>
    public bool AutoSized { get; init; }

    public IReadOnlyList<MagmaKey> Keys { get; init; } = [];
}

public sealed class MagmaArea
{
    public string Name { get; init; } = "";

    public bool IsPage { get; init; }

    public bool IsButton { get; init; }

    public float FrameRate { get; init; } = 10f;

    /// <summary>Buttons: first frame of each state (enabled, pressed, disabled, selected, over).</summary>
    public IReadOnlyDictionary<string, int> Timings { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<MagmaWidget> Widgets { get; init; } = [];

    /// <summary>Index of a widget by name (first match, ignoring case), or -1.</summary>
    public int WidgetIndex(string name)
    {
        for (int i = 0; i < Widgets.Count; i++)
        {
            if (string.Equals(Widgets[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class MagmaMaterial
{
    public string Name { get; init; } = "";

    /// <summary>Texture base name (no folder, no extension).</summary>
    public string Texture { get; init; } = "";

    /// <summary>Texel region x1, y1, x2, y2.</summary>
    public Rect4 Region { get; init; }
}

/// <summary>
/// A converted Magma package (<c>shared/ui/magma/&lt;Package&gt;.json</c>, format svm.magma/1, written by the ui step
/// of svmbuild). Materials, areas and pages; links are indexes into <see cref="Areas"/>.
/// </summary>
public sealed class MagmaPackage
{
    public const string Format = "svm.magma/1";

    private readonly Dictionary<string, int> _areaByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _materialByName = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; private init; } = "";

    public IReadOnlyList<MagmaArea> Areas { get; private init; } = [];

    public IReadOnlyList<MagmaMaterial> Materials { get; private init; } = [];

    public IReadOnlyDictionary<string, int> Pages { get; private init; } = new Dictionary<string, int>();

    public int FindArea(string name) => _areaByName.TryGetValue(name, out int i) ? i : -1;

    public int FindMaterial(string name) => _materialByName.TryGetValue(name, out int i) ? i : -1;

    public static MagmaPackage Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument doc = JsonDocument.Parse(stream);
        return Parse(doc.RootElement, path);
    }

    public static MagmaPackage Parse(JsonElement root, string source = "")
    {
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetString() != Format)
        {
            throw new InvalidDataException($"{source}: not a {Format} file");
        }

        var materials = new List<MagmaMaterial>();
        foreach (JsonElement m in root.GetProperty("materials").EnumerateArray())
        {
            materials.Add(new MagmaMaterial
            {
                Name = m.GetProperty("name").GetString() ?? "",
                Texture = m.GetProperty("texture").GetString() ?? "",
                Region = ReadRect(m.GetProperty("region")),
            });
        }

        var areas = new List<MagmaArea>();
        foreach (JsonElement a in root.GetProperty("areas").EnumerateArray())
        {
            string kind = a.GetProperty("kind").GetString() ?? "area";
            var widgets = new List<MagmaWidget>();
            foreach (JsonElement w in a.GetProperty("widgets").EnumerateArray())
            {
                widgets.Add(ReadWidget(w));
            }

            var timings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (a.TryGetProperty("timings", out JsonElement t))
            {
                foreach (JsonProperty p in t.EnumerateObject())
                {
                    timings[p.Name] = p.Value.GetInt32();
                }
            }

            areas.Add(new MagmaArea
            {
                Name = a.GetProperty("name").GetString() ?? "",
                IsPage = kind == "page",
                IsButton = kind == "button",
                FrameRate = Math.Max(1f, a.GetProperty("frameRate").GetSingle()),
                Timings = timings,
                Widgets = widgets,
            });
        }

        var pages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty p in root.GetProperty("pages").EnumerateObject())
        {
            pages[p.Name] = p.Value.GetInt32();
        }

        var package = new MagmaPackage
        {
            Name = root.GetProperty("name").GetString() ?? "",
            Areas = areas,
            Materials = materials,
            Pages = pages,
        };
        for (int i = 0; i < areas.Count; i++)
        {
            package._areaByName.TryAdd(areas[i].Name, i);
        }

        for (int i = 0; i < materials.Count; i++)
        {
            package._materialByName.TryAdd(materials[i].Name, i);
        }

        return package;
    }

    private static MagmaWidget ReadWidget(JsonElement w)
    {
        string kind = w.GetProperty("kind").GetString() ?? "";
        var keys = new List<MagmaKey>();
        foreach (JsonElement k in w.GetProperty("keys").EnumerateArray())
        {
            keys.Add(ReadKey(k));
        }

        keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return new MagmaWidget
        {
            Kind = kind switch
            {
                "image" => MagmaWidgetKind.Image,
                "text" => MagmaWidgetKind.Text,
                "inst" => MagmaWidgetKind.Instance,
                _ => MagmaWidgetKind.List,
            },
            Name = w.GetProperty("name").GetString() ?? "",
            Hidden = w.TryGetProperty("hidden", out JsonElement h) && h.GetBoolean(),
            Mask = Str(w, "mask") switch
            {
                "set" => MagmaMask.Set,
                "use" => MagmaMask.Use,
                _ => MagmaMask.None,
            },
            Blend = ParseBlend(Str(w, "blend")),
            Material = Index(w, "material"),
            Text = Str(w, "text"),
            Font = Str(w, "font"),
            Align = Str(w, "align") switch
            {
                "center" => MagmaAlign.Center,
                "right" => MagmaAlign.Right,
                "justify" => MagmaAlign.Justify,
                _ => MagmaAlign.Left,
            },
            Area = kind == "list" ? Index(w, "item") : Index(w, "area"),
            Sync = w.TryGetProperty("sync", out JsonElement s) && s.GetBoolean(),
            Rows = w.TryGetProperty("rows", out JsonElement r) ? r.GetInt32() : 0,
            AutoSized = HasFlag(w, "autosized"),
            Keys = keys,
        };
    }

    private static bool HasFlag(JsonElement w, string flag)
    {
        if (!w.TryGetProperty("flags", out JsonElement flags) || flags.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement f in flags.EnumerateArray())
        {
            if (string.Equals(f.GetString(), flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static MagmaBlend ParseBlend(string blend) => blend switch
    {
        "Dodge" => MagmaBlend.Dodge,
        "Dodge 2X" => MagmaBlend.Dodge2X,
        "Dodge 4X" => MagmaBlend.Dodge4X,
        "Burn 2X" => MagmaBlend.Burn2X,
        "Burn 4X" => MagmaBlend.Burn4X,
        _ => MagmaBlend.Normal,
    };

    private static MagmaKey ReadKey(JsonElement k)
    {
        var actions = new List<MagmaAction>();
        if (k.TryGetProperty("actions", out JsonElement acts))
        {
            foreach (JsonElement a in acts.EnumerateArray())
            {
                string op = Str(a, "op");
                if (op is "goto" or "stop")
                {
                    actions.Add(new MagmaAction(op == "goto", Index(a, "area"),
                        a.TryGetProperty("frame", out JsonElement f) ? f.GetInt32() : 0));
                }
            }
        }

        var key = new MagmaKey
        {
            Frame = k.GetProperty("f").GetInt32(),
            Interp = Str(k, "interp") switch
            {
                "linear" => MagmaInterp.Linear,
                "circle" => MagmaInterp.Circle,
                "square" => MagmaInterp.Square,
                "root" => MagmaInterp.Root,
                _ => MagmaInterp.None,
            },
            Colors = ReadCorners(k),
            Actions = actions,
        };
        if (k.TryGetProperty("rect", out JsonElement rect))
        {
            key.Rect = ReadRect(rect);
        }

        if (k.TryGetProperty("pos", out JsonElement pos))
        {
            key.Pos = ReadVec(pos);
        }

        if (k.TryGetProperty("rot", out JsonElement rot))
        {
            key.Rot = rot.GetSingle();
        }

        if (k.TryGetProperty("state", out JsonElement state))
        {
            key.State = ReadRgba(state);
        }

        if (k.TryGetProperty("color", out JsonElement color))
        {
            key.TextColor = ReadRgba(color);
        }

        if (k.TryGetProperty("shadow", out JsonElement shadow))
        {
            key.Shadow = ReadRgba(shadow);
        }

        if (k.TryGetProperty("shadowOffset", out JsonElement so))
        {
            key.ShadowOffset = ReadVec(so);
        }

        if (k.TryGetProperty("height", out JsonElement height))
        {
            key.Height = height.GetSingle();
        }

        if (k.TryGetProperty("tracking", out JsonElement tracking))
        {
            key.Tracking = tracking.GetSingle();
        }

        if (k.TryGetProperty("tiling", out JsonElement tiling))
        {
            key.Tiling = ReadVec(tiling);
        }

        if (k.TryGetProperty("offset", out JsonElement offset))
        {
            key.Offset = ReadVec(offset);
        }

        return key;
    }

    /// <summary>COLORn is 0xAABBGGRR: red in the low byte.</summary>
    public static Color FromAbgr(uint v) =>
        new((v & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 24) & 0xFF) / 255f);

    private static Color[] ReadCorners(JsonElement k)
    {
        if (!k.TryGetProperty("colors", out JsonElement colors))
        {
            return [Colors4.White, Colors4.White, Colors4.White, Colors4.White];
        }

        var c = new Color[4];
        int i = 0;
        foreach (JsonElement v in colors.EnumerateArray())
        {
            if (i < 4)
            {
                c[i++] = FromAbgr(v.GetUInt32());
            }
        }

        for (; i < 4; i++)
        {
            c[i] = c[0];
        }

        return c;
    }

    private static Color ReadRgba(JsonElement e)
    {
        Span<float> v = stackalloc float[4];
        int i = 0;
        foreach (JsonElement x in e.EnumerateArray())
        {
            if (i < 4)
            {
                v[i++] = x.GetSingle() / 255f;
            }
        }

        return new Color(v[0], v[1], v[2], v[3]);
    }

    private static Rect4 ReadRect(JsonElement e)
    {
        Span<float> v = stackalloc float[4];
        int i = 0;
        foreach (JsonElement x in e.EnumerateArray())
        {
            if (i < 4)
            {
                v[i++] = x.GetSingle();
            }
        }

        return new Rect4(v[0], v[1], v[2], v[3]);
    }

    private static Vector2 ReadVec(JsonElement e)
    {
        float x = 0f, y = 0f;
        int i = 0;
        foreach (JsonElement v in e.EnumerateArray())
        {
            if (i == 0)
            {
                x = v.GetSingle();
            }
            else if (i == 1)
            {
                y = v.GetSingle();
            }

            i++;
        }

        return new Vector2(x, y);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>An index property; a foreign {package, id} reference or null reads as -1.</summary>
    private static int Index(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({Areas.Count} areas)");
}
