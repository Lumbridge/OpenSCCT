using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenScctGame.Render;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class GlassFractureTests
{
    private static GlassFracture.Vertex V(float x, float y) => new(new(x, y), new(x * .5f + .5f, y * .5f + .5f));
    private static List<GlassFracture.Triangle> Polygon(int count)
    {
        var vertices = Enumerable.Range(0, count).Select(i => V(MathF.Cos(i * MathF.Tau / count), MathF.Sin(i * MathF.Tau / count))).ToArray();
        return Enumerable.Range(1, count - 2).Select(i => new GlassFracture.Triangle(vertices[0], vertices[i], vertices[i + 1])).ToList();
    }
    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(6)] [InlineData(64)]
    public void FragmentsCoverTheRealPaneAndKeepItsTexture(int sides)
    {
        var triangles = Polygon(sides);
        var parts = GlassFracture.Break(triangles, new(.2f, -.1f), 2981);
        float initial = triangles.Sum(t => GlassFracture.Area([t.A, t.B, t.C]));
        float broken = parts.Sum(p => p.Polygons.Sum(GlassFracture.Area));
        Assert.InRange(MathF.Abs(initial - broken), 0, .00003f);
        Assert.InRange(parts.Count, 20, 80);
        foreach (var part in parts) foreach (var poly in part.Polygons) foreach (var v in poly)
        {
            Assert.InRange(v.Point.LengthSquared(), 0, 1.0001f);
            Assert.InRange(Vector2.Distance(v.Uv, v.Point * .5f + new Vector2(.5f)), 0, .00001f);
        }
    }
    [Fact]
    public void AShardHasNoInternalWallsAlongTheOriginalMeshTriangles()
    {
        var a=V(-1,-1);var b=V(1,-1);var c=V(1,1);var d=V(-1,1);
        var fragment=new GlassFracture.Fragment(Vector2.Zero,[[a,b,c],[a,c,d]]);
        Assert.Equal(4,GlassFracture.Boundary(fragment).Count);
    }
    [Fact]
    public void LayoutIsDeterministicAndFollowsTheImpact()
    {
        var shape = Polygon(64);
        var a = GlassFracture.Break(shape, new(.1f, .2f), 2981);
        var b = GlassFracture.Break(shape, new(.1f, .2f), 2981);
        Assert.Equal(a.Select(p => p.Center), b.Select(p => p.Center));
        Assert.NotEqual(a[0].Center, GlassFracture.Break(shape, new(-.6f, .4f), 2981)[0].Center);
    }
    [Fact]
    public void HolesAndConcaveEdgesStayEmpty()
    {
        var shape = new List<GlassFracture.Triangle>();
        void Rect(float x0, float y0, float x1, float y1)
        { shape.Add(new(V(x0,y0),V(x1,y0),V(x1,y1))); shape.Add(new(V(x0,y0),V(x1,y1),V(x0,y1))); }
        Rect(-1,-1,1,-.5f); Rect(-1,.5f,1,1); Rect(-1,-.5f,-.5f,.5f); Rect(.5f,-.5f,1,.5f);
        var pieces = GlassFracture.Break(shape, Vector2.Zero, 944);
        Assert.InRange(MathF.Abs(pieces.Sum(p => p.Polygons.Sum(GlassFracture.Area)) - 3), 0, .00003f);
        foreach (var p in pieces) foreach (var poly in p.Polygons) foreach (var v in poly)
            Assert.True(MathF.Abs(v.Point.X) >= .49999f || MathF.Abs(v.Point.Y) >= .49999f);
    }
}
