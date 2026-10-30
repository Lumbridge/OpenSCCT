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
/// spec/devices/vitre.md rules 1 and 2 over the wire: a SteeE pane broken on the server stops being drawn and loses its
/// collision on the server and on a connected client (the client used to keep the pane's colliders, so its own
/// moves still bumped into the empty frame). Skipped without the converted SteeE.
/// </summary>
public sealed class VitreNetTests
{
    private const string MapName = "SteeE";

    private static bool AnyColliderOn(CollisionWorld cw, Actor actor)
    {
        var colliders = new List<int>();
        cw.GetActorColliders(actor, colliders);
        foreach (int ci in colliders)
        {
            if (cw.GetCollider(ci).Enabled)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_pane_broken_on_the_server_loses_its_mesh_and_collision_on_the_client()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.Get("B"), 41UL, 1,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer, GameOptions = BotMatchSettings.StoryOptions },
            new ClientSettings { Gameplay = true });
        ClientSession client = rig.Clients[0];
        for (int n = 0; n < 10 * Sim.Hz && client.Map is null; n++)
        {
            rig.Step();
        }

        Assert.NotNull(client.Map);
        CollisionWorld serverCw = rig.Server.Map.Physics!.Collision;
        CollisionWorld clientCw = client.Map!.Physics!.Collision;

        // A pane with colliders on both sides.
        SVitre? pane = null;
        SVitre? copy = null;
        foreach (Actor? actor in rig.Server.Map.MapActors)
        {
            if (actor is SVitre { bDeleteMe: false } v && AnyColliderOn(serverCw, v))
            {
                var c = (SVitre)client.Map.MapActors[rig.Server.Driver.MapBinding.IndexOf(v)]!;
                if (AnyColliderOn(clientCw, c))
                {
                    pane = v;
                    copy = c;
                    break;
                }
            }
        }

        Assert.NotNull(pane);
        Assert.NotNull(copy);
        Assert.NotEqual(EDrawType.DT_None, copy.DrawType);

        pane.eventTakeDamage(1, null, pane.Location, default, SDamageType.SDTBase);
        Assert.Equal(EDrawType.DT_None, pane.DrawType);
        Assert.False(AnyColliderOn(serverCw, pane));
        for (int n = 0; n < 10 * Sim.Hz && copy.DrawType != EDrawType.DT_None; n++)
        {
            rig.Step();
        }

        Assert.True(copy.DrawType == EDrawType.DT_None, "the client pane is still drawn");
        Assert.False(AnyColliderOn(clientCw, copy), "the client pane kept its collision");
    }
}
