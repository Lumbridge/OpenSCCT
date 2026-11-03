using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.Core;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class RollPoseContentTests
{
    [Fact]
    public void LinearTracksComposeScaleRotationAndTranslationThroughAncestors()
    {
        using var file = new Fixture();
        RollPoseContent content = Assert.IsType<RollPoseContent>(RollPoseContent.Load(file.Path));
        var pose = new Dictionary<string, Matrix4x4> { ["stale"] = Matrix4x4.Identity };
        Assert.True(content.TryGetDuration("RollStJpFdL", out float seconds));
        Assert.Equal(2f, seconds);
        Assert.True(content.TrySampleSeconds("RollStJpFdL", 1f, pose));
        Assert.Equal(2, pose.Count);
        Assert.Equal(new Vector3(15f, 0f, 0f), pose["Root"].Translation);
        Vector3 child = pose["Child"].Translation;
        Assert.Equal(17.12132f, child.X, 4);
        Assert.Equal(2.12132f, child.Y, 4);
        Assert.Equal(0f, child.Z, 4);
        Assert.Equal(default, content.RotOrigin);
        Assert.Equal(new FVector(1f, 1f, 1f), content.MeshScale);
    }

    [Theory]
    [InlineData(-1f, 12f, 0f)]
    [InlineData(0f, 12f, 0f)]
    [InlineData(2f, 20f, 4f)]
    [InlineData(3f, 20f, 4f)]
    public void SecondsClampToTheFirstAndLastAnimationSamples(float seconds, float x, float y)
    {
        using var file = new Fixture();
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(RollPoseContent.Load(file.Path)!.TrySampleSeconds("RollStJpFdL", seconds, pose));
        Assert.Equal(x, pose["Child"].Translation.X, 4);
        Assert.Equal(y, pose["Child"].Translation.Y, 4);
    }

    [Fact]
    public void NormalizedTimeMapsToTheLastStoredFrameAndNamesAreCaseInsensitive()
    {
        using var file = new Fixture();
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var normalized = new Dictionary<string, Matrix4x4>();
        var seconds = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySample("rollstjpfdl", 0.5f, normalized));
        Assert.True(content.TrySampleSeconds("RollStJpFdL", 1f, seconds));
        Assert.Equal(seconds["Child"], normalized["Child"]);
    }

    [Fact]
    public void AntipodalQuaternionKeysTakeTheShortestArc()
    {
        using var file = new Fixture(antipodal: true);
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(RollPoseContent.Load(file.Path)!.TrySampleSeconds("RollStJpFdL", 1f, pose));
        Assert.Equal(new Vector3(18f, 0f, 0f), pose["Child"].Translation);
    }

    [Fact]
    public void MeshMetadataIsExposedWithoutBeingAppliedToTheSample()
    {
        using var file = new Fixture(root => root["extras"] = JsonNode.Parse("""
            {"svmap":{"rotOrigin":[1024,-16384,2048],"meshScale":[2,3,4]}}
            """));
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        Assert.Equal(new FRotator(1024, -16384, 2048), content.RotOrigin);
        Assert.Equal(new FVector(2f, 3f, 4f), content.MeshScale);
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleSeconds("RollStJpFdL", 0f, pose));
        Assert.Equal(new Vector3(12f, 0f, 0f), pose["Child"].Translation);
    }

    [Fact]
    public void UnrelatedAnimationsAreNotLoadedAndUnknownClipsLeaveTheDestinationAlone()
    {
        using var file = new Fixture(root => root["animations"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"Idle","channels":[{"sampler":999}],"samplers":[]}
            """)));
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var pose = new Dictionary<string, Matrix4x4> { ["existing"] = Matrix4x4.Identity };
        Assert.False(content.TrySample("Idle", 0f, pose));
        Assert.False(content.TryGetDuration("Idle", out _));
        Assert.Single(pose);
        Assert.Equal(Matrix4x4.Identity, pose["existing"]);
    }

    [Fact]
    public void MissingOptionalContentReturnsNull()
    {
        Assert.Null(RollPoseContent.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".glb")));
    }

    [Fact]
    public void InvalidSamplingTimeIsRejected()
    {
        using var file = new Fixture();
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.Throws<ArgumentOutOfRangeException>(() => RollPoseContent.Load(file.Path)!.TrySample("RollStJpFdL", float.NaN, pose));
    }

    [Theory]
    [InlineData("bounds")]
    [InlineData("cycle")]
    [InlineData("interpolation")]
    [InlineData("sparse")]
    [InlineData("count")]
    public void MalformedRollDataReportsItsSourceInsteadOfProducingAPose(string kind)
    {
        using var file = new Fixture(root =>
        {
            switch (kind)
            {
                case "bounds": root["accessors"]![0]!["byteOffset"] = 1024; break;
                case "cycle": root["nodes"]![0]!["children"] = new JsonArray(1); break;
                case "interpolation": root["animations"]![0]!["samplers"]![0]!["interpolation"] = "CUBICSPLINE"; break;
                case "sparse": root["accessors"]![0]!["sparse"] = new JsonObject(); break;
                case "count": root["accessors"]![1]!["count"] = 1; break;
            }
        });
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => RollPoseContent.Load(file.Path));
        Assert.Contains(file.Path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedGlbIsDiagnosed()
    {
        using var file = new Fixture();
        byte[] bytes = File.ReadAllBytes(file.Path);
        File.WriteAllBytes(file.Path, bytes[..^4]);
        Assert.Throws<InvalidDataException>(() => RollPoseContent.Load(file.Path));
    }

    [Fact]
    public void RollBlendsWithLocomotionInLocalSpaceBeforeAccumulatingParents()
    {
        using var file = new Fixture(root =>
        {
            JsonNode walking = root["animations"]![0]!.DeepClone();
            walking["name"] = "JoggStAlFd0";
            root["animations"]!.AsArray().Add(walking);
        });
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleLayers([
            new("JoggStAlFd0", 0f, 1f),
            new("RollStJpFdL", 2f, 0.5f),
        ], pose));
        Assert.Equal(15f, pose["Root"].Translation.X, 4);
        // Local root scale 3 and rotation 45 degrees are blended before moving its child.
        // Blending the two global child positions would incorrectly produce (16, 2, 0).
        Assert.Equal(17.12132f, pose["Child"].Translation.X, 4);
        Assert.Equal(2.12132f, pose["Child"].Translation.Y, 4);
        Assert.False(RollPoseContent.IsRollClip("JoggStAlFd0"));
        Assert.True(RollPoseContent.IsRollClip("rollstjpfdl"));
    }

    [Fact]
    public void StartBoneRestrictsTheLayerToItsSubtree()
    {
        using var file = new Fixture(root =>
        {
            JsonNode walking = root["animations"]![0]!.DeepClone();
            walking["name"] = "WalkStAlFd0";
            foreach (JsonNode? channel in walking["channels"]!.AsArray()) channel!["target"]!["node"] = 0;
            root["animations"]!.AsArray().Add(walking);
        });
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleLayers([
            new("RollStJpFdL", 0f, 1f),
            new("RollStJpFdL", 2f, 1f, "Child"), // all its root tracks are excluded
            new("WalkStAlFd0", 2f, 0.5f, "Child"),
        ], pose));
        Assert.Equal(10f, pose["Root"].Translation.X, 4);
        Assert.Equal(31f, pose["Child"].Translation.X, 4); // (rest 1 + target 20)/2 * parent scale 2 + parent 10
        Assert.Equal(0f, pose["Child"].Translation.Y, 4);
    }

    [Fact]
    public void LoopSeamInterpolatesTheLastFrameToTheFirstOverOneFrame()
    {
        using var file = new Fixture();
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleLayers([new("RollStJpFdL", 2.5f, 1f, Loop: true, Fps: 1f)], pose));
        Assert.Equal(15f, pose["Root"].Translation.X, 4);
        // The renderer wraps translation and rotation; its ordinary channels retain the final scale.
        Assert.Equal(17.82843f, pose["Child"].Translation.X, 4);
        Assert.Equal(2.82843f, pose["Child"].Translation.Y, 4);
    }

    [Theory]
    [InlineData("missing-clip")]
    [InlineData("missing-bone")]
    [InlineData("loop-rate")]
    [InlineData("invalid-time")]
    public void AnUnsupportedActiveLayerLeavesTheDestinationUntouched(string kind)
    {
        using var file = new Fixture();
        var pose = new Dictionary<string, Matrix4x4> { ["existing"] = Matrix4x4.Identity };
        RollPoseContent.Layer invalid = kind switch
        {
            "missing-clip" => new("Unloaded", 0f, 1f),
            "missing-bone" => new("RollStJpFdL", 0f, 1f, "Unknown"),
            "loop-rate" => new("RollStJpFdL", 0f, 1f, Loop: true),
            _ => new("RollStJpFdL", float.NaN, 1f),
        };
        Assert.False(RollPoseContent.Load(file.Path)!.TrySampleLayers([new("RollStJpFdL", 0f, 1f), invalid], pose));
        Assert.Single(pose);
        Assert.Equal(Matrix4x4.Identity, pose["existing"]);
    }

    [Fact]
    public void AZeroWeightUnknownLayerDoesNotBlockTheSupportedPose()
    {
        using var file = new Fixture();
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(RollPoseContent.Load(file.Path)!.TrySampleLayers([
            new("Unloaded", 0f, 0f, "Unknown"), new("RollStJpFdL", 0f, 1f),
        ], pose));
        Assert.Equal(12f, pose["Child"].Translation.X, 4);
    }

    [Fact]
    public void TweenKeepsThePreviousPoseThenAdvancesOnceForEachFrame()
    {
        using var file = new Fixture();
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var history = new RollPoseContent.PoseHistory();
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleLayers([new("RollStJpFdL", 2f, 1f)], pose, history));
        foreach ((float frame, float expected) in new[] { (-.5f, 20f), (-.25f, 15f), (-.25f, 15f), (-.125f, 12.5f), (0f, 10f) })
        {
            Assert.True(content.TrySampleLayers([new("RollStJpFdL", 0f, 1f, Frame: frame, Frames: 2)], pose, history));
            Assert.Equal(expected, pose["Root"].Translation.X, 4);
        }
        history.Reset();
        Assert.True(content.TrySampleLayers([new("RollStJpFdL", 0f, 1f, Frame: -.5f, Frames: 2)], pose, history));
        Assert.Equal(10f, pose["Root"].Translation.X, 4); // No previous pose survives a reset.
    }

    [Theory]
    [InlineData(1f, 12f, 0f)]
    [InlineData(.5f, 16f, 2f)]
    public void GlobalStartBoneKeepsTheClipsMeshSpaceTransform(float alpha, float x, float y)
    {
        using var file = new Fixture();
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(RollPoseContent.Load(file.Path)!.TrySampleLayers([
            new("RollStJpFdL", 2f, 1f),
            new("RollStJpFdL", 0f, alpha, "Child", GlobalPose: true),
        ], pose));
        Assert.Equal(20f, pose["Root"].Translation.X, 4);
        Assert.Equal(x, pose["Child"].Translation.X, 4);
        Assert.Equal(y, pose["Child"].Translation.Y, 4);
    }

    [Fact]
    public void MirroringLeavesTheUnmappedSkeletonRootInPlace()
    {
        using var file = new Fixture(root => root["nodes"]![1]!["name"] = "B");
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(RollPoseContent.Load(file.Path)!.TrySampleLayers([new("RollStJpFdL", 0f, 1f, Mirror: true)], pose));
        Assert.Equal(new Vector3(10f, 0f, 0f), pose["B"].Translation);
    }

    [Fact]
    public void GeneralLoadingIncludesNonLocomotionClips()
    {
        using var file = new Fixture(root => root["animations"]![0]!["name"] = "SyntheticClimb");
        RollPoseContent content = RollPoseContent.Load(file.Path, allClips: true)!;
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleSeconds("SyntheticClimb", 1f, pose));
        Assert.Equal(15f, pose["Root"].Translation.X, 3);
    }

    [Fact]
    public void MirrorTableUsesMeshJointOrderIncludingUnanimatedHelperBones()
    {
        using var file = new Fixture(root =>
        {
            root["nodes"]![1]!["name"] = "B";
            root["nodes"]![0]!["name"] = "Left";
            root["nodes"]!.AsArray().Add(JsonNode.Parse("""{"name":"Helper"}"""));
            root["nodes"]!.AsArray().Add(JsonNode.Parse("""{"name":"Right","translation":[-1,0,0]}"""));
            root["nodes"]![1]!["children"] = new JsonArray(0, 2, 3);
            root["skins"] = JsonNode.Parse("""[{"joints":[1,2,0,3]}]""");
        });
        var pose = new Dictionary<string, Matrix4x4>();
        Assert.True(RollPoseContent.Load(file.Path)!.TrySampleLayers([
            new("RollStJpFdL", 0f, 1f, Mirror: true, BoneMirrors: new[] { -1, -1, 3, 2 }),
        ], pose));
        Assert.Equal(new Vector3(10f, 0f, 0f), pose["B"].Translation);
        Assert.Equal(new Vector3(-8f, 0f, 0f), pose["Left"].Translation);
        Assert.Equal(new Vector3(-12f, 0f, 0f), pose["Right"].Translation);
        Assert.Equal(new Vector3(10f, 0f, 0f), pose["Helper"].Translation);
    }

    [Fact]
    public void MirroringExchangesLimbsAndReflectsTheirGlobalPositions()
    {
        using var file = new Fixture(root =>
        {
            root["nodes"]![0]!["name"] = "B L Hand";
            root["nodes"]!.AsArray().Add(JsonNode.Parse("""{"name":"B R Hand","translation":[-1,0,0]}"""));
            root["nodes"]![1]!["children"] = new JsonArray(0, 2);
        });
        RollPoseContent content = RollPoseContent.Load(file.Path)!;
        var ordinary = new Dictionary<string, Matrix4x4>();
        var mirrored = new Dictionary<string, Matrix4x4>();
        Assert.True(content.TrySampleSeconds("RollStJpFdL", 1f, ordinary));
        Assert.True(content.TrySampleLayers([new("RollStJpFdL", 1f, 1f, Mirror: true)], mirrored));
        foreach ((string target, string source) in new[] { ("B L Hand", "B R Hand"), ("B R Hand", "B L Hand") })
        {
            Vector3 expected = ordinary[source].Translation * new Vector3(-1f, 1f, 1f);
            Assert.InRange(Vector3.Distance(expected, mirrored[target].Translation), 0f, 0.0001f);
        }
    }

    /// <summary>Synthetic data only. Translation uses accessor offsets and a padded stride; the child precedes its parent.</summary>
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "roll-pose-" + Guid.NewGuid() + ".glb");

        public Fixture(Action<JsonObject>? mutate = null, bool antipodal = false)
        {
            using var data = new MemoryStream();
            using var bin = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
            bin.Write(0f); bin.Write(2f); // time accessor
            bin.Write(99f); // accessor byte offset inside translation view
            bin.Write(10f); bin.Write(0f); bin.Write(0f); bin.Write(99f);
            bin.Write(20f); bin.Write(0f); bin.Write(0f); bin.Write(99f);
            bin.Write(0f); bin.Write(0f); bin.Write(0f); bin.Write(1f);
            bin.Write(0f); bin.Write(0f); bin.Write(antipodal ? 0f : 0.70710678f); bin.Write(antipodal ? -1f : 0.70710678f);
            bin.Write(2f); bin.Write(2f); bin.Write(2f);
            bin.Write(4f); bin.Write(4f); bin.Write(4f);
            byte[] binary = data.ToArray();
            JsonObject root = JsonNode.Parse("""
                {
                  "asset":{"version":"2.0"},
                  "nodes":[{"name":"Child","translation":[1,0,0]},{"name":"Root","children":[0]}],
                  "buffers":[{"byteLength":100}],
                  "bufferViews":[
                    {"buffer":0,"byteOffset":0,"byteLength":8},
                    {"buffer":0,"byteOffset":8,"byteLength":36,"byteStride":16},
                    {"buffer":0,"byteOffset":44,"byteLength":32},
                    {"buffer":0,"byteOffset":76,"byteLength":24}],
                  "accessors":[
                    {"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR"},
                    {"bufferView":1,"byteOffset":4,"componentType":5126,"count":2,"type":"VEC3"},
                    {"bufferView":2,"componentType":5126,"count":2,"type":"VEC4"},
                    {"bufferView":3,"componentType":5126,"count":2,"type":"VEC3"}],
                  "animations":[{"name":"RollStJpFdL",
                    "samplers":[{"input":0,"output":1,"interpolation":"LINEAR"},{"input":0,"output":2},{"input":0,"output":3}],
                    "channels":[{"sampler":0,"target":{"node":1,"path":"translation"}},
                                {"sampler":1,"target":{"node":1,"path":"rotation"}},
                                {"sampler":2,"target":{"node":1,"path":"scale"}}]}]
                }
                """)!.AsObject();
            mutate?.Invoke(root);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(root);
            int padded = (json.Length + 3) & ~3;
            using var output = File.Create(Path);
            using var writer = new BinaryWriter(output);
            writer.Write(0x46546c67u); writer.Write(2u); writer.Write((uint)(12 + 8 + padded + 8 + binary.Length));
            writer.Write((uint)padded); writer.Write(0x4e4f534au); writer.Write(json);
            for (int i = json.Length; i < padded; i++) writer.Write((byte)' ');
            writer.Write((uint)binary.Length); writer.Write(0x004e4942u); writer.Write(binary);
        }

        public void Dispose() => File.Delete(Path);
    }
}
