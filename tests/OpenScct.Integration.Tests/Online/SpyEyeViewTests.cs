using System.Linq;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class SpyEyeViewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void DrawingAndChangingStanceKeepsTheEyeViewAboveThePawnCenter(int latency)
    {
        MapCatalog maps = MapCatalog.CreateDefault();
        string? folder = maps.FindFolder("TestBox");
        Assert.SkipWhen(folder is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 119, SimLinkProfile.A with { OneWayLatencyMs = latency / 2 });
        using var serverTransport = network.Bind("server");
        using var clientTransport = network.Bind("client");
        using var server = new ServerHost(serverTransport, new ServerSettings
        {
            Map = "TestBox", Maps = maps, SpawnWalkers = false, Profile = NetProfile.Enhanced,
            GameOptions = "?GameMode=0?TimeLimit=0?Players=1", UseStartGate = true, PlayersToStart = 1,
        });
        var loader = new PlayMapLoader(SvMap.Load(folder!), string.Empty, PlayMapLoader.FindCharsDir(folder!));
        var clientMaps = new MapCatalog(maps.Roots.ToArray());
        loader.Register(clientMaps);
        using var client = new ClientSession(clientTransport, "server", 0, new ClientSettings
        {
            Maps = clientMaps, Gameplay = true, InstallGameplayServices = false, LoginOptions = "?Team=0",
        });
        using var play = new PlaySession(new PlaySettings { Map = "TestBox", Side = PlaySide.Spy }, clock, client, clientMaps, folder!, loader);
        long ticks = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++ticks, Sim.Hz));
                server.Tick(clock.NowMicroseconds);
                play.Step(clock.NowMicroseconds);
            }
        }
        Tick(600);
        var pawn = Assert.IsAssignableFrom<SPawnAttaque>(play.LocalPawn);
        SPlayerController pc = play.LocalController!;
        for (int stance = 0; stance < 4; stance++)
        {
            play.Input.Set("bFireMode", true);
            Tick(6);
            play.Input.Set("bFireMode", false);
            Tick(60);
            Assert.Equal(2, pawn.WeaponLoaded);
            pc.SetSpyFirstPerson(true);
            Tick(1);
            for (int frame = 0; frame < 30; frame++)
            {
                Tick(1);
                Assert.True(play.View.SpyFirstPerson);
                float eye = play.View.Location.Z - pawn.Location.Z;
                Assert.True(eye > 15f, $"eye={eye}; EyeHeight={pawn.EyeHeight}; BaseEyeHeight={pawn.BaseEyeHeight}; oldZ={pawn.OldZ}; loc={pawn.Location}; crouched={pawn.bIsCrouched}; state={pc.GetStateName()}");
            }
            pc.SetSpyFirstPerson(false);
            play.Input.Set("bFireMode", true);
            Tick(6);
            play.Input.Set("bFireMode", false);
            Tick(60);
            Assert.Equal(0, pawn.WeaponLoaded);
            play.Input.Set("bCrouch", true);
            Tick(1);
            play.Input.Set("bCrouch", false);
            Tick(60);
        }
    }
}
