using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Json;
using OpenScct.Core;

namespace OpenScct.Content;

/// <summary>GE kinds with the codes the level's GE list stores (names as the editor's GE ADD command takes them).</summary>
public enum GEKind : byte
{
    POLE = 1,
    FENCE = 2,

    /// <summary>Hand over hand bar.</summary>
    HOH = 4,

    /// <summary>Ledge grab.</summary>
    LG = 8,
    LADDER = 16,
    PIPE = 32,

    /// <summary>Zip line.</summary>
    ZL = 64,
}

/// <summary>The platform a GE exists on (original values).</summary>
public enum GEPlatform : byte
{
    Common = 0,
    PS2Only = 1,
    XboxOnly = 2,
}

/// <summary>Where a GE came from: the original level list, an authoring node of a native map, or a generator.</summary>
public sealed record GESource(string Kind, int? Index, string? Node, string? Note)
{
    public static GESource Level(int index) => new("level", index, null, null);

    public static GESource Authored(string node) => new("authored", null, node, null);

    public static GESource Synthetic(string? note = null) => new("synthetic", null, null, note);
}

/// <summary>
/// One geometric event: an authored traversal element (ledge, pipe, zip line...) defined by 2 or 3 UE-space
/// points and gated by its ID byte <see cref="Mask"/>.
/// </summary>
public sealed class GERecord
{
    private readonly FVector[] _points;

    public GERecord(int index, string id, GEKind kind, IReadOnlyList<FVector> points, byte mask, GEPlatform platform, GESource source)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(source);
        if (points.Count != PointCount(kind))
        {
            throw new ArgumentException($"A {kind} GE has {PointCount(kind)} points, not {points.Count}.", nameof(points));
        }

        Index = index;
        Id = id;
        Kind = kind;
        _points = new FVector[points.Count];
        for (int i = 0; i < _points.Length; i++)
        {
            _points[i] = points[i];
        }

        Mask = mask;
        Platform = platform;
        Source = source;
    }

    /// <summary>Position in the GE list (original level order).</summary>
    public int Index { get; }

    public string Id { get; }

    public GEKind Kind { get; }

    /// <summary>The points: 2, or 3 for <see cref="GEKind.LADDER"/> and <see cref="GEKind.PIPE"/>.</summary>
    public ReadOnlySpan<FVector> Points => _points;

    public FVector P0 => _points[0];

    public FVector P1 => _points[1];

    /// <summary>The ID byte. 0 is always active; otherwise active while it shares a bit with the activation mask.</summary>
    public byte Mask { get; }

    public GEPlatform Platform { get; }

    public GESource Source { get; }

    /// <summary>Active under <paramref name="activationMask"/> (<c>LevelInfo.GEActivationMask</c>).</summary>
    public bool IsActive(byte activationMask) => Mask == 0 || (Mask & activationMask) != 0;

    /// <summary>Points a GE of <paramref name="kind"/> has.</summary>
    public static int PointCount(GEKind kind) => kind is GEKind.LADDER or GEKind.PIPE ? 3 : 2;

    public override string ToString() => $"{Id} ({Kind})";
}

/// <summary><c>sim/traversal.json</c>: the GEs in original list order (spec/svmap/1.0/README.md).</summary>
public sealed class TraversalDoc
{
    public const string RelativePath = "sim/traversal.json";

    private readonly Dictionary<string, GERecord> _byId;

    public TraversalDoc(string formatVersion, string map, IReadOnlyList<GERecord> ges)
    {
        ArgumentNullException.ThrowIfNull(formatVersion);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ges);
        FormatVersion = formatVersion;
        Map = map;
        var list = new List<GERecord>(ges);
        _byId = new Dictionary<string, GERecord>(list.Count, AsciiCaseComparer.Instance);
        foreach (GERecord ge in list)
        {
            if (!_byId.TryAdd(ge.Id, ge))
            {
                throw new ArgumentException($"Duplicate GE id '{ge.Id}' (ids ignore ASCII case).", nameof(ges));
            }
        }

        Ges = list;
    }

    public string FormatVersion { get; }

    public string Map { get; }

    public IReadOnlyList<GERecord> Ges { get; }

    public GERecord? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Number of GEs of each kind active under <paramref name="activationMask"/>.</summary>
    public int CountActive(byte activationMask, GEKind? kind = null)
    {
        int n = 0;
        foreach (GERecord ge in Ges)
        {
            if ((kind is null || ge.Kind == kind) && ge.IsActive(activationMask))
            {
                n++;
            }
        }

        return n;
    }

    public static TraversalDoc Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static TraversalDoc Parse(ReadOnlySpan<byte> utf8, string file = RelativePath) => FromJson(SvJson.Parse(utf8, file), file);

    public static TraversalDoc FromJson(SvJsonNode node, string file = RelativePath)
    {
        ArgumentNullException.ThrowIfNull(node);
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(node, "");
        string version = j.FormatVersion(root, "");
        string map = j.StringMember(root, "map", "");
        if (!PropJson.IsIdentifier(map))
        {
            throw j.Error("/map", $"'{map}' is not a map id");
        }

        SvJsonArray array = j.ArrayMember(root, "ges", "");
        var ges = new List<GERecord>(array.Count);
        var seen = new HashSet<string>(AsciiCaseComparer.Instance);
        for (int i = 0; i < array.Count; i++)
        {
            string at = $"/ges/{i}";
            SvJsonObject ge = j.Object(array[i], at);
            string id = j.StringMember(ge, "id", at);
            if (!PropJson.IsIdentifier(id))
            {
                throw j.Error($"{at}/id", $"'{id}' is not a GE id");
            }

            if (!seen.Add(id))
            {
                throw j.Error($"{at}/id", $"duplicate GE id '{id}'");
            }

            string kindName = j.StringMember(ge, "kind", at);
            if (!Enum.TryParse(kindName, ignoreCase: false, out GEKind kind) || !Enum.IsDefined(kind) || kind.ToString() != kindName)
            {
                throw j.Error($"{at}/kind", $"unknown GE kind '{kindName}'");
            }

            SvJsonArray pointArray = j.ArrayMember(ge, "points", at);
            if (pointArray.Count != GERecord.PointCount(kind))
            {
                throw j.Error($"{at}/points", $"a {kindName} GE has {GERecord.PointCount(kind)} points, not {pointArray.Count}");
            }

            var points = new FVector[pointArray.Count];
            for (int p = 0; p < points.Length; p++)
            {
                string pat = $"{at}/points/{p}";
                SvJsonArray xyz = j.Array(pointArray[p], pat);
                if (xyz.Count != 3)
                {
                    throw j.Error(pat, "a point has 3 coordinates");
                }

                points[p] = new FVector(j.Number(xyz[0], $"{pat}/0"), j.Number(xyz[1], $"{pat}/1"), j.Number(xyz[2], $"{pat}/2"));
            }

            byte mask = (byte)j.IntegerMember(ge, "mask", at, 0, 255);
            var platform = (GEPlatform)j.IntegerMember(ge, "platform", at, 0, 2);
            ges.Add(new GERecord(i, id, kind, points, mask, platform, ReadSource(j, j.ObjectMember(ge, "src", at), $"{at}/src")));
        }

        return new TraversalDoc(version, map, ges);
    }

    private static GESource ReadSource(JsonRead j, SvJsonObject src, string at)
    {
        string kind = j.StringMember(src, "kind", at);
        return kind switch
        {
            "level" => GESource.Level((int)j.IntegerMember(src, "index", at, 0, int.MaxValue)),
            "authored" => GESource.Authored(j.StringMember(src, "node", at)),
            "synthetic" => GESource.Synthetic(j.OptionalString(src, "note", at)),
            _ => throw j.Error($"{at}/kind", $"unknown GE source kind '{kind}'"),
        };
    }

    /// <summary>The document as JSON (points as floats).</summary>
    public SvJsonObject ToJson()
    {
        var root = new SvJsonObject();
        root.Add("formatVersion", SvJsonNode.String(FormatVersion));
        root.Add("map", SvJsonNode.String(Map));
        var ges = new SvJsonArray();
        foreach (GERecord ge in Ges)
        {
            var o = new SvJsonObject();
            o.Add("id", SvJsonNode.String(ge.Id));
            o.Add("kind", SvJsonNode.String(ge.Kind.ToString()));
            var points = new SvJsonArray();
            foreach (FVector p in ge.Points)
            {
                points.Add(new SvJsonArray([SvJsonNode.Float(p.X), SvJsonNode.Float(p.Y), SvJsonNode.Float(p.Z)]));
            }

            o.Add("points", points);
            o.Add("mask", SvJsonNode.Integer(ge.Mask));
            o.Add("platform", SvJsonNode.Integer((int)ge.Platform));
            var src = new SvJsonObject();
            src.Add("kind", SvJsonNode.String(ge.Source.Kind));
            if (ge.Source.Index is int index)
            {
                src.Add("index", SvJsonNode.Integer(index));
            }

            if (ge.Source.Node is not null)
            {
                src.Add("node", SvJsonNode.String(ge.Source.Node));
            }

            if (ge.Source.Note is not null)
            {
                src.Add("note", SvJsonNode.String(ge.Source.Note));
            }

            o.Add("src", src);
            ges.Add(o);
        }

        root.Add("ges", ges);
        return root;
    }
}
