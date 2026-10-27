using System;
using System.IO;

namespace OpenScct.Content.Tests;

/// <summary>Locations in the repository and scratch folders for tests.</summary>
internal static class TestPaths
{
    private static readonly Lazy<string> s_root = new(FindRoot);

    /// <summary>The repository root (the folder holding OpenSCCT.slnx).</summary>
    public static string RepoRoot => s_root.Value;

    public static string TestBox => Path.Combine(RepoRoot, "fixtures", "testbox");

    public static string Spec => Path.Combine(RepoRoot, "spec", "svmap", "1.0");

    public static string ContentPipeline => Path.Combine(RepoRoot, "content");

    /// <summary>The pipeline's output root: <c>$SVM_CONTENT</c>, else <c>&lt;repo&gt;/.content</c> (local only).</summary>
    public static string ContentRoot =>
        Environment.GetEnvironmentVariable("SVM_CONTENT") is { Length: > 0 } root ? root : Path.Combine(RepoRoot, ".content");

    /// <summary>Converted map folders under <c>&lt;content&gt;/svmap</c>, sorted by name (empty when there are none).</summary>
    public static string[] ConvertedMaps()
    {
        string dir = Path.Combine(ContentRoot, "svmap");
        if (!Directory.Exists(dir))
        {
            return [];
        }

        string[] maps = Array.FindAll(Directory.GetDirectories(dir), d => File.Exists(Path.Combine(d, "manifest.json")));
        Array.Sort(maps, StringComparer.Ordinal);
        return maps;
    }

    /// <summary>A folder under <c>local/</c> (gitignored) for reports, created on demand.</summary>
    public static string LocalDir(string name)
    {
        string dir = Path.Combine(RepoRoot, "local", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static byte[] TestBoxFile(string relPath) => File.ReadAllBytes(Path.Combine(TestBox, relPath));

    /// <summary>Copies the TestBox fixture into a new scratch folder.</summary>
    public static ScratchDir CopyTestBox()
    {
        var dir = new ScratchDir();
        CopyTree(TestBox, dir.Path);
        return dir;
    }

    public static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string FindRoot()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("The repository root (OpenSCCT.slnx) was not found above the test binaries.");
    }
}

/// <summary>A temporary folder deleted on dispose.</summary>
internal sealed class ScratchDir : IDisposable
{
    public ScratchDir()
    {
        Path = Directory.CreateTempSubdirectory("svm-content-tests-").FullName;
    }

    public string Path { get; }

    public string File(string relPath) => System.IO.Path.Combine(Path, relPath);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
