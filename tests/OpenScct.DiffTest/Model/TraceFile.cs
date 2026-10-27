using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenScct.DiffTest.Model;

/// <summary>One animation channel of the pawn's mesh instance in one frame.</summary>
/// <param name="Channel">Channel index (0 is the base pose).</param>
/// <param name="Sequence">Animation sequence name.</param>
/// <param name="Frame">Position in the clip, 0..1 (negative while tweening in).</param>
/// <param name="Rate">Play rate in clips per second.</param>
/// <param name="Alpha">Blend weight (1 for channel 0).</param>
/// <param name="Tween">Channel 0 only: tween weight while the frame is negative.</param>
public sealed record AnimChannelSample(int Channel, string Sequence, double Frame, double Rate, double Alpha, double? Tween);

/// <summary>
/// The inputs the recorder (or a sim runner) applied before one frame's tick: held input bytes, a jump press,
/// a view rotation and a speed-mode write.
/// </summary>
public sealed record FrameInput(IReadOnlyDictionary<string, int> Buttons, bool Jump, Rot3? View, bool? FastSpeed)
{
    public static readonly FrameInput None = new(new Dictionary<string, int>(), false, null, null);

    public int Button(string name) => Buttons.TryGetValue(name, out int v) ? v : 0;
}

/// <summary>One frame of a run (svm.trace/1 frame line). Frame -1 is the settled state before the inputs start.</summary>
public sealed record TraceFrame
{
    public required int F { get; init; }

    /// <summary>LevelInfo.TimeSeconds of the frame.</summary>
    public required double T { get; init; }

    /// <summary>LevelInfo.m_dT: the frame's delta time.</summary>
    public required double Dt { get; init; }

    public double Wall { get; init; }
    public required Vec3 Loc { get; init; }
    public required Vec3 Vel { get; init; }
    public Vec3 Acc { get; init; }
    public Rot3 Rot { get; init; }
    public Rot3 ControllerRot { get; init; }
    public required int Physics { get; init; }
    public string? PawnState { get; init; }
    public string? ControllerState { get; init; }
    public bool Crouched { get; init; }
    public bool Rolling { get; init; }
    public int Health { get; init; }
    public double GroundSpeed { get; init; }
    public double CollisionHeight { get; init; }
    public double ZMaxFalling { get; init; }
    public int JumpNumber { get; init; }
    public double AForward { get; init; }
    public double AStrafe { get; init; }
    public double NormePadMove { get; init; }
    public int IMove { get; init; }
    public bool FastSpeed { get; init; }
    public bool CrouchWithRoll { get; init; }
    public bool PressedJump { get; init; }
    public FrameInput Input { get; init; } = FrameInput.None;
    public IReadOnlyList<AnimChannelSample> Anim { get; init; } = [];

    /// <summary>The inputs may have landed after this frame's tick began (seen one frame late).</summary>
    public bool Late { get; init; }

    /// <summary>Frames the recorder missed just before this one.</summary>
    public int Gap { get; init; }

    public bool Unstable { get; init; }

    public double Speed2D => Vel.Length2D;
}

/// <summary>A recorded or simulated run: header, frames (frame -1 first when present) and footer.</summary>
public sealed class TraceRun
{
    public const string Format = "svm.trace/1";

    public TraceRun(JsonObject header, IReadOnlyList<TraceFrame> frames, JsonObject? footer, string? path = null)
    {
        Header = header;
        Frames = frames;
        Footer = footer;
        Path = path;
    }

    public JsonObject Header { get; }

    public IReadOnlyList<TraceFrame> Frames { get; }

    public JsonObject? Footer { get; }

    public string? Path { get; }

    public string ScenarioId => Header["scenario"]?["id"]?.GetValue<string>() ?? "";

    public int RunIndex => Header["run"]?.GetValue<int>() ?? 0;

    public string Name => Path is null ? $"run{RunIndex}" : System.IO.Path.GetFileName(Path).Split('.')[0];

    /// <summary>The frames from frame 0 on (the inputs' frames).</summary>
    public IEnumerable<TraceFrame> Played => Frames.Where(f => f.F >= 0);

    /// <summary>Frame -1 (settled, before any input), or the first frame.</summary>
    public TraceFrame Start => Frames.FirstOrDefault(f => f.F == -1) ?? Frames[0];

    /// <summary>The frame with index f, or null.</summary>
    public TraceFrame? At(int f)
    {
        foreach (TraceFrame fr in Frames)
        {
            if (fr.F == f)
            {
                return fr;
            }
        }

        return null;
    }
}

/// <summary>Reads and writes svm.trace/1 files: gzip-compressed JSON lines (header, frames, footer).</summary>
public static class TraceFile
{
    public static TraceRun Read(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return Read(reader, path);
    }

    public static TraceRun Read(TextReader reader, string? path = null)
    {
        JsonObject? header = null;
        JsonObject? footer = null;
        var frames = new List<TraceFrame>();
        int lineNo = 0;
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            lineNo++;
            if (line.Length == 0)
            {
                continue;
            }

            JsonObject obj = JsonNode.Parse(line)?.AsObject()
                ?? throw new InvalidDataException($"{path}:{lineNo}: not a JSON object");
            if (header is null)
            {
                if (obj["format"]?.GetValue<string>() != TraceRun.Format)
                {
                    throw new InvalidDataException($"{path}: not a {TraceRun.Format} file");
                }

                header = obj;
            }
            else if (obj["end"]?.GetValue<bool>() == true)
            {
                footer = obj;
            }
            else
            {
                frames.Add(ParseFrame(obj));
            }
        }

        return new TraceRun(header ?? throw new InvalidDataException($"{path}: empty trace"), frames, footer, path);
    }

    public static TraceFrame ParseFrame(JsonObject o)
    {
        return new TraceFrame
        {
            F = o["f"]!.GetValue<int>(),
            T = Num(o["t"]),
            Dt = Num(o["dt"]),
            Wall = Num(o["wall"]),
            Loc = V(o["loc"]),
            Vel = V(o["vel"]),
            Acc = V(o["acc"]),
            Rot = R(o["rot"]),
            ControllerRot = R(o["crot"]),
            Physics = Int(o["phys"]),
            PawnState = o["ps"]?.GetValue<string>(),
            ControllerState = o["cs"]?.GetValue<string>(),
            Crouched = Int(o["crouch"]) != 0,
            Rolling = Int(o["roll"]) != 0,
            Health = Int(o["hp"]),
            GroundSpeed = Num(o["gs"]),
            CollisionHeight = Num(o["ch"]),
            ZMaxFalling = Num(o["zmax"]),
            JumpNumber = Int(o["jn"]),
            AForward = Num(o["fwd"]),
            AStrafe = Num(o["str"]),
            NormePadMove = Num(o["npm"]),
            IMove = Int(o["imove"]),
            FastSpeed = Int(o["fast"]) != 0,
            CrouchWithRoll = Int(o["cwr"]) != 0,
            PressedJump = Int(o["pj"]) != 0,
            Input = ParseInput(o["in"] as JsonObject),
            Anim = ParseAnim(o["anim"] as JsonArray),
            Late = Int(o["late"]) != 0,
            Gap = Int(o["gap"]),
            Unstable = Int(o["unstable"]) != 0,
        };
    }

    private static FrameInput ParseInput(JsonObject? o)
    {
        if (o is null || o.Count == 0)
        {
            return FrameInput.None;
        }

        var buttons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        bool jump = false;
        Rot3? view = null;
        bool? fast = null;
        foreach (KeyValuePair<string, JsonNode?> kv in o)
        {
            switch (kv.Key)
            {
                case "jump":
                    jump = Int(kv.Value) != 0;
                    break;
                case "view":
                    JsonArray a = kv.Value!.AsArray();
                    view = new Rot3(a[0]!.GetValue<int>(), a[1]!.GetValue<int>(), 0);
                    break;
                case "fastSpeed":
                    fast = Int(kv.Value) != 0;
                    break;
                default:
                    buttons[kv.Key] = Int(kv.Value);
                    break;
            }
        }

        return new FrameInput(buttons, jump, view, fast);
    }

    private static List<AnimChannelSample> ParseAnim(JsonArray? a)
    {
        var list = new List<AnimChannelSample>();
        if (a is null)
        {
            return list;
        }

        foreach (JsonNode? n in a)
        {
            JsonArray c = n!.AsArray();
            list.Add(new AnimChannelSample(c[0]!.GetValue<int>(), c[1]!.GetValue<string>(), Num(c[2]), Num(c[3]),
                Num(c[4]), c.Count > 5 ? Num(c[5]) : null));
        }

        return list;
    }

    private static double Num(JsonNode? n) => n is null ? 0.0 : n.GetValue<double>();

    private static int Int(JsonNode? n)
    {
        if (n is null)
        {
            return 0;
        }

        JsonValue v = n.AsValue();
        if (v.TryGetValue(out int i))
        {
            return i;
        }

        if (v.TryGetValue(out bool b))
        {
            return b ? 1 : 0;
        }

        return (int)v.GetValue<double>();
    }

    private static Vec3 V(JsonNode? n)
    {
        if (n is not JsonArray a || a.Count < 3)
        {
            return Vec3.Zero;
        }

        return new Vec3(Num(a[0]), Num(a[1]), Num(a[2]));
    }

    private static Rot3 R(JsonNode? n)
    {
        if (n is not JsonArray a || a.Count < 3)
        {
            return default;
        }

        return new Rot3(Int(a[0]), Int(a[1]), Int(a[2]));
    }

    /// <summary>
    /// Writes a run as svm.trace/1. Used by sim runners and by tests that build synthetic traces; values are
    /// written as float32 text, as the recorder does.
    /// </summary>
    public static void Write(string path, JsonObject header, IEnumerable<TraceFrame> frames, JsonObject? footer = null)
    {
        using FileStream file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(gz, new UTF8Encoding(false)) { NewLine = "\n" };
        var head = (JsonObject)header.DeepClone();
        head["format"] = TraceRun.Format;
        writer.WriteLine(head.ToJsonString());
        int count = 0;
        foreach (TraceFrame f in frames)
        {
            writer.WriteLine(FrameToJson(f).ToJsonString());
            count++;
        }

        var foot = footer is null ? new JsonObject() : (JsonObject)footer.DeepClone();
        foot["end"] = true;
        foot["frames"] = count;
        writer.WriteLine(foot.ToJsonString());
    }

    public static JsonObject FrameToJson(TraceFrame f)
    {
        var o = new JsonObject
        {
            ["f"] = f.F,
            ["t"] = F32(f.T),
            ["dt"] = F32(f.Dt),
            ["wall"] = f.Wall,
            ["loc"] = Vec(f.Loc),
            ["vel"] = Vec(f.Vel),
            ["acc"] = Vec(f.Acc),
            ["rot"] = new JsonArray(f.Rot.Pitch, f.Rot.Yaw, f.Rot.Roll),
            ["crot"] = new JsonArray(f.ControllerRot.Pitch, f.ControllerRot.Yaw, f.ControllerRot.Roll),
            ["phys"] = f.Physics,
            ["ps"] = f.PawnState,
            ["cs"] = f.ControllerState,
            ["crouch"] = f.Crouched ? 1 : 0,
            ["roll"] = f.Rolling ? 1 : 0,
            ["hp"] = f.Health,
            ["gs"] = F32(f.GroundSpeed),
            ["ch"] = F32(f.CollisionHeight),
            ["zmax"] = F32(f.ZMaxFalling),
            ["jn"] = f.JumpNumber,
            ["fwd"] = F32(f.AForward),
            ["str"] = F32(f.AStrafe),
            ["npm"] = F32(f.NormePadMove),
            ["imove"] = f.IMove,
            ["fast"] = f.FastSpeed ? 1 : 0,
            ["cwr"] = f.CrouchWithRoll ? 1 : 0,
            ["pj"] = f.PressedJump ? 1 : 0,
            ["late"] = f.Late ? 1 : 0,
            ["gap"] = f.Gap,
        };
        var input = new JsonObject();
        foreach (KeyValuePair<string, int> b in f.Input.Buttons.Where(b => b.Value != 0))
        {
            input[b.Key] = b.Value;
        }

        if (f.Input.Jump)
        {
            input["jump"] = 1;
        }

        if (f.Input.View is Rot3 view)
        {
            input["view"] = new JsonArray(view.Pitch, view.Yaw);
        }

        if (f.Input.FastSpeed is bool fast)
        {
            input["fastSpeed"] = fast ? 1 : 0;
        }

        o["in"] = input;
        var anim = new JsonArray();
        foreach (AnimChannelSample c in f.Anim)
        {
            var ch = new JsonArray(c.Channel, c.Sequence, F32(c.Frame), F32(c.Rate), F32(c.Alpha));
            if (c.Tween is double tw)
            {
                ch.Add(F32(tw));
            }

            anim.Add(ch);
        }

        o["anim"] = anim;
        return o;
    }

    private static JsonArray Vec(Vec3 v) => new(F32(v.X), F32(v.Y), F32(v.Z));

    /// <summary>The shortest text of the value as a float32, as a JSON number.</summary>
    private static JsonNode F32(double value)
    {
        float f = (float)value;
        return JsonNode.Parse(f.ToString("R", CultureInfo.InvariantCulture))!;
    }
}
