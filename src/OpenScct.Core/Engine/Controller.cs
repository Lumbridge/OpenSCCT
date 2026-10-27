using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Engine.Controller: the mind behind a Pawn. Controllers form the level's ControllerList.</summary>
public abstract partial class Controller : Actor
{
    protected Controller()
    {
        bHidden = true;
    }

    public Pawn? Pawn;
    public PlayerReplicationInfo? PlayerReplicationInfo;

    /// <summary>Next controller in LevelInfo.ControllerList.</summary>
    public Controller? nextController { get; internal set; }

    public bool bIsPlayer;
    public int PlayerNum;

    /// <summary>State before the last state change (SCCT addition).</summary>
    public Name PreviousState;

    public FVector Destination;
    public Actor? MoveTarget;
    public Actor? Focus;

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Controller.PreBeginPlay calls AddController first")]
    protected internal override void PreBeginPlay()
    {
        AddController();
        base.PreBeginPlay();
    }

    /// <summary>
    /// On the authority: leaves the ControllerList, leaves the team (a player, not a spectator) and destroys the
    /// PlayerReplicationInfo, so a player who leaves drops off every client's player list.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Controller.Destroyed: Role check, RemoveController, Team.RemoveFromTeam unless bOnlySpectator, PlayerReplicationInfo.Destroy")]
    protected internal override void Destroyed()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            base.Destroyed();
            return;
        }

        RemoveController();
        if (PlayerReplicationInfo is { } pri)
        {
            if (!pri.bOnlySpectator && pri.Team is { } team)
            {
                team.RemoveFromTeam(this);
            }

            pri.Destroy();
        }

        base.Destroyed();
    }

    /// <summary>Puts this controller at the head of LevelInfo.ControllerList.</summary>
    [UcNative("AController::execAddController")]
    public void AddController()
    {
        LevelInfo info = Level;
        for (Controller? c = info.ControllerList; c is not null; c = c.nextController)
        {
            if (ReferenceEquals(c, this))
            {
                return;
            }
        }

        nextController = info.ControllerList;
        info.ControllerList = this;
    }

    /// <summary>Unlinks this controller from LevelInfo.ControllerList.</summary>
    [UcNative("AController::execRemoveController")]
    public void RemoveController()
    {
        LevelInfo info = Level;
        if (ReferenceEquals(info.ControllerList, this))
        {
            info.ControllerList = nextController;
        }
        else
        {
            for (Controller? c = info.ControllerList; c is not null; c = c.nextController)
            {
                if (ReferenceEquals(c.nextController, this))
                {
                    c.nextController = nextController;
                    break;
                }
            }
        }

        nextController = null;
    }

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Pawn = Live(Pawn);
        PlayerReplicationInfo = Live(PlayerReplicationInfo);
        MoveTarget = Live(MoveTarget);
        Focus = Live(Focus);
    }
}
