using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PassiveSecurityNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Fact]
    public void AquariusLaserCrossingMarksItsAuthoredPositionOnBothMercClients()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("AquaE") is null, "Aquarius content is required.");
        using var scene = new NetworkScene("AquaE");
        SLaserTrigger? laser = null;
        SAlarm? alarm = null;
        foreach (SLaserTrigger candidate in scene.Match.Server.World.Level.AllActors<SLaserTrigger>())
        {
            if (candidate.bHidden || candidate.bLaserHidden || candidate.LaserLength <= 0 || candidate.iPriority < 5) continue;
            foreach (SAlarm target in scene.Match.Server.World.Level.AllActors<SAlarm>())
                if (!target.bAlarmOn && target.Tag == candidate.Event && !candidate.Event.IsNone)
                {
                    laser = candidate;
                    alarm = target;
                    break;
                }
            if (laser is not null) break;
        }
        Assert.NotNull(laser);
        Assert.NotNull(alarm);
        var spy = Assert.IsAssignableFrom<SBasePawn>(scene.Match.ServerController(scene.Match.Bots[0])!.Pawn);
        FVector before = spy.Location;
        spy.Location = (laser.Location + laser.LaserEndLocation) * 0.5f;
        laser.Rotation.GetAxes(out _, out FVector across, out _);
        spy.Velocity = across * 350f;
        laser.CheckPresence();
        spy.Location = before;
        spy.Velocity = default;
        Assert.True(alarm.bAlarmOn);
        Assert.Same(laser, alarm.TriggerActor);
        scene.Step(60);
        foreach (var merc in scene.Match.Bots.Skip(2))
            Assert.Contains(laser.Location, Assert.IsType<SHud>(merc.Controller!.myHUD).MalfunctionGamePlayObjects);

        alarm.SetAlarmListening();
        scene.Step(60);
        foreach (var merc in scene.Match.Bots.Skip(2))
            Assert.DoesNotContain(laser.Location, Assert.IsType<SHud>(merc.Controller!.myHUD).MalfunctionGamePlayObjects);
    }

    [Theory]
    [InlineData("laser")]
    [InlineData("camera")]
    [InlineData("sensor")]
    public void AlarmMarkerReachesBothMercsAndClearsWhenAlarmExpires(string kind)
    {
        using var scene = new NetworkScene();
        SBasicGamePlayObject device = kind switch
        {
            "laser" => new SLaserTrigger(),
            "camera" => new SCamera { bAutoWallSupport = false, PatrolAngle = 0 },
            _ => new SVolumetricSensor(),
        };
        device.bAlwaysRelevant = true;
        device.bNoDelete = false;
        var location = new FVector(2000f, 2000f, 600f);
        Assert.Same(device, scene.Match.Server.World.Level.SpawnActor(device, location, default));
        SAlarm alarm = PlaceAlarm(scene.Match.Server.World, scene.Match.Server.Driver, true);
        foreach (var bot in scene.Match.Bots) PlaceAlarm(bot.Session.World, bot.Session.Driver, false);
        scene.Step(120);
        alarm.eventTrigger(device, scene.Match.ServerController(scene.Match.Bots[0])!.Pawn);
        scene.Step(60);
        Assert.True(alarm.bAlarmOn);
        foreach (var merc in scene.Match.Bots.Skip(2))
        {
            SHud hud = Assert.IsType<SHud>(merc.Controller!.myHUD);
            Assert.Contains(location, hud.MalfunctionGamePlayObjects);
            Assert.Contains(hud.AlarmsList, entry => entry is { bAlarmOn: true });
        }

        scene.Step(5 * Sim.Hz);
        Assert.False(alarm.bAlarmOn);
        foreach (var merc in scene.Match.Bots.Skip(2))
        {
            SHud hud = Assert.IsType<SHud>(merc.Controller!.myHUD);
            Assert.DoesNotContain(location, hud.MalfunctionGamePlayObjects);
            Assert.DoesNotContain(hud.AlarmsList, entry => entry is { bAlarmOn: true });
        }
    }

    [Fact]
    public void SensorConfirmationLoopStartsAndStopsOnRemoteClients()
    {
        using var scene = new NetworkScene();
        var sensor = scene.Match.Server.World.Level.SpawnActor(new SVolumetricSensor
        {
            bAlwaysRelevant = true, DetectionDelay = 4f,
        }, new FVector(2000f, 2000f, 600f), default)!;
        scene.Step(120);
        sensor.GotoState("s_SomethingDetected");
        scene.Step(60);
        foreach (var merc in scene.Match.Bots.Skip(2))
        {
            SVolumetricSensor seen = SensorOn(merc.Session.World.Level);
            Assert.Equal(sensor.bLightState, seen.bLightState);
            Assert.Equal(sensor.AmbientSound, seen.AmbientSound);
            Assert.Equal(sensor.SoundVolume, seen.SoundVolume);
            Assert.Equal(sensor.SoundRadius, seen.SoundRadius);
        }

        scene.Step(5 * Sim.Hz);
        foreach (var merc in scene.Match.Bots.Skip(2))
        {
            SVolumetricSensor seen = SensorOn(merc.Session.World.Level);
            Assert.True(seen.AmbientSound.IsNone);
            Assert.Equal(sensor.bLightState, seen.bLightState);
        }
    }

    private static SVolumetricSensor SensorOn(Level level)
    {
        SVolumetricSensor? result = null;
        foreach (SVolumetricSensor sensor in level.AllActors<SVolumetricSensor>())
        {
            Assert.Null(result);
            result = sensor;
        }
        return Assert.IsType<SVolumetricSensor>(result);
    }

    private static SAlarm PlaceAlarm(World world, NetDriver driver, bool authority)
    {
        SAlarm alarm = world.Level.SpawnActor(new SAlarm { bNoDelete = false, Duration = 4 }, default, default)!;
        alarm.bNoDelete = true;
        alarm.Role = authority ? ENetRole.ROLE_Authority : ENetRole.ROLE_SimulatedProxy;
        alarm.RemoteRole = authority ? ENetRole.ROLE_SimulatedProxy : ENetRole.ROLE_Authority;
        var actors = new List<Actor?>();
        for (int i = 0; i < driver.MapBinding.Count; i++) actors.Add(driver.MapBinding.Get(i));
        actors.Add(alarm);
        driver.MapBinding.Bind(actors);
        return alarm;
    }

    private sealed class NetworkScene : IDisposable
    {
        private readonly VirtualClock _clock = new();
        private long _frame;

        public NetworkScene(string map = MapCatalog.TestBox)
        {
            var network = new SimLinkNetwork(_clock, 628, SimLinkProfile.Ping200);
            Match = new BotMatch(new BotMatchSettings
            {
                Map = map, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
                Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
                GameOptions = "?TimeLimit=0",
            }, network.Bind, "server", 0);
            Step(900);
        }

        public BotMatch Match { get; }

        public void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(++_frame, Sim.Hz));
                Match.Step(_clock.NowMicroseconds);
            }
        }

        public void Dispose() => Match.Dispose();
    }
}
