using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Animation;

namespace OpenScct.Content;

/// <summary>Simulation-authoritative animation contract. Visual asset digests participate in the sim hash.</summary>
public sealed class AnimationBundle
{
    public const string RelativePath = "sim/animations.json";
    public const int RuntimeVersion = 1;
    public string Format { get; set; } = "svm.animation/1";
    public int RequiresRuntime { get; set; } = RuntimeVersion;
    public AnimationProject Project { get; set; } = new();
    public SortedDictionary<string, string> Assets { get; set; } = new(StringComparer.Ordinal);
    public static AnimationBundle? Load(string folder)
    {
        string path = Path.Combine(folder, RelativePath);
        if (!File.Exists(path)) return null;
        AnimationBundle bundle = JsonSerializer.Deserialize<AnimationBundle>(File.ReadAllBytes(path), AnimationJson.Options) ?? throw new InvalidDataException("Empty animation bundle.");
        if (bundle.Format != "svm.animation/1" || bundle.RequiresRuntime > RuntimeVersion || bundle.RequiresRuntime < 1)
            throw new InvalidDataException("This map needs a newer OpenSCCT animation runtime.");
        AnimationJson.Validate(bundle.Project);
        if (bundle.Assets is null) throw new InvalidDataException("Missing animation asset digests.");
        foreach (SkeletonDefinition skeleton in bundle.Project.Skeletons)
            if (skeleton.Model is { } model && !bundle.Assets.ContainsKey(model)) throw new InvalidDataException("Animation model has no required digest: " + model);
        foreach (var asset in bundle.Assets)
        {
            string full = AssetPath(folder, asset.Key);
            if (!File.Exists(full) || SvMapHash.Sha256Hex(File.ReadAllBytes(full)) != asset.Value)
                throw new InvalidDataException("Missing or changed animation asset: " + asset.Key);
        }
        return bundle;
    }
    public static string AssetPath(string folder, string relative)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe animation asset path.");
        return path;
    }
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, AnimationJson.Options);
    public static AnimationProject Resolve(AnimationProject? shared, AnimationProject? map)
    {
        var result = new AnimationProject();
        foreach (AnimationProject source in new[] { shared, map }.OfType<AnimationProject>())
        {
            AnimationJson.Validate(source);
            Merge(result.Skeletons, source.Skeletons, s => s.Id); Merge(result.Clips, source.Clips, c => c.Id);
            Merge(result.Graphs, source.Graphs, g => g.Id); Merge(result.Sequences, source.Sequences, s => s.Id);
            Merge(result.FirstPersonProfiles, source.FirstPersonProfiles, p => p.Id);
            Merge(result.Scene, source.Scene, r => r.Id);
            foreach (AnimationBinding binding in source.Bindings)
            {
                result.Bindings.RemoveAll(b => b.Target.Equals(binding.Target, StringComparison.OrdinalIgnoreCase) && b.Slot.Equals(binding.Slot, StringComparison.OrdinalIgnoreCase));
                result.Bindings.Add(AnimationJson.Clone(binding));
            }
        }
        AnimationJson.Validate(result);
        return result;
    }
    private static void Merge<T>(List<T> destination, IEnumerable<T> source, Func<T, string> key)
    {
        foreach (T item in source) { destination.RemoveAll(old => key(old) == key(item)); destination.Add(AnimationJson.Clone(item)); }
    }
}
