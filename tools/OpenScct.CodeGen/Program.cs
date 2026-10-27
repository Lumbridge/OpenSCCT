using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenScct.CodeGen;

/// <summary>
/// Command line: <c>dotnet run --project tools/OpenScct.CodeGen -- [options]</c>. With no options it reads the local
/// inputs under <c>local/</c>, writes the generated files and the report (<c>local/codegen/report.md</c>).
/// </summary>
internal static class Program
{
    private const string Usage = """
        OpenScct.CodeGen: class skeletons from the local decompiler index, class defaults and reflection dumps.

        Options (paths default to the repository's local/ folder):
          --repo <dir>              repository root (default: the folder holding OpenSCCT.slnx above the current one)
          --uc-index <file>         local/re-out/uc/uc-index.json
          --reflect <file>          reflection dump, highest priority first; repeatable
                                    (default: local/reflect/game.json then local/reflect/editor.json)
          --defaults <file>         local/formats/classdefaults_localized.json
          --defaults-raw <file>     local/formats/classdefaults.json
          --report <file>           local/codegen/report.md
          --check                   compare only; exit 1 when a generated file is missing, different or stale
        """;

    public static int Main(string[] args)
    {
        string? repo = null;
        string? ucIndex = null;
        var reflect = new List<string>();
        string? defaults = null;
        string? defaultsRaw = null;
        string? reportPath = null;
        bool check = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value.");
            switch (a)
            {
                case "--repo": repo = Next(); break;
                case "--uc-index": ucIndex = Next(); break;
                case "--reflect": reflect.Add(Next()); break;
                case "--defaults": defaults = Next(); break;
                case "--defaults-raw": defaultsRaw = Next(); break;
                case "--report": reportPath = Next(); break;
                case "--check": check = true; break;
                case "-h":
                case "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option {a}.");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        repo ??= FindRepoRoot(Directory.GetCurrentDirectory());
        if (repo is null)
        {
            Console.Error.WriteLine("Cannot find the repository root (OpenSCCT.slnx); pass --repo.");
            return 2;
        }

        string local = Path.Combine(repo, "local");
        ucIndex ??= Path.Combine(local, "re-out", "uc", "uc-index.json");
        if (reflect.Count == 0)
        {
            reflect.Add(Path.Combine(local, "reflect", "game.json"));
            reflect.Add(Path.Combine(local, "reflect", "editor.json"));
        }

        defaults ??= Path.Combine(local, "formats", "classdefaults_localized.json");
        defaultsRaw ??= Path.Combine(local, "formats", "classdefaults.json");
        reportPath ??= Path.Combine(local, "codegen", "report.md");
        if (!File.Exists(ucIndex))
        {
            Console.Error.WriteLine($"Missing {ucIndex}. The inputs come from the local decompiler and reflection tools (docs/CODEGEN.md).");
            return 2;
        }

        GeneratorInputs inputs = Generator.LoadInputs(repo, ucIndex, reflect, defaults, defaultsRaw);
        GeneratorResult result = Generator.Run(inputs);
        List<string> differing = Generator.Write(result, repo, check);

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, result.Report.ToMarkdown(), new UTF8Encoding(false));
        IReadOnlyCollection<string> errors = result.Report.Section("Errors");
        Console.WriteLine($"{result.Files.Count} generated files, {differing.Count} {(check ? "differ" : "written or removed")}; report: {reportPath}");
        foreach (string e in errors)
        {
            Console.Error.WriteLine("error: " + e);
        }

        if (check && differing.Count > 0)
        {
            foreach (string d in differing)
            {
                Console.Error.WriteLine("out of date: " + d);
            }

            return 1;
        }

        return errors.Count > 0 ? 1 : 0;
    }

    private static string? FindRepoRoot(string start)
    {
        for (DirectoryInfo? d = new(start); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenSCCT.slnx")))
            {
                return d.FullName;
            }
        }

        return null;
    }
}
