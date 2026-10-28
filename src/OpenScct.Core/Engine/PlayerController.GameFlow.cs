using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.PlayerController parts the game flow uses: restart and possession hand-off, asking for a pawn, level
// switches, and the PlayerTick event. Written from spec/game/respawn-and-spawning.md and spec/game/round-travel.md.
public partial class PlayerController
{
    private static readonly Name NAME_GameEnded = new("GameEnded");

    /// <summary>A state version of PlayerTick (state dispatch, see the OpenScct.Core README).</summary>
    public interface IPlayerTick
    {
        void PlayerTick(PlayerController self, float DeltaTime);
    }

    /// <summary>
    /// PlayerTick through the current state. The engine calls it every tick for a player controlled on this machine,
    /// before physics; the input and movement layer drives it.
    /// </summary>
    public void eventPlayerTick(float DeltaTime)
    {
        if (StateNode is IPlayerTick s)
        {
            s.PlayerTick(this, DeltaTime);
        }
        else
        {
            PlayerTick(DeltaTime);
        }
    }

    /// <summary>The class-level PlayerTick (empty in Engine; the SBase controller defines its own).</summary>
    [Uc("Engine.PlayerController.PlayerTick", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.PlayerController:PlayerTick", Spec = "spec/game/spectating.md#rules")]
    protected internal virtual void PlayerTick(float DeltaTime)
    {
    }

    /// <summary>True for a player on another machine: the server reaches it through its network connection.</summary>
    public bool IsRemotePlayer => Player is UNetConnection;

    /// <summary>The level's game host (see <see cref="IGameHost"/>), or the null host.</summary>
    protected IGameHost GameHost => XLevel.GetService<IGameHost>() ?? NullGameHost.Instance;

    [Uc("Engine.PlayerController.Restart", Sha1 = "df4b2e9df93d55422c3cf929410dc05eba5421ff")]
    [PortOf("uc:Engine.PlayerController:Restart", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void Restart()
    {
        base.Restart();
        ServerTimeStamp = 0f;
        TimeMargin = 0f;
        EnterStartState();
        SetViewTarget(Pawn);
        bBehindView = Pawn?.PointOfView() ?? false;
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("ClientRestart"));
        }
        else
        {
            ClientRestart();
        }
    }

    [Uc("Engine.PlayerController.Possess", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.PlayerController:Possess", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void Possess(Pawn aPawn)
    {
    }

    [Uc("Engine.PlayerController.UnPossess", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.PlayerController:UnPossess", Spec = "spec/game/death-lives-respawn.md#rules")]
    public virtual void UnPossess()
    {
    }

    /// <summary>Picks the controller's start state after a spawn; SBase defines it.</summary>
    [Uc("Engine.PlayerController.EnterStartState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.PlayerController:EnterStartState", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void EnterStartState()
    {
    }

    /// <summary>Server to owner: the owning machine takes the new pawn; SBase defines it.</summary>
    [Uc("Engine.PlayerController.ClientRestart", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.PlayerController:ClientRestart", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void ClientRestart()
    {
    }

    [Uc("Engine.PlayerController.CanRestartPlayer", Sha1 = "19654755042791fc56203f13244e2b516814f5e4")]
    [PortOf("uc:Engine.PlayerController:CanRestartPlayer", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual bool CanRestartPlayer() => !(PlayerReplicationInfo?.bOnlySpectator ?? false);

    /// <summary>Owner to server: get the pawn the server has, or have the current state restart the player.</summary>
    [Uc("Engine.PlayerController.AskForPawn", Sha1 = "2537e694b9fe737c2474e5bbe3a14c09adab7089")]
    [PortOf("uc:Engine.PlayerController:AskForPawn", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void AskForPawn()
    {
        if (IsInState(NAME_GameEnded))
        {
            if (IsRemotePlayer)
            {
                GameHost.ClientCall(this, new GameCall("ClientGotoState") { Name1 = NAME_GameEnded, Name2 = NAME_Begin });
            }
            else
            {
                GotoState(NAME_GameEnded, NAME_Begin);
            }
        }
        else if (Pawn is not null)
        {
            GivePawn(Pawn);
        }
        else
        {
            bFrozen = false;
            eventServerReStartPlayer();
        }
    }

    [Uc("Engine.PlayerController.GivePawn", Sha1 = "ae430debbf108173ef55c309e0b291c684b0a365")]
    [PortOf("uc:Engine.PlayerController:GivePawn", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void GivePawn(Pawn? NewPawn)
    {
        if (NewPawn is null)
        {
            return;
        }

        Pawn = NewPawn;
        NewPawn.Controller = this;
        ClientRestart();
    }

    /// <summary>Native SetViewTarget: the actor the camera follows; the controller itself when None.</summary>
    [UcNative("APlayerController::execSetViewTarget")]
    [Provenance(ProvenanceKind.Estimated, "UE2 SetViewTarget: a None target views the controller itself")]
    public void SetViewTarget(Actor? NewViewTarget)
    {
        ViewTarget = NewViewTarget ?? this;
    }

    /// <summary>
    /// Server to owner: the camera follows <paramref name="A"/>. A remote player's copy on the server sends it to his
    /// machine; the owning machine (or a local player) sets the view target. Not ported: a None target asking the
    /// server to resend its view target (ServerVerifyViewTarget).
    /// </summary>
    [Uc("Engine.PlayerController.ClientSetViewTarget", Sha1 = "600a457d28e0077afd08703d13dcc3d52990caf6")]
    [PortOf("uc:Engine.PlayerController:ClientSetViewTarget", Spec = "spec/game/spectating.md#rules")]
    public virtual void ClientSetViewTarget(Actor? A)
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall(NAME_ClientSetViewTarget) { Actor = A });
            return;
        }

        SetViewTarget(A);
    }

    private static readonly Name NAME_ClientSetViewTarget = new("ClientSetViewTarget");

    /// <summary>Server travel to <paramref name="URL"/>; only a standalone game or a listen server can switch level.</summary>
    [Uc("Engine.PlayerController.SwitchLevel", Sha1 = "ed2fa429bc8ccadbaf9eaab8d5562ed758696af4")]
    [PortOf("uc:Engine.PlayerController:SwitchLevel", Spec = "spec/game/round-travel.md#rules")]
    public virtual void SwitchLevel(string URL)
    {
        if (Level.NetMode == ENetMode.NM_Standalone || Level.NetMode == ENetMode.NM_ListenServer)
        {
            Level.ServerTravel(URL, false);
        }
    }
}
