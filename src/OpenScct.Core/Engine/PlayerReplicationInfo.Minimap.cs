using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.PlayerReplicationInfo: the strategic map's positions (spec/hud/strategic-map.md). The page asks the server
// for them every half second; the Minimap fields then replicate to every client.
public partial class PlayerReplicationInfo
{
    /// <summary>
    /// Client to server (reliable): every player controller's PRI gets its pawn's location (whole units) and yaw. The
    /// yaw is nudged by one when it did not change, so the fields are sent again, and -1 is kept for "no position".
    /// A controller without a pawn or PRI sets the caller's own MinimapYaw to -1, as the original does (its other
    /// PRIs keep their last position).
    /// </summary>
    [Uc("Engine.PlayerReplicationInfo.UpdateMinimapParams", Sha1 = "cc23812b634e9b19e9b5f253c786295051711d91")]
    [PortOf("uc:Engine.PlayerReplicationInfo:UpdateMinimapParams", Spec = "spec/hud/strategic-map.md#rules")]
    public void UpdateMinimapParams()
    {
        if (Level is not { } level)
        {
            return;
        }

        for (Controller? c = level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is not PlayerController pc)
            {
                continue;
            }

            if (pc.Pawn is null || pc.PlayerReplicationInfo is null)
            {
                MinimapYaw = -1;
                continue;
            }

            PlayerReplicationInfo pri = pc.PlayerReplicationInfo;
            FVector pos = pc.Pawn.Location;
            int oldYaw = pri.MinimapYaw;
            pri.MinimapX = (int)pos.X;
            pri.MinimapY = (int)pos.Y;
            pri.MinimapZ = (int)pos.Z;
            pri.MinimapYaw = pc.Pawn.Rotation.Yaw;
            if (pri.MinimapYaw == oldYaw)
            {
                pri.MinimapYaw++;
            }

            if (pri.MinimapYaw == -1)
            {
                pri.MinimapYaw = 0;
            }
        }
    }
}
