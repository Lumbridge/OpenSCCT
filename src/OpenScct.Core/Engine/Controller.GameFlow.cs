using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.Controller parts the game flow uses: the player's replication info, possession, restart and the end of
// the match. Written from spec/game/respawn-and-spawning.md and spec/game/end-of-round.md.
public abstract partial class Controller
{
    private static readonly Name NAME_GameEnded = new("GameEnded");

    /// <summary>A state version of ServerReStartPlayer (state dispatch, see the OpenScct.Core README).</summary>
    public interface IServerReStartPlayer
    {
        void ServerReStartPlayer(Controller self);
    }

    /// <summary>ServerReStartPlayer through the current state.</summary>
    public void eventServerReStartPlayer()
    {
        if (StateNode is IServerReStartPlayer s)
        {
            s.ServerReStartPlayer(this);
        }
        else
        {
            ServerReStartPlayer();
        }
    }

    /// <summary>A player's controller spawns its replication info on the server, owned by itself.</summary>
    [Uc("Engine.Controller.PostBeginPlay", Sha1 = "7c26713824e83d8be2131177ce9e560630792924")]
    [PortOf("uc:Engine.Controller:PostBeginPlay", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (bIsPlayer && Role == ENetRole.ROLE_Authority)
        {
            PlayerReplicationInfo = UcSpawn.Spawn(this, PlayerReplicationInfoClass, this, default(FVector), default(FRotator))
                as PlayerReplicationInfo;
            InitPlayerReplicationInfo();
        }
    }

    [Uc("Engine.Controller.InitPlayerReplicationInfo", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Controller:InitPlayerReplicationInfo", Spec = "spec/game/replication-info.md#rules")]
    public virtual void InitPlayerReplicationInfo()
    {
    }

    [Uc("Engine.Controller.Restart", Sha1 = "18a458b36f2099582a4bec323738c24d94165c2d")]
    [PortOf("uc:Engine.Controller:Restart", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void Restart()
    {
        Enemy = null;
    }

    [Uc("Engine.Controller.Possess", Sha1 = "dad615d5653c94cc0f11cc0ce7434e24b3e8cfa0")]
    [PortOf("uc:Engine.Controller:Possess", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void Possess(Pawn aPawn)
    {
        aPawn.PossessedBy(this);
        Pawn = aPawn;
        Restart();
    }

    /// <summary>On the server, gives the existing pawn back (ServerGivePawn, which is empty in this game).</summary>
    [Uc("Engine.Controller.ServerReStartPlayer", Sha1 = "a141b8cf9898ea14df8ea40c78ca19bad76316bc")]
    [PortOf("uc:Engine.Controller:ServerReStartPlayer", Spec = "spec/game/respawn-and-spawning.md#rules")]
    protected virtual void ServerReStartPlayer()
    {
        if (Level.NetMode == ENetMode.NM_Client)
        {
            return;
        }

        if (Pawn is not null)
        {
            ServerGivePawn();
        }
    }

    /// <summary>Declared without a body in Engine.Controller.</summary>
    protected virtual void ServerGivePawn()
    {
    }

    [Uc("Engine.Controller.GameHasEnded", Sha1 = "fbabb106e0f255b8fc806165ebbb678e11144d5f")]
    [PortOf("uc:Engine.Controller:GameHasEnded", Spec = "spec/game/end-of-round.md#rules")]
    public virtual void GameHasEnded()
    {
        if (Pawn is not null)
        {
            Pawn.bNoWeaponFiring = true;
        }

        GotoState(NAME_GameEnded);
    }

    /// <summary>Server to owner: the player's own machine enters the end state.</summary>
    [Uc("Engine.Controller.ClientGameEnded", Sha1 = "69f8e4f2c85a23166bb9c25aa20b6a7a23ff746a")]
    [PortOf("uc:Engine.Controller:ClientGameEnded", Spec = "spec/game/end-of-round.md#rules")]
    public virtual void ClientGameEnded()
    {
        GotoState(NAME_GameEnded);
    }

    /// <summary>Loads the named SBase pawn class; PawnClass changes only when the load succeeds.</summary>
    [Uc("Engine.Controller.SetPawnClass", Sha1 = "52c5d0324b33d6c7300822116ce1db03fb6a48f5")]
    [PortOf("uc:Engine.Controller:SetPawnClass", Spec = "spec/game/login-and-teams.md#rules")]
    public virtual void SetPawnClass(Name inClass)
    {
        if (inClass.IsNone)
        {
            return;
        }

        UcClassInfo? info = UcSpawn.IndexFor(this).Find("SBase." + inClass.ToString());
        if (info is not null && typeof(Pawn).IsAssignableFrom(info.Type))
        {
            PawnClass = new ClassRef(info.Path);
        }
    }

    /// <summary>Server to owner: sets the controller rotation and the pawn's yaw.</summary>
    [Uc("Engine.Controller.ClientSetRotation", Sha1 = "b502af0b47e97c59391f31e4914ec4fea81f29d8")]
    [PortOf("uc:Engine.Controller:ClientSetRotation", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void ClientSetRotation(FRotator NewRotation)
    {
        Rotation = NewRotation;
        if (Pawn is not null)
        {
            Pawn.Rotation = new FRotator(0, NewRotation.Yaw, 0);
        }
    }
}
