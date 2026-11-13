using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Audio;

namespace OpenScctGame.Smoke;

/// <summary>Exercises the browser's built-in mixer path without the native EAX extension.</summary>
public sealed class BrowserAudioSmoke : SmokeTest
{
    public const string TestName = "browser-audio";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        using (var shared = new ReverbBuses(null, browserBackend: true)) { }
        int before = AudioServer.BusCount;
        var map = new MapAudioDoc();
        map.Zones["Room"] = new MapSoundZone("Room", null, 128, 64, "Room", new Dictionary<string, float>
        { ["DecayTime"] = 2.5f, ["RoomHF"] = -500, ["ReflectionsDelay"] = 0.06f });
        map.Zones["Bad"] = new MapSoundZone("Bad", null, 128, 64, "Bad", new Dictionary<string, float> { ["DecayTime"] = 50 });
        using (var buses = new ReverbBuses(map, browserBackend: true))
        {
            if (!buses.BackendVersion.StartsWith("Browser", StringComparison.Ordinal) || buses.ReverbBusCount != 1)
                return SmokeResult.Fail("browser mixer did not create its built-in environment effect");
            int environmentBus = AudioServer.GetBusIndex(buses.EnvironmentBus!);
            if (AudioServer.GetBusEffect(environmentBus, 0) is not AudioEffectReverb reverb || reverb.Dry != 0 || reverb.Wet != 1)
                return SmokeResult.Fail("room bus would duplicate dry audio or omit its wet signal");
            ulong effect = reverb.GetInstanceId();
            buses.SetEnvironment("Room");
            if (buses.CurrentEnvironment?.DecayTime != 2.5f || Math.Abs(reverb.PredelayMsec - 60) > 0.01)
                return SmokeResult.Fail("authored zone did not configure the browser room");
            EaxSourceRoute? route = buses.CreateSourceRoute(0);
            if (route is null) return SmokeResult.Fail("browser source has no separate direct and room paths");
            route.Set(new EaxSourceLevels(0.5f, 0.2f), new EaxSourceLevels(0.25f, 0.1f), 5000);
            int dryBus = AudioServer.GetBusIndex(route.DryBus), wetBus = AudioServer.GetBusIndex(route.RoomBus);
            if (Math.Abs(AudioServer.GetBusVolumeDb(dryBus) - Mathf.LinearToDb(0.5f)) > 0.01 ||
                Math.Abs(AudioServer.GetBusVolumeDb(wetBus) - Mathf.LinearToDb(0.25f)) > 0.01 ||
                AudioServer.GetBusEffect(wetBus, 0) is not AudioEffectHighShelfFilter { CutoffHz: 5000 } shelf || Math.Abs(shelf.Gain - 0.1f) > 0.001)
                return SmokeResult.Fail("browser source lost its separate gain or HF filter");
            buses.SetEnvironment("Bad");
            if (buses.CurrentEnvironment?.DecayTime != 2.5f) return SmokeResult.Fail("invalid original zone replaced the current room");
            buses.SetEnvironment(null);
            if (!ReferenceEquals(buses.CurrentEnvironment, EaxEnvironment.NullZoneEffect) ||
                AudioServer.GetBusEffect(environmentBus, 0).GetInstanceId() != effect)
                return SmokeResult.Fail("zone change replaced the effect and lost its tail");
        }
        return AudioServer.BusCount == before
            ? SmokeResult.Pass("browser room, source gains/HF filters, invalid-zone handling, tail lifetime and bus cleanup work without native EAX")
            : SmokeResult.Fail("browser audio buses leaked after disposal");
    }
}
