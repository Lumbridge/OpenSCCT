using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Hud.Magma;

/// <summary>
/// Per-widget values a page driver writes, the way the native page code writes widget fields: visibility, text,
/// colour, rotation, position and the texture region (icon swaps inside an atlas).
/// </summary>
public sealed class MagmaOverride
{
    public bool? Hidden { get; set; }

    public string? Text { get; set; }

    /// <summary>Replaces the keyframe colour: every corner of an image, the text colour of a text.</summary>
    public Color? Color { get; set; }

    public float? Rotation { get; set; }

    /// <summary>Moves the widget's rect so its top-left corner is here (page units of its area), keeping the size.</summary>
    public Vector2? Pos { get; set; }

    /// <summary>Moves only the rect's top edge (gauge fills).</summary>
    public float? Top { get; set; }

    /// <summary>Moves the rect's top edge keeping its height (gauge arrows).</summary>
    public float? MoveTop { get; set; }

    /// <summary>Texel region in the widget's own texture.</summary>
    public Rect4? Region { get; set; }

    /// <summary>Texts: shifted left when they would end past the right edge of the screen (the time limit).</summary>
    public bool KeepInside { get; set; }

    public void Clear()
    {
        Hidden = null;
        Text = null;
        Color = null;
        Rotation = null;
        Pos = null;
        Top = null;
        MoveTop = null;
        Region = null;
        KeepInside = false;
    }
}

/// <summary>
/// One instance of a Magma area (a page is the root instance). Each instance has its own clock at its area's frame
/// rate unless it is synchronized with its parent; integer frames fire the keyframe actions (GotoFrame, Stop) while
/// keyframe values are interpolated at the continuous frame. A hidden instance does not advance.
/// </summary>
public sealed class MagmaNode
{
    private const int MaxDepth = 12;
    private const int MaxFramesPerTick = 64;

    private readonly Dictionary<string, MagmaOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MagmaNode> _children = new();

    public MagmaNode(MagmaPackage package, int area, MagmaNode? parent = null, string? name = null, int widgetIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(package);
        Package = package;
        AreaIndex = area;
        Parent = parent;
        Name = name ?? package.Areas[area].Name;
        WidgetIndex = widgetIndex;
        int depth = 0;
        for (MagmaNode? p = parent; p is not null; p = p.Parent)
        {
            depth++;
        }

        MagmaArea a = package.Areas[area];
        for (int i = 0; i < a.Widgets.Count; i++)
        {
            MagmaWidget w = a.Widgets[i];
            if (w.Kind == MagmaWidgetKind.Instance && w.Area >= 0 && w.Area < package.Areas.Count && depth < MaxDepth)
            {
                _children.Add(new MagmaNode(package, w.Area, this, w.Name, i));
            }
        }
    }

    public MagmaPackage Package { get; }

    public int AreaIndex { get; }

    public MagmaArea Area => Package.Areas[AreaIndex];

    public MagmaNode? Parent { get; }

    /// <summary>The instance name (the AREAINST widget's name), or the area name for a page.</summary>
    public string Name { get; }

    /// <summary>Index of the AREAINST widget in the parent area, -1 for a root.</summary>
    public int WidgetIndex { get; }

    public IReadOnlyList<MagmaNode> Children => _children;

    /// <summary>Seconds on this instance's clock.</summary>
    public float Time { get; private set; }

    public int LastFrame { get; private set; } = -1;

    public bool Playing { get; set; } = true;

    public bool Visible { get; private set; } = true;

    /// <summary>Instance position in the parent area, replacing the keyframe position (native position writes).</summary>
    public Vector2? PositionOverride { get; set; }

    /// <summary>Replaces the instance keyframe STATECOLOR (native colour writes).</summary>
    public Color? StateOverride { get; set; }

    public int Frame => Mathf.FloorToInt(Time * Area.FrameRate + 1e-4f);

    public float FrameF => Time * Area.FrameRate;

    public string Path => Parent is null ? Name : (Parent.Parent is null ? Name : Parent.Path + "/" + Name);

    /// <summary>The override record of a widget of this area (created on first use).</summary>
    public MagmaOverride Widget(string widgetName)
    {
        if (!_overrides.TryGetValue(widgetName, out MagmaOverride? o))
        {
            o = new MagmaOverride();
            _overrides[widgetName] = o;
        }

        return o;
    }

    public MagmaOverride? FindOverride(string widgetName) => _overrides.TryGetValue(widgetName, out MagmaOverride? o) ? o : null;

    /// <summary>Shows or hides the instance; like the native SetVisible(v, 1) the children follow.</summary>
    public void SetVisible(bool visible, bool children = true)
    {
        Visible = visible;
        if (children)
        {
            foreach (MagmaNode c in _children)
            {
                c.SetVisible(visible, true);
            }
        }
    }

    /// <summary>Native SetTime: the frame's own actions run on the next tick.</summary>
    public void SetTime(float seconds)
    {
        Time = Math.Max(0f, seconds);
        LastFrame = Frame - 1;
    }

    public void SetFrame(int frame) => SetTime(frame / Area.FrameRate);

    /// <summary>Show from frame 0 and play: what the native code does when an area appears.</summary>
    public void Restart()
    {
        SetVisible(true);
        SetFrame(0);
        Playing = true;
    }

    /// <summary>The direct child instance <paramref name="path"/>, or a path "A/B/C" through instances.</summary>
    public MagmaNode? Find(string path)
    {
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        string head = slash < 0 ? path : path[..slash];
        foreach (MagmaNode c in _children)
        {
            if (string.Equals(c.Name, head, StringComparison.OrdinalIgnoreCase))
            {
                return slash < 0 ? c : c.Find(path[(slash + 1)..]);
            }
        }

        return null;
    }

    /// <summary>The first descendant instance of the area named <paramref name="areaName"/>.</summary>
    public MagmaNode? FindArea(string areaName)
    {
        foreach (MagmaNode c in _children)
        {
            if (string.Equals(c.Area.Name, areaName, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }

            if (c.FindArea(areaName) is { } d)
            {
                return d;
            }
        }

        return null;
    }

    public void Tick(float dt)
    {
        if (!Visible)
        {
            return;
        }

        if (Playing)
        {
            Time += dt;
        }

        // Step frame by frame so every keyframe action runs; a GotoFrame moves Time and LastFrame.
        for (int guard = 0; guard < MaxFramesPerTick; guard++)
        {
            int f = Frame;
            if (f <= LastFrame)
            {
                break;
            }

            int next = LastFrame + 1;
            LastFrame = next;
            FireActions(next);
        }

        foreach (MagmaNode c in _children)
        {
            MagmaWidget w = Area.Widgets[c.WidgetIndex];
            if (w.Sync)
            {
                c.Time = Time * Area.FrameRate / c.Area.FrameRate;
                c.Tick(0f);
            }
            else
            {
                c.Tick(dt);
            }
        }
    }

    private void FireActions(int frame)
    {
        // Indexed loops: foreach over the read-only lists boxed an enumerator per widget per frame.
        IReadOnlyList<MagmaWidget> widgets = Area.Widgets;
        for (int wi = 0; wi < widgets.Count; wi++)
        {
            IReadOnlyList<MagmaKey> keys = widgets[wi].Keys;
            for (int ki = 0; ki < keys.Count; ki++)
            {
                MagmaKey k = keys[ki];
                if (k.Frame == frame)
                {
                    for (int ai = 0; ai < k.Actions.Count; ai++)
                    {
                        DoAction(k.Actions[ai]);
                    }
                }
            }
        }
    }

    private void DoAction(MagmaAction action)
    {
        MagmaNode? target = FindAreaUp(action.Area);
        if (target is null)
        {
            return;
        }

        if (action.IsGoto)
        {
            target.Time = action.Frame / target.Area.FrameRate;
            target.LastFrame = action.Frame;   // the target frame's own actions do not fire again, so loops restart cleanly
        }
        else
        {
            target.Playing = false;
        }
    }

    /// <summary>This instance or an ancestor of the area; else a descendant instance of it.</summary>
    private MagmaNode? FindAreaUp(int area)
    {
        for (MagmaNode? n = this; n is not null; n = n.Parent)
        {
            if (n.AreaIndex == area)
            {
                return n;
            }
        }

        for (MagmaNode? n = this; n is not null; n = n.Parent)
        {
            if (FindDown(n, area) is { } d)
            {
                return d;
            }
        }

        return null;
    }

    private static MagmaNode? FindDown(MagmaNode n, int area)
    {
        foreach (MagmaNode c in n._children)
        {
            if (c.AreaIndex == area)
            {
                return c;
            }

            if (FindDown(c, area) is { } d)
            {
                return d;
            }
        }

        return null;
    }

    /// <summary>Whether widget <paramref name="index"/> is hidden now (its override, else its HIDDEN flag).</summary>
    public bool IsWidgetHidden(int index)
    {
        MagmaWidget w = Area.Widgets[index];
        return FindOverride(w.Name)?.Hidden ?? w.Hidden;
    }

    /// <summary>Bounds of what this instance draws at its current frame, in its own area space; null when nothing.</summary>
    public Rect4? ChildBounds()
    {
        float f = FrameF;
        Rect4? b = null;
        MagmaArea a = Area;
        for (int i = 0; i < a.Widgets.Count; i++)
        {
            MagmaWidget w = a.Widgets[i];
            if (IsWidgetHidden(i) || !MagmaEval.Evaluate(w, f, out MagmaKey k))
            {
                continue;
            }

            if (w.Kind == MagmaWidgetKind.Instance)
            {
                foreach (MagmaNode c in _children)
                {
                    if (c.WidgetIndex == i && c.Visible && c.ChildBounds() is { } cb)
                    {
                        Vector2 p = c.PositionOverride ?? k.Pos;
                        b = Union(b, cb.Offset(p));
                    }
                }
            }
            else if (w.Mask != MagmaMask.Set)
            {
                Rect4 r = k.Rect;
                b = Union(b, new Rect4(Math.Min(r.L, r.R), Math.Min(r.T, r.B), Math.Max(r.L, r.R), Math.Max(r.T, r.B)));
            }
        }

        return b;
    }

    private static Rect4 Union(Rect4? a, Rect4 b) => a is not { } x
        ? b
        : new Rect4(Math.Min(x.L, b.L), Math.Min(x.T, b.T), Math.Max(x.R, b.R), Math.Max(x.B, b.B));

    public override string ToString() => $"{Path} ({Area.Name}) frame {Frame}{(Visible ? "" : " hidden")}";
}

/// <summary>Keyframe evaluation.</summary>
public static class MagmaEval
{
    public static float Ease(MagmaInterp interp, float t) => interp switch
    {
        MagmaInterp.Linear => t,
        MagmaInterp.Circle => 1f - MathF.Sqrt(Math.Max(0f, 1f - t * t)),
        MagmaInterp.Square => t * t,
        MagmaInterp.Root => MathF.Sqrt(t),
        _ => 0f,
    };

    /// <summary>
    /// The widget's values at <paramref name="frame"/>. False before its first keyframe (not drawn); it holds its last
    /// keyframe after the end. A segment interpolates with the destination keyframe's interpolation.
    /// </summary>
    public static bool Evaluate(MagmaWidget w, float frame, out MagmaKey key)
    {
        IReadOnlyList<MagmaKey> keys = w.Keys;
        if (keys.Count == 0 || frame < keys[0].Frame)
        {
            key = null!;
            return false;
        }

        int i = 0;
        while (i + 1 < keys.Count && keys[i + 1].Frame <= frame)
        {
            i++;
        }

        MagmaKey a = keys[i];
        if (i + 1 >= keys.Count || frame == a.Frame || keys[i + 1].Interp == MagmaInterp.None)
        {
            key = a;
            return true;
        }

        MagmaKey b = keys[i + 1];
        float t = Ease(b.Interp, Math.Clamp((frame - a.Frame) / Math.Max(1, b.Frame - a.Frame), 0f, 1f));
        MagmaKey o = a.Clone();
        o.Rect = Rect4.Lerp(a.Rect, b.Rect, t);
        o.Pos = a.Pos.Lerp(b.Pos, t);
        o.Rot = a.Rot + (b.Rot - a.Rot) * t;
        for (int c = 0; c < 4; c++)
        {
            o.Colors[c] = a.Colors[c].Lerp(b.Colors[c], t);
        }

        o.State = a.State.Lerp(b.State, t);
        o.TextColor = a.TextColor.Lerp(b.TextColor, t);
        o.Shadow = a.Shadow.Lerp(b.Shadow, t);
        o.Height = a.Height + (b.Height - a.Height) * t;
        o.Tracking = a.Tracking + (b.Tracking - a.Tracking) * t;
        o.Tiling = a.Tiling.Lerp(b.Tiling, t);
        o.Offset = a.Offset.Lerp(b.Offset, t);
        key = o;
        return true;
    }
}
