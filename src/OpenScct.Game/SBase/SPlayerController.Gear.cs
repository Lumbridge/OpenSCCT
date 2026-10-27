using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

// The mid-round gear change's calls (an OpenSCCT addition, spec/additions/gear-change.md): the owner asks the server
// to put its newly saved kit on, and the server answers whether it went on now or waits for the next spawn.
public partial class SPlayerController
{
    private static readonly Name CNAME_ServerChangeGear = new("ServerChangeGear");
    private static readonly Name CNAME_ClientGearChangeResult = new("ClientGearChangeResult");

    /// <summary>Level time of this player's last kit swap (server; the hub's cooldown reads it).</summary>
    public float GearChangedAt = float.NegativeInfinity;

    /// <summary>Owner to server: the player saved a new kit; the game info decides when it goes on.</summary>
    public void ServerChangeGear()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_ServerChangeGear));
            return;
        }

        if (Level.Game is not SGameInfo game)
        {
            return;
        }

        GearChangeResult result = game.CanChangeGearNow(this, out float wait);
        if (result == GearChangeResult.Now && Pawn is SBasePawn pawn)
        {
            GearChangedAt = Level.TimeSeconds;
            game.ChangeGearNow(pawn);
        }

        ClientGearChangeResult(result, wait);
    }

    /// <summary>Server to owner: the answer, shown on the owner's HUD.</summary>
    public void ClientGearChangeResult(GearChangeResult Result, float WaitSeconds)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_ClientGearChangeResult) { Int1 = (int)Result, Float1 = WaitSeconds });
            return;
        }

        XLevel?.GetService<IGearChangeFeedback>()?.GearChangeAnswered(Result, WaitSeconds);
    }
}
