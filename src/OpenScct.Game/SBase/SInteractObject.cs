using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// The invisible use cylinder of every usable thing, written from spec/objectives/interaction-system.md.
public partial class SInteractObject
{
    /// <summary>State version of <see cref="InitInteract"/>.</summary>
    public interface IInitInteract
    {
        void InitInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction);
    }

    /// <summary>State version of <see cref="Interact"/>.</summary>
    public interface IInteract
    {
        void Interact(SInteractObject self, SPlayerController Instigator, int SubInteraction);
    }

    /// <summary>State version of <see cref="PostInteract"/>.</summary>
    public interface IPostInteract
    {
        void PostInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction);
    }

    /// <summary>Dispatcher of InitInteract (current state first). The server calls it from InteractionInit.</summary>
    public void eventInitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (StateNode is IInitInteract s)
        {
            s.InitInteract(this, Instigator, SubInteraction);
        }
        else
        {
            InitInteract(Instigator, SubInteraction);
        }
    }

    /// <summary>Dispatcher of Interact. The server calls it from Interaction.</summary>
    public void eventInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (StateNode is IInteract s)
        {
            s.Interact(this, Instigator, SubInteraction);
        }
        else
        {
            Interact(Instigator, SubInteraction);
        }
    }

    /// <summary>Dispatcher of PostInteract. The server calls it from InteractionPost.</summary>
    public void eventPostInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (StateNode is IPostInteract s)
        {
            s.PostInteract(this, Instigator, SubInteraction);
        }
        else
        {
            PostInteract(Instigator, SubInteraction);
        }
    }

    // ---- setup -----------------------------------------------------------------------------------------------

    [Uc("SBase.SInteractObject.PostBeginPlay", Sha1 = "5f812cbbc6a023e89f24c91ebcf31a20d94d0415")]
    [PortOf("uc:SBase.SInteractObject:PostBeginPlay", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void PostBeginPlay()
    {
        var gpo = Owner as SBasicGamePlayObject;
        float ownerRadius = Owner?.CollisionRadius ?? 0f;
        float ownerHeight = Owner?.CollisionHeight ?? 0f;
        ItRadius = gpo is not null && gpo.fInteractionRadius >= 0f ? gpo.fInteractionRadius : ownerRadius + K.InteractionPadding;
        ItHeight = gpo is not null && gpo.fInteractionHeight >= 0f ? gpo.fInteractionHeight : ownerHeight + K.InteractionPadding;
        ObjectiveNatives.SetCollisionSize(this, ItRadius, ItHeight);
        SetBase(Owner);
        if (gpo is not null && gpo.UsableBy != Usable.U_DEFAULT_USABILITY)
        {
            UsableBy = gpo.UsableBy;
        }
    }

    [Uc("SBase.SInteractObject.PostNetBeginPlay", Sha1 = "4dad55a51f80de27624199ce7cf90226da364ee8")]
    [PortOf("uc:SBase.SInteractObject:PostNetBeginPlay", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void PostNetBeginPlay() => ObjectiveNatives.SetCollisionSize(this, ItRadius, ItHeight);

    [Uc("SBase.SInteractObject.Destroyed", Sha1 = "973329c94c7e8cd007e10303aa77309890daf11e")]
    [PortOf("uc:SBase.SInteractObject:Destroyed", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void Destroyed()
    {
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is SPlayerController { IManager: { } manager })
            {
                manager.RemoveInteractionObj(this);
            }
        }

        base.Destroyed();
    }

    // ---- entering and leaving the menu list ------------------------------------------------------------------

    [Uc("SBase.SInteractObject.Touch", Sha1 = "dea42799d852475e10ea7e192b6d9a6c28783886")]
    [PortOf("uc:SBase.SInteractObject:Touch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void Touch(Actor Other) => DoTouch(Other);

    [Uc("SBase.SInteractObject.UnTouch", Sha1 = "1eec1ce7dc10c57d62d2a9720a6f45f66c5e19e7")]
    [PortOf("uc:SBase.SInteractObject:UnTouch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void UnTouch(Actor Other)
    {
        if (Other is Pawn { Controller: SPlayerController { IManager: { } manager } })
        {
            manager.RemoveInteractionObj(this);
        }
    }

    /// <summary>
    /// Adds this entry to the Use menu of the player whose pawn touched it. Expected contract (rule 7): a pawn of an
    /// SPlayerController that has a manager and accepts the entry (CanAddInteract).
    /// </summary>
    [UcNative("ASInteractObject::execDoTouch", Exe = 0x10AF6BD0, Editor = 0x111BC0B0)]
    [PortOf("nat:SBase.SInteractObject.DoTouch", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected DoTouch contract; filters and the client copy are open questions")]
    public virtual void DoTouch(Actor Other)
    {
        if (Other is Pawn { bDeleteMe: false, Controller: SPlayerController { IManager: { } manager } pc } &&
            InteractionMenuRules.CanAddInteract(pc, this))
        {
            manager.AddInteractionObj(this);
        }
    }

    // ---- use ------------------------------------------------------------------------------------------------

    /// <summary>Server side of a use: the default sends a living instigator back to the state before the menu.</summary>
    [Uc("SBase.SInteractObject.InitInteract", Sha1 = "7767f93592550700e326c8ddea880302e379d096")]
    [PortOf("uc:SBase.SInteractObject:InitInteract", Spec = "spec/objectives/interaction-system.md#rules")]
    protected internal virtual void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (ObjectiveNatives.IsAlive(Instigator) && !Instigator.PreviousState.IsNone)
        {
            ObjectiveNatives.MirrorState(Instigator, Instigator.PreviousState);
        }
    }

    [Uc("SBase.SInteractObject.Interact", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SInteractObject:Interact", Spec = "spec/objectives/interaction-system.md#rules")]
    protected internal virtual void Interact(SPlayerController Instigator, int SubInteraction)
    {
    }

    /// <summary>Default end of a use (native). No effect known yet (spec open question 6).</summary>
    [UcNative("ASInteractObject::execPostInteract", Exe = 0x10AF6C90, Editor = 0x111BC1B0)]
    [PortOf("nat:SBase.SInteractObject.PostInteract", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Native PostInteract default assumed to do nothing")]
    protected internal virtual void PostInteract(SPlayerController Instigator, int SubInteraction)
    {
    }

    /// <summary>
    /// Whether the entry is shown and usable for <paramref name="TheController"/>. Expected and partly measured
    /// (rule 23): usable, a living pawn of a side UsableBy allows, the reuse delay passed, and facing the object when
    /// <see cref="bInteractInFront"/>. With <see cref="bInteractInFrontWithCollisions"/> (decoded) the pawn must also
    /// stand on the object's front side within the fan distance (<see cref="InCollisionFan"/>), except on the mine
    /// entries, whose own native CanUse is not decoded. Subclasses add their own conditions.
    /// </summary>
    [UcNative("ASInteractObject::execCanUse", Exe = 0x10AB96F0, Editor = 0x111BC330)]
    [PortOf("nat:SBase.SInteractObject.CanUse", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected CanUse conditions; reuse rule measured (RE+ lab-power), facing rule estimated")]
    [Provenance(ProvenanceKind.Measured, "Reuse delay: refused 19.4 s and accepted 20.9 s after a use with iReusableTime 20")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AF6EF0 (vtable slot 0x138 of the thunk for SInteractObject, SBombTargetInteraction and the script-only SBombInteraction): the bInteractInFrontWithCollisions fan rule")]
    public virtual bool CanUse(SPlayerController? TheController)
    {
        if (!bUsable || TheController is null || !ObjectiveNatives.IsAlive(TheController))
        {
            return false;
        }

        if (!ObjectiveNatives.UsableByAllows(UsableBy, ObjectiveNatives.PawnTypeOf(TheController)))
        {
            return false;
        }

        // Last use replicates in the server's clock, which can be ahead of a joining client's.
        // Entries without a cooldown (including laptops) must not wait for that clock to catch up.
        if (iReusableTime > 0 && Level.TimeSeconds - iLastUsedTime < iReusableTime)
        {
            return false;
        }

        if (!InCollisionFan(TheController.Pawn!.Location))
        {
            return false;
        }

        return !bInteractInFront || IsInFrontOf(TheController.Pawn!);
    }

    /// <summary>
    /// Whether <see cref="CanUse"/> applies the fan (<see cref="InCollisionFan"/>): entries with
    /// <see cref="bInteractInFrontWithCollisions"/> that use the base native CanUse (bomb spots).
    /// </summary>
    public virtual bool UsesCollisionFan => bInteractInFrontWithCollisions;

    /// <summary>
    /// The <see cref="bInteractInFrontWithCollisions"/> part of the base native CanUse (bomb spots) for a pawn centred
    /// at <paramref name="pawnLocation"/>; true when <see cref="UsesCollisionFan"/> is false. The horizontal direction
    /// from the pawn to the object must lie against the object's X axis (dot below <see cref="K.FanFrontMaxDot"/>, so
    /// the pawn stands within about 53 degrees of the front), and the pawn's centre must be closer than the fan line on
    /// its side (<see cref="MaxDistToInteract"/>, built on first use). There is no line from the pawn.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10AF6EF0: d = normalized horizontal (Location - pawn), t = d dot X axis of (Pitch, Yaw - 0x3FFF, Roll), refused unless |Location - pawn| < MaxDistToInteract[trunc(5 (t + 1))] and d dot Rotation.Vector() < -0.6")]
    public bool InCollisionFan(FVector pawnLocation)
    {
        if (!UsesCollisionFan)
        {
            return true;
        }

        if (MaxDistToInteract[0] == -1f)
        {
            BuildMaxDistToInteract();
        }

        FVector toObject = Location - pawnLocation;
        toObject.Z = 0f;
        toObject = toObject.SafeNormal();
        float t = toObject | new FRotator(Rotation.Pitch, Rotation.Yaw - K.FanIndexYaw, Rotation.Roll).Vector();
        // The original reads MaxDistToInteract[10], past the array, when t is 1. That pawn stands square to the side,
        // which the front test below refuses anyway, so the index stops at the last line.
        int line = Math.Clamp((int)(K.FanIndexScale * (t + 1f)), 0, MaxDistToInteract.Length - 1);
        return (Location - pawnLocation).Size() < MaxDistToInteract[line] &&
            (toObject | Rotation.Vector()) < K.FanFrontMaxDot;
    }

    /// <summary>
    /// Fills <see cref="MaxDistToInteract"/> once, on the machine that first asks <see cref="CanUse"/> (it is not
    /// replicated): ten lines fanned around the object's front, from Yaw - 0x4000 in steps of 0xCCC, each traced from
    /// <see cref="K.FanUpLength"/> out (when the X axis points up) or <see cref="ItRadius"/> out, in to
    /// <see cref="K.FanInnerOffset"/> from the object, against movers and the world. Each slot holds the distance from
    /// the object to the hit, or the full length when the line is clear, so a wall beside the object shortens the reach
    /// on its side.
    /// </summary>
    [UcNative("ASInteractObject::BuildMaxDistToInteract", Exe = 0x10AF7120)]
    [PortOf("eng:ASInteractObject::BuildMaxDistToInteract", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AF7120 (vtable slot 0x13C, called from CanUse while MaxDistToInteract[0] is -1): SingleLineCheck flags 0x86, zero extent, self ignored, from Location + dir L to Location + dir 5")]
    public void BuildMaxDistToInteract()
    {
        float length = Rotation.Vector().Z > K.FanUpMinZ ? K.FanUpLength : ItRadius;
        const ETraceFlags flags = ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
            ETraceFlags.TRACE_SingleResult;
        for (int i = 0; i < MaxDistToInteract.Length; i++)
        {
            FVector dir = new FRotator(Rotation.Pitch, Rotation.Yaw - K.FanStartYaw + i * K.FanStepYaw, Rotation.Roll).Vector();
            MaxDistToInteract[i] = GadgetWorld.Line(this, Location + dir * K.FanInnerOffset, Location + dir * length, flags,
                out FCheckResult hit)
                ? (hit.Location - Location).Size()
                : length;
        }
    }

    /// <summary>
    /// The in-front rule: the pawn stands on the object's front side (its X axis) and faces the object, both in the
    /// horizontal plane with a dot product above <see cref="K.InFrontMinDot"/>.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Facing rule of native Calc/CanUse; threshold and eye or pawn direction are open questions")]
    public bool IsInFrontOf(Pawn pawn)
    {
        FVector toPawn = pawn.Location - Location;
        toPawn.Z = 0f;
        FVector front = Rotation.Vector();
        front.Z = 0f;
        FVector facing = pawn.Rotation.Vector();
        facing.Z = 0f;
        return (front.SafeNormal() | toPawn.SafeNormal()) > K.InFrontMinDot &&
            (facing.SafeNormal() | (-toPawn).SafeNormal()) > K.InFrontMinDot;
    }

    // ---- small natives and events ----------------------------------------------------------------------------

    [UcNative("ASInteractObject::execTriggerEventServer", Exe = 0x10AF6CF0)]
    [PortOf("nat:SBase.SInteractObject.TriggerEventServer", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected to fire TriggerEvent on the server")]
    public void TriggerEventServer(Name EventName, Actor? Other, Pawn? EventInstigator) =>
        TriggerEvent(EventName, Other, EventInstigator);

    [UcNative("ASInteractObject::execUnTriggerEventServer", Exe = 0x10AF6DB0)]
    [PortOf("nat:SBase.SInteractObject.UnTriggerEventServer", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected to fire UntriggerEvent on the server")]
    public void UnTriggerEventServer(Name EventName, Actor? Other, Pawn? EventInstigator) =>
        UntriggerEvent(EventName, Other, EventInstigator);

    /// <summary>Sets the menu priority; menus read it when they insert the object (already listed menus keep their order).</summary>
    [UcNative("ASInteractObject::execSetPriority", Exe = 0x10AF6E70, Editor = 0x111BC260)]
    [PortOf("nat:SBase.SInteractObject.SetPriority", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected iPriority = n plus a re-sort")]
    public void SetPriority(int iNewPriority) => iPriority = iNewPriority;

    /// <summary>Reliable client-to-server call from gadget code.</summary>
    [Uc("SBase.SInteractObject.ChangePresenceDetected", Sha1 = "25f96a3be820b6db30d40bfa5edd56efe28a4a3a")]
    [PortOf("uc:SBase.SInteractObject:ChangePresenceDetected", Spec = "spec/objectives/interaction-system.md#rules")]
    public void ChangePresenceDetected(bool _b) => bPresenceDetected = _b;

    /// <summary>Focus highlight event raised by native code (empty in script).</summary>
    [Uc("SBase.SInteractObject.SetFocus", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SInteractObject:SetFocus", Spec = "spec/objectives/interaction-system.md#rules")]
    public virtual void SetFocus(bool Activate)
    {
    }
}
