using System;
using System.Linq;
using System.IO;
using System.Text.Json.Nodes;
using OpenScct.Core;
using OpenScct.Content;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class GlassImpactNetworkTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "openscct-glass-net-" + Guid.NewGuid().ToString("N"));
    private MapCatalog GlassMap()
    {
        string source = WalkerRig.Maps.FindFolder(MapCatalog.TestBox)!;
        string target = Path.Combine(folder, MapCatalog.TestBox);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest);
        }
        string gameplayPath = Path.Combine(target,"sim","gameplay.json");
        var gameplay = JsonNode.Parse(File.ReadAllBytes(gameplayPath))!;
        gameplay["actors"]!.AsArray().Add(new JsonObject { ["class"]="SBase.SVitre", ["id"]="GlassPane",
            ["props"]=new JsonObject { ["Location"]=new JsonObject { ["X"]=100,["Y"]=100,["Z"]=300 },
                ["bAlwaysRelevant"]=true,["bStatic"]=true,["bNoDelete"]=true } });
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(gameplay.ToJsonString()); File.WriteAllBytes(gameplayPath,bytes);
        string manifestPath=Path.Combine(target,"manifest.json"); var manifest=JsonNode.Parse(File.ReadAllBytes(manifestPath))!;
        SvMapHashes hashes = SvMapHash.HashMap(target);
        SvMapFileDigest digest = hashes.Files.Single(f => f.Path == "sim/gameplay.json");
        manifest["files"]!["sim/gameplay.json"]!["size"]=digest.Size;
        manifest["files"]!["sim/gameplay.json"]!["sha256"]=digest.Sha256;
        manifest["hashes"]!["sim"] = hashes.Sim;
        File.WriteAllText(manifestPath,manifest.ToJsonString());
        return new MapCatalog(folder);
    }
    public void Dispose()
    {
        string root=Path.GetFullPath(folder);
        if (root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(root).StartsWith("openscct-glass-net-",StringComparison.Ordinal) && Directory.Exists(root)) Directory.Delete(root,true);
    }
    [Theory]
    [InlineData("A")]
    [InlineData("100")]
    [InlineData("D")]
    public void ObserversAndLateJoinReceiveTheBreakAndItsExactImpact(string profile)
    {
        MapCatalog maps=GlassMap();
        using var rig = WalkerRig.Create(SimLinkProfile.Get(profile), 10119, 2,
            new ServerSettings { SpawnWalkers = false, Maps=maps }, new ClientSettings { Gameplay = true, Maps=maps });
        rig.Server.PlayerJoined += slot =>
        {
            var viewer = rig.Server.World.Level.Spawn<PlayerController>()!;
            viewer.Player = new SimNetConnection(); viewer.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = viewer;
        };
        void Step(int count) { for (int i = 0; i < count; i++) rig.Step(); }
        Step(180);
        SVitre? found = null; foreach (SVitre actor in rig.Server.World.Level.AllActors<SVitre>()) found = actor;
        var pane = Assert.IsType<SVitre>(found);
        uint id = rig.Server.Driver.ClientConnections.First().FindActorChannel(pane)?.NetGuid ?? 0;
        Assert.True(rig.Clients[0].Driver.FindActorByNetGuid(id) is not null,
            $"Glass source id={id} remote={pane.RemoteRole} role={pane.Role} static={pane.bStatic} noDelete={pane.bNoDelete} deleted={pane.bDeleteMe} temporary={pane.bNetTemporary}; server={rig.Server.World.Level.Info.NetMode}");
        Assert.All(rig.Clients, c => Assert.IsType<SVitre>(c.Driver.FindActorByNetGuid(id)));
        var hit = new FVector(99.5f,113.25f,327.75f);
        pane.eventTakeDamage(1, null, hit, new FVector(200,0,0), SDamageType.SDTFlash);
        Step(240);
        void Check(ClientSession client)
        {
            var observed = Assert.IsType<SVitre>(client.Driver.FindActorByNetGuid(id));
            Assert.Equal(EDrawType.DT_None, observed.DrawType);
            Assert.Equal(1, observed.GlassImpactSerial);
            Assert.InRange(FVector.Dist(observed.GlassImpactLocation, hit), 0, .001f);
            Assert.InRange(FVector.Dist(observed.GlassImpactDirection, new FVector(1,0,0)), 0, .001f);
        }
        Assert.All(rig.Clients, Check);
        ClientSession late = rig.AddClient(); Step(300); Check(late);
    }
}
