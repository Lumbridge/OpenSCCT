using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Maps;

namespace OpenScct.Server;

/// <summary>
/// Where an installed game finds its content, set once at start. The launcher builds the player's content from
/// their own install into one folder laid out as svmbuild writes it (<c>shared/</c>, <c>svmap/</c>) and starts the
/// game with <c>--svm-content=&lt;folder&gt;</c> (or SVM_CONTENT); the maps a release ships itself (the hub and the
/// test maps) are in <c>maps/</c> next to the exe. A checkout keeps finding <c>.content</c> and <c>fixtures</c> as
/// before: every lookup tries these folders first and then its own.
/// </summary>
public static class ContentPaths
{
    public const string EnvironmentVariable = "SVM_CONTENT";

    /// <summary>The content folder (with shared/ and svmap/), or null when none is set or it does not exist.</summary>
    public static string? Root { get; private set; }

    /// <summary>The desktop modernisations read base assets only from the selected local conversion.</summary>
    public static bool LocalBaseOnly { get; private set; }

    /// <summary>A complete local base was available when the desktop game was configured.</summary>
    public static bool SteamBaseReady { get; private set; }

    /// <summary>
    /// The language of the game's texts, as a BCP 47 primary tag: the one the player's content was built in (the
    /// launcher's text language), <c>en</c> without content. Maps show their translation into it when they have one.
    /// </summary>
    public static string Language { get; private set; } = "en";

    /// <summary>Folders of maps shipped with the build (maps/ next to the exe), searched after the content's svmap.</summary>
    public static IReadOnlyList<string> BuiltInMapRoots { get; private set; } = [];

    /// <summary>
    /// The folder of content packs downloaded from OpenSCCT-Content (docs/CONTENT-PACKS.md), laid out like the
    /// content folder (svmap/, shared/), or null when there is none yet.
    /// </summary>
    public static string? Packs => Existing(PacksFolder);

    /// <summary>Where downloaded packs go (made on the first download): %LOCALAPPDATA%\OpenSCCT\packs unless set.</summary>
    public static string PacksFolder { get; private set; } = DefaultPacksFolder;

    public static string DefaultPacksFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSCCT", "packs");

    /// <summary>
    /// The shared folder: the content's (converted from the player's install), else the downloaded packs', or null.
    /// </summary>
    public static string? Shared =>
        Existing(Root is null ? null : Path.Combine(Root, "shared")) ??
        (LocalBaseOnly ? null : Existing(Path.Combine(PacksFolder, "shared")));

    /// <summary>The content's converted maps, or null.</summary>
    public static string? SvMap => Existing(Root is null ? null : Path.Combine(Root, "svmap"));

    /// <summary>Every map root these paths add: the content's svmap, then the downloaded packs', then the built-in maps.</summary>
    public static IReadOnlyList<string> MapRoots
    {
        get
        {
            var roots = new List<string>();
            if (SvMap is { } svmap)
            {
                roots.Add(svmap);
            }

            // Listed even before the first download, so a catalog made now finds maps downloaded later.
            roots.Add(Path.Combine(PacksFolder, "svmap"));
            // Flatpak keeps its own writable packs and can read maps installed by the host launcher.
            if (LocalBaseOnly && Existing(Environment.GetEnvironmentVariable("OPENSCCT_GODOT_OPTIONAL_MAPS")) is { } hostMaps &&
                !roots.Contains(hostMaps)) roots.Add(hostMaps);

            roots.AddRange(BuiltInMapRoots);
            return roots;
        }
    }

    /// <summary>
    /// Sets the content folder (<paramref name="root"/>, else SVM_CONTENT) and the folder holding the exe, whose
    /// <c>maps</c> holds the built-in maps.
    /// </summary>
    public static void Configure(string? root, string? exeDir, string? packs = null)
    {
        string? chosen = string.IsNullOrWhiteSpace(root) ? Environment.GetEnvironmentVariable(EnvironmentVariable) : root;
        SetPaths(chosen, exeDir, packs ?? DefaultPacksFolder, localOnly: false);
    }

    /// <summary>Resolve the launcher-managed Steam conversion, including when an older Editor passes its packs folder.</summary>
    public static void ConfigureDesktop(string? root, string? exeDir, string? checkoutContent = null, string? packs = null)
    {
        string packRoot = Path.GetFullPath(packs ?? DefaultPacksFolder);
        string? chosen = string.IsNullOrWhiteSpace(root) ? Environment.GetEnvironmentVariable(EnvironmentVariable) : root;
        string? managed = Environment.GetEnvironmentVariable("OPENSCCT_GODOT_CONTENT_DIR");
        // Portable installs can live anywhere. The released game and launcher share the same local data layout.
        if (string.IsNullOrWhiteSpace(managed) && exeDir is not null &&
            Directory.GetParent(exeDir) is { Name: "versions", Parent: { } install })
        {
            managed = Path.Combine(install.FullName, "content");
            packRoot = packs ?? Path.Combine(install.FullName, "packs");
        }
        managed = string.IsNullOrWhiteSpace(managed) ? Path.Combine(packRoot, "..", "content") : managed;
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var folder = new DirectoryInfo(chosen);
            if (string.Equals(folder.Name, "packs", StringComparison.OrdinalIgnoreCase) && folder.Parent is { } parent)
            {
                // A shipped Editor or older launcher requests its legacy downloaded base.
                packRoot = packs ?? folder.FullName;
                chosen = SteamContentInstall.ActiveRoot(Path.Combine(parent.FullName, "content"));
            }
            else if (folder.Parent is { Name: SteamContentInstall.BuildsDirectory, Parent: { } container })
            {
                chosen = SteamContentInstall.ActiveRoot(container.FullName);
                if (container.Parent is { } launcher) packRoot = packs ?? Path.Combine(launcher.FullName, "packs");
            }
        }
        else
        {
            chosen = checkoutContent is not null && Directory.Exists(Path.Combine(checkoutContent, "shared"))
                ? checkoutContent : SteamContentInstall.ActiveRoot(managed);
        }
        SetPaths(chosen, exeDir, packRoot, localOnly: true);
    }

    private static void SetPaths(string? chosen, string? exeDir, string packs, bool localOnly)
    {
        PacksFolder = string.IsNullOrWhiteSpace(packs) ? DefaultPacksFolder : Path.GetFullPath(packs);
        Root = Existing(string.IsNullOrWhiteSpace(chosen) ? null : chosen);
        LocalBaseOnly = localOnly;
        SteamBaseReady = localOnly && Root is not null && SteamContentInstall.IsComplete(Root);
        BuiltInMapRoots = Existing(string.IsNullOrWhiteSpace(exeDir) ? null : Path.Combine(exeDir, "maps")) is { } maps ? [maps] : [];
        Language = Root is null ? "en" : OpenScct.Maps.ContentBuildFile.LanguageTag(OpenScct.Maps.ContentBuildFile.Language(Root));

        // The game's localized text comes from the player's own content (never from the repository).
        // Map-only content roots still use the downloaded shared text, like Shared above.
        if (Root is not null && File.Exists(Path.Combine(Root, OpenScct.Core.Uc.UcText.RelativePath)))
        {
            OpenScct.Core.Uc.UcText.Use(Path.Combine(Root, OpenScct.Core.Uc.UcText.RelativePath));
        }
        else if (!localOnly && Packs is { } downloaded && File.Exists(Path.Combine(downloaded, OpenScct.Core.Uc.UcText.RelativePath)))
        {
            OpenScct.Core.Uc.UcText.Use(Path.Combine(downloaded, OpenScct.Core.Uc.UcText.RelativePath));
        }
        else
        {
            OpenScct.Core.Uc.UcText.Use(null);
        }
    }

    private static string? Existing(string? dir) => dir is not null && Directory.Exists(dir) ? Path.GetFullPath(dir) : null;
}
