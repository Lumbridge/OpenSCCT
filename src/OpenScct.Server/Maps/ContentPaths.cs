using System;
using System.Collections.Generic;
using System.IO;

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
        Existing(Root is null ? null : Path.Combine(Root, "shared")) ?? Existing(Packs is null ? null : Path.Combine(Packs, "shared"));

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
        PacksFolder = string.IsNullOrWhiteSpace(packs) ? DefaultPacksFolder : Path.GetFullPath(packs);
        string? chosen = string.IsNullOrWhiteSpace(root) ? Environment.GetEnvironmentVariable(EnvironmentVariable) : root;
        Root = Existing(string.IsNullOrWhiteSpace(chosen) ? null : Path.GetFullPath(chosen));
        BuiltInMapRoots = Existing(string.IsNullOrWhiteSpace(exeDir) ? null : Path.Combine(exeDir, "maps")) is { } maps ? [maps] : [];

        // The game's localized text comes from the player's own content (never from the repository).
        // (or from downloaded packs when the player has not converted an install).
        if (Root is not null)
        {
            OpenScct.Core.Uc.UcText.Use(Path.Combine(Root, OpenScct.Core.Uc.UcText.RelativePath));
        }
        else if (Packs is { } downloaded && File.Exists(Path.Combine(downloaded, OpenScct.Core.Uc.UcText.RelativePath)))
        {
            OpenScct.Core.Uc.UcText.Use(Path.Combine(downloaded, OpenScct.Core.Uc.UcText.RelativePath));
        }
    }

    private static string? Existing(string? dir) => dir is not null && Directory.Exists(dir) ? Path.GetFullPath(dir) : null;
}
