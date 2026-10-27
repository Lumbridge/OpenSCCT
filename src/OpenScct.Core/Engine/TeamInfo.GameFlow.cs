using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.TeamInfo membership. Written from spec/game/login-and-teams.md.
public partial class TeamInfo
{
    /// <summary>
    /// Adds a controller: Size goes up, the PRI joins the team and gets the lowest TeamID (from 0 for a player
    /// controller, 1 otherwise) that no other player on the team holds.
    /// </summary>
    [Uc("Engine.TeamInfo.AddToTeam", Sha1 = "df6dc8c61386493a55a124a41916c87b08ec6a61")]
    [PortOf("uc:Engine.TeamInfo:AddToTeam", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual bool AddToTeam(Controller? Other)
    {
        if (Other is null || Other is MessagingSpectator)
        {
            return false;
        }

        Size++;
        PlayerReplicationInfo? pri = Other.PlayerReplicationInfo;
        if (pri is null)
        {
            return true;
        }

        pri.Team = this;
        pri.TeamID = Other is PlayerController ? 0 : 1;
        bool bSuccess = false;
        while (!bSuccess)
        {
            bSuccess = true;
            for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
            {
                if (p.bIsPlayer && !ReferenceEquals(p, Other) && p.PlayerReplicationInfo is { } other &&
                    ReferenceEquals(other.Team, pri.Team) && other.TeamID == pri.TeamID)
                {
                    bSuccess = false;
                }
            }

            if (!bSuccess)
            {
                pri.TeamID++;
            }
        }

        return true;
    }

    /// <summary>Only the size goes down; the PRI keeps its team reference.</summary>
    [Uc("Engine.TeamInfo.RemoveFromTeam", Sha1 = "7132f556a6459ab47374793984fada073ae1b18d")]
    [PortOf("uc:Engine.TeamInfo:RemoveFromTeam", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual void RemoveFromTeam(Controller? Other)
    {
        Size--;
    }
}
