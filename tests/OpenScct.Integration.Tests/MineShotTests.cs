using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MineShotTests
{
    [Theory]
    [InlineData("proximity")]
    [InlineData("laser")]
    [InlineData("poison")]
    [InlineData("spy trap")]
    public void ShockerHitsTheSameMineHullOnClientAndServer(string kind)
    {
        string? folder = WalkerRig.Maps.FindFolder(MapCatalog.TestBox);
        Assert.SkipWhen(folder is null || WalkerRig.Maps.GadgetMeshes is null,
            "TestBox and the converted mine collision pack are required.");
        var clientMaps = MapCatalog.CreateDefault();
        new PlayMapLoader(SvMap.Load(folder!), "", charsDir: null).Register(clientMaps);
        using WalkerRig rig = WalkerRig.Create(null, 9715, 1,
            new ServerSettings { Map = MapCatalog.TestBox, Profile = NetProfile.Enhanced },
            new ClientSettings { Maps = clientMaps, Gameplay = true, InstallGameplayServices = false });
        for (int i = 0; i < 180; i++) rig.Step();

        Level server = rig.Server.World.Level;
        SMines mine = kind switch
        {
            "proximity" => new SMineProx(),
            "laser" => new SMineLaser(),
            "poison" => new SMinePoison(),
            _ => new SMinePiegeSpy(),
        };
        mine.bAlwaysRelevant = true;
        FVector placement = new(30f, 0f, 100f);
        Assert.Same(mine, server.SpawnActor(mine, placement, default));
        for (int i = 0; i < 30; i++) rig.Step();
        Level client = rig.Clients[0].World.Level;
        Actor copy = Assert.Single(client.Actors, actor => actor.GetType() == mine.GetType() && !actor.bDeleteMe);
        Assert.Equal(mine.Location, copy.Location);

        // Over the inherited 2 uu cylinder, but through the original mesh hull.
        FVector edge = mine.Location + new FVector(0f, 0f, 3.5f);
        FVector start = edge + new FVector(100f, 0f, 0f);
        FVector end = edge - new FVector(20f, 0f, 0f);
        var zones = new TraceHitZones(EstimatedPillPose.Instance);
        BoneTraceResult authorityHit = zones.TraceBone(server.Info, end, start, true);
        BoneTraceResult clientHit = zones.TraceBone(client.Info, end, start, false);
        Assert.Same(mine, authorityHit.Actor);
        Assert.Same(copy, clientHit.Actor);
        Assert.Equal(authorityHit.HitLocation, clientHit.HitLocation);

        FVector above = new(0f, 0f, 20f);
        Assert.NotSame(mine, zones.TraceBone(server.Info, end + above, start + above, true).Actor);

        var spy = server.SpawnActor(new SPawnAttaque(), start + new FVector(100f, 0f, 0f), default)!;
        var pistol = server.SpawnActor(new SStickyShocker(), spy.Location, default, spy, spy)!;
        FRotator aim = new(0, 32768, 0);
        FVector offset = SWeaponAttaque.GetAxesAndLocation(default, aim, out _, out _, out _);
        pistol.TraceFireWithPos(start - offset, aim, 0f, 0f, 0f);
        Assert.True(mine.bDeleteMe);
    }
}
