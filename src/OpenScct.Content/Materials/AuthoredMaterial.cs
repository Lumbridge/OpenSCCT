using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenScct.Content.Materials;

/// <summary>A texture's interpretation is per binding. Data maps default to linear sampling.</summary>
public sealed record MaterialTexture
{
    public string File { get; set; } = "";
    public string Channel { get; set; } = "r";
    public bool Srgb { get; set; }
    public bool Invert { get; set; }
}

/// <summary>Scene-clock animation. All channels in a texture set share the same cell and UV transform.</summary>
public sealed record MaterialAnimation
{
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public int Frames { get; set; } = 1;
    public float Fps { get; set; } = 12;
    public bool Loop { get; set; } = true;
    public bool PingPong { get; set; }
    public float Phase { get; set; }
    public float ScrollU { get; set; }
    public float ScrollV { get; set; }
    public float Rotate { get; set; }

    public int Frame(double seconds)
    {
        int count = Math.Max(1, Frames);
        double step = Math.Max(0, Math.Floor(seconds * Fps + Phase));
        int period = PingPong && count > 1 ? 2 * count - 2 : count;
        int frame = (int)(Loop ? step % period : Math.Min(step, PingPong && count > 1 ? period : period - 1));
        return frame < count ? frame : period - frame;
    }
}

/// <summary>Versioned authored material; stock material flags continue to use the original rendering path.</summary>
public sealed record AuthoredMaterial
{
    public const int CurrentVersion = 1;
    public static readonly string[] Slots = ["baseColor", "normal", "height", "roughness", "metallic", "specular", "emission", "ao", "opacity", "detailColor", "detailNormal", "environment"];
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, MaterialTexture> Textures { get; set; } = new(StringComparer.Ordinal);
    public float[] BaseColor { get; set; } = [1, 1, 1];
    public float Opacity { get; set; } = 1;
    public string Surface { get; set; } = "opaque";
    public float Cutoff { get; set; } = 0.5f;
    public bool TwoSided { get; set; }
    public bool Unlit { get; set; }
    public float NormalStrength { get; set; } = 1;
    public bool NormalDirectX { get; set; }
    public bool BumpFromHeight { get; set; }
    public float Roughness { get; set; } = 0.85f;
    public float Metallic { get; set; }
    public float Specular { get; set; } = 0.5f;
    public float[] SpecularColor { get; set; } = [1, 1, 1];
    public float[] EmissionColor { get; set; } = [1, 1, 1];
    public float EmissionEnergy { get; set; }
    public bool BakeEmission { get; set; }
    public float AoStrength { get; set; } = 1;
    public float Clearcoat { get; set; }
    public float ClearcoatGloss { get; set; } = 0.5f;
    public float Anisotropy { get; set; }
    public float Refraction { get; set; }
    public float DetailStrength { get; set; }
    public float ParallaxDepth { get; set; }
    public int ParallaxSteps { get; set; } = 16;
    public int Subdivision { get; set; }
    public float Displacement { get; set; }
    public float HeightMidpoint { get; set; } = 0.5f;
    public bool DisplacementCollision { get; set; }
    public float TileU { get; set; } = 1;
    public float TileV { get; set; } = 1;
    public float OffsetU { get; set; }
    public float OffsetV { get; set; }
    public float Rotation { get; set; }
    public bool Triplanar { get; set; }
    public MaterialAnimation Animation { get; set; } = new();

    public AuthoredMaterial Copy() => JsonSerializer.Deserialize<AuthoredMaterial>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    public JsonObject ToJson() => JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();
    public static AuthoredMaterial? From(JsonNode? node) => node is JsonObject ? node.Deserialize<AuthoredMaterial>(JsonOptions) : null;
    public static AuthoredMaterial? From(JsonElement node) => node.ValueKind == JsonValueKind.Object ? node.Deserialize<AuthoredMaterial>(JsonOptions) : null;

    public IReadOnlyList<string> Problems()
    {
        var errors = new List<string>();
        if (Textures is null || Animation is null || BaseColor is null || SpecularColor is null || EmissionColor is null)
            return ["Texture bindings, animation and colour values cannot be null."];
        if (Version != CurrentVersion) errors.Add($"Unsupported authored material version {Version}.");
        if (Surface is not ("opaque" or "masked" or "translucent")) errors.Add("Surface must be opaque, masked or translucent.");
        foreach (var property in GetType().GetProperties().Where(p => p.PropertyType == typeof(float)))
            if (!float.IsFinite((float)property.GetValue(this)!)) errors.Add(property.Name + " must be finite.");
        foreach (float[] color in new[] { BaseColor, SpecularColor, EmissionColor })
            if (color.Length != 3 || color.Any(c => !float.IsFinite(c) || c < 0)) errors.Add("Colours need three finite nonnegative components.");
        if (Subdivision is < 0 or > 6) errors.Add("Subdivision must be between 0 and 6.");
        if (ParallaxSteps is < 4 or > 64) errors.Add("Parallax steps must be between 4 and 64.");
        foreach (float value in new[] { Opacity, Roughness, Metallic, Specular, Cutoff, AoStrength, Clearcoat, ClearcoatGloss, Anisotropy, HeightMidpoint })
            if (value < 0 || value > 1) errors.Add("Surface factors must be between zero and one.");
        if (NormalStrength < 0 || EmissionEnergy < 0 || Refraction < 0 || DetailStrength < 0 || ParallaxDepth < 0) errors.Add("Material strengths must be nonnegative.");
        if ((Displacement != 0 || ParallaxDepth > 0 || BumpFromHeight) && !Textures.ContainsKey("height")) errors.Add("Depth and height-to-normal conversion need a height map.");
        if (Animation.Columns is < 1 or > 128 || Animation.Rows is < 1 or > 128 || Animation.Frames < 1 || Animation.Frames > Animation.Columns * Animation.Rows)
            errors.Add("Animation frames must fit the atlas grid (1–128 rows/columns).");
        if (!float.IsFinite(Animation.Fps) || Animation.Fps < 0 || Animation.Fps > 240) errors.Add("Animation FPS must be between 0 and 240.");
        if (new[] { Animation.Phase, Animation.ScrollU, Animation.ScrollV, Animation.Rotate }.Any(f => !float.IsFinite(f))) errors.Add("Animation values must be finite.");
        if (Triplanar && ParallaxDepth != 0) errors.Add("Parallax requires UV projection; disable triplanar or parallax.");
        if (Triplanar && Displacement != 0) errors.Add("Real displacement requires UV projection so every instance and collider uses the same height samples.");
        if (Displacement != 0 && (Animation.Frames > 1 || Animation.ScrollU != 0 || Animation.ScrollV != 0 || Animation.Rotate != 0)) errors.Add("Build-time displacement requires a static texture set.");
        foreach ((string slot, MaterialTexture binding) in Textures)
        {
            if (binding is null || binding.File is null) { errors.Add($"{slot} needs a texture binding and file."); continue; }
            if (!Slots.Contains(slot)) errors.Add($"Unknown texture slot {slot}.");
            if (binding.Channel is not ("r" or "g" or "b" or "a" or "luminance")) errors.Add($"Invalid channel for {slot}.");
            if (binding.File.Length == 0 || binding.File.Contains("\\", StringComparison.Ordinal) || binding.File.StartsWith("/", StringComparison.Ordinal) || binding.File.Split('/').Any(p => p is ".." or ".") || binding.File.Contains(':', StringComparison.Ordinal))
                errors.Add($"{slot} must reference a relative project texture.");
        }
        return errors;
    }
}
