using System;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Equipment;

public sealed partial class ResponsiveSmokeWorld
{
    private static int s_warmed;

    /// <summary>
    /// Prepare the optional field, collision and payload paths during an enabled match's loading phase. The
    /// scratch world is isolated: warmup never adds actors, advances clocks or changes geometry in the real match.
    /// </summary>
    public static void Warmup(Level level)
    {
        if (!IsEnabled(level) || Interlocked.CompareExchange(ref s_warmed, 1, 0) != 0) return;
        try
        {
            ResponsiveSmokeNoise.Warmup();
            var scratch = new World(1, ENetMode.NM_Standalone);
            Level sample = scratch.Level;
            sample.Info.GRI = sample.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
            var builder = new CollisionDataBuilder();
            builder.AddBox(new FVector(-500, -500, -25), new FVector(500, 500, 0));
            CollisionWorld collision = CollisionWorld.Build(builder.Build()); sample.SetService<ICollisionWorld>(collision);
            var center = new FVector(0, 0, 100);
            SSmokeEmitter emitter = sample.SpawnActor(new SSmokeEmitter(), center, default)!;
            ResponsiveSmokeWorld smoke = For(sample);
            scratch.Step(90);
            ResponsiveSmokeSnapshot before = smoke.Capture(emitter);
            Bullet(emitter, center - new FVector(150, 0, 0), center + new FVector(150, 0, 0));
            Blast(emitter, center, 180);
            _ = smoke.SampleDensity(center); _ = smoke.InSmoke(center, 50);
            _ = smoke.SmokeBetween(center - new FVector(400, 0, 0), center + new FVector(400, 0, 0));
            _ = smoke.ExposureSource(new SPawnDefense_1Mesh { Location = center }, smoke.CurrentTime, out _);
            ResponsiveSmokeSnapshot after = smoke.Capture(emitter);
            _ = ResponsiveSmokeCodec.Decode(ResponsiveSmokeCodec.Encode(after));
            _ = ResponsiveSmokeCodec.DecodeDelta(before, ResponsiveSmokeCodec.EncodeDelta(before, after));
            _ = ResponsiveSmokeCodec.DecodeCarves(ResponsiveSmokeCodec.EncodeCarves(after.Carves));
            collision.SetColliderEnabled(0, false); scratch.Step(4);
            emitter.Destroy();
            Volatile.Write(ref s_warmed, 2);
        }
        catch
        {
            Volatile.Write(ref s_warmed, 0);
            throw;
        }
    }
}
