using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScct.Server.Hub;

/// <summary>One gate of the course: a disc on the floor (radius in uu), passed when a pawn's centre is over it.</summary>
public readonly record struct HubGate(string Id, FVector At, float Radius, float Depth = HubPlayK.GateDepth)
{
    /// <summary>
    /// True when <paramref name="p"/> is over the gate: within the radius on the floor, from <see cref="Depth"/> below
    /// its height to <see cref="HubPlayK.GateHeight"/> above it (a deck's gate has a shallow depth, so a pawn in the
    /// pit under the deck is not on it).
    /// </summary>
    public bool Contains(FVector p)
    {
        float dx = p.X - At.X;
        float dy = p.Y - At.Y;
        float dz = p.Z - At.Z;
        return dx * dx + dy * dy <= Radius * Radius && dz > -Depth && dz < HubPlayK.GateHeight;
    }
}

/// <summary>
/// Where the hub's activities are (an OpenSCCT addition): the course's start, checkpoints and finish, the aim range's
/// firing line, lane and backstop, the jukebox, the plaza ball's pitch and the boards' anchors. Read from the hub
/// map's area manifest (<c>fixtures/hubmap/areas.json</c>, made by tools/hubmap), with our own defaults for anything
/// it lacks (the manifest of layout version 1 has no checkpoints or anchors).
/// </summary>
public sealed class HubLayout
{
    public HubGate CourseStart { get; private set; } = new("Start", new FVector(3328f, 1920f, 0f), HubPlayK.StartRadius);

    public HubGate CourseFinish { get; private set; } = new("Finish", new FVector(8960f, 1920f, 0f), HubPlayK.FinishRadius);

    /// <summary>The course's checkpoints in order (from its traversal pieces, its own list, or spaced on the line).</summary>
    public List<HubGate> Checkpoints { get; } = new();

    /// <summary>The course's area on the floor (x, y): a run that leaves it is cancelled.</summary>
    public (float X0, float Y0, float X1, float Y1) CourseArea { get; private set; } = (3072f, 512f, 9216f, 3328f);

    /// <summary>
    /// A running spy whose centre goes under this height in the course's area, once past the first checkpoint, fell
    /// into the pit (layout version 3, a raised course); none (negative infinity) for a course on the floor.
    /// </summary>
    public float CourseFallZ { get; private set; } = float.NegativeInfinity;

    /// <summary>The manifest's layout version (0 when it names none): the course's board is cleared when it changes.</summary>
    public int LayoutVersion { get; private set; }

    public FVector CourseBoard { get; private set; }

    /// <summary>The course board's face (yaw and size from the map), or a billboard.</summary>
    public HubSignFace CourseBoardFace { get; private set; } = HubSignFace.None;

    /// <summary>The aim range's area on the floor.</summary>
    public (float X0, float Y0, float X1, float Y1) RangeArea { get; private set; } = (512f, 4352f, 3328f, 7936f);

    /// <summary>The firing line (x): shooters stand at or behind it (greater x) and shoot towards the backstop.</summary>
    public float FiringLine { get; private set; } = 3072f;

    /// <summary>The backstop wall (x).</summary>
    public float Backstop { get; private set; } = 576f;

    /// <summary>The lane the targets rise in (y from, y to).</summary>
    public (float Y0, float Y1) Lane { get; private set; } = (4864f, 7424f);

    public FVector RangeBoard { get; private set; }

    /// <summary>The range board's face (yaw and size from the map), or a billboard.</summary>
    public HubSignFace RangeBoardFace { get; private set; } = HubSignFace.None;

    /// <summary>The plaza (x, y): the ball's pitch.</summary>
    public (float X0, float Y0, float X1, float Y1) Plaza { get; private set; } = (4608f, 4608f, 7680f, 7680f);

    /// <summary>The fountain in the plaza's middle, which the ball bounces off (x, y).</summary>
    public (float X0, float Y0, float X1, float Y1) Fountain { get; private set; } = (5888f, 5888f, 6400f, 6400f);

    public FVector Jukebox { get; private set; }

    public FVector BallBoard { get; private set; }

    /// <summary>The floor's height (the hall's floor).</summary>
    public float Floor { get; private set; }

    /// <summary>Where the layout came from (a file path, or "defaults").</summary>
    public string Source { get; private set; } = "defaults";

    public HubLayout() => Derive(new Dictionary<string, FVector>());

    /// <summary>The layout of the hub map at <paramref name="path"/>'s manifest, or the defaults when it is missing or unreadable.</summary>
    public static HubLayout Load(string? path)
    {
        var layout = new HubLayout();
        if (path is null || !File.Exists(path))
        {
            return layout;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            layout.Read(doc.RootElement);
            layout.Source = path;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            layout = new HubLayout();
        }

        return layout;
    }

    /// <summary>The manifest next to a hub map's folder: <c>areas.json</c> in it, or in a sibling <c>hubmap</c> folder (the repository's fixtures).</summary>
    public static string? FindManifest(string? mapFolder)
    {
        if (mapFolder is null)
        {
            return null;
        }

        foreach (string candidate in new[]
                 {
                     Path.Combine(mapFolder, "areas.json"),
                     Path.Combine(mapFolder, "..", "hubmap", "areas.json"),
                 })
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private void Read(JsonElement root)
    {
        LayoutVersion = (int)Num(root, "layoutVersion", 0f);
        var ges = new Dictionary<string, FVector>(StringComparer.Ordinal);
        if (root.TryGetProperty("ges", out JsonElement geList) && geList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement ge in geList.EnumerateArray())
            {
                if (ge.TryGetProperty("id", out JsonElement id) && ge.TryGetProperty("points", out JsonElement pts) &&
                    pts.ValueKind == JsonValueKind.Array && pts.GetArrayLength() > 0)
                {
                    FVector sum = FVector.Zero;
                    int n = 0;
                    foreach (JsonElement p in pts.EnumerateArray())
                    {
                        sum += Vec(p, 0f);
                        n++;
                    }

                    ges[id.GetString() ?? string.Empty] = sum * (1f / n);
                }
            }
        }

        if (root.TryGetProperty("hall", out JsonElement hall) && hall.TryGetProperty("lo", out JsonElement hallLo) && hallLo.GetArrayLength() >= 3)
        {
            Floor = hallLo[2].GetSingle();
        }

        var anchors = new Dictionary<string, FVector>(StringComparer.Ordinal);
        var faces = new Dictionary<string, HubSignFace>(StringComparer.Ordinal);
        if (root.TryGetProperty("areas", out JsonElement areas) && areas.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement area in areas.EnumerateArray())
            {
                string kind = area.TryGetProperty("kind", out JsonElement k) ? k.GetString() ?? string.Empty : string.Empty;
                (float, float, float, float)? box = Box(area);
                if (area.TryGetProperty("leaderboardAnchor", out JsonElement anchor))
                {
                    anchors[kind] = Vec(anchor, Floor + HubPlayK.BoardHeight);
                    if (area.TryGetProperty("leaderboardSize", out JsonElement size) && size.GetArrayLength() >= 2)
                    {
                        faces[kind] = new HubSignFace(Num(area, "leaderboardYaw", -1f), size[0].GetSingle(), size[1].GetSingle());
                    }
                }

                switch (kind)
                {
                    case "movementCourse":
                        ReadCourse(area, box, ges);
                        break;
                    case "aimRange":
                        RangeArea = box ?? RangeArea;
                        FiringLine = Num(area, "firingLine", FiringLine);
                        Backstop = Num(area, "backstop", Backstop);
                        if (area.TryGetProperty("lane", out JsonElement lane) && lane.GetArrayLength() >= 2)
                        {
                            Lane = (lane[0].GetSingle(), lane[1].GetSingle());
                        }

                        break;
                    case "spawn":
                        Plaza = box ?? Plaza;
                        if (area.TryGetProperty("jukebox", out JsonElement jukebox))
                        {
                            anchors["jukebox"] = Vec(jukebox, Floor);
                        }

                        break;
                }
            }
        }

        CourseBoardFace = faces.GetValueOrDefault("movementCourse", HubSignFace.None);
        RangeBoardFace = faces.GetValueOrDefault("aimRange", HubSignFace.None);
        Derive(anchors);
    }

    private void ReadCourse(JsonElement area, (float, float, float, float)? box, Dictionary<string, FVector> ges)
    {
        CourseArea = box ?? CourseArea;
        CourseFallZ = Num(area, "fallZ", float.NegativeInfinity);
        if (area.TryGetProperty("start", out JsonElement start))
        {
            CourseStart = CourseStart with { At = Vec(start, Floor) };
        }

        if (area.TryGetProperty("finish", out JsonElement finish))
        {
            CourseFinish = CourseFinish with { At = Vec(finish, Floor) };
        }

        Checkpoints.Clear();
        if (area.TryGetProperty("checkpoints", out JsonElement cps) && cps.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement cp in cps.EnumerateArray())
            {
                // A plain point, or an object with its own radius and depth (a deck's checkpoint).
                JsonElement at = cp.ValueKind == JsonValueKind.Object ? cp.GetProperty("at") : cp;
                float radius = cp.ValueKind == JsonValueKind.Object ? Num(cp, "radius", HubPlayK.CheckpointRadius) : HubPlayK.CheckpointRadius;
                float depth = cp.ValueKind == JsonValueKind.Object ? Num(cp, "depth", HubPlayK.GateDepth) : HubPlayK.GateDepth;
                Checkpoints.Add(new HubGate("CP" + (Checkpoints.Count + 1), Vec(at, Floor), radius, depth));
            }
        }
        else if (area.TryGetProperty("ges", out JsonElement names) && names.ValueKind == JsonValueKind.Array)
        {
            // Each traversal piece of the course is a checkpoint, in the manifest's order (the route's): the gate
            // sits over the piece's middle, at its height, so the piece itself has to be climbed or crossed.
            foreach (JsonElement name in names.EnumerateArray())
            {
                if (name.GetString() is { } id && ges.TryGetValue(id, out FVector at))
                {
                    Checkpoints.Add(new HubGate(id, at, HubPlayK.PieceCheckpointRadius));
                }
            }
        }
    }

    /// <summary>Fills what the manifest left out: spaced checkpoints, board anchors, the jukebox.</summary>
    private void Derive(Dictionary<string, FVector> anchors)
    {
        if (Checkpoints.Count == 0)
        {
            for (int i = 1; i <= HubPlayK.LineCheckpoints; i++)
            {
                float t = i / (HubPlayK.LineCheckpoints + 1f);
                FVector at = CourseStart.At + (CourseFinish.At - CourseStart.At) * t;
                Checkpoints.Add(new HubGate("CP" + i, at, HubPlayK.CheckpointRadius));
            }
        }

        float boardZ = Floor + HubPlayK.BoardHeight;
        CourseBoard = anchors.TryGetValue("movementCourse", out FVector cb) ? cb :
            new FVector(CourseStart.At.X + 320f, Math.Min(CourseStart.At.Y + 640f, CourseArea.Y1 - 64f), boardZ);
        RangeBoard = anchors.TryGetValue("aimRange", out FVector rb) ? rb :
            new FVector(Math.Min(FiringLine + 192f, RangeArea.X1 - 32f), Lane.Y1 + 256f, boardZ);
        BallBoard = anchors.TryGetValue("spawn", out FVector bb) ? bb : new FVector((Plaza.X0 + Plaza.X1) / 2f, Plaza.Y0 + 192f, boardZ);
        Jukebox = anchors.TryGetValue("jukebox", out FVector jb) ? jb : new FVector(Plaza.X1 - 384f, Plaza.Y1 - 384f, Floor);
        CourseStart = CourseStart with { At = CourseStart.At with { Z = Math.Max(CourseStart.At.Z, Floor) } };
        CourseFinish = CourseFinish with { At = CourseFinish.At with { Z = Math.Max(CourseFinish.At.Z, Floor) } };
    }

    private static (float, float, float, float)? Box(JsonElement area) =>
        area.TryGetProperty("lo", out JsonElement lo) && area.TryGetProperty("hi", out JsonElement hi) && lo.GetArrayLength() >= 2 && hi.GetArrayLength() >= 2
            ? (lo[0].GetSingle(), lo[1].GetSingle(), hi[0].GetSingle(), hi[1].GetSingle())
            : null;

    private static float Num(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static FVector Vec(JsonElement p, float z) =>
        new(p[0].GetSingle(), p[1].GetSingle(), p.GetArrayLength() >= 3 ? p[2].GetSingle() : z);
}
