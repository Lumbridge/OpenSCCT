using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenScct.Maps;

/// <summary>One source file of a reviewed community mod, pinned to a Git commit and a SHA-256.</summary>
public sealed record CommunityModFile(string Path, string Destination, long Size, string Sha256);

/// <summary>Only original mod inputs are fetched. The converter reads the remaining packages from Steam.</summary>
public sealed class SteamMapRecipe
{
    private SteamMapRecipe(string code, string simHash, string commit, string credits, string evidence, CommunityModFile[] files, string[] steamFiles)
    {
        Code = code;
        Commit = commit;
        Credits = credits;
        Evidence = evidence;
        Files = Array.AsReadOnly(files);
        SteamFiles = Array.AsReadOnly(steamFiles);
        DownloadSize = files.Sum(f => f.Size);
        string identity = simHash + "\n" + commit + "\n" + string.Join('\n', files.OrderBy(f => f.Destination, StringComparer.Ordinal)
            .Select(f => f.Destination + "\t" + f.Sha256)) + "\n" + string.Join('\n', steamFiles.Order(StringComparer.Ordinal));
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public string Code { get; }
    public string Commit { get; }
    public string Credits { get; }
    public string Evidence { get; }
    public IReadOnlyList<CommunityModFile> Files { get; }
    public IReadOnlyList<string> SteamFiles { get; }
    public long DownloadSize { get; }
    public string Fingerprint { get; }

    internal ContentDownload Download(CommunityModFile file)
    {
        string path = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        return new ContentDownload($"https://raw.githubusercontent.com/OpenSCCT/SCCT-Maps/{Commit}/{path}", file.Size, file.Sha256);
    }

    internal static SteamMapRecipe? Read(JsonElement input, string code, string simHash)
    {
        if (input.ValueKind != JsonValueKind.Object || SteamVersusMaps.Contains(code)) return null;
        string commit = ContentIndex.Str(input, "commit");
        string credits = ContentIndex.Str(input, "credits");
        string evidence = ContentIndex.Str(input, "evidence");
        if (commit.Length != 40 || !commit.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(credits) ||
            !Uri.TryCreate(evidence, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps ||
            !input.TryGetProperty("files", out var inputs) || inputs.ValueKind != JsonValueKind.Array) return null;
        var files = new List<CommunityModFile>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? prefix = null;
        bool hasMap = false;
        var dependencies = new List<string>();
        try
        {
            foreach (var item in inputs.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return null;
                string path = ContentIndex.Str(item, "path");
                string destination = ContentIndex.Str(item, "destination");
                string sha = ContentIndex.Str(item, "sha256").ToLowerInvariant();
                MapPaths.ValidateRelative(path);
                MapPaths.ValidateRelative(destination);
                if (!item.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number ||
                    !size.TryGetInt64(out long bytes) || bytes <= 0 || !ContentIndex.IsSha256(sha) ||
                    !targets.Add(destination) || !MapPaths.IsPlayable(destination) ||
                    !path.EndsWith("/" + destination, StringComparison.Ordinal)) return null;
                MapPaths.ValidateSize(bytes);
                string parent = path[..^(destination.Length + 1)];
                string[] parts = parent.Split('/');
                if (!(parts.Length == 2 && parts[0] == "community" ||
                      parts.Length == 3 && parts[0] == "release" && parts[1].Equals(code, StringComparison.OrdinalIgnoreCase))) return null;
                prefix ??= parent;
                if (prefix != parent) return null;
                if (destination.StartsWith("Packages/Maps/", StringComparison.Ordinal))
                {
                    if (hasMap || !destination.Equals($"Packages/Maps/{code}.sdc", StringComparison.Ordinal)) return null;
                    hasMap = true;
                }
                files.Add(new CommunityModFile(path, destination, bytes, sha));
            }
            if (input.TryGetProperty("steamFiles", out var steam))
            {
                if (steam.ValueKind != JsonValueKind.Array) return null;
                foreach (var item in steam.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } path) return null;
                    MapPaths.ValidateRelative(path);
                    bool steamScript = path.StartsWith("System/_PC_/", StringComparison.Ordinal) &&
                        path.Count(c => c == '/') == 2 && path.EndsWith(".u", StringComparison.OrdinalIgnoreCase);
                    if ((!steamScript && !MapPaths.IsPlayable(path)) || !targets.Add(path)) return null;
                    dependencies.Add(path);
                }
            }
        }
        catch (InvalidDataException) { return null; }
        return hasMap ? new(code, simHash, commit, credits, evidence, files.ToArray(), dependencies.ToArray()) : null;
    }
}
