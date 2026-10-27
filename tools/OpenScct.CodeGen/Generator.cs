using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenScct.CodeGen.Emit;
using OpenScct.CodeGen.HandWritten;
using OpenScct.CodeGen.Model;
using OpenScct.CodeGen.Plan;

namespace OpenScct.CodeGen;

/// <summary>The generator's inputs, as parsed JSON roots, plus the hand-written runtime code.</summary>
internal sealed class GeneratorInputs
{
    public required JsonElement UcIndex { get; init; }

    /// <summary>Reflection dumps in priority order (game first, then editor).</summary>
    public required IReadOnlyList<JsonElement> Reflect { get; init; }

    /// <summary>classdefaults_localized.json: effective defaults with localized text applied.</summary>
    public JsonElement? DefaultsEffective { get; init; }

    /// <summary>classdefaults.json: per class props and flags.</summary>
    public JsonElement? DefaultsRaw { get; init; }

    public required HandWrittenCode Hand { get; init; }
}

internal sealed class GeneratorResult
{
    public required SortedDictionary<string, string> Files { get; init; }

    public required Report Report { get; init; }

    /// <summary>Output folders the generator owns (stale .g.cs files in them are removed).</summary>
    public required IReadOnlyList<string> OutputDirs { get; init; }
}

/// <summary>Runs the whole pipeline in memory: read, plan, emit. Writing files is the caller's job.</summary>
internal static class Generator
{
    public static GeneratorResult Run(GeneratorInputs inputs, GeneratorOptions? options = null)
    {
        options ??= new GeneratorOptions();
        var report = new Report();
        var reader = new ModelReader();
        UcModel model = reader.Read(inputs.UcIndex, inputs.Reflect, inputs.DefaultsEffective, inputs.DefaultsRaw);
        foreach (string note in reader.Notes)
        {
            report.Add("Input notes", note);
        }

        var planner = new Planner(model, inputs.Hand, options, report);
        planner.Run();
        var emitter = new Emitter(model, planner, report);
        emitter.Run();
        CheckConstructorAssignments(model, planner, report);
        return new GeneratorResult
        {
            Files = emitter.Files,
            Report = report,
            OutputDirs = options.Targets.Select(t => t.OutputDir).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// Hand-written constructor bodies run after SetDefaults, so a literal they assign wins over the decoded default.
    /// The report lists the ones that disagree with the decoded value.
    /// </summary>
    private static void CheckConstructorAssignments(UcModel model, Planner planner, Report report)
    {
        foreach (ClassPlan c in planner.Classes.Values)
        {
            if (c.Hand is null || !model.Defaults.TryGetValue(c.Class.Path, out Dictionary<string, Json.JValue>? defaults))
            {
                continue;
            }

            foreach ((string member, string value) in c.Hand.ConstructorAssignments)
            {
                FieldPlan? f = c.Chain().SelectMany(x => x.Fields).FirstOrDefault(f => f.CsName == member);
                if (f is null)
                {
                    continue;
                }

                defaults.TryGetValue(f.Var.Name, out Json.JValue? decoded);
                string decodedText = decoded is null || decoded.IsNull ? "(zero)" : decoded.ToString();
                bool agrees = Agrees(value.Trim(), decoded);
                if (!agrees)
                {
                    report.Add("Hand-written constructor assignments that override decoded defaults",
                        $"{c.Class.Path}: {member} = {value} (decoded {decodedText})");
                }
            }
        }
    }

    /// <summary>True when a literal assigned by hand equals the decoded value (bool, number or enum member).</summary>
    internal static bool Agrees(string literal, Json.JValue? decoded)
    {
        bool zero = decoded is null || decoded.IsNull;
        if (literal is "true" or "false")
        {
            bool b = literal == "true";
            return zero ? !b : decoded!.Kind == Json.JKind.Bool && decoded.Bool == b;
        }

        string number = literal.EndsWith('f') || literal.EndsWith('F') ? literal[..^1] : literal;
        if (double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
        {
            double actual = zero ? 0 : decoded!.Kind == Json.JKind.Number ? decoded.Number : decoded.Get("value")?.Number ?? double.NaN;
            return (float)actual == (float)d;
        }

        int dot = literal.LastIndexOf('.');
        if (dot > 0 && !zero && decoded!.Kind == Json.JKind.Object)
        {
            return decoded.Get("enum")?.String == literal[(dot + 1)..];
        }

        return false;
    }

    /// <summary>Loads the inputs from the default local paths under <paramref name="repoRoot"/>.</summary>
    public static GeneratorInputs LoadInputs(string repoRoot, string ucIndex, IReadOnlyList<string> reflect, string? defaultsEffective, string? defaultsRaw)
    {
        static JsonElement Load(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 256 });
            return doc.RootElement.Clone();
        }

        HandWrittenCode hand = HandWrittenCode.Load(
            PackageTarget.Default.Select(t => Path.Combine(repoRoot, "src", t.Assembly)).Distinct(StringComparer.Ordinal));
        return new GeneratorInputs
        {
            UcIndex = Load(ucIndex),
            Reflect = reflect.Where(File.Exists).Select(Load).ToList(),
            DefaultsEffective = defaultsEffective is not null && File.Exists(defaultsEffective) ? Load(defaultsEffective) : null,
            DefaultsRaw = defaultsRaw is not null && File.Exists(defaultsRaw) ? Load(defaultsRaw) : null,
            Hand = hand,
        };
    }

    /// <summary>
    /// Writes (or with <paramref name="checkOnly"/> compares) the files under <paramref name="repoRoot"/> and removes
    /// stale .g.cs files in the output folders. Returns the repository-relative paths that differ.
    /// </summary>
    public static List<string> Write(GeneratorResult result, string repoRoot, bool checkOnly)
    {
        var differing = new List<string>();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach ((string rel, string content) in result.Files)
        {
            string path = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            string? existing = File.Exists(path) ? File.ReadAllText(path, utf8) : null;
            if (existing == content)
            {
                continue;
            }

            differing.Add(rel);
            if (!checkOnly)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content, utf8);
            }
        }

        foreach (string dir in result.OutputDirs)
        {
            string full = Path.Combine(repoRoot, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(full, "*.g.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (result.Files.ContainsKey(rel))
                {
                    continue;
                }

                differing.Add(rel + " (stale)");
                if (!checkOnly)
                {
                    File.Delete(file);
                }
            }
        }

        differing.Sort(StringComparer.Ordinal);
        return differing;
    }
}
