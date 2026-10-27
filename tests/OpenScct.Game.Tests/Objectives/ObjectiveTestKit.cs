using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Tests.Objectives;

/// <summary>Records what the objective code sends to other machines; owner-client calls run here as for a local player.</summary>
internal sealed class RecordingNet : IObjectiveNet
{
    public readonly List<(PlayerController Receiver, int Switch, Actor? Obj)> Messages = new();
    public readonly List<(string Call, Actor Target, bool Flag)> Multicasts = new();
    public readonly List<(SInteractObject? Obj, int Index)> ClientAdds = new();
    public readonly List<SInteractObject?> ClientRemoves = new();

    public void AddRemoveObjectiveOnClientHud(SObjective Objective, bool Add) => Multicasts.Add(("ObjectiveHud", Objective, Add));

    public void AddRemoveObjectiveForRadar(SObjectiveTrigger Trigger, bool bAdd) => Multicasts.Add(("Radar", Trigger, bAdd));

    public void AddRemoveActorOnClientHud(STriggerInteraction Interaction, SPlayerController? Instigator, bool Add) =>
        Multicasts.Add(("ActorHud", Interaction, Add));

    public void SetFlagCarrier(SFlag Flag, bool taken) => Multicasts.Add(("FlagCarrier", Flag, taken));

    public void AddObjetOnClient(SInteractionManager Manager, SInteractObject? Obj, int i)
    {
        ClientAdds.Add((Obj, i));
        Manager.AddObjetOnClient(Obj, i);
    }

    public void RemoveInteractionObjOnClient(SInteractionManager Manager, SInteractObject? Object)
    {
        ClientRemoves.Add(Object);
        Manager.RemoveInteractionObjOnClient(Object);
    }

    public void ReceiveLocalizedMessage(PlayerController Receiver, int Switch, Actor? OptionalObject) =>
        Messages.Add((Receiver, Switch, OptionalObject));

    /// <summary>The receivers of every message with <paramref name="Switch"/>.</summary>
    public List<PlayerController> To(int Switch)
    {
        var list = new List<PlayerController>();
        foreach ((PlayerController receiver, int s, _) in Messages)
        {
            if (s == Switch)
            {
                list.Add(receiver);
            }
        }

        return list;
    }

    public int Count(int Switch) => To(Switch).Count;
}

/// <summary>Records the calls into the game rules.</summary>
internal sealed class RecordingHost : IObjectiveHost
{
    public readonly List<(Controller? Scorer, int Index)> Points = new();
    public readonly List<SObjective> Taken = new();

    public void AddPoint(Controller? Scorer, int ActionIndex) => Points.Add((Scorer, ActionIndex));

    public void NotifyObjectiveTaken(SObjective Objective) => Taken.Add(Objective);
}

/// <summary>A controller state that runs the objective rules the controller port will call from its states.</summary>
internal sealed class PcState : ActorState
{
    private readonly Action<SPlayerController>? _begin;
    private readonly Action<SPlayerController>? _end;
    private readonly Action<SPlayerController>? _timer;
    private readonly Action<SPlayerController, int>? _code;

    public PcState(Name name, Action<SPlayerController>? begin = null, Action<SPlayerController>? end = null,
        Action<SPlayerController>? timer = null, Action<SPlayerController, int>? code = null, params (string, int)[] labels)
        : base(nameof(TestPC), name.ToString(), null, false, labels)
    {
        _begin = begin;
        _end = end;
        _timer = timer;
        _code = code;
    }

    public override void BeginState(Actor self) => _begin?.Invoke((SPlayerController)self);

    public override void EndState(Actor self) => _end?.Invoke((SPlayerController)self);

    public override void Timer(Actor self) => _timer?.Invoke((SPlayerController)self);

    public override void Code(Actor self, ref LatentFrame frame)
    {
        int pc = frame.Pc;
        frame.Pc = LatentFrame.NoCode;
        _code?.Invoke((SPlayerController)self, pc);
    }
}

/// <summary>
/// A player controller whose objective states call the rules exactly where the controller port will: the use
/// states start and stop hacks and take objectives, the leave labels report the result.
/// </summary>
internal class TestPC : SPlayerController
{
    public static readonly StateTable States = new(SPlayerController.ClassStates,
        new PcState(K.s_PlayerWalkingSt0H),
        new PcState(K.s_PlayerWalkingCr0H),
        new PcState(K.GameEnded),
        new PcState(K.s_MenuInteraction, begin: InteractionMenuRules.MenuBeginState),
        new PcState(K.s_InteractWithObjectWaitingEvent),
        new PcState(K.s_Computer_GoTo),
        new PcState(K.s_Computer_Enter),
        new PcState(K.s_Computer_Use, begin: TerminalHackRules.UseBeginState, end: TerminalHackRules.UseEndState,
            timer: TerminalHackRules.UseTimer),
        new PcState(K.s_Computer_Leave, begin: TerminalHackRules.LeaveBeginState),
        new PcState(K.s_Computer_FastLeave, begin: TerminalHackRules.FastLeaveBeginState),
        new PcState(K.s_Hack_GoTo),
        new PcState(K.s_Hack_Enter, begin: SwitchHackRules.EnterStart),
        new PcState(K.s_Hack_Use, begin: SwitchHackRules.UseBeginState, timer: SwitchHackRules.UseTimer),
        new PcState(K.s_Hack_Leave, code: static (pc, at) => SwitchHackRules.PostAndForget(pc, at == 1),
            labels: [("TriggerHack_Successfull", 1), ("TriggerHack_Failed", 2)]),
        new PcState(K.s_Hack_FastLeave, begin: static pc => SwitchHackRules.PostAndForget(pc, false)),
        new PcState(K.s_Maintain_GoTo),
        new PcState(K.s_Maintain_Leave),
        new PcState(K.s_FlagAndDropZone_GoTo),
        new PcState(K.s_Pickup_GoTo),
        new PcState(K.s_Pickup, begin: DiskCarryRules.PickupBeginState, end: DiskCarryRules.PickupEndState),
        new PcState(K.s_PutFlagInDropZone, begin: DiskCarryRules.PutFlagBeginState, end: DiskCarryRules.PutFlagEndState));

    /// <summary>Every server-ordered state change (MirrorState) this controller received.</summary>
    public readonly List<(Name State, Name Label, bool DontSave)> Mirrors = new();

    public override StateTable GetStateTable() => States;

    protected override void MirrorState(Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState, Actor? AddActor)
    {
        Mirrors.Add((nomdustate, nomduLabel, bDontSaveCurrentState));
        base.MirrorState(nomdustate, nomduLabel, bDontSaveCurrentState, AddActor);
    }
}

/// <summary>A synthetic level for objective tests: services installed, a GRI with two teams, map-load helpers.</summary>
internal sealed class ObjWorld
{
    public ObjWorld(ulong seed = 1)
    {
        World = new World(seed, ENetMode.NM_DedicatedServer);
        Level = World.Level;
        Level.SetService<IObjectiveNet>(Net);
        Level.SetService<IObjectiveHost>(Host);
        Gri = new GameReplicationInfo();
        Gri.Teams[0] = new STeamInfo { TeamIndex = K.TeamSpies };
        Gri.Teams[1] = new STeamInfo { TeamIndex = K.TeamMercs };
        Level.Info.GRI = Gri;
    }

    public World World { get; }

    public Level Level { get; }

    public RecordingNet Net { get; } = new();

    public RecordingHost Host { get; } = new();

    public GameReplicationInfo Gri { get; }

    /// <summary>Places map actors (bNoDelete classes only spawn while a map loads) and runs the begin-play passes.</summary>
    public void Load(Action<ObjWorld> place)
    {
        Level.BeginLoad();
        place(this);
        Level.EndLoad();
    }

    /// <summary>Spawns <paramref name="actor"/> (with its map properties already set) at a pose.</summary>
    public T Place<T>(T actor, FVector location, FRotator rotation = default)
        where T : Actor => Level.SpawnActor(actor, location, rotation) ?? throw new InvalidOperationException($"{actor} did not spawn");

    /// <summary>A living spy or merc player with a Use menu, standing at <paramref name="location"/>.</summary>
    public TestPC AddPlayer(bool spy, FVector location, FRotator rotation = default)
    {
        var pc = Level.SpawnActor(new TestPC(), location, rotation) ?? throw new InvalidOperationException("no controller");
        SBasePawn pawn = spy ? new SPawnAttaque() : new SPAWNDEFENSE();
        Level.SpawnActor(pawn, location, rotation);
        pawn.Health = 100;
        pawn.Controller = pc;
        pc.Pawn = pawn;
        pc.SPawnType = pawn.SPawnType;
        pc.PlayerReplicationInfo = new PlayerReplicationInfo { Team = Gri.Teams[spy ? 0 : 1], Stats = new USPlayerStats() };
        InteractionMenuRules.EnsureManager(pc);
        pc.GotoState(K.s_PlayerWalkingSt0H);
        return pc;
    }

    /// <summary>Runs whole ticks covering <paramref name="seconds"/>.</summary>
    public void Run(float seconds) => World.Step((int)(seconds * Sim.Hz + 0.5f));

    /// <summary>The pawn of <paramref name="pc"/> touches <paramref name="actor"/>.</summary>
    public static void Touch(SPlayerController pc, Actor actor) => pc.Pawn!.BeginTouch(actor);

    /// <summary>Opens the menu on <paramref name="entry"/> and releases Use on it (rules 10 to 17).</summary>
    public static EMenuRelease UseEntry(SPlayerController pc, SInteractObject entry)
    {
        pc.bUsing = 1;
        pc.bCanCheckUsingButton = true;
        if (!InteractionMenuRules.DoInteraction(pc, canInteract: true))
        {
            throw new InvalidOperationException("the Use menu did not open");
        }

        SInteractionManager manager = pc.IManager!;
        int slot = manager.IndexOf(entry);
        if (slot < 0)
        {
            throw new InvalidOperationException($"{entry} is not in the menu");
        }

        manager.SelectedInteractions = slot;
        pc.bUsing = 0;
        return InteractionMenuRules.MenuRelease(pc);
    }
}
