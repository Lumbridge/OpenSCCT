using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class ResponsiveSmokeReplayTests
{
    [Fact]
    public void MidCloudRecordingReconstructsAgeClearingAndExpiryAfterRestart()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture is unavailable.");
        string path = Path.Combine(Path.GetTempPath(), "openscct-responsive-smoke-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
        try
        {
            float createdAt;
            var center = new FVector(0, 0, 100);
            using (WalkerRig rig = WalkerRig.Create(null, 506, 0,
                new ServerSettings { Map = MapCatalog.TestBox, GameOptions = "?GameMode=5?TimeLimit=0?SvmResponsiveSmoke=1", PlayersToStart = 4 }))
            {
                Level level = rig.Server.World.Level;
                // An empty collision fixture isolates packet/replay correctness from a map's authored rooms.
                level.SetService<ICollisionWorld>(CollisionWorld.Build(new CollisionDataBuilder().Build()));
                SSmokeEmitter cloud = level.SpawnActor(new SSmokeEmitter(), center, default)!;
                ResponsiveSmokeWorld smoke = ResponsiveSmokeWorld.For(level);
                for (int i = 0; i < 3 * Sim.Hz; i++) rig.Step();
                Assert.True(smoke.TryGet(cloud, out ResponsiveSmokeField field));
                createdAt = field.CreatedAt;
                Assert.True(smoke.CurrentTime - createdAt >= 2.9f);
                Assert.True(field.SampleDensity(center, smoke.CurrentTime) > .9f);
                using var writer = new DemoWriter(path, new DemoHeader
                {
                    Map = MapCatalog.TestBox, ProtocolHash = rig.Server.Driver.ProtocolHash,
                    ContentHash = rig.Server.Map.ContentHash, GameOptions = "?GameMode=5?SvmResponsiveSmoke=1",
                    TickRate = Sim.Hz, NetRate = rig.Server.NetRate,
                });
                rig.Server.StartRecording(writer, rig.Clock.NowMicroseconds);
                for (int i = 0; i < Sim.Hz; i++) rig.Step();
                ResponsiveSmokeWorld.Bullet(cloud, center - new FVector(400, 0, 0), center + new FVector(400, 0, 0));
                Assert.Equal(0f, field.SampleDensity(center, smoke.CurrentTime));
                for (int i = 0; i < 7 * Sim.Hz; i++) rig.Step();
                Assert.True(cloud.bDeleteMe);
                rig.Server.StopRecording();
            }

            DemoRecording recording = DemoFile.Read(path);
            Assert.Contains("?SvmResponsiveSmoke=1", recording.Header.GameOptions);
            Assert.True(recording.PacketCount > 0);
            List<(uint Tick, float Age, float Density)>? firstWatch = null;
            for (int visit = 0; visit < 2; visit++)
            {
                using var playback = new DemoPlaybackTransport(recording);
                using var replay = new ClientSession(playback, "demo", 0, new ClientSettings
                {
                    PlayerName = "Demo", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
                });
                var samples = new List<(uint Tick, float Age, float Density)>();
                bool sawInitial = false, sawClearing = false, sawRecovery = false, sawExpired = false;
                float lastAge = 0f;
                for (uint tick = recording.Index.FirstTick; tick <= recording.Index.LastTick; tick++)
                {
                    playback.ReleaseUpTo(tick);
                    replay.Tick(VirtualClock.TickToMicroseconds(tick - recording.Index.FirstTick + 1, recording.Header.TickRate));
                    ResponsiveSmokeWorld smoke = ResponsiveSmokeWorld.For(replay.World.Level);
                    SSmokeEmitter? cloud = null;
                    foreach (SSmokeEmitter candidate in replay.World.Level.DynamicActors<SSmokeEmitter>())
                        if (!candidate.bDeleteMe) { cloud = candidate; break; }
                    if (cloud is null || !smoke.TryGet(cloud, out ResponsiveSmokeField field))
                    {
                        sawExpired |= sawInitial && lastAge > 9f;
                        continue;
                    }
                    if (ResponsiveSmokeReplication.For(replay.Driver).BaselinesReceived == 0) continue;
                    Assert.Equal(createdAt, field.CreatedAt);
                    float age = smoke.CurrentTime - field.CreatedAt;
                    float density = field.SampleDensity(center, smoke.CurrentTime);
                    if (!sawInitial)
                    {
                        Assert.InRange(age, 2.9f, 3.5f);
                        Assert.True(density > .9f, $"First recorded smoke density {density}, age {age}");
                        sawInitial = true;
                    }
                    samples.Add((tick, age, density));
                    sawClearing |= age is >= 3.9f and <= 4.35f && density < .1f;
                    sawRecovery |= age is >= 4.6f and <= 5.5f && density > .9f;
                    sawExpired |= age >= ResponsiveSmokeField.Lifetime && density == 0f;
                    lastAge = age;
                }
                Assert.True(playback.AtEnd);
                Assert.True(sawInitial && sawClearing && sawRecovery && sawExpired,
                    $"initial={sawInitial}, clearing={sawClearing}, recovery={sawRecovery}, expired={sawExpired}, lastAge={lastAge}");
                ResponsiveSmokeReplication received = ResponsiveSmokeReplication.For(replay.Driver);
                Assert.True(received.BaselinesReceived > 0);
                Assert.True(received.CarveUpdatesReceived > 0);
                Assert.Equal(0, received.InvalidPayloads);
                // Backward seeking rebuilds a fresh replay client and fast-forwards the same packet prefix.
                if (firstWatch is null) firstWatch = samples;
                else Assert.Equal(firstWatch, samples);
            }
        }
        finally { File.Delete(path); }
    }
}
