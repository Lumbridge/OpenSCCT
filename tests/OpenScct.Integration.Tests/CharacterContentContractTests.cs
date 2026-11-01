using System;
using System.IO;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CharacterContentContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "svm-character-contract-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private MapCatalog Content(string name, string json = "{\"sequences\":{}}")
    {
        string directory = Path.Combine(_root, name);
        string chars = Path.Combine(directory, "shared", "chars", "SPerso");
        Directory.CreateDirectory(chars);
        File.WriteAllText(Path.Combine(chars, "PRO.anim.json"), json);
        File.WriteAllText(Path.Combine(chars, "Def.anim.json"), json);
        File.WriteAllBytes(Path.Combine(chars, "ATT_01.glb"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(chars, "DEF_01.glb"), [4, 5, 6]);
        return new MapCatalog(Path.Combine(directory, "maps"));
    }

    [Theory]
    [InlineData("PRO.anim.json")]
    [InlineData("Def.anim.json")]
    [InlineData("ATT_01.glb")]
    [InlineData("DEF_01.glb")]
    public void EachGameplayCharacterAssetParticipatesInTheContract(string file)
    {
        ulong baseline = Content("first").CharacterContentHash;
        var changed = Content("second");
        string path = Path.Combine(_root, "second", "shared", "chars", "SPerso", file);
        if (file.EndsWith(".json", StringComparison.Ordinal)) File.WriteAllText(path, "{\"sequences\":{},\"changed\":true}");
        else File.WriteAllBytes(path, [7, 8, 9]);
        Assert.NotEqual(baseline, changed.CharacterContentHash);
    }

    [Fact]
    public void CharacterJsonFormattingAndInstallLocationDoNotChangeTheContract()
    {
        var first = Content("first");
        var second = Content("second", "{\n  \"sequences\" : { }\n}\n");
        Assert.Equal(first.CharacterContentHash, second.CharacterContentHash);
        Assert.NotEqual(first.CharacterContentHash, new MapCatalog().CharacterContentHash);
    }

    [Fact]
    public void GameplayRejectsDifferentCharacterContentBeforeLoadingOrSpawning()
    {
        string chars = Path.Combine(_root, "shared", "chars", "SPerso");
        Directory.CreateDirectory(chars);
        File.WriteAllText(Path.Combine(chars, "PRO.anim.json"), "{\"sequences\":{\"WaitStAlFd0\":{\"frames\":2,\"rate\":1}}}");
        var serverMaps = new MapCatalog();
        var clientMaps = new MapCatalog(Path.Combine(_root, "maps"));
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 711, SimLinkProfile.A);
        using var serverTransport = network.Bind("server");
        using var server = new ServerHost(serverTransport, new ServerSettings
        {
            Classes = WalkerRig.Classes, Maps = serverMaps, GameOptions = "?GameMode=5?LivesATT=2?LivesDEF=2",
        });
        using var clientTransport = network.Bind("client");
        using var client = new ClientSession(clientTransport, "server", 0, new ClientSettings
        {
            Classes = WalkerRig.Classes, Maps = clientMaps, Gameplay = true,
        });
        for (int tick = 1; tick <= 300 && client.State == EClientState.Connecting; tick++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
            server.Tick(clock.NowMicroseconds); client.Tick(clock.NowMicroseconds);
        }
        Assert.Equal(EClientState.Rejected, client.State);
        Assert.Equal(ControlMessage.VersionMismatchText, client.RejectReason);
        Assert.Equal("shared/chars", client.RejectFile);
        Assert.Null(client.Map);
        Assert.Empty(server.Players);
    }
}
