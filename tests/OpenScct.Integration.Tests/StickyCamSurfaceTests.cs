using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// spec/equipment/sticky-cam.md rule 2.5 on the real SteeE: the stick line (flags 0xc10bf, with OnlyProjActor) passes
/// through the map's zone volumes, as AVolume::ShouldTrace (editor 0x110a7ec0) traces a volume under OnlyProjActor
/// only when it is bProjTarget or blocks both actors and players. The line used to stop on the first volume face it
/// crossed, so about a third of the walls seen from the player starts refused a cam. Skipped without the converted SteeE.
/// </summary>
public sealed class StickyCamSurfaceTests
{
    private const string MapName = "SteeE";

    [Fact]
    public void The_stick_line_passes_through_zone_volumes_to_the_walls_of_SteeE()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(null, 1UL, 0,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer, GameOptions = BotMatchSettings.StoryOptions });
        CollisionWorld cw = rig.Server.Map.Physics!.Collision;
        int hits = 0;
        int stick = 0;
        var volumes = new List<string>();
        foreach (PlayerStart start in rig.Server.World.Level.AllActors<PlayerStart>())
        {
            FVector eye = start.Location + new FVector(0f, 0f, 40f);
            for (int yaw = 0; yaw < 65536; yaw += 65536 / 36)
            {
                foreach (int pitch in new[] { -3000, 0, 3000 })
                {
                    FVector d = new FRotator(pitch, yaw, 0).Vector();
                    if (!cw.LineCheck(out FCheckResult hit, null, eye + (d * 2000f), eye, default, SGadStickyCam.StickTraceFlags) || !hit.bHit)
                    {
                        continue;
                    }

                    hits++;
                    stick += SGadStickyCam.CanStickOn(hit) ? 1 : 0;
                    if (hit.Actor is Volume { bWorldGeometry: false, bProjTarget: false } v && !(v.bBlockActors && v.bBlockPlayers))
                    {
                        volumes.Add(v.ToString());
                    }
                }
            }
        }

        Assert.True(hits > 1000, $"only {hits} lines hit anything");
        Assert.Empty(volumes);

        // What is left are the doors (movers), props and the damage volume a merc can stand in: a few percent.
        Assert.True(stick >= hits * 95 / 100, $"{stick} of {hits} hits take a cam");
    }
}
