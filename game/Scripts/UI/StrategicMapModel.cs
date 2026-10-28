using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Provenance;

namespace OpenScctGame.UI;

/// <summary>A point in world units (Unreal axes: X forward, Y right, Z up).</summary>
public readonly record struct StrategicMapPoint(float X, float Y, float Z)
{
    public float Dot(StrategicMapPoint o) => X * o.X + Y * o.Y + Z * o.Z;

    public static StrategicMapPoint operator -(StrategicMapPoint a, StrategicMapPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}

/// <summary>One floor of a map's strategic map (<c>LevelInfo.MapFloors</c>): its height and its picture file, if converted.</summary>
public sealed record StrategicMapFloor(float FloorZ, string Texture, string? Picture);

/// <summary>A marker on the map picture: 0..1 across (U) and down (V), and its turn in radians (clockwise, 0 pointing right).</summary>
public readonly record struct StrategicMapMarker(float U, float V, float Angle);

/// <summary>A position the page places: a world point and a yaw (65536 a turn).</summary>
public readonly record struct StrategicMapPosition(StrategicMapPoint Location, int Yaw);

/// <summary>
/// The camera the map pictures were taken with (<c>LevelInfo.SnapshotCamera</c>): its position, its forward, right and up
/// axes (the columns of the stored rows) and its field of view. Maps without one keep the pictures but place no markers.
/// </summary>
public sealed record StrategicMapCamera(StrategicMapPoint Position, StrategicMapPoint Forward, StrategicMapPoint Right, StrategicMapPoint Up, float Fov)
{
    /// <summary>A usable camera: a field of view between 0 and 180 degrees and a forward axis that is not zero.</summary>
    public bool Valid => Fov > 0f && Fov < 180f && Forward.Dot(Forward) > 0.5f;
}

/// <summary>
/// The strategic map of one map (spec/hud/strategic-map.md): its floors and their pictures, the snapshot camera and the
/// legend, read from the converted map folder (<c>sim/gameplay.json</c> levelInfo, <c>ui/mapinfo.json</c>,
/// <c>ui/strategicmap/floor&lt;n&gt;.png</c>). Loading never throws: a missing or broken file gives a map with nothing
/// to show (the null guards Enhanced and Reloaded added for the F7 crash).
/// </summary>
public sealed class StrategicMapData
{
    /// <summary>The folder of the floor pictures in a converted map (the maps step writes them).</summary>
    public const string PictureFolder = "ui/strategicmap";

    public static readonly StrategicMapData Empty = new(Array.Empty<StrategicMapFloor>(), null, string.Empty);

    public StrategicMapData(IReadOnlyList<StrategicMapFloor> floors, StrategicMapCamera? camera, string legend)
    {
        Floors = floors;
        Camera = camera;
        Legend = legend;
    }

    /// <summary>The floors, lowest first (the map's order).</summary>
    public IReadOnlyList<StrategicMapFloor> Floors { get; }

    /// <summary>The snapshot camera, or null when the map has none (no markers are placed then).</summary>
    public StrategicMapCamera? Camera { get; }

    /// <summary>The legend beside the picture (<c>SMapSettings.LegendMapInBrief</c>), "|" turned into line breaks.</summary>
    public string Legend { get; }

    /// <summary>The picture file of floor <paramref name="i"/>, or null when it is not converted (or there is no such floor).</summary>
    public string? Picture(int i) => i >= 0 && i < Floors.Count ? Floors[i].Picture : null;

    /// <summary>The floor a height is on: the highest floor whose FloorZ is at or below it, else the lowest.</summary>
    [Provenance(ProvenanceKind.Estimated, "Native 1531/1868 find the map section by height; the stock native could not be decoded (packed code)")]
    public int FloorAt(float z)
    {
        int floor = 0;
        for (int i = 0; i < Floors.Count; i++)
        {
            if (z >= Floors[i].FloorZ)
            {
                floor = i;
            }
        }

        return floor;
    }

    /// <summary>
    /// The world point on the picture (0..1 across and down) as the snapshot camera saw it, or null without a camera or
    /// for a point behind it. The point may land outside 0..1.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-hooks.md rule 21: projection with the camera's axes and tan(FOV/2); axes checked against the floor pictures")]
    public (float U, float V)? Project(StrategicMapPoint p)
    {
        if (Camera is not { Valid: true } cam)
        {
            return null;
        }

        StrategicMapPoint d = p - cam.Position;
        float depth = d.Dot(cam.Forward);
        if (depth < 1f)
        {
            return null;
        }

        float half = depth * MathF.Tan(cam.Fov * MathF.PI / 360f);
        return (0.5f + 0.5f * d.Dot(cam.Right) / half, 0.5f - 0.5f * d.Dot(cam.Up) / half);
    }

    /// <summary>A yaw's direction on the picture in radians, clockwise from pointing right (0 without a camera).</summary>
    public float Angle(int yaw)
    {
        if (Camera is not { Valid: true } cam)
        {
            return 0f;
        }

        float a = yaw * MathF.PI * 2f / 65536f;
        var dir = new StrategicMapPoint(MathF.Cos(a), MathF.Sin(a), 0f);
        return MathF.Atan2(-dir.Dot(cam.Up), dir.Dot(cam.Right));
    }

    /// <summary>Reads a converted map folder (null or missing: <see cref="Empty"/>).</summary>
    public static StrategicMapData Load(string? mapFolder)
    {
        if (string.IsNullOrEmpty(mapFolder) || !Directory.Exists(mapFolder))
        {
            return Empty;
        }

        var floors = new List<StrategicMapFloor>();
        StrategicMapCamera? camera = null;
        try
        {
            string gameplay = Path.Combine(mapFolder, "sim", "gameplay.json");
            if (File.Exists(gameplay))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(gameplay));
                if (doc.RootElement.TryGetProperty("levelInfo", out JsonElement li) && li.TryGetProperty("props", out JsonElement props))
                {
                    if (props.TryGetProperty("MapFloors", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement f in list.EnumerateArray())
                        {
                            int i = floors.Count;
                            string picture = Path.Combine(mapFolder, PictureFolder, $"floor{i}.png");
                            floors.Add(new StrategicMapFloor(Number(f, "FloorZ"), Asset(f, "FloorMapTexture"), File.Exists(picture) ? picture : null));
                        }
                    }

                    if (props.TryGetProperty("SnapshotCamera", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
                    {
                        // The stored rows hold the camera's axes in their columns: forward, right, up.
                        StrategicMapPoint r0 = Point(c, "RotationRow0"), r1 = Point(c, "RotationRow1"), r2 = Point(c, "RotationRow2");
                        camera = new StrategicMapCamera(Point(c, "Position"), new(r0.X, r1.X, r2.X), new(r0.Y, r1.Y, r2.Y), new(r0.Z, r1.Z, r2.Z),
                            Number(c, "FOV"));
                    }
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            floors.Clear();
            camera = null;
        }

        return new StrategicMapData(floors, camera, ReadLegend(mapFolder));
    }

    private static string ReadLegend(string mapFolder)
    {
        try
        {
            string path = Path.Combine(mapFolder, "ui", "mapinfo.json");
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.TryGetProperty("LegendMapInBrief", out JsonElement l) && l.ValueKind == JsonValueKind.String
                ? (l.GetString() ?? string.Empty).Replace('|', '\n')
                : string.Empty;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static float Number(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0f;

    private static StrategicMapPoint Point(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object ? new(Number(v, "X"), Number(v, "Y"), Number(v, "Z")) : default;

    private static string Asset(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$asset", out JsonElement a) &&
        a.ValueKind == JsonValueKind.String ? a.GetString() ?? string.Empty : string.Empty;
}

/// <summary>
/// The page's state (<c>GUI.StrategicMap</c>, spec/hud/strategic-map.md): the shown floor, Up and Down, the blink of the
/// markers every half second, the "you are here" marker and up to two team-mate markers.
/// </summary>
public sealed class StrategicMapModel
{
    /// <summary>GUI.StrategicMap.InitComponent: SetTimer(0.5, true).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.StrategicMap.InitComponent: SetTimer(0.5, true)")]
    public const double TimerSeconds = 0.5;

    /// <summary>GUI.StrategicMap.UpdateTeamMatesPositions: at most two team mates are placed.</summary>
    public const int MaxTeamMates = 2;

    private readonly StrategicMapMarker?[] _mates = new StrategicMapMarker?[MaxTeamMates];

    public StrategicMapModel(StrategicMapData data)
    {
        Data = data ?? StrategicMapData.Empty;
    }

    public StrategicMapData Data { get; }

    /// <summary>The floor shown.</summary>
    public int FloorIndex { get; private set; }

    /// <summary>The markers' blink (<c>bFade</c>): true draws them bright, false dim.</summary>
    public bool Bright { get; private set; }

    /// <summary>The local player's marker, or null when he is not on the shown floor, not on the picture or has no position.</summary>
    public StrategicMapMarker? You { get; private set; }

    /// <summary>The team-mate markers (two slots; a slot is null when that mate is not shown).</summary>
    public IReadOnlyList<StrategicMapMarker?> Mates => _mates;

    /// <summary>The Up and Down buttons show only for a map with more than one floor.</summary>
    public bool HasFloorButtons => Data.Floors.Count > 1;

    /// <summary>The picture of the shown floor, or null (no floors, or not converted).</summary>
    public string? Picture => Data.Picture(FloorIndex);

    /// <summary>
    /// <c>InitComponent</c>: the page opens on the player's floor (the lowest without a position) and places the markers.
    /// </summary>
    [Uc("GUI.StrategicMap.InitComponent", Sha1 = "b9f73c4830ec2bb54f88e89698498dbdda15e79d")]
    [PortOf("uc:GUI.StrategicMap:InitComponent", Spec = "spec/hud/strategic-map.md#rules")]
    public void Open(StrategicMapPosition? you, IEnumerable<StrategicMapPosition> mates)
    {
        FloorIndex = 0;
        Place(you, followPlayer: true);
        PlaceMates(mates);
    }

    /// <summary><c>Timer</c>: the markers blink, the player's marker moves on the shown floor, the mates are placed again.</summary>
    [Uc("GUI.StrategicMap.Timer", Sha1 = "ce5b301722f3d52add3536e0c109ff3496b509a9")]
    [PortOf("uc:GUI.StrategicMap:Timer", Spec = "spec/hud/strategic-map.md#rules")]
    public void Tick(StrategicMapPosition? you, IEnumerable<StrategicMapPosition> mates)
    {
        Bright = !Bright;
        Place(you, followPlayer: false);
        PlaceMates(mates);
    }

    /// <summary><c>ButtonClick</c> on Up: the floor above, when there is one; true when the floor changed.</summary>
    [Uc("GUI.StrategicMap.ButtonClick", Sha1 = "beadabc57cec2363bf298f746c29c33bbd86681e")]
    [PortOf("uc:GUI.StrategicMap:ButtonClick", Spec = "spec/hud/strategic-map.md#rules")]
    public bool Up(StrategicMapPosition? you, IEnumerable<StrategicMapPosition> mates) =>
        FloorIndex < Data.Floors.Count - 1 && Show(FloorIndex + 1, you, mates);

    /// <summary><c>ButtonClick</c> on Down: the floor below, when there is one; true when the floor changed.</summary>
    public bool Down(StrategicMapPosition? you, IEnumerable<StrategicMapPosition> mates) =>
        FloorIndex > 0 && Show(FloorIndex - 1, you, mates);

    private bool Show(int floor, StrategicMapPosition? you, IEnumerable<StrategicMapPosition> mates)
    {
        FloorIndex = floor;
        Place(you, followPlayer: false);
        PlaceMates(mates);
        return true;
    }

    /// <summary>
    /// <c>UpdateStrategicMapPosition</c> (native 1531): on opening the shown floor follows the player; the marker shows
    /// only on his own floor and inside the picture.
    /// </summary>
    [PortOf("nat:GUI.StrategicMap.UpdateStrategicMapPosition", Spec = "spec/hud/strategic-map.md#rules")]
    private void Place(StrategicMapPosition? you, bool followPlayer)
    {
        You = null;
        if (Data.Floors.Count == 0 || you is not { } p)
        {
            return;
        }

        int floor = Data.FloorAt(p.Location.Z);
        if (followPlayer)
        {
            FloorIndex = floor;
        }

        You = Marker(p, floor);
    }

    /// <summary>
    /// <c>UpdateTeamMatesPositions</c> and <c>UpdateTeamMate</c> (native 1868): the first two mates take the two slots
    /// in order; a slot shows when its mate is on the shown floor and inside the picture.
    /// </summary>
    [Uc("GUI.StrategicMap.UpdateTeamMatesPositions", Sha1 = "c2cf14733fd6a8a587ec9c253405708938bf5a49")]
    [PortOf("uc:GUI.StrategicMap:UpdateTeamMatesPositions", Spec = "spec/hud/strategic-map.md#rules")]
    [PortOf("nat:GUI.StrategicMap.UpdateTeamMate", Spec = "spec/hud/strategic-map.md#rules")]
    private void PlaceMates(IEnumerable<StrategicMapPosition> mates)
    {
        Array.Clear(_mates);
        int slot = 0;
        foreach (StrategicMapPosition m in mates ?? Array.Empty<StrategicMapPosition>())
        {
            if (slot >= MaxTeamMates)
            {
                break;
            }

            _mates[slot++] = Data.Floors.Count == 0 ? null : Marker(m, Data.FloorAt(m.Location.Z));
        }
    }

    private StrategicMapMarker? Marker(StrategicMapPosition p, int floor)
    {
        if (floor != FloorIndex || Data.Project(p.Location) is not { } uv || uv.U < 0f || uv.U > 1f || uv.V < 0f || uv.V > 1f)
        {
            return null;
        }

        return new StrategicMapMarker(uv.U, uv.V, Data.Angle(p.Yaw));
    }
}
