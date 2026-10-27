using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenScct.Coverage;

/// <summary>One unit of the original game as the inventory lists it (re/inventory/README.md).</summary>
/// <param name="Id">Coverage id, for example <c>uc:SBase.SInterrupteur:Trigger</c>.</param>
/// <param name="Kind">uc-func, uc-state-func, uc-state-code, nat, rep-var, rep-func or def.</param>
/// <param name="Weight">Port weight in bytes: the in-memory bytecode size for script and native units, 0 for
/// default properties and replication members (they have no bytecode of their own).</param>
internal sealed record InventoryUnit(
    string Id,
    string Kind,
    string Package,
    string Class,
    string? State,
    string? Name,
    long Weight,
    string? Sha1,
    string Subsystem,
    bool Slice,
    string? SliceReason)
{
    /// <summary>Package-qualified class, for example <c>SBase.SInterrupteur</c>.</summary>
    public string ClassPath => Package + "." + Class;
}

/// <summary>The coverage inventory (re/inventory/inventory.json) with lookups by id, class and member.</summary>
internal sealed class Inventory
{
    public static readonly string[] Packages = ["Core", "Engine", "GUI", "IpDrv", "SBase"];

    private readonly Dictionary<string, InventoryUnit> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _classesByName = new(StringComparer.Ordinal);
    private readonly HashSet<string> _classes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);

    public Inventory(IEnumerable<InventoryUnit> units, string format = "svm.inventory/1")
    {
        Format = format;
        var list = new List<InventoryUnit>();
        foreach (InventoryUnit u in units)
        {
            if (!_byId.TryAdd(u.Id, u))
            {
                throw new InvalidDataException($"Duplicate inventory id {u.Id}.");
            }

            list.Add(u);
            if (_classes.Add(u.ClassPath))
            {
                if (!_classesByName.TryGetValue(u.Class, out List<string>? paths))
                {
                    paths = [];
                    _classesByName[u.Class] = paths;
                }

                paths.Add(u.ClassPath);
            }

            if (u.State is { Length: > 0 })
            {
                _states.Add(u.ClassPath + ":" + u.State);
            }
        }

        Units = list.OrderBy(u => u.Id, StringComparer.Ordinal).ToList();
    }

    public string Format { get; }

    /// <summary>Every unit, sorted by id.</summary>
    public IReadOnlyList<InventoryUnit> Units { get; }

    public IReadOnlyCollection<string> Classes => _classes;

    public static Inventory Load(string path)
    {
        using FileStream fs = File.OpenRead(path);
        using JsonDocument doc = JsonDocument.Parse(fs);
        JsonElement root = doc.RootElement;
        string format = root.TryGetProperty("meta", out JsonElement meta) && meta.TryGetProperty("format", out JsonElement f)
            ? f.GetString() ?? "" : "";
        var units = new List<InventoryUnit>();
        foreach (JsonElement e in root.GetProperty("units").EnumerateArray())
        {
            string kind = Str(e, "kind") ?? throw new InvalidDataException("Inventory unit without kind.");
            long size = e.TryGetProperty("script_size", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
            long weight = kind is "def" or "rep-var" or "rep-func" ? 0 : size;
            units.Add(new InventoryUnit(
                Str(e, "id") ?? throw new InvalidDataException("Inventory unit without id."),
                kind,
                Str(e, "pkg") ?? "",
                Str(e, "class") ?? "",
                Str(e, "state"),
                Str(e, "name"),
                weight,
                Str(e, "sha1"),
                Str(e, "subsystem") ?? "unknown",
                e.TryGetProperty("slice", out JsonElement sl) && sl.ValueKind == JsonValueKind.True,
                Str(e, "slice_reason")));
        }

        return new Inventory(units, format);
    }

    public bool TryGet(string id, out InventoryUnit unit) => _byId.TryGetValue(id, out unit!);

    public bool Contains(string id) => _byId.ContainsKey(id);

    public bool HasClass(string classPath) => _classes.Contains(classPath);

    public bool HasState(string classPath, string state) => _states.Contains(classPath + ":" + state);

    /// <summary>Adds a state known from elsewhere (the generated metadata): states with no functions and no labels
    /// have no unit, so the inventory alone does not know them.</summary>
    public void AddState(string classPath, string state) => _states.Add(classPath + ":" + state);

    public static bool IsPackage(string name) => Array.IndexOf(Packages, name) >= 0;

    /// <summary>Package-qualified paths of the classes with this name (usually one).</summary>
    public IReadOnlyList<string> ClassesNamed(string name) =>
        _classesByName.TryGetValue(name, out List<string>? p) ? p : [];

    /// <summary>True for the id kinds the inventory lists (uc:, nat:, rep:, def:).</summary>
    public static bool IsInventoryKind(string id) =>
        id.StartsWith("uc:", StringComparison.Ordinal) || id.StartsWith("nat:", StringComparison.Ordinal) ||
        id.StartsWith("rep:", StringComparison.Ordinal) || id.StartsWith("def:", StringComparison.Ordinal);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
