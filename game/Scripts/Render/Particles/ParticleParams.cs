using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScctGame.Render.Particles;

/// <summary>The draw kind of a sub-emitter (its class).</summary>
public enum ParticleKind
{
    Sprite,
    Spark,
    Mesh,
    Beam,
}

/// <summary>A UE2 Range: a pick is uniform between the two ends (spec/native/emitters.md, Data).</summary>
public readonly record struct FxRange(float Min, float Max)
{
    /// <summary>Max + (Min - Max) x FRand, as the engine picks.</summary>
    public float Pick(ref FxRandom rng) => Max + ((Min - Max) * rng.Next());

    public float Average => (Min + Max) * 0.5f;

    public float Larger => MathF.Max(Min, Max);

    public static FxRange From(JsonElement e, FxRange fallback) => e.ValueKind != JsonValueKind.Object ? fallback
        : new FxRange(e.TryGetProperty("Min", out JsonElement a) && a.ValueKind == JsonValueKind.Number ? a.GetSingle() : fallback.Min,
            e.TryGetProperty("Max", out JsonElement b) && b.ValueKind == JsonValueKind.Number ? b.GetSingle() : fallback.Max);
}

/// <summary>A UE2 RangeVector: X, Y and Z picked independently (the engine picks Y, then X, then Z).</summary>
public readonly record struct FxRangeVector(FxRange X, FxRange Y, FxRange Z)
{
    public FVector Pick(ref FxRandom rng)
    {
        float y = Y.Pick(ref rng);
        float x = X.Pick(ref rng);
        float z = Z.Pick(ref rng);
        return new FVector(x, y, z);
    }

    public static FxRangeVector Same(float v) => new(new FxRange(v, v), new FxRange(v, v), new FxRange(v, v));

    public static FxRangeVector From(JsonElement e, FxRangeVector fallback) => e.ValueKind != JsonValueKind.Object ? fallback
        : new FxRangeVector(
            e.TryGetProperty("X", out JsonElement x) ? FxRange.From(x, fallback.X) : fallback.X,
            e.TryGetProperty("Y", out JsonElement y) ? FxRange.From(y, fallback.Y) : fallback.Y,
            e.TryGetProperty("Z", out JsonElement z) ? FxRange.From(z, fallback.Z) : fallback.Z);
}

/// <summary>An FColor (bytes), as particles store it.</summary>
public readonly record struct FxColor(byte R, byte G, byte B, byte A)
{
    public static readonly FxColor White = new(255, 255, 255, 255);
}

/// <summary>The particle stream's own random numbers: a seeded xorshift, uniform in [0, 1) like FRand.</summary>
public struct FxRandom
{
    private uint _state;

    public FxRandom(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

    public float Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return (_state >> 8) / 16777216f;
    }

    /// <summary>A stable seed from a text (FNV-1a).</summary>
    public static uint Seed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        uint h = 2166136261u;
        foreach (char ch in text)
        {
            h = (h ^ ch) * 16777619u;
        }

        return h == 0 ? 1u : h;
    }
}

/// <summary>
/// Every property of one UE2 sub-emitter (ParticleEmitter and its Sprite, Spark, Mesh and Beam subclasses) that the
/// update and the drawing read, taken from its stored values over its class defaults (spec/native/emitters.md).
/// Godot-free.
/// </summary>
public sealed class ParticleParams
{
    public ParticleKind Kind { get; init; }

    public string Class { get; init; } = "";

    public string Name { get; init; } = "";

    // ---- spawning ----
    public int MaxParticles { get; init; } = 10;

    public bool RespawnDeadParticles { get; init; } = true;

    public bool AutomaticInitialSpawning { get; init; } = true;

    public bool Disabled { get; init; }

    public bool TriggerDisabled { get; init; } = true;

    public bool ResetOnTrigger { get; init; }

    public bool AutoDestroy { get; init; }

    public bool AutoReset { get; init; }

    public FxRange AutoResetTimeRange { get; init; }

    public float ParticlesPerSecond { get; init; }

    public float InitialParticlesPerSecond { get; init; }

    public FxRange SpawnOnTriggerRange { get; init; }

    public float SpawnOnTriggerPPS { get; init; }

    public FxRange InitialDelayRange { get; init; }

    public FxRange InitialTimeRange { get; init; }

    public FxRange LifetimeRange { get; init; } = new(4f, 4f);

    public float WarmupTicksPerSecond { get; init; }

    public float RelativeWarmupTime { get; init; }

    public float SecondsBeforeInactive { get; init; } = 1f;

    // ---- start ----
    public FVector StartLocationOffset { get; init; }

    public FxRangeVector StartLocationRange { get; init; }

    public int AddLocationFromOtherEmitter { get; init; } = -1;

    public int UseRotationFrom { get; init; }

    public FRotator RotationOffset { get; init; }

    public FVector RotationNormal { get; init; }

    public int EffectAxis { get; init; }

    public int CoordinateSystem { get; init; }

    public FxRangeVector StartVelocityRange { get; init; }

    public FxRange StartVelocityRadialRange { get; init; }

    public int GetVelocityDirectionFrom { get; init; }

    public int AddVelocityFromOtherEmitter { get; init; } = -1;

    public FxRangeVector AddVelocityMultiplierRange { get; init; } = FxRangeVector.Same(1f);

    public FxRange StartMassRange { get; init; } = new(1f, 1f);

    public FxRangeVector StartSizeRange { get; init; } = FxRangeVector.Same(100f);

    public bool UniformSize { get; init; }

    public FxRangeVector ColorMultiplierRange { get; init; } = FxRangeVector.Same(1f);

    public bool SpinParticles { get; init; }

    public FxRangeVector StartSpinRange { get; init; }

    public FxRangeVector SpinsPerSecondRange { get; init; }

    public FVector SpinCCWorCW { get; init; } = new(0.5f, 0.5f, 0.5f);

    public bool UseRandomSubdivision { get; init; }

    public int SubdivisionStart { get; init; }

    public int SubdivisionEnd { get; init; }

    public FxRangeVector RevolutionCenterOffsetRange { get; init; }

    public FxRangeVector RevolutionsPerSecondRange { get; init; }

    // ---- life ----
    public FVector Acceleration { get; init; }

    public FxRangeVector VelocityLossRange { get; init; }

    public FVector MaxAbsVelocity { get; init; }

    public bool UseRevolution { get; init; }

    public bool UseRevolutionScale { get; init; }

    public (float Time, FVector Value)[] RevolutionScale { get; init; } = [];

    public float RevolutionScaleRepeats { get; init; }

    public bool UseVelocityScale { get; init; }

    public (float Time, FVector Value)[] VelocityScale { get; init; } = [];

    public float VelocityScaleRepeats { get; init; }

    public bool UseSizeScale { get; init; }

    public bool UseRegularSizeScale { get; init; } = true;

    public (float Time, float Size)[] SizeScale { get; init; } = [];

    public float SizeScaleRepeats { get; init; }

    public bool UseColorScale { get; init; }

    public (float Time, FxColor Color)[] ColorScale { get; init; } = [];

    public float ColorScaleRepeats { get; init; }

    public bool FadeIn { get; init; }

    public bool FadeOut { get; init; }

    public float FadeInEndTime { get; init; }

    public float FadeOutStartTime { get; init; }

    /// <summary>FadeInFactor (X, Y, Z, W for R, G, B, A).</summary>
    public (float X, float Y, float Z, float W) FadeInFactor { get; init; } = (1f, 1f, 1f, 1f);

    public (float X, float Y, float Z, float W) FadeOutFactor { get; init; } = (1f, 1f, 1f, 1f);

    // ---- collision ----
    public bool UseCollision { get; init; }

    public FVector ExtentMultiplier { get; init; } = new(1f, 1f, 1f);

    public FxRangeVector DampingFactorRange { get; init; } = FxRangeVector.Same(1f);

    public bool DampRotation { get; init; }

    public FxRangeVector RotationDampingFactorRange { get; init; }

    public bool UseMaxCollisions { get; init; }

    public FxRange MaxCollisions { get; init; }

    public float MinSquaredVelocity { get; init; }

    public int SpawnFromOtherEmitter { get; init; } = -1;

    public int SpawnAmount { get; init; }

    public bool UseSpawnedVelocityScale { get; init; }

    public FxRangeVector SpawnedVelocityScaleRange { get; init; }

    // ---- drawing ----
    public int DrawStyle { get; init; } = 3;

    public bool AlphaTest { get; init; } = true;

    public int AlphaRef { get; init; }

    public bool ZTest { get; init; } = true;

    public bool ZWrite { get; init; }

    public string? Texture { get; init; }

    public int TextureUSubdivisions { get; init; }

    public int TextureVSubdivisions { get; init; }

    public bool BlendBetweenSubdivisions { get; init; }

    public bool UseSubdivisionScale { get; init; }

    public float[] SubdivisionScale { get; init; } = [];

    // ---- sprite ----
    public int UseDirectionAs { get; init; }

    public FVector ProjectionNormal { get; init; } = new(0f, 0f, 1f);

    // ---- spark ----
    public FxRange LineSegmentsRange { get; init; } = new(5f, 5f);

    public FxRange TimeBeforeVisibleRange { get; init; }

    public FxRange TimeBetweenSegmentsRange { get; init; }

    // ---- mesh ----
    public string? StaticMesh { get; init; }

    public bool UseMeshBlendMode { get; init; } = true;

    public bool UseParticleColor { get; init; }

    public bool RenderTwoSided { get; init; }

    // ---- beam ----
    public int DetermineEndPointBy { get; init; }

    public FxRange BeamDistanceRange { get; init; }

    public FVector[] BeamEndPointOffsets { get; init; } = [];

    public float BeamTextureUScale { get; init; } = 1f;

    public float BeamTextureVScale { get; init; } = 1f;

    public int LowFrequencyPoints { get; init; } = 3;

    public FxRangeVector LowFrequencyNoiseRange { get; init; }

    public int HighFrequencyPoints { get; init; } = 10;

    public FxRangeVector HighFrequencyNoiseRange { get; init; }

    /// <summary>USubdivisions x VSubdivisions, at least 1.</summary>
    public int Cells => Math.Max(1, TextureUSubdivisions) * Math.Max(1, TextureVSubdivisions);

    /// <summary>The kind drawn for a sub-emitter class (FlicFlocEmitter and TrailEmitter draw as sprites here).</summary>
    public static ParticleKind KindOf(string cls) =>
        cls.EndsWith("SparkEmitter", StringComparison.Ordinal) ? ParticleKind.Spark
        : cls.EndsWith("MeshEmitter", StringComparison.Ordinal) ? ParticleKind.Mesh
        : cls.EndsWith("BeamEmitter", StringComparison.Ordinal) ? ParticleKind.Beam
        : ParticleKind.Sprite;

    /// <summary>The parameters from a property lookup (stored values over the class defaults).</summary>
    public static ParticleParams From(string cls, string name, Func<string, JsonElement?> get, string? texture)
    {
        ArgumentNullException.ThrowIfNull(get);
        float F(string n, float d = 0f) => get(n) is { ValueKind: JsonValueKind.Number } v ? v.GetSingle() : d;
        int I(string n, int d = 0) => get(n) is { ValueKind: JsonValueKind.Number } v ? v.GetInt32() : d;
        bool B(string n, bool d = false) => get(n) is { } v ? v.ValueKind == JsonValueKind.True : d;
        FxRange R(string n, FxRange d = default) => get(n) is { } v ? FxRange.From(v, d) : d;
        FxRangeVector RV(string n, FxRangeVector d = default) => get(n) is { } v ? FxRangeVector.From(v, d) : d;
        FVector V(string n, FVector d = default) => get(n) is { ValueKind: JsonValueKind.Object } v
            ? new FVector(Num(v, "X", d.X), Num(v, "Y", d.Y), Num(v, "Z", d.Z))
            : d;
        (float, float, float, float) P(string n) => get(n) is { ValueKind: JsonValueKind.Object } v
            ? (Num(v, "X", 1f), Num(v, "Y", 1f), Num(v, "Z", 1f), Num(v, "W", 1f))
            : (1f, 1f, 1f, 1f);
        FRotator Rot(string n) => get(n) is { ValueKind: JsonValueKind.Object } v
            ? new FRotator((int)Num(v, "Pitch"), (int)Num(v, "Yaw"), (int)Num(v, "Roll"))
            : default;
        (float, FVector)[] VectorCurve(string n, string field)
        {
            var list = new List<(float, FVector)>();
            if (get(n) is { ValueKind: JsonValueKind.Array } a)
            {
                foreach (JsonElement s in a.EnumerateArray())
                {
                    JsonElement r = s.TryGetProperty(field, out JsonElement rv) ? rv : default;
                    list.Add((Num(s, "RelativeTime"), new FVector(Num(r, "X"), Num(r, "Y"), Num(r, "Z"))));
                }
            }

            return [.. list];
        }

        var sizes = new List<(float, float)>();
        if (get("SizeScale") is { ValueKind: JsonValueKind.Array } ss)
        {
            foreach (JsonElement s in ss.EnumerateArray())
            {
                sizes.Add((Num(s, "RelativeTime"), Num(s, "RelativeSize")));
            }
        }

        var colours = new List<(float, FxColor)>();
        if (get("ColorScale") is { ValueKind: JsonValueKind.Array } cs)
        {
            foreach (JsonElement c in cs.EnumerateArray())
            {
                JsonElement col = c.TryGetProperty("Color", out JsonElement cc) ? cc : default;
                colours.Add((Num(c, "RelativeTime"), new FxColor(Byte(col, "R"), Byte(col, "G"), Byte(col, "B"), Byte(col, "A"))));
            }
        }

        var subScale = new List<float>();
        if (get("SubdivisionScale") is { ValueKind: JsonValueKind.Array } sub)
        {
            foreach (JsonElement s in sub.EnumerateArray())
            {
                subScale.Add(s.ValueKind == JsonValueKind.Number ? s.GetSingle() : 0f);
            }
        }

        var ends = new List<FVector>();
        if (get("BeamEndPoints") is { ValueKind: JsonValueKind.Array } bep)
        {
            foreach (JsonElement s in bep.EnumerateArray())
            {
                if (s.TryGetProperty("Offset", out JsonElement o))
                {
                    FxRangeVector r = FxRangeVector.From(o, default);
                    ends.Add(new FVector(r.X.Average, r.Y.Average, r.Z.Average));
                }
            }
        }

        string? mesh = get("StaticMesh") is { ValueKind: JsonValueKind.Object } m && m.TryGetProperty("$asset", out JsonElement ma) ? ma.GetString() : null;
        return new ParticleParams
        {
            Kind = KindOf(cls),
            Class = cls,
            Name = name,
            MaxParticles = Math.Clamp(I("MaxParticles", 10), 0, 4096),
            RespawnDeadParticles = B("RespawnDeadParticles", true),
            AutomaticInitialSpawning = B("AutomaticInitialSpawning", true),
            Disabled = B("Disabled"),
            TriggerDisabled = B("TriggerDisabled", true),
            ResetOnTrigger = B("ResetOnTrigger"),
            AutoDestroy = B("AutoDestroy"),
            AutoReset = B("AutoReset"),
            AutoResetTimeRange = R("AutoResetTimeRange"),
            ParticlesPerSecond = F("ParticlesPerSecond"),
            InitialParticlesPerSecond = F("InitialParticlesPerSecond"),
            SpawnOnTriggerRange = R("SpawnOnTriggerRange"),
            SpawnOnTriggerPPS = F("SpawnOnTriggerPPS"),
            InitialDelayRange = R("InitialDelayRange"),
            InitialTimeRange = R("InitialTimeRange"),
            LifetimeRange = R("LifetimeRange", new FxRange(4f, 4f)),
            WarmupTicksPerSecond = F("WarmupTicksPerSecond"),
            RelativeWarmupTime = F("RelativeWarmupTime"),
            SecondsBeforeInactive = F("SecondsBeforeInactive", 1f),
            StartLocationOffset = V("StartLocationOffset"),
            StartLocationRange = RV("StartLocationRange"),
            AddLocationFromOtherEmitter = I("AddLocationFromOtherEmitter", -1),
            UseRotationFrom = I("UseRotationFrom"),
            RotationOffset = Rot("RotationOffset"),
            RotationNormal = V("RotationNormal"),
            EffectAxis = I("EffectAxis"),
            CoordinateSystem = I("CoordinateSystem"),
            StartVelocityRange = RV("StartVelocityRange"),
            StartVelocityRadialRange = R("StartVelocityRadialRange"),
            GetVelocityDirectionFrom = I("GetVelocityDirectionFrom"),
            AddVelocityFromOtherEmitter = I("AddVelocityFromOtherEmitter", -1),
            AddVelocityMultiplierRange = RV("AddVelocityMultiplierRange", FxRangeVector.Same(1f)),
            StartMassRange = R("StartMassRange", new FxRange(1f, 1f)),
            StartSizeRange = RV("StartSizeRange", FxRangeVector.Same(100f)),
            UniformSize = B("UniformSize"),
            ColorMultiplierRange = RV("ColorMultiplierRange", FxRangeVector.Same(1f)),
            SpinParticles = B("SpinParticles"),
            StartSpinRange = RV("StartSpinRange"),
            SpinsPerSecondRange = RV("SpinsPerSecondRange"),
            SpinCCWorCW = V("SpinCCWorCW", new FVector(0.5f, 0.5f, 0.5f)),
            UseRandomSubdivision = B("UseRandomSubdivision"),
            SubdivisionStart = I("SubdivisionStart"),
            SubdivisionEnd = I("SubdivisionEnd"),
            RevolutionCenterOffsetRange = RV("RevolutionCenterOffsetRange"),
            RevolutionsPerSecondRange = RV("RevolutionsPerSecondRange"),
            Acceleration = V("Acceleration"),
            VelocityLossRange = RV("VelocityLossRange"),
            MaxAbsVelocity = V("MaxAbsVelocity"),
            UseRevolution = B("UseRevolution"),
            UseRevolutionScale = B("UseRevolutionScale"),
            RevolutionScale = VectorCurve("RevolutionScale", "RelativeRevolution"),
            RevolutionScaleRepeats = F("RevolutionScaleRepeats"),
            UseVelocityScale = B("UseVelocityScale"),
            VelocityScale = VectorCurve("VelocityScale", "RelativeVelocity"),
            VelocityScaleRepeats = F("VelocityScaleRepeats"),
            UseSizeScale = B("UseSizeScale"),
            UseRegularSizeScale = B("UseRegularSizeScale", true),
            SizeScale = [.. sizes],
            SizeScaleRepeats = F("SizeScaleRepeats"),
            UseColorScale = B("UseColorScale"),
            ColorScale = [.. colours],
            ColorScaleRepeats = F("ColorScaleRepeats"),
            FadeIn = B("FadeIn"),
            FadeOut = B("FadeOut"),
            FadeInEndTime = F("FadeInEndTime"),
            FadeOutStartTime = F("FadeOutStartTime"),
            FadeInFactor = P("FadeInFactor"),
            FadeOutFactor = P("FadeOutFactor"),
            UseCollision = B("UseCollision"),
            ExtentMultiplier = V("ExtentMultiplier", new FVector(1f, 1f, 1f)),
            DampingFactorRange = RV("DampingFactorRange", FxRangeVector.Same(1f)),
            DampRotation = B("DampRotation"),
            RotationDampingFactorRange = RV("RotationDampingFactorRange"),
            UseMaxCollisions = B("UseMaxCollisions"),
            MaxCollisions = R("MaxCollisions"),
            MinSquaredVelocity = F("MinSquaredVelocity"),
            SpawnFromOtherEmitter = I("SpawnFromOtherEmitter", -1),
            SpawnAmount = I("SpawnAmount"),
            UseSpawnedVelocityScale = B("UseSpawnedVelocityScale"),
            SpawnedVelocityScaleRange = RV("SpawnedVelocityScaleRange"),
            DrawStyle = I("DrawStyle", 3),
            AlphaTest = B("AlphaTest", true),
            AlphaRef = I("AlphaRef"),
            ZTest = B("ZTest", true),
            ZWrite = B("ZWrite"),
            Texture = texture,
            TextureUSubdivisions = I("TextureUSubdivisions"),
            TextureVSubdivisions = I("TextureVSubdivisions"),
            BlendBetweenSubdivisions = B("BlendBetweenSubdivisions"),
            UseSubdivisionScale = B("UseSubdivisionScale"),
            SubdivisionScale = [.. subScale],
            UseDirectionAs = I("UseDirectionAs"),
            ProjectionNormal = V("ProjectionNormal", new FVector(0f, 0f, 1f)),
            LineSegmentsRange = R("LineSegmentsRange", new FxRange(5f, 5f)),
            TimeBeforeVisibleRange = R("TimeBeforeVisibleRange"),
            TimeBetweenSegmentsRange = R("TimeBetweenSegmentsRange"),
            StaticMesh = mesh,
            UseMeshBlendMode = B("UseMeshBlendMode", true),
            UseParticleColor = B("UseParticleColor"),
            RenderTwoSided = B("RenderTwoSided"),
            DetermineEndPointBy = I("DetermineEndPointBy"),
            BeamDistanceRange = R("BeamDistanceRange"),
            BeamEndPointOffsets = [.. ends],
            BeamTextureUScale = F("BeamTextureUScale", 1f),
            BeamTextureVScale = F("BeamTextureVScale", 1f),
            LowFrequencyPoints = I("LowFrequencyPoints", 3),
            LowFrequencyNoiseRange = RV("LowFrequencyNoiseRange"),
            HighFrequencyPoints = I("HighFrequencyPoints", 10),
            HighFrequencyNoiseRange = RV("HighFrequencyNoiseRange"),
        };
    }

    /// <summary>The parameters from a sub-emitter's own values over its class defaults (both may be absent).</summary>
    public static ParticleParams From(string cls, string name, JsonElement own, JsonElement? defaults, string? texture) =>
        From(cls, name, n =>
        {
            if (own.ValueKind == JsonValueKind.Object && own.TryGetProperty(n, out JsonElement v))
            {
                return v;
            }

            return defaults is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(n, out JsonElement dv) ? dv : null;
        }, texture);

    private static float Num(JsonElement e, string name, float fallback = 0f) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static byte Byte(JsonElement e, string name) => (byte)Math.Clamp((int)Num(e, name), 0, 255);
}
