using System;
using System.IO;
using System.Text.Json.Nodes;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Physics.Tests;

public sealed class Ps2WorldGeometryTraceTests
{
    [Theory]
    [InlineData(ColliderKind.MeshPoly)]
    [InlineData(ColliderKind.MeshHull)]
    public void Ps2_world_geometry_preserves_false_player_block_flag_but_answers_pawn_trace(ColliderKind kind)
    {
        var builder = new CollisionDataBuilder();
        ColliderFlags flags = CollisionDataBuilder.WorldFlags & ~ColliderFlags.BlockPlayers;
        int index = builder.AddBoxMeshCollider(kind, null, flags,
            [(new FVector(-100, -100, -10), new FVector(100, 100, 0))]);
        CollisionData data = builder.Build();
        CollisionWorld pc = CollisionWorld.Build(data);
        CollisionWorld ps2 = CollisionWorld.Build(data, ps2WorldGeometryTrace: true);
        var pawn = new TestPawn();
        FVector start = new(0, 0, 200), end = new(0, 0, -100), extent = new(35, 35, 87.5f);
        Assert.False(pc.LineCheck(out _, pawn, end, start, extent, ETraceFlags.TRACE_World));
        Assert.True(ps2.LineCheck(out FCheckResult hit, pawn, end, start, extent, ETraceFlags.TRACE_World));
        Assert.Equal(index, hit.Collider);
        Assert.Same(pc.Model, ps2.Model);
        Assert.Equal(flags, ps2.GetCollider(index).Flags);
        Assert.False((ps2.GetCollider(index).Flags & ColliderFlags.BlockPlayers) != 0);
    }

    [Theory]
    [InlineData(ColliderKind.Gpo)]
    [InlineData(ColliderKind.Attached)]
    public void Ps2_profile_does_not_enable_non_world_actor_collision(ColliderKind kind)
    {
        var builder = new CollisionDataBuilder();
        builder.AddBoxMeshCollider(kind, null, CollisionDataBuilder.WorldFlags &
            ~(ColliderFlags.BlockPlayers | ColliderFlags.BlockActors),
            [(new FVector(-100, -100, -10), new FVector(100, 100, 0))]);
        CollisionWorld world = CollisionWorld.Build(builder.Build(), ps2WorldGeometryTrace: true);
        Assert.False(world.LineCheck(out _, new TestPawn(), new FVector(0, 0, -100), new FVector(0, 0, 200),
            new FVector(35, 35, 87.5f), ETraceFlags.TRACE_AllBlocking));
    }

    [Fact]
    public void Ps2_profile_keeps_collide_actor_and_trace_mask_gates()
    {
        var builder = new CollisionDataBuilder();
        builder.AddBoxMeshCollider(ColliderKind.MeshPoly, null,
            CollisionDataBuilder.WorldFlags & ~ColliderFlags.CollideActors,
            [(new FVector(-100, -100, -10), new FVector(100, 100, 0))]);
        CollisionWorld world = CollisionWorld.Build(builder.Build(), ps2WorldGeometryTrace: true);
        Assert.False(world.LineCheck(out _, new TestPawn(), new FVector(0, 0, -100), new FVector(0, 0, 200),
            new FVector(35, 35, 87.5f), ETraceFlags.TRACE_AllBlocking));
    }

    [Fact]
    public void Explicit_level_metadata_selects_profile_and_changes_sim_hash()
    {
        string temp = Path.Combine(Path.GetTempPath(), "openscct-ps2-trace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (string file in Directory.EnumerateFiles(TestPaths.TestBox, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(temp, Path.GetRelativePath(TestPaths.TestBox, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
            }
            SvMap pc = SvMap.Load(temp);
            Assert.False(CollisionWorld.Build(pc).Ps2WorldGeometryTrace);
            string path = Path.Combine(temp, "sim", "gameplay.json");
            JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
            json["levelInfo"]!["props"]!["bPS2WorldGeometryTrace"] = true;
            File.WriteAllText(path, json.ToJsonString());
            SvMap ps2 = SvMap.Load(temp, new SvMapLoadOptions { VerifySimHash = false });
            Assert.True(CollisionWorld.Build(ps2).Ps2WorldGeometryTrace);
            Assert.NotEqual(pc.SimHash, ps2.SimHash);
        }
        finally
        {
            Assert.StartsWith(Path.GetTempPath(), Path.GetFullPath(temp));
            Directory.Delete(temp, recursive: true);
        }
    }
}
