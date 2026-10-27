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

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Controller.Destroyed calls RemoveController")]
    protected internal override void Destroyed()
    {
        RemoveController();
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
