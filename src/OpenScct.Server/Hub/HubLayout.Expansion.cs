using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScct.Server.Hub;

public readonly record struct HubVolume(FVector Lo, FVector Hi)
{
    public bool Contains(FVector p) => p.X >= Lo.X && p.X <= Hi.X && p.Y >= Lo.Y && p.Y <= Hi.Y && p.Z >= Lo.Z && p.Z <= Hi.Z;
}

/// <summary>A safe section landing. FirstGate is the number of route gates already earned on arrival.</summary>
public sealed record HubCourseSection(string Name, int FirstGate, FVector Recovery, int Yaw, float FallZ,
    IReadOnlyList<HubVolume>? Failures = null);

public sealed record HubCourseDefinition(
    string Id, string Title, int Revision, HubGate Start, HubGate Finish,
    (float X0, float Y0, float X1, float Y1) Area, IReadOnlyList<HubGate> Gates,
    IReadOnlyList<HubCourseSection> Sections, IReadOnlyList<HubVolume> Failures,
    FVector Board, HubSignFace BoardFace, float MaxSeconds, bool LegacyRecovery = false)
{
    public string BoardId => Id == "easy" ? HubActivities.CourseBoardId : "CourseBoard." + Id;
    public int ProgressCount => LegacyRecovery ? Gates.Count : Sections.Count;

    public int AdvanceGates(int next, FVector at)
    {
        while (next < Gates.Count && Gates[next].Contains(at)) next++;
        return next;
    }

    public int AdvanceGates(int next, FVector from, FVector at, float bodyRadius, float halfHeight, out FVector remaining)
    {
        remaining = from;
        while (next < Gates.Count && Gates[next].Crosses(remaining, at, bodyRadius, halfHeight, out float entry))
        {
            remaining += (at - remaining) * entry;
            next++;
        }
        return next;
    }
}

public sealed record HubTeleportDestination(string Id, string Title, FVector Station, IReadOnlyList<FVector> Arrivals, int Yaw);

public sealed partial class HubLayout
{
    private readonly Dictionary<string, HubCourseDefinition> _courses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HubTeleportDestination> _destinations = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, HubCourseDefinition> Courses => _courses;
    public IReadOnlyDictionary<string, HubTeleportDestination> TeleportDestinations => _destinations;

    private void LegacyCourse()
    {
        var sections = new List<HubCourseSection> { new("Start", 0, CourseStart.At, 0, CourseFallZ) };
        for (int i = 0; i < Checkpoints.Count; i++)
            sections.Add(new("Checkpoint " + (i + 1), i + 1, Checkpoints[i].At, 0, CourseFallZ));
        _courses["easy"] = new("easy", "Easy - Training Grounds", LayoutVersion, CourseStart, CourseFinish,
            CourseArea, Checkpoints.ToArray(), sections, [], CourseBoard, CourseBoardFace, HubPlayK.MaxRunSeconds, true);
    }

    private void ReadExpansion(JsonElement root)
    {
        LegacyCourse();
        if (root.TryGetProperty("areas", out var areas))
            foreach (var area in areas.EnumerateArray())
            {
                if (Text(area, "kind") != "movementCourse" || !area.TryGetProperty("courseId", out var idValue)) continue;
                string id = idValue.GetString() ?? "";
                if (id.Length == 0) throw new FormatException("A course needs an id.");
                var gates = new List<HubGate>();
                foreach (var g in area.GetProperty("checkpoints").EnumerateArray())
                    gates.Add(new("Gate" + gates.Count, Vec(g.GetProperty("at"), Floor), Num(g, "radius", 96), Num(g, "depth", 8), Num(g, "height", 180)));
                var sections = new List<HubCourseSection>();
                if (area.TryGetProperty("sections", out var list))
                    foreach (var s in list.EnumerateArray())
                        sections.Add(new(Text(s, "name"), (int)Num(s, "firstGate", 0), Vec(s.GetProperty("recovery"), Floor),
                            (int)Num(s, "yaw", 0), Num(s, "fallZ", float.NegativeInfinity),
                            s.TryGetProperty("failures", out var sf) ? sf.EnumerateArray().Select(v =>
                                new HubVolume(Vec(v.GetProperty("lo"), Floor), Vec(v.GetProperty("hi"), Floor))).ToArray() : []));
                bool legacy = id == "easy";
                if (legacy && sections.Count == 0) sections.AddRange(_courses["easy"].Sections);
                if (sections.Count == 0 || sections[0].FirstGate != 0 ||
                    sections.Any(s => s.FirstGate < 0 || s.FirstGate > gates.Count) ||
                    !sections.Select(s => s.FirstGate).SequenceEqual(sections.Select(s => s.FirstGate).Order()))
                    throw new FormatException("Invalid course sections: " + id);
                var failures = new List<HubVolume>();
                if (area.TryGetProperty("failures", out var volumes))
                    foreach (var v in volumes.EnumerateArray()) failures.Add(new(Vec(v.GetProperty("lo"), Floor), Vec(v.GetProperty("hi"), Floor)));
                var face = area.TryGetProperty("leaderboardSize", out var size)
                    ? new HubSignFace(Num(area, "leaderboardYaw", -1), size[0].GetSingle(), size[1].GetSingle()) : HubSignFace.None;
                _courses[id] = new(id, Text(area, "title"), (int)Num(area, "revision", 1),
                    new("Start", Vec(area.GetProperty("start"), Floor), Num(area, "startRadius", 192), 8, 180),
                    new("Finish", Vec(area.GetProperty("finish"), Floor), Num(area, "finishRadius", 192), 8, 180),
                    Box(area) ?? throw new FormatException("Missing course bounds"), gates, sections, failures,
                    Vec(area.GetProperty("leaderboardAnchor"), Floor), face, Num(area, "maxSeconds", 2700), legacy);
                if (legacy) _courses[id] = _courses[id] with { Start = CourseStart, Finish = CourseFinish, Gates = Checkpoints.ToArray() };
            }
        if (!root.TryGetProperty("teleports", out var teleports)) return;
        foreach (var t in teleports.EnumerateArray())
        {
            string id = Text(t, "id");
            var arrivals = t.GetProperty("arrivals").EnumerateArray().Select(p => Vec(p, Floor)).ToArray();
            if (id.Length == 0 || arrivals.Length == 0 || !_destinations.TryAdd(id,
                new(id, Text(t, "title"), Vec(t.GetProperty("station"), Floor), arrivals, (int)Num(t, "yaw", 0))))
                throw new FormatException("Invalid teleport destination");
        }
    }

    private static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
}
