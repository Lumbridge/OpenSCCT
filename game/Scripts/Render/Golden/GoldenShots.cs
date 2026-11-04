using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScctGame.Render.Golden;

/// <summary>
/// One view to render: recorded in the original (tools/golden/capture_game.py, local/golden/game/&lt;Map&gt;/cameras.json),
/// or asked for by a map picture request (<c>--svm-mapshot</c>, which has no pawn and may name a <see cref="Look"/>).
/// </summary>
public sealed record GoldenShot(
    string Id,
    string Kind,
    string Image,
    int Width,
    int Height,
    FVector CameraLocation,
    FRotator CameraRotation,
    float Fov,
    FVector PawnLocation,
    FRotator PawnRotation,
    FVector PawnPrePivot,
    float PawnCollisionHeight,
    float TimeSeconds,
    string? PawnAnim,
    float PawnAnimFrame,
    string? Zone,
    bool BehindView = true,
    bool HasPawn = true,
    string Look = GoldenShots.PlainLook);

/// <summary>
/// Reads the camera records the capture tool writes (format svm.golden-cameras/1). Map picture requests use the same
/// format with <c>pawn</c> left out and an optional <c>look</c> per shot.
/// </summary>
public static class GoldenShots
{
    public const string Format = "svm.golden-cameras/1";

    /// <summary>The looks a shot may ask for: the normal view and the goggles' night vision and thermal.</summary>
    public const string PlainLook = "plain", NightVisionLook = "nightvision", ThermalLook = "thermal";

    /// <summary>PlayerController.RendMap of a look, or null for an unknown one.</summary>
    public static int? RendMapOf(string look) => look switch
    {
        PlainLook => RenderK.RendMapNormal,
        NightVisionLook => RenderK.RendMapNight,
        ThermalLook => RenderK.RendMapThermal,
        _ => null,
    };

    /// <summary>The side the captures were played as ("spy" or "merc"; spy when the file does not say).</summary>
    public static string TeamOf(string camerasJson)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(camerasJson));
        return doc.RootElement.TryGetProperty("team", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "spy" : "spy";
    }

    public static List<GoldenShot> Load(string camerasJson)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(camerasJson));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement f) || f.GetString() != Format)
        {
            throw new InvalidDataException($"{camerasJson}: not {Format}");
        }

        var shots = new List<GoldenShot>();
        foreach (JsonProperty p in root.GetProperty("shots").EnumerateObject())
        {
            JsonElement s = p.Value;
            JsonElement cam = s.GetProperty("camera");
            bool hasPawn = s.TryGetProperty("pawn", out JsonElement pawn) && pawn.ValueKind == JsonValueKind.Object;
            string? anim = null;
            float animFrame = 0f;
            if (hasPawn && pawn.TryGetProperty("anim", out JsonElement a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0)
            {
                // [channel, sequence, frame, rate, alpha] of the base channel.
                JsonElement ch0 = a[0];
                anim = ch0[1].GetString();
                animFrame = ch0[2].GetSingle();
            }

            shots.Add(new GoldenShot(
                p.Name,
                s.TryGetProperty("kind", out JsonElement k) ? k.GetString() ?? "" : "",
                s.TryGetProperty("image", out JsonElement image) && image.GetString() is { Length: > 0 } file ? file : p.Name + ".png",
                s.GetProperty("width").GetInt32(),
                s.GetProperty("height").GetInt32(),
                Vec(cam.GetProperty("location")),
                Rot(cam.GetProperty("rotation")),
                cam.GetProperty("fov").GetSingle(),
                hasPawn ? Vec(pawn.GetProperty("location")) : default,
                hasPawn ? Rot(pawn.GetProperty("rotation")) : default,
                hasPawn && pawn.TryGetProperty("prePivot", out JsonElement pp) && pp.ValueKind == JsonValueKind.Array ? Vec(pp) : default,
                hasPawn && pawn.TryGetProperty("collisionHeight", out JsonElement ch) ? ch.GetSingle() : 89f,
                s.TryGetProperty("timeSeconds", out JsonElement ts) && ts.ValueKind == JsonValueKind.Number ? ts.GetSingle() : 0f,
                anim,
                animFrame,
                s.TryGetProperty("zone", out JsonElement z) && z.ValueKind == JsonValueKind.String ? z.GetString() : null,
                !cam.TryGetProperty("behindView", out JsonElement bv) || bv.ValueKind != JsonValueKind.Number || bv.GetInt32() != 0,
                hasPawn,
                s.TryGetProperty("look", out JsonElement look) && look.ValueKind == JsonValueKind.String ? look.GetString() ?? PlainLook : PlainLook));
        }

        return shots;
    }

    private static FVector Vec(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());

    private static FRotator Rot(JsonElement v) => new(v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32());
}
