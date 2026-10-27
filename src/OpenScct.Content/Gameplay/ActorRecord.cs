using System;
using OpenScct.Core;

namespace OpenScct.Content;

/// <summary>
/// One placed actor of <c>sim/gameplay.json</c>: its id (the original export name), class, and the property values
/// that differ from the class defaults.
/// </summary>
public sealed class ActorRecord
{
    internal ActorRecord(int index, string id, string classPath, PropBag props, int? sourceExport)
    {
        Index = index;
        Id = id;
        Class = classPath;
        Props = props;
        SourceExport = sourceExport;
        int dot = classPath.IndexOf('.', StringComparison.Ordinal);
        ClassPackage = classPath[..dot];
        ClassName = classPath[(dot + 1)..];
    }

    /// <summary>Position in level order (<c>Level.Actors</c>): 0 is the LevelInfo, placed actors follow.</summary>
    public int Index { get; }

    /// <summary>Original export name (<c>SFlag28768</c>), unique per map ignoring ASCII case.</summary>
    public string Id { get; }

    /// <summary><c>Package.Class</c> with the original names (<c>SBase.SFlag</c>).</summary>
    public string Class { get; }

    /// <summary>The package part of <see cref="Class"/> (<c>SBase</c>).</summary>
    public string ClassPackage { get; }

    /// <summary>The class part of <see cref="Class"/> (<c>SFlag</c>).</summary>
    public string ClassName { get; }

    /// <summary>Property values that differ from the class defaults.</summary>
    public PropBag Props { get; }

    /// <summary>1-based export index in the original map package, when converted.</summary>
    public int? SourceExport { get; }

    public bool IsLevelInfo => Index == 0;

    /// <summary>The effective Tag: the stored one (converters always write it), else the class name as in UE2.</summary>
    public string Tag =>
        Props.TryGetValue("Tag", out PropValue v) && v.TryGetString(out string s) && GameplayDoc.IsName(s) ? s : ClassName;

    /// <summary>The Event this actor fires, or null when it fires none.</summary>
    public string? Event =>
        Props.TryGetValue("Event", out PropValue v) && v.TryGetString(out string s) && GameplayDoc.IsName(s) ? s : null;

    /// <summary>The stored Location (zero when the class default applies).</summary>
    public FVector Location => Props.GetVector("Location");

    /// <summary>The stored Rotation (zero when the class default applies).</summary>
    public FRotator Rotation => Props.GetRotator("Rotation");

    public override string ToString() => $"{Id} ({Class})";
}
