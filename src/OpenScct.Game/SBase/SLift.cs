using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SLift: an elevator cabin, written from spec/devices/lift.md. Each key is a floor; per floor the map lists
/// the floor's landing doors (plain Movers, key 1 = shut) and the call switches (SInterrupteur) that send the cabin
/// there. The cabin's own doors (SLiftDoor, key 1 = shut) ride on it. A switch use triggers the lift, which shuts
/// every door (PrepareGo), travels straight to the floor (Go), then opens the doors of that floor and its own
/// (WaitDoors), and re-arms every switch except the ones of the floor it stands at.
/// </summary>
public partial class SLift
{
    public static new readonly StateTable ClassStates = new(Mover.ClassStates,
        Go.Instance, PrepareGo.Instance, WaitDoors.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name NAME_Gib = new("Gib");
    private const int Go_Begin = 1;
    private const int Go_Arrive = 2;

    /// <summary>
    /// Every switch sends its event to the lift; the switches of the floor the lift stands at are unusable, the
    /// others usable. The cabin doors are based on the lift. The lift waits in no state.
    /// </summary>
    [Uc("SBase.SLift.PostNetBeginPlay", Sha1 = "675f95546799b06fb765583e84bb49e6e034e869")]
    [PortOf("uc:SBase.SLift:PostNetBeginPlay", Spec = "spec/devices/lift.md#rules")]
    protected override void PostNetBeginPlay()
    {
        // Not simulated: a client copy does nothing here.
        if (Role < ENetRole.ROLE_Authority)
        {
            return;
        }

        for (i = 0; i < DoorsAndSwitches.Count; i++)
        {
            var switches = DoorsAndSwitches[i].Switches;
            for (j = 0; j < switches.Count; j++)
            {
                SetSwitch(switches[j], i != KeyNum);
            }
        }

        for (i = 0; i < LiftDoors.Count; i++)
        {
            LiftDoors[i]?.SetBase(this);
        }

        GotoState(Name.None);
    }

    /// <summary>
    /// A call switch was used: every switch is made unusable, the cabin doors are based on the lift again, the
    /// destination is the floor whose switch list holds the sender (unchanged when none does), then PrepareGo.
    /// </summary>
    [Uc("SBase.SLift.Trigger", Sha1 = "435ccd54d4ad8b11caa575bb364d36181096ebd0")]
    [PortOf("uc:SBase.SLift:Trigger", Spec = "spec/devices/lift.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        for (i = 0; i < DoorsAndSwitches.Count; i++)
        {
            var switches = DoorsAndSwitches[i].Switches;
            for (j = 0; j < switches.Count; j++)
            {
                SetSwitch(switches[j], false);
            }
        }

        for (i = 0; i < LiftDoors.Count; i++)
        {
            LiftDoors[i]?.SetBase(this);
        }

        for (i = 0; i < DoorsAndSwitches.Count; i++)
        {
            var switches = DoorsAndSwitches[i].Switches;
            for (j = 0; j < switches.Count; j++)
            {
                if (switches[j] is not null && ReferenceEquals(switches[j], Other))
                {
                    DestinationKey = i;
                    break;
                }
            }
        }

        GotoState(nameof(PrepareGo));
    }

    /// <summary>
    /// The cabin runs into something: nothing within 2 s of the last time; else crush debris and controllerless
    /// pawns, deal EncroachDamage, stop the leg and head for the next floor back the way it came (Go from Begin).
    /// Never refuses the step, and MoverEncroachType is not read.
    /// </summary>
    [Uc("SBase.SLift.EncroachingOn", Sha1 = "e0fa2e15db70a8b03938fa25ab7e8eb2d3b477fd")]
    [PortOf("uc:SBase.SLift:EncroachingOn", Spec = "spec/devices/lift.md#rules")]
    protected override bool EncroachingOn(Actor? Other)
    {
        // Not simulated: on a client the engine's call returns false (mover.md, Network).
        if (Role < ENetRole.ROLE_Authority)
        {
            return false;
        }

        if (Level.TimeSeconds - LastEncroachTime < SLiftK.EncroachCooldown)
        {
            return false;
        }

        if (Other is null)
        {
            return false;
        }

        if (Other is Pawn { Controller: null } || Other is Decoration)
        {
            Other.eventTakeDamage(MoverK.CrushDamage, null, Other.Location, FVector.Zero, SDamageType.Crushed);
            return false;
        }

        if (Other is Fragment or Projectile || Other.ClassName == NAME_Gib)
        {
            Other.Destroy();
            return false;
        }

        if (EncroachDamage != 0)
        {
            if (Other is Pawn pawn)
            {
                if (Role == ENetRole.ROLE_Authority)
                {
                    pawn.Controller?.PawnTakeDamage(MoverK.EncroachDamageScale * EncroachDamage, Instigator, Other.Location,
                        FVector.Zero, SDamageType.Crushed);
                }
            }
            else
            {
                Other.eventTakeDamage(EncroachDamage, Instigator, Other.Location, FVector.Zero, SDamageType.Crushed, 1);
            }
        }

        if (Other is Pawn { Controller.bIsPlayer: true } && !PlayerBumpEvent.IsNone)
        {
            eventBump(Other);

            // UnderLift(self) for a controller waiting on this lift is an empty bot navigation hook in this build.
        }

        bInterpolating = false;
        AmbientSound = AssetRef.None;
        LastEncroachTime = (int)Level.TimeSeconds;
        DestinationKey = PrevKeyNum < KeyNum ? KeyNum - 1 : KeyNum + 1;
        GotoState(nameof(Go), NAME_Begin);
        return false;
    }

    /// <summary>A leg ended: no chaining through the keys (a lift goes straight to its floor), the loop sound stops.</summary>
    [Uc("SBase.SLift.KeyFrameReached", Sha1 = "030e1601ad70887f55a5daba1a9e18a86b20312a")]
    [PortOf("uc:SBase.SLift:KeyFrameReached", Spec = "spec/devices/lift.md#rules")]
    public override void KeyFrameReached()
    {
        PrevKeyNum = KeyNum;
        PhysAlpha = 0f;
        ClientUpdate--;
        AmbientSound = AssetRef.None;
        if (ClientUpdate == 0 && Level.NetMode != ENetMode.NM_Client)
        {
            RealPosition = Location;
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    /// <summary>
    /// Points a call switch at the lift and sets its interaction usable or not. As in the script, a missing
    /// switch or interaction is skipped (Access None).
    /// </summary>
    private void SetSwitch(SInterrupteur? s, bool usable)
    {
        if (s is null)
        {
            return;
        }

        s.Event = Tag;
        if (s.InteractionObj is { } io)
        {
            io.bUsable = usable;
        }
    }

    /// <summary>The landing doors of floor <paramref name="key"/> (empty past the end, as an out-of-range read).</summary>
    private System.Collections.Generic.List<Mover?> DoorsOf(int key) =>
        key >= 0 && key < DoorsAndSwitches.Count ? DoorsAndSwitches[key].Doors : NoDoors;

    private static readonly System.Collections.Generic.List<Mover?> NoDoors = new();

    // A None door reads as key 0 at rest and ignores triggers (Access None), as in the script.
    private static int KeyOf(Mover? m) => m?.KeyNum ?? 0;

    private static bool Moving(Mover? m) => m is not null && m.KeyNum != m.PrevKeyNum;

    private void TriggerDoor(Mover? m) => m?.eventTrigger(this, null);

    /// <summary>
    /// The half-second door check shared by PrepareGo and WaitDoors: true while any door is moving or was just
    /// told to move. <paramref name="arrived"/> false wants every door shut (key 1); true wants the doors of the
    /// lift's floor and the cabin doors open (key 0) and the other floors shut.
    /// </summary>
    private bool DoorsBadPosition(bool arrived)
    {
        bool bBadPosition = false;
        for (i = 0; i < DoorsAndSwitches.Count; i++)
        {
            var doors = DoorsAndSwitches[i].Doors;
            for (j = 0; j < doors.Count; j++)
            {
                Mover? d = doors[j];
                if (Moving(d))
                {
                    bBadPosition = true;
                }
                else if (i == KeyNum && KeyOf(d) == (arrived ? 1 : 0))
                {
                    TriggerDoor(d);
                    bBadPosition = true;
                }
                else if (i != KeyNum && KeyOf(d) == 0)
                {
                    TriggerDoor(d);
                    bBadPosition = true;
                }
            }
        }

        for (i = 0; i < LiftDoors.Count; i++)
        {
            SLiftDoor? d = LiftDoors[i];
            if (Moving(d))
            {
                bBadPosition = true;
            }
            else if (KeyOf(d) == (arrived ? 1 : 0))
            {
                bBadPosition = true;
                TriggerDoor(d);
            }
        }

        return bBadPosition;
    }

    // ---- states -------------------------------------------------------------------------------------------

    /// <summary>Base of the lift states: all three ignore Trigger and UnTrigger.</summary>
    public abstract class LiftState : MoverState
    {
        protected LiftState(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
        }
    }

    /// <summary>Shutting the doors: the floor's doors not shut and the cabin doors are triggered, then a 0.5 s check.</summary>
    [Uc("SBase.SLift.PrepareGo")]
    public class PrepareGo : LiftState
    {
        public static readonly PrepareGo Instance = new(UcMeta.PrepareGo, null);

        protected PrepareGo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SLift.PrepareGo.BeginState", Sha1 = "a1863cb76e3f0e4e28a71f72289fdd23f366cb06")]
        [PortOf("uc:SBase.SLift:PrepareGo.BeginState", Spec = "spec/devices/lift.md#rules")]
        public override void BeginState(Actor self)
        {
            var lift = (SLift)self;
            var doors = lift.DoorsOf(lift.KeyNum);
            for (lift.j = 0; lift.j < doors.Count; lift.j++)
            {
                if (KeyOf(doors[lift.j]) != 1)
                {
                    lift.TriggerDoor(doors[lift.j]);
                }
            }

            // Every cabin door is toggled, whatever its key (the check below puts any wrong one right).
            for (lift.i = 0; lift.i < lift.LiftDoors.Count; lift.i++)
            {
                lift.TriggerDoor(lift.LiftDoors[lift.i]);
            }

            lift.SetTimer(SLiftK.DoorCheckInterval, true);
        }

        /// <summary>Every door shut and at rest: Go. Otherwise trigger the open ones and check again.</summary>
        [Uc("SBase.SLift.PrepareGo.Timer", Sha1 = "dc3a9104c74c5ecd6d2db5a2602faa54ddbb8f10")]
        [PortOf("uc:SBase.SLift:PrepareGo.Timer", Spec = "spec/devices/lift.md#rules")]
        public override void Timer(Actor self)
        {
            var lift = (SLift)self;
            if (!lift.DoorsBadPosition(arrived: false))
            {
                lift.GotoState(nameof(Go));
            }
        }
    }

    /// <summary>Travelling: one leg straight to the destination floor, MoveTime per floor, then Arrived.</summary>
    [Uc("SBase.SLift.Go")]
    public class Go : LiftState
    {
        public static readonly Go Instance = new(UcMeta.Go, null);

        protected Go(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SLift.Go@code", Sha1 = "6842bab81cfaf72a33c2aa16e03de93f82e5918f")]
        [PortOf("uc:SBase.SLift:Go@code", Spec = "spec/devices/lift.md#rules")]
        public override void Code(Actor self, ref LatentFrame frame) => ((SLift)self).Go_Code(ref frame);
    }

    private void Go_Code(ref LatentFrame f)
    {
        switch (f.Pc)
        {
            case Go_Begin:
                InterpolateTo((byte)DestinationKey, MoveTime * Math.Abs(KeyNum - DestinationKey));
                MakeNoise(1f);
                AmbientSound = MoveAmbientSound;
                f.Pc = Go_Arrive;
                FinishInterpolation();
                return;
            case Go_Arrive:
                f.Pc = LatentFrame.NoCode;
                Arrived();
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }

    /// <summary>
    /// At the floor: its landing doors not at key 0 are triggered, each cabin door's BasePos is moved to where the
    /// cabin carried it and the door is triggered, then WaitDoors.
    /// </summary>
    [Uc("SBase.SLift.Go.Arrived", Sha1 = "1003a7e5ecb822178810bcbb51b9f1d8c00f4325")]
    [PortOf("uc:SBase.SLift:Go.Arrived", Spec = "spec/devices/lift.md#rules")]
    private void Arrived()
    {
        var doors = DoorsOf(KeyNum);
        for (j = 0; j < doors.Count; j++)
        {
            if (KeyOf(doors[j]) != 0)
            {
                TriggerDoor(doors[j]);
            }
        }

        for (i = 0; i < LiftDoors.Count; i++)
        {
            if (LiftDoors[i] is { } d)
            {
                d.BasePos = d.Location - d.KeyPos[d.KeyNum];
                TriggerDoor(d);
            }
        }

        GotoState(nameof(WaitDoors));
    }

    /// <summary>Opening at the floor: a 0.5 s check until the right doors are open and the rest shut, then re-arm.</summary>
    [Uc("SBase.SLift.WaitDoors")]
    public class WaitDoors : LiftState
    {
        public static readonly WaitDoors Instance = new(UcMeta.WaitDoors, null);

        protected WaitDoors(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SLift.WaitDoors.BeginState", Sha1 = "d9d3d60724ab95ee21add7e238051f24ad91c345")]
        [PortOf("uc:SBase.SLift:WaitDoors.BeginState", Spec = "spec/devices/lift.md#rules")]
        public override void BeginState(Actor self) => self.SetTimer(SLiftK.DoorCheckInterval, true);

        /// <summary>
        /// Doors right and at rest: every switch points at the lift, the ones of this floor unusable and the rest
        /// usable, and the lift leaves its state (the 0.5 s timer keeps running Mover.Timer).
        /// </summary>
        [Uc("SBase.SLift.WaitDoors.Timer", Sha1 = "7a89901d056190c575a657cc594a8d79d6ea0d0b")]
        [PortOf("uc:SBase.SLift:WaitDoors.Timer", Spec = "spec/devices/lift.md#rules")]
        public override void Timer(Actor self)
        {
            var lift = (SLift)self;
            if (lift.DoorsBadPosition(arrived: true))
            {
                return;
            }

            for (lift.i = 0; lift.i < lift.DoorsAndSwitches.Count; lift.i++)
            {
                var switches = lift.DoorsAndSwitches[lift.i].Switches;
                for (lift.j = 0; lift.j < switches.Count; lift.j++)
                {
                    lift.SetSwitch(switches[lift.j], lift.i != lift.KeyNum);
                }
            }

            lift.GotoState(Name.None);
        }
    }
}

/// <summary>SLift constants taken from the original (spec/devices/lift.md).</summary>
public static class SLiftK
{
    [Provenance(ProvenanceKind.Decoded, "SBase.SLift PrepareGo.BeginState and WaitDoors.BeginState SetTimer(0.5, true)")]
    public const float DoorCheckInterval = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SLift.EncroachingOn: nothing within 2 s of LastEncroachTime")]
    public const float EncroachCooldown = 2f;
}
