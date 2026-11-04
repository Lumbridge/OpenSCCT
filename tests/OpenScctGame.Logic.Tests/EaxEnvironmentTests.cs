using System;
using System.Collections.Generic;
using OpenScctGame.Audio;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class EaxEnvironmentTests
{
    [Fact]
    public void PreservesIndependentEarlyLateSpectralAndDirectionalProperties()
    {
        float[] values = EaxEnvironment.FromParameters(new Dictionary<string, float>
        {
            ["EnvironmentSize"] = 2, ["EnvironmentDiffusion"] = 0.25f,
            ["Room"] = -1000, ["RoomHF"] = -2000, ["RoomLF"] = -500,
            ["DecayTime"] = 0.2f, ["DecayHFRatio"] = 0.4f, ["DecayLFRatio"] = 1.8f,
            ["Reflections"] = -600, ["ReflectionsDelay"] = 0.003f, ["ReflectionsPanX"] = -0.5f,
            ["Reverb"] = 900, ["ReverbDelay"] = 0.001f, ["ReverbPanZ"] = 0.75f,
            ["EchoTime"] = 0.12f, ["EchoDepth"] = 0.8f, ["ModulationTime"] = 2, ["ModulationDepth"] = 0.2f,
            ["AirAbsorptionHF"] = -10, ["HFReference"] = 6000, ["LFReference"] = 150,
            ["RoomRolloffFactor"] = 0.9f, ["bDecayHFLimit"] = 0,
        }).ToArray();
        Assert.Equal(27, values.Length);
        Assert.Equal(0.5f, values[0]);
        Assert.Equal(0.31622776f, values[2], 6);
        Assert.Equal(0.1f, values[3], 6);
        Assert.Equal(0.5623413f, values[4], 6);
        Assert.Equal(0.2f, values[5]);
        Assert.Equal(1.8f, values[7]);
        Assert.Equal(0.003f, values[9]);
        Assert.Equal(-0.5f, values[10]);
        Assert.Equal(2.818383f, values[13], 5);
        Assert.Equal(0.001f, values[14]);
        Assert.Equal(0.75f, values[17]);
        Assert.Equal(0.8f, values[19]);
        Assert.Equal(2, values[20]);
        Assert.Equal(0.9f, values[25]);
        Assert.Equal(0, values[26]);
    }

    [Fact]
    public void DefaultsComeFromOriginalListenerClassAndInvalidDataCannotReachNativeDsp()
    {
        EaxEnvironment environment = EaxEnvironment.FromParameters(new Dictionary<string, float>());
        float[] values = environment.ToArray();
        Assert.Equal(1, values[0]);
        Assert.Equal(MathF.Pow(10, -100f / 2000), values[3]);
        Assert.Equal(1, values[4]);
        Assert.Equal(0.25f, values[20]);
        Assert.Equal(1, values[26]);
        Assert.Equal(-5, environment.AirAbsorptionMillibels, 3);
        Assert.Equal(-100, EaxEnvironment.FromParameters(new Dictionary<string, float>
            { ["AirAbsorptionHF"] = -100 }).AirAbsorptionMillibels);
        Assert.Equal(0.1f, environment.RoomRolloff);
        EaxEnvironment negativeRolloff = EaxEnvironment.FromParameters(new Dictionary<string, float>
            { ["RoomRolloffFactor"] = -2 });
        Assert.True(negativeRolloff.IsOriginalValid);
        Assert.Equal(0.1f, negativeRolloff.RoomRolloff);
        Assert.Equal(0, EaxEnvironment.DriverDefault.RoomRolloff);
        Assert.Equal(0.179f, EaxEnvironment.NullZoneEffect.ToArray()[9]);
        Assert.Equal(63, EaxEnvironment.DriverDefault.ToEaxArray()[27]);
        Assert.Equal(63, EaxEnvironment.NullZoneEffect.ToEaxArray()[27]);
        Assert.Equal(127, environment.ToEaxArray()[27]);
        Assert.Throws<ArgumentException>(() => EaxEnvironment.FromParameters(new Dictionary<string, float> { ["Room"] = float.NaN }));
    }

    [Theory]
    [InlineData("EnvironmentSize", 0.7f)]
    [InlineData("EnvironmentDiffusion", 1.2f)]
    [InlineData("EchoTime", 0.4f)]
    public void IdentifiesWholeEnvironmentUpdatesTheOriginalVendorRejects(string name, float value)
    {
        EaxEnvironment environment = EaxEnvironment.FromParameters(new Dictionary<string, float> { [name] = value });
        Assert.False(environment.IsOriginalValid);
        Assert.Equal(name, environment.InvalidProperty);
    }

    [Fact]
    public void SourceOcclusionSeparatesDirectAndRoomLowAndHighBands()
    {
        EaxSourceLevels direct = EaxSourceLevels.Occlusion(0.1f, 0.5f, 1);
        EaxSourceLevels room = EaxSourceLevels.Occlusion(0.1f, 0.5f, 1.5f);
        Assert.Equal(MathF.Sqrt(0.1f), direct.Gain, 6);
        Assert.Equal(0.1f, direct.Gain * direct.GainHF, 6);
        Assert.Equal(0.1f, room.Gain, 6);
        Assert.Equal(MathF.Pow(0.1f, 1.5f), room.Gain * room.GainHF, 6);
        Assert.Equal(new EaxSourceLevels(1, 1), EaxSourceLevels.Occlusion(1, 0.4f, 0.43f));
    }

    [Fact]
    public void SourceDistanceAndAirRemainIndependentFromDirectRolloff()
    {
        Assert.Equal(1, EaxSourceLevels.RoomDistanceGain(100, 100, 1));
        Assert.Equal(0.5f, EaxSourceLevels.RoomDistanceGain(300, 100, 0.5f));
        Assert.Equal(1, EaxSourceLevels.RoomDistanceGain(300, 100, 0));
        EaxEnvironment environment = EaxEnvironment.FromParameters(new Dictionary<string, float> { ["DecayTime"] = 0.4f });
        Assert.Equal(new EaxSourceGains(1, 1, 1), environment.SourceGains(100, 100));
        EaxSourceGains distant = environment.SourceGains(1000, 100);
        Assert.InRange(distant.Direct, 0.181f, 0.183f);
        Assert.InRange(distant.Room, 0.523f, 0.527f);
        Assert.InRange(distant.RoomHF, 0.99f, 1);
    }

    // Independently evaluated through the pinned native reAL EAX3 helper with
    // the actual Room map packet, not values computed by this managed method.
    [Theory]
    [InlineData(100, 1f, 1f, 1f)]
    [InlineData(200, 0.6668067574501038f, 0.908866286277771f, 1f)]
    [InlineData(400, 0.4004054665565491f, 0.7691304087638855f, 0.9988493919372559f)]
    [InlineData(1000, 0.18197008967399597f, 0.5248074531555176f, 0.9965521097183228f)]
    public void ManagedSourceLevelsMatchOriginalNativeReferenceVectors(float distance, float direct, float room, float roomHF)
    {
        EaxEnvironment environment = EaxEnvironment.FromParameters(new Dictionary<string, float> { ["DecayTime"] = 0.4f });
        Assert.Equal(new EaxSourceGains(direct, room, roomHF), environment.SourceGains(distance, 100));
    }
}
