using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using OpenScct.Content;
using OpenScct.Content.Json;
using OpenScct.Game.Combat;

namespace OpenScct.Server;

public sealed partial class MapCatalog
{
    public const string CharacterContentFile = "shared/chars";
    private ulong? _characterContentHash;
    private IPillPose? _pillPose;

    /// <summary>The same animated character collision geometry on clients and dedicated servers.</summary>
    public IPillPose PillPose
    {
        get
        {
            lock (_gate) return _pillPose ??= RollPillPose.FromContent(FindCharacterContent());
        }
    }

    /// <summary>
    /// Shared character clip timing/root tracks and skeletal mesh/bone data, independent of the chosen map.
    /// Cached with this catalog's loaded animation source. Texture files are outside this gameplay contract.
    /// </summary>
    public ulong CharacterContentHash
    {
        get
        {
            lock (_gate)
            {
                if (_characterContentHash is { } cached) return cached;
                string? folder = FindCharacterContent();
                var files = new List<(string Path, string Digest)> { ("contract", "character-content-1") };
                if (AnimationPackFolder is { } pack)
                {
                    _ = OpenScct.Content.AnimationBundle.Load(pack) ?? throw new InvalidDataException("The selected animation pack has no manifest.");
                    files.Add(("animation-pack", SvMapHash.Sha256Hex(CanonicalJson.Canonicalize(File.ReadAllBytes(Path.Combine(pack, OpenScct.Content.AnimationBundle.RelativePath)), OpenScct.Content.AnimationBundle.RelativePath))));
                }
                foreach (string relative in new[] { "SPerso/PRO.anim.json", "SPerso/Def.anim.json", "SPerso/ATT_01.glb", "SPerso/DEF_01.glb" })
                {
                    string? path = folder is null ? null : Path.Combine(folder, relative);
                    string digest = "missing";
                    if (path is not null && File.Exists(path))
                    {
                        if (relative.EndsWith(".json", System.StringComparison.Ordinal))
                            digest = SvMapHash.Sha256Hex(CanonicalJson.Canonicalize(File.ReadAllBytes(path), relative));
                        else
                        {
                            using var stream = File.OpenRead(path);
                            digest = System.Convert.ToHexStringLower(SHA256.HashData(stream));
                        }
                    }
                    files.Add((relative, digest));
                }
                return (_characterContentHash = ContentHashOf(SvMapHash.GroupHash(files))).Value;
            }
        }
    }
}
