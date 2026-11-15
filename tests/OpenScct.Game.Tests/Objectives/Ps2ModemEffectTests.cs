using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class Ps2ModemEffectTests
{
    [Fact]
    public void Runtime_preset_matches_every_independently_decoded_source_component_tag()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Directory.Build.props"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        string path = Environment.GetEnvironmentVariable("OPENSCCT_PS2_MODEM_REFERENCE") ??
            Path.Combine(root, "local", "ps2", "original-modem-effect.json");
        Assert.SkipWhen(!File.Exists(path), "The original PS2 modem reference is local-only; set OPENSCCT_PS2_MODEM_REFERENCE to its decoded JSON.");
        using JsonDocument source = JsonDocument.Parse(File.ReadAllBytes(path));
        var effect = new Ps2ModemEffect { SmokeTexture = new AssetRef("PS2.SourceSmoke"), FireTexture = new AssetRef("PS2.SourceFlash") };
        JsonElement emitters = source.RootElement.GetProperty("emitters");
        Assert.Equal(3, effect.SourceEmitters.Count);
        for (int i = 0; i < 3; i++)
            foreach (JsonProperty property in emitters[i].GetProperty("props").EnumerateObject())
            {
                object actual = effect.SourceEmitters[i].GetType().GetField(property.Name)!.GetValue(effect.SourceEmitters[i])!;
                if (property.Name == "Texture")
                    Assert.Equal(i == 2 ? effect.FireTexture : effect.SmokeTexture, actual);
                else Check(property.Value, actual);
            }
        Assert.Equal(23f, effect.LifeSpan);
        Assert.Equal(16f, effect.Mass);
        Assert.False(effect.bNoDelete);
        Assert.Equal(ENetRole.ROLE_SimulatedProxy, effect.RemoteRole);
    }

    private static void Check(JsonElement expected, object actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.True: Assert.Equal(true, actual); break;
            case JsonValueKind.False: Assert.Equal(false, actual); break;
            case JsonValueKind.Number:
                if (actual is float number) Assert.Equal(expected.GetSingle(), number);
                else Assert.Equal(expected.GetInt32(), Convert.ToInt32(actual));
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in expected.EnumerateObject())
                    Check(property.Value, actual.GetType().GetField(property.Name)!.GetValue(actual)!);
                break;
            case JsonValueKind.Array:
                if (actual is FVector vector)
                {
                    Assert.Equal(expected[0].GetSingle(), vector.X);
                    Assert.Equal(expected[1].GetSingle(), vector.Y);
                    Assert.Equal(expected[2].GetSingle(), vector.Z);
                }
                else if (actual is FColor color)
                {
                    Assert.Equal(expected[0].GetByte(), color.B);
                    Assert.Equal(expected[1].GetByte(), color.G);
                    Assert.Equal(expected[2].GetByte(), color.R);
                    Assert.Equal(expected[3].GetByte(), color.A);
                }
                else
                {
                    var list = Assert.IsAssignableFrom<IList>(actual);
                    Assert.Equal(expected.GetArrayLength(), list.Count);
                    for (int i = 0; i < list.Count; i++) Check(expected[i], list[i]!);
                }
                break;
            default: Assert.Fail($"Unhandled source value {expected}"); break;
        }
    }
}
