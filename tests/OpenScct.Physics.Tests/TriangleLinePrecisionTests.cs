using System;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Physics.Tests;

public sealed class TriangleLinePrecisionTests
{
    [Theory]
    [InlineData(400f, -250f)]
    [InlineData(61f, -11f)]
    [InlineData(65f, -200f)]
    [InlineData(30f, 28f)]
    public void World_triangle_hit_does_not_depend_on_trace_length(float startZ, float endZ)
    {
        // A top-face triangle from Kitsune's shrine ledge. Long rays hit it while short probes missed.
        var builder = new CollisionDataBuilder();
        builder.AddTriangle(new FVector(470, 1932, 29), new FVector(470, 2054, 29), new FVector(254, 2054, 29));
        var world = CollisionWorld.Build(builder.Build());
        Assert.True(world.LineCheck(out var hit, null, new FVector(265, 2050, endZ),
            new FVector(265, 2050, startZ), default, ETraceFlags.TRACE_World));
        Assert.Equal(29.5f, hit.Location.Z, 3);
        Assert.InRange((hit.Normal - new FVector(0, 0, 1)).Size(), 0, .000001f);
        Assert.False(hit.bStartPenetrating);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(100000f, 0)]
    [InlineData(-100000f, 1)]
    [InlineData(100000f, 2)]
    public void Short_oblique_traces_work_for_translated_and_reoriented_triangles(float offset, int axis)
    {
        FVector Transform(FVector p) => axis switch
        {
            1 => new FVector(p.Z + offset, p.X + offset, p.Y + offset),
            2 => new FVector(p.Y + offset, p.Z + offset, p.X + offset),
            _ => p + new FVector(offset, offset, offset),
        };
        var a = Transform(new FVector(470, 1932, 29));
        var b = Transform(new FVector(470, 2054, 42));
        var c = Transform(new FVector(254, 2054, 19));
        var normal = ((b - a) ^ (c - a)).SafeNormal();
        var point = a * .25f + b * .25f + c * .5f;
        var builder = new CollisionDataBuilder();
        builder.AddTriangle(a, b, c);
        var world = CollisionWorld.Build(builder.Build());
        Assert.True(world.LineCheck(out var hit, null, point - normal * 40, point + normal * 32,
            default, ETraceFlags.TRACE_World));
        Assert.InRange(MathF.Abs(((hit.Location - point) | normal) - .5f), 0, .03f);
        Assert.True((hit.Normal | normal) > .9999f);
        Assert.False(world.LineCheck(out _, null, point + normal * 32, point - normal * 40,
            default, ETraceFlags.TRACE_World));
    }

    [Theory]
    [InlineData(0f, 0f, true)] // Vertex.
    [InlineData(5f, 0f, true)] // Edge.
    [InlineData(5f, 5f, true)] // Shared diagonal when this triangle belongs to a quad.
    [InlineData(2f, 2f, true)]
    [InlineData(-.01f, 0f, false)]
    [InlineData(5.01f, 5.01f, false)]
    public void Triangle_edges_are_inclusive_without_accepting_points_outside(float x, float y, bool expected)
    {
        var builder = new CollisionDataBuilder();
        builder.AddTriangle(new FVector(0, 0, 29), new FVector(10, 0, 29), new FVector(0, 10, 29));
        var world = CollisionWorld.Build(builder.Build());
        Assert.Equal(expected, world.LineCheck(out _, null, new FVector(x, y, 28), new FVector(x, y, 30),
            default, ETraceFlags.TRACE_World));
    }

    [Fact]
    public void Starting_on_the_front_face_hits_but_the_far_endpoint_parallel_and_back_faces_do_not()
    {
        var builder = new CollisionDataBuilder();
        builder.AddTriangle(new FVector(0, 0, 29), new FVector(10, 0, 29), new FVector(0, 10, 29));
        var world = CollisionWorld.Build(builder.Build());
        Assert.True(world.LineCheck(out var start, null, new FVector(2, 2, 28), new FVector(2, 2, 29), default, ETraceFlags.TRACE_World));
        Assert.Equal(0, start.Time);
        Assert.False(start.bStartPenetrating);
        Assert.False(world.LineCheck(out _, null, new FVector(2, 2, 29), new FVector(2, 2, 30), default, ETraceFlags.TRACE_World));
        Assert.False(world.LineCheck(out _, null, new FVector(2, 2, 30), new FVector(2, 2, 29), default, ETraceFlags.TRACE_World));
        Assert.False(world.LineCheck(out _, null, new FVector(3, 2, 29), new FVector(2, 2, 29), default, ETraceFlags.TRACE_World));
    }
}
