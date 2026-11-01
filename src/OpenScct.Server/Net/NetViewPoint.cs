using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>The authority's current viewing position for distance and visibility replication tests.</summary>
internal static class NetViewPoint
{
    public static Actor? ActorForConnection(NetConnection connection)
    {
        Actor? actor = connection.Actor;
        if (actor is not PlayerController pc) return actor;
        if (pc is SPlayerController player)
        {
            PlayerCalcView.EViewSource source = PlayerCalcView.SourceOf(player);
            if ((source is PlayerCalcView.EViewSource.StickyCam or PlayerCalcView.EViewSource.StickyCamSmoothToPosition or
                PlayerCalcView.EViewSource.Mirador) && player.SBPawn?.UsedObject is { bDeleteMe: false } used)
                return used;
            if (source == PlayerCalcView.EViewSource.Spectating && player.SpecTarget is { bDeleteMe: false } target)
                return target;
            if (source == PlayerCalcView.EViewSource.Spectating && pc.ViewTarget is { bDeleteMe: false } spectatorView)
                return spectatorView;
        }
        return pc.Pawn is { bDeleteMe: false } pawn ? pawn :
            pc.ViewTarget is { bDeleteMe: false } viewTarget ? viewTarget : pc;
    }

    public static FVector ForConnection(NetConnection connection)
    {
        Actor? actor = connection.Actor;
        if (actor is not PlayerController pc) return actor?.Location ?? default;

        if (pc is SPlayerController player)
        {
            PlayerCalcView.EViewSource source = PlayerCalcView.SourceOf(player);
            if (player.SBPawn?.UsedObject is { bDeleteMe: false })
            {
                if (source is PlayerCalcView.EViewSource.StickyCam or PlayerCalcView.EViewSource.StickyCamSmoothToPosition)
                {
                    // Use the authoritative destination during fly-in too, so the feed has arrived when it opens.
                    PlayerCalcView.StickyCamView(player, false, out _, out FVector location, out _);
                    return location;
                }
                if (source == PlayerCalcView.EViewSource.Mirador)
                {
                    PlayerCalcView.MiradorView(player, false, out _, out FVector location, out _);
                    return location;
                }
            }
        }

        // The render-only camera solver moves the client controller. The server controller can remain at the
        // map origin, so it cannot supply the ordinary view source (nearby gas and remote cameras then disappear).
        return ActorForConnection(connection)?.Location ?? default;
    }
}
