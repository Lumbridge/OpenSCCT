using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Collision;
using OpenScct.Content.Json;

namespace OpenScct.Content;

/// <summary>Options for <see cref="SvMap.Load"/>.</summary>
public sealed record SvMapLoadOptions
{
    /// <summary>Compare the sim hash computed from the files with the manifest's (default on).</summary>
    public bool VerifySimHash { get; init; } = true;

    /// <summary>Enforce the 1.0 writer rules on collision.bin (default on).</summary>
    public bool StrictCollision { get; init; } = true;

    /// <summary>Fail on actor names in collision.bin that are not actor ids (default on).</summary>
    public bool CheckReferences { get; init; } = true;
}

/// <summary>
/// The simulation side of one SVMap folder: the manifest and the <c>sim/</c> files, which are all a dedicated
/// server reads. Load it once per map; the documents are read-only.
/// </summary>
public sealed class SvMap
{
    private SvMap(string root, SvMapManifest manifest, GameplayDoc gameplay, ClassTable? classes, CollisionData collision,
        TraversalDoc traversal, string simHash, IReadOnlyList<SvMapFileDigest> simFiles)
    {
        Root = root;
        Manifest = manifest;
        Gameplay = gameplay;
        Classes = classes;
        Collision = collision;
        Traversal = traversal;
        SimHash = simHash;
        SimFiles = simFiles;
    }

    /// <summary>The map folder.</summary>
    public string Root { get; }

    public string Id => Manifest.Map;

    public SvMapManifest Manifest { get; }

    public GameplayDoc Gameplay { get; }

    /// <summary>The class table of <c>sim/classes.json</c> (SVMap 1.1), or null when the map has none.</summary>
    public ClassTable? Classes { get; }

    public CollisionData Collision { get; }

    public TraversalDoc Traversal { get; }

    /// <summary>The sim hash computed from the files on disk.</summary>
    public string SimHash { get; }

    /// <summary>Digest of every file under <c>sim/</c>, in UTF-8 path order.</summary>
    public IReadOnlyList<SvMapFileDigest> SimFiles { get; }

    /// <summary>
    /// Loads and checks the map at <paramref name="root"/>: manifest, the three sim files and the optional class table,
    /// the sim hash against the manifest, map ids, and collision actor names. Throws <see cref="SvMapFormatException"/>
    /// naming the file.
    /// </summary>
    public static SvMap Load(string root, SvMapLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new SvMapLoadOptions();
        string fullRoot = Path.GetFullPath(root);
        SvMapManifest manifest = SvMapManifest.Load(Path.Combine(fullRoot, SvMapHash.ManifestFile));

        // Read every sim file once: JSON is parsed once for both its digest and its document.
        var simFiles = new List<SvMapFileDigest>();
        var json = new Dictionary<string, SvJsonNode>(StringComparer.Ordinal);
        byte[]? collisionBytes = null;
        foreach (string rel in SvMapHash.GroupFiles(fullRoot, SvMapHash.SimGroup))
        {
            byte[] data = File.ReadAllBytes(Path.Combine(fullRoot, rel));
            string digest;
            if (SvMapHash.IsCanonicalized(rel))
            {
                SvJsonNode node = SvJson.Parse(data, rel);
                json[rel] = node;
                digest = SvMapHash.Sha256Hex(CanonicalJson.ToCanonicalBytes(node));
            }
            else
            {
                digest = SvMapHash.Sha256Hex(data);
            }

            if (rel == CollisionReader.RelativePath)
            {
                collisionBytes = data;
            }

            simFiles.Add(new SvMapFileDigest(rel, digest, data.LongLength));
        }

        string simHash = SvMapHash.GroupHash(simFiles);
        if (options.VerifySimHash && simHash != manifest.SimHash)
        {
            List<string> differing = DifferingFiles(manifest, simFiles);
            throw new SvMapFormatException(differing.Count > 0 ? differing[0] : SvMapHash.ManifestFile,
                $"sim hash {simHash} differs from the manifest's {manifest.SimHash} (differing: {string.Join(", ", differing)})");
        }

        GameplayDoc gameplay = GameplayDoc.FromJson(Required(json, GameplayDoc.RelativePath));
        TraversalDoc traversal = TraversalDoc.FromJson(Required(json, TraversalDoc.RelativePath));
        ClassTable? classes = json.TryGetValue(ClassTable.RelativePath, out SvJsonNode? classNode) ? ClassTable.FromJson(classNode) : null;
        CollisionData collision = CollisionReader.Read(
            collisionBytes ?? throw new SvMapFormatException(CollisionReader.RelativePath, "missing"), options.StrictCollision);

        if (gameplay.Map != manifest.Map || traversal.Map != manifest.Map)
        {
            throw new SvMapFormatException(SvMapHash.ManifestFile,
                $"map id differs between manifest.json ({manifest.Map}), gameplay.json ({gameplay.Map}) and traversal.json ({traversal.Map})");
        }

        if (options.CheckReferences)
        {
            List<string> problems = collision.CheckActorNames(gameplay);
            if (problems.Count > 0)
            {
                throw new SvMapFormatException(CollisionReader.RelativePath, string.Join("; ", problems));
            }
        }

        return new SvMap(fullRoot, manifest, gameplay, classes, collision, traversal, simHash, simFiles);
    }

    /// <summary>Sim files whose digest differs from the manifest's, or that only one side has.</summary>
    private static List<string> DifferingFiles(SvMapManifest manifest, List<SvMapFileDigest> simFiles)
    {
        var differing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SvMapFileDigest f in simFiles)
        {
            seen.Add(f.Path);
            if (!manifest.TryGetFile(f.Path, out SvMapFileDigest listed) || listed.Sha256 != f.Sha256)
            {
                differing.Add(f.Path);
            }
        }

        foreach (SvMapFileDigest f in manifest.Files)
        {
            if (f.Path.StartsWith("sim/", StringComparison.Ordinal) && !seen.Contains(f.Path))
            {
                differing.Add(f.Path);
            }
        }

        return differing;
    }

    private static SvJsonNode Required(Dictionary<string, SvJsonNode> json, string relPath) =>
        json.TryGetValue(relPath, out SvJsonNode? node) ? node : throw new SvMapFormatException(relPath, "missing");
}
