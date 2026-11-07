using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Content.Json;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Traversal;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Physics.Tests;

public sealed class MovingGETests
{
    [Fact]
    public void HangingAndClimbingAnchorsRideTheCarrierAndReleaseOnFalling()
    {
        var geometry = Geo.Floor();
        geometry.AddBoxHullCollider(ColliderKind.Mover, "Lift", CollisionDataBuilder.WorldFlags,
            new(-176, -176, -32), new(176, 176, 32), new(0, 0, 400));
        TestMover? lift = null;
        var ge = new GERecord(0, "South", GEKind.LG, [new(-176, -176, 32), new(176, -176, 32)], 0, GEPlatform.Common, GESource.Synthetic(), "Lift");
        var scene = Scene.Create(geometry.Build(), level =>
        {
            lift = level.SpawnActor(new TestMover(), new(0, 0, 400), default)!;
            lift.BasePos = lift.Location;
            return new Dictionary<string, Actor> { ["Lift"] = lift };
        }, ges: [ge]);
        scene.Driver.GEs.BindActors(_ => lift);
        var pawn = scene.SpawnSpy(new(0, -215, 315), EPhysics.PHYS_Ledge, rotation: new(0, 16384, 0));
        pawn.ZipP1 = new(-176, -176, 432); pawn.ZipP2 = new(176, -176, 432); pawn.m_geoNormal = new(0, -1, 0);
        pawn.SetBase(lift);
        scene.Driver.GetState(pawn).TraversalBase = lift;
        lift!.KeyPos[1] = new(400, 0, 200); lift.InterpolateTo(1, 1);
        scene.Step(70);
        Assert.Same(lift, pawn.Base);
        Assert.InRange(pawn.Location.X, 399, 401); Assert.InRange(pawn.Location.Z, 514, 516);
        Assert.InRange(FVector.Dist(new(224, -176, 632), pawn.ZipP1), 0, .01f);
        scene.Driver.setPhysics(pawn, EPhysics.PHYS_RootMotion);
        Assert.Same(lift, pawn.Base);
        scene.Driver.setPhysics(pawn, EPhysics.PHYS_Falling);
        Assert.Null(pawn.Base); Assert.Null(scene.Driver.GetState(pawn).TraversalBase);
    }

    [Fact]
    public void MovingLedgeFollowsTranslationRotationAndNeverLeavesAnOldGrabBehind()
    {
        var mover = new Mover { Location = new FVector(1000, 2000, 300) };
        var ge = new GERecord(0, "LiftSouth", GEKind.LG, [new(-176, -176, 32), new(176, -176, 32)],
            0, GEPlatform.Common, GESource.Synthetic(), "Lift");
        var doc = new TraversalDoc("1.2.0", "Hub", [ge]);
        var roundtrip = TraversalDoc.Parse(CanonicalJson.ToFileBytes(doc.ToJson()));
        Assert.Equal("Lift", roundtrip.Ges[0].AttachedTo);
        var index = new GEIndex(roundtrip);
        index.BindActors(id => id == "Lift" ? mover : null);
        var hits = new List<GEHit>();
        Assert.Equal(1, index.QuerySphere(new(1000, 1824, 332), 10, (int)GEKind.LG, 0, hits));
        mover.Location = new FVector(1000, 2000, 940);
        Assert.Equal(0, index.QuerySphere(new(1000, 1824, 332), 10, (int)GEKind.LG, 0, hits));
        Assert.Equal(1, index.QuerySphere(new(1000, 1824, 972), 10, (int)GEKind.LG, 0, hits));
        mover.Rotation = new FRotator(0, 16384, 0);
        Assert.Equal(0, index.QuerySphere(new(1000, 1824, 972), 10, (int)GEKind.LG, 0, hits));
        Assert.Equal(1, index.QuerySphere(new(1176, 2000, 972), 10, (int)GEKind.LG, 0, hits));
        Assert.Same(mover, index.OwnerOf(hits[0].GE));
        index.BindActors(_ => null);
        Assert.Equal(0, index.QuerySphere(new(1176, 2000, 972), 10, (int)GEKind.LG, 0, hits));
    }
}
