using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state changes across machines: the server mirrors a state to the owning client, the
// owning client asks the server to follow. Written from spec/game/netmode-branches.md (rules 1 to 4).
public partial class SPlayerController
{
    private static readonly Name NAME_s_UseObject_StickyCam_SmoothToPosition = new("s_UseObject_StickyCam_SmoothToPosition");

    /// <summary>A state version of the controller's GotoState (states that ignore it implement it empty).</summary>
    public interface IGotoState
    {
        EGotoState GotoState(SPlayerController self, Name NewState, Name Label, bool bDontSaveCurrentState);
    }

    /// <summary>A state version of MirrorState (states that ignore it implement it empty).</summary>
    public interface IMirrorState
    {
        void MirrorState(SPlayerController self, Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState, Actor? AddActor);
    }

    /// <summary>A state version of ChangeState (states that ignore it implement it empty).</summary>
    public interface IChangeState
    {
        void ChangeState(SPlayerController self, Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState);
    }

    /// <summary>Script GotoState of a controller: through the state (so it can be ignored), saving the previous state.</summary>
    public override EGotoState GotoState(Name NewState = default, Name Label = default) => GotoState(NewState, Label, false);

    /// <summary>
    /// The controller's GotoState (native 1175, hooked by Reloaded): remembers the current state in PreviousState
    /// unless <paramref name="bDontSaveCurrentState"/>, then changes state. Goes through the current state first.
    /// </summary>
    [UcNative("AController::execGotoState")]
    [Provenance(ProvenanceKind.Estimated, "Controller.GotoState native 1175 (hooked by Reloaded): PreviousState is the state left")]
    public EGotoState GotoState(Name NewState, Name Label, bool bDontSaveCurrentState)
    {
        if (StateNode is IGotoState s)
        {
            return s.GotoState(this, NewState, Label, bDontSaveCurrentState);
        }

        return GlobalGotoState(NewState, Label, bDontSaveCurrentState);
    }

    /// <summary>The class-level GotoState (UnrealScript Global.GotoState).</summary>
    public EGotoState GlobalGotoState(Name NewState, Name Label, bool bDontSaveCurrentState)
    {
        if (!bDontSaveCurrentState)
        {
            PreviousState = StateNode.Name;
        }

        return base.GotoState(NewState, Label);
    }

    /// <summary>True for the player this machine controls (the host's own player, or a client's own copy).</summary>
    [Uc("SBase.SPlayerController.IAmOnClient", Sha1 = "af1606566197d38b667e906f88c547599158aa86")]
    [PortOf("uc:SBase.SPlayerController:IAmOnClient", Spec = "spec/game/netmode-branches.md#data")]
    public bool IAmOnClient() => RemoteRole != ENetRole.ROLE_AutonomousProxy;

    /// <summary>MirrorState through the current state.</summary>
    public void eventMirrorState(Name nomdustate, Name nomduLabel = default, bool bDontSaveCurrentState = false, Actor? AddActor = null)
    {
        if (StateNode is IMirrorState s)
        {
            s.MirrorState(this, nomdustate, nomduLabel, bDontSaveCurrentState, AddActor);
        }
        else
        {
            MirrorState(nomdustate, nomduLabel, bDontSaveCurrentState, AddActor);
        }
    }

    /// <summary>
    /// The server changes state here and, for a player without a local viewport, tells the owning client
    /// (PCChangeClientState) with an acknowledgement time refreshed when older than 1 s.
    /// </summary>
    [Uc("SBase.SPlayerController.MirrorState", Sha1 = "8619ddcddb5ca685e3eaf6ffd1ce8e6c69824c57")]
    [PortOf("uc:SBase.SPlayerController:MirrorState", Spec = "spec/game/netmode-branches.md#rules")]
    protected virtual void MirrorState(Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState, Actor? AddActor)
    {
        GotoState(nomdustate, nomduLabel, bDontSaveCurrentState);
        if (Player is UViewport)
        {
            return;
        }

        if (Level.TimeSeconds - MirrorStateAcknowledgementTime > K.MirrorStateAckRefresh)
        {
            MirrorStateAcknowledgementTime = Level.TimeSeconds;
        }

        GameFlowServices.Host(this).ClientCall(this, new GameCall("PCChangeClientState")
        {
            Float1 = MirrorStateAcknowledgementTime,
            Float2 = Level.TimeSeconds,
            Name1 = nomdustate,
            Name2 = nomduLabel,
            Bool = bDontSaveCurrentState,
            Actor = AddActor,
        });
    }

    /// <summary>
    /// Server to client: follow the server's state unless the order is older than the last one applied (one sticky
    /// camera case is skipped).
    /// </summary>
    [Uc("SBase.SPlayerController.PCChangeClientState", Sha1 = "6acb8bdc3eb9d42e71760a5d163a4f9372c621d8")]
    [PortOf("uc:SBase.SPlayerController:PCChangeClientState", Spec = "spec/game/netmode-branches.md#rules")]
    public void PCChangeClientState(float MSATime, float fNewTime, Name nomdustate, Name nomduLabel = default,
        bool bDontSaveCurrentState = false, Actor? AddActor = null)
    {
        if (MirrorStateLastTime > fNewTime)
        {
            return;
        }

        MirrorStateLastTime = fNewTime;
        ActorPassedByMirrorState = AddActor;
        MirrorStateAcknowledgementTime = MSATime;
        if (nomdustate == NAME_s_UseObject_StickyCam_SmoothToPosition && Level.TimeSeconds < PredictedStickyCamDeadline)
        {
            return;
        }

        GotoState(nomdustate, nomduLabel, bDontSaveCurrentState);
    }

    /// <summary>ChangeState through the current state.</summary>
    public void eventChangeState(Name nomdustate, Name nomduLabel = default, bool bDontSaveCurrentState = false)
    {
        if (StateNode is IChangeState s)
        {
            s.ChangeState(this, nomdustate, nomduLabel, bDontSaveCurrentState);
        }
        else
        {
            ChangeState(nomdustate, nomduLabel, bDontSaveCurrentState);
        }
    }

    /// <summary>The owning machine changes state; a client also asks the server to follow (PCChangeServerState).</summary>
    [Uc("SBase.SPlayerController.ChangeState", Sha1 = "4ccd2cbe61e3153a31fb67699605e8db5248b5ad")]
    [PortOf("uc:SBase.SPlayerController:ChangeState", Spec = "spec/game/netmode-branches.md#rules")]
    protected virtual void ChangeState(Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState)
    {
        GotoState(nomdustate, nomduLabel, bDontSaveCurrentState);
        if (Level.NetMode is ENetMode.NM_Standalone or ENetMode.NM_ListenServer)
        {
            return;
        }

        GameFlowServices.Host(this).ServerCall(this, new GameCall("PCChangeServerState")
        {
            Float1 = MirrorStateAcknowledgementTime,
            Name1 = nomdustate,
            Name2 = nomduLabel,
            Bool = bDontSaveCurrentState,
        });
    }

    /// <summary>
    /// Client to server: follow the client only when its acknowledgement time matches within 0.01 s. Not ported
    /// here: the gas knock-out credit of the knock-down states (combat spec).
    /// </summary>
    [Uc("SBase.SPlayerController.PCChangeServerState", Sha1 = "cc31e893422013af3f50a4f84f08b33c96e05059")]
    [PortOf("uc:SBase.SPlayerController:PCChangeServerState", Spec = "spec/game/netmode-branches.md#rules")]
    public void PCChangeServerState(float CodeTime, Name nomdustate, Name nomduLabel = default, bool bDontSaveCurrentState = false)
    {
        if (SvmMath.Abs(CodeTime - MirrorStateAcknowledgementTime) < K.ServerStateTolerance)
        {
            GotoState(nomdustate, nomduLabel, bDontSaveCurrentState);
        }
    }
}
