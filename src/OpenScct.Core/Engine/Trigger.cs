using OpenScct.Core.Uc;
using OpenScct.Provenance;
using EngineTrigger = OpenScct.Core.Engine.Trigger;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.Trigger: an invisible cylinder that fires its Event when something relevant enters (and untriggers when
/// it leaves), written from spec/devices/triggers.md. SteeE uses it for one-shot ambient sounds.
/// </summary>
public partial class Trigger
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates,
        NormalTrigger.Instance, OtherTriggerToggles.Instance, OtherTriggerTurnsOn.Instance, OtherTriggerTurnsOff.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Pawn-only touches for the proximity types that only pawns can satisfy.</summary>
    [Uc("Engine.Trigger.PreBeginPlay", Sha1 = "3ffe60fdf71a549a601c93093aecca63ed15cd97")]
    [PortOf("uc:Engine.Trigger:PreBeginPlay", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected internal override void PreBeginPlay()
    {
        base.PreBeginPlay();
        if (TriggerType is ETriggerType.TT_PlayerProximity or ETriggerType.TT_PawnProximity or ETriggerType.TT_HumanPlayerProximity ||
            (TriggerType == ETriggerType.TT_ClassProximity && ClassIsChildOf(FindClass(ClassProximityType), PawnClass)))
        {
            OnlyAffectPawns(true);
        }
    }

    [Uc("Engine.Trigger.PostBeginPlay", Sha1 = "9530e80dddcb9f4190063da9c8fb580a290ebc64")]
    [PortOf("uc:Engine.Trigger:PostBeginPlay", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected internal override void PostBeginPlay()
    {
        if (!bInitiallyActive)
        {
            FindTriggerActor();
        }

        if (TriggerType == ETriggerType.TT_Shoot)
        {
            bHidden = false;
            bProjTarget = true;
            SetDrawType(EDrawType.DT_None);
        }

        bSavedInitialActive = bInitiallyActive;
        bSavedInitialCollision = bCollideActors;
        base.PostBeginPlay();
    }

    /// <summary>Restores the initial active flag and collision.</summary>
    [Uc("Engine.Trigger.Reset", Sha1 = "0426a0653f001fd37a150e27e22b72e21fe6a33c")]
    [PortOf("uc:Engine.Trigger:Reset", Spec = "spec/devices/triggers.md#enginetrigger")]
    public override void Reset()
    {
        base.Reset();
        bInitiallyActive = bSavedInitialActive;
        SetCollision(bSavedInitialCollision, bBlockActors, bBlockPlayers);
    }

    /// <summary>Bot helper: the first two actors in the level whose Event is this trigger's Tag.</summary>
    [Uc("Engine.Trigger.FindTriggerActor", Sha1 = "5f88b385ea5c6ad2b83b822f0648d70cff8a11df")]
    [PortOf("uc:Engine.Trigger:FindTriggerActor", Spec = "spec/devices/triggers.md#enginetrigger")]
    public void FindTriggerActor()
    {
        TriggerActor = null;
        TriggerActor2 = null;
        foreach (Actor a in XLevel.AllActors<Actor>())
        {
            if (a.Event != Tag)
            {
                continue;
            }

            if (TriggerActor is null)
            {
                TriggerActor = a;
                continue;
            }

            TriggerActor2 = a;
            return;
        }
    }

    /// <summary>
    /// Bot path helper. Players are never affected; it answers None for an inactive trigger without trigger actors,
    /// and itself otherwise (the shoot-special and nearest-trigger-actor choices only matter to bots).
    /// </summary>
    [Uc("Engine.Trigger.SpecialHandling", Sha1 = "5b71785076945653ea7042301ec2961e5b7edf07")]
    [PortOf("uc:Engine.Trigger:SpecialHandling", Spec = "spec/devices/triggers.md#enginetrigger")]
    [Provenance(ProvenanceKind.Estimated, "Bot-only helper, simplified: versus has no bots")]
    public Actor? SpecialHandling(Pawn? Other)
    {
        if (!bInitiallyActive)
        {
            return TriggerActor is null ? null : this;
        }

        if (Other is not null && IsRelevant(Other))
        {
            eventTouch(Other);
        }

        return this;
    }

    /// <summary>Touches everything already inside (used when the trigger becomes active).</summary>
    [Uc("Engine.Trigger.CheckTouchList", Sha1 = "cf6696548fb44b7f6cf57658c03bebdcaf1e0640")]
    [PortOf("uc:Engine.Trigger:CheckTouchList", Spec = "spec/devices/triggers.md#enginetrigger")]
    public void CheckTouchList()
    {
        Actor[] touching = Touching.ToArray();
        foreach (Actor a in touching)
        {
            eventTouch(a);
        }
    }

    /// <summary>Whether <paramref name="Other"/> sets the trigger off, by TriggerType; false while inactive.</summary>
    [Uc("Engine.Trigger.IsRelevant", Sha1 = "8325d8a743f0765458c8117d0263ca224a154faf")]
    [PortOf("uc:Engine.Trigger:IsRelevant", Spec = "spec/devices/triggers.md#enginetrigger")]
    public bool IsRelevant(Actor? Other)
    {
        if (!bInitiallyActive || Other is null)
        {
            return false;
        }

        switch (TriggerType)
        {
            case ETriggerType.TT_HumanPlayerProximity:
                return Other is Pawn human && Mover.IsHumanControlled(human);
            case ETriggerType.TT_PlayerProximity:
                // Pawn.WasPlayerPawn is false in this build, so only current player pawns count.
                return Other is Pawn player && Mover.IsPlayerPawn(player);
            case ETriggerType.TT_PawnProximity:
                // Pawn.CanTrigger is true in this build.
                return Other is Pawn;
            case ETriggerType.TT_ClassProximity:
                return ClassIsChildOf(Other.GetUcClass(), ClassProximityType);
            case ETriggerType.TT_AnyProximity:
                return true;
            case ETriggerType.TT_Shoot:
                return Other is Projectile projectile && projectile.Damage >= DamageThreshold;
            default:
                return false;
        }
    }

    /// <summary>
    /// A relevant toucher fires the Event (after the retrigger delay), with its instigator; then collision off for
    /// once-only triggers, or the repeat timer.
    /// </summary>
    [Uc("Engine.Trigger.Touch", Sha1 = "7b71b55b9b5fb4da332e497e1958f22165e0b123")]
    [PortOf("uc:Engine.Trigger:Touch", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected internal override void Touch(Actor Other)
    {
        if (!IsRelevant(Other))
        {
            return;
        }

        if (ReTriggerDelay > 0f)
        {
            if (Level.TimeSeconds - TriggerTime < ReTriggerDelay)
            {
                return;
            }

            TriggerTime = Level.TimeSeconds;
        }

        TriggerEvent(Event, this, Other.Instigator);

        // A Message goes to the toucher's instigator as a client message (HUD); no SteeE trigger sets one.
        if (bTriggerOnceOnly)
        {
            SetCollision(false);
        }
        else if (RepeatTriggerTime > 0f)
        {
            SetTimer(RepeatTriggerTime, false);
        }
    }

    /// <summary>Re-touches every relevant actor still inside, and keeps the timer running while there is one.</summary>
    [Uc("Engine.Trigger.Timer", Sha1 = "ac4dd8fc6f54bed3e60bb7a89e8639ad11eda649")]
    [PortOf("uc:Engine.Trigger:Timer", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected internal override void Timer()
    {
        bool keepTiming = false;
        Actor[] touching = Touching.ToArray();
        foreach (Actor a in touching)
        {
            if (IsRelevant(a))
            {
                keepTiming = true;
                eventTouch(a);
            }
        }

        if (keepTiming)
        {
            SetTimer(RepeatTriggerTime, false);
        }
    }

    /// <summary>A shoot trigger hit hard enough by an instigated attack fires its Event.</summary>
    [Uc("Engine.Trigger.TakeDamage", Sha1 = "85a279b9a9fc57851d04d4d708eb1930fcd65a1e")]
    [PortOf("uc:Engine.Trigger:TakeDamage", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected internal override void TakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (!bInitiallyActive || TriggerType != ETriggerType.TT_Shoot || Damage < DamageThreshold || instigatedBy is null)
        {
            return;
        }

        if (ReTriggerDelay > 0f)
        {
            if (Level.TimeSeconds - TriggerTime < ReTriggerDelay)
            {
                return;
            }

            TriggerTime = Level.TimeSeconds;
        }

        TriggerEvent(Event, this, instigatedBy);
        if (bTriggerOnceOnly)
        {
            SetCollision(false);
        }
    }

    /// <summary>A relevant actor leaving untriggers the Event.</summary>
    [Uc("Engine.Trigger.UnTouch", Sha1 = "91609e3b7c685f8073f299bd50f0739cef0408ce")]
    [PortOf("uc:Engine.Trigger:UnTouch", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected internal override void UnTouch(Actor Other)
    {
        if (IsRelevant(Other))
        {
            UntriggerEvent(Event, this, Other.Instigator);
        }
    }

    private static readonly ClassRef PawnClass = new("Engine.Pawn");

    // ---- states -------------------------------------------------------------------------------------------

    /// <summary>The default state: nothing extra.</summary>
    [Uc("Engine.Trigger.NormalTrigger")]
    public class NormalTrigger : UcActorState
    {
        public static readonly NormalTrigger Instance = new(UcMeta.NormalTrigger, null);

        protected NormalTrigger(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }

    /// <summary>Another trigger switches this one on and off.</summary>
    [Uc("Engine.Trigger.OtherTriggerToggles")]
    public class OtherTriggerToggles : UcActorState
    {
        public static readonly OtherTriggerToggles Instance = new(UcMeta.OtherTriggerToggles, null);

        protected OtherTriggerToggles(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Trigger.OtherTriggerToggles.Trigger", Sha1 = "2653aa2b5da07a1fa9e49acee5d593cff830bc88")]
        [PortOf("uc:Engine.Trigger:OtherTriggerToggles.Trigger", Spec = "spec/devices/triggers.md#enginetrigger")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var trigger = (EngineTrigger)self;
            trigger.bInitiallyActive = !trigger.bInitiallyActive;
            if (trigger.bInitiallyActive)
            {
                trigger.CheckTouchList();
            }
        }
    }

    /// <summary>Another trigger switches this one on.</summary>
    [Uc("Engine.Trigger.OtherTriggerTurnsOn")]
    public class OtherTriggerTurnsOn : UcActorState
    {
        public static readonly OtherTriggerTurnsOn Instance = new(UcMeta.OtherTriggerTurnsOn, null);

        protected OtherTriggerTurnsOn(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Trigger.OtherTriggerTurnsOn.Trigger", Sha1 = "28cd67c76e41196c740969223575c784f9d08442")]
        [PortOf("uc:Engine.Trigger:OtherTriggerTurnsOn.Trigger", Spec = "spec/devices/triggers.md#enginetrigger")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var trigger = (EngineTrigger)self;
            bool wasActive = trigger.bInitiallyActive;
            trigger.bInitiallyActive = true;
            if (!wasActive)
            {
                trigger.CheckTouchList();
            }
        }
    }

    /// <summary>Another trigger switches this one off.</summary>
    [Uc("Engine.Trigger.OtherTriggerTurnsOff")]
    public class OtherTriggerTurnsOff : UcActorState
    {
        public static readonly OtherTriggerTurnsOff Instance = new(UcMeta.OtherTriggerTurnsOff, null);

        protected OtherTriggerTurnsOff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Trigger.OtherTriggerTurnsOff.Trigger", Sha1 = "fb73606cf6702b2aa3bb7638d47916cbbb84550d")]
        [PortOf("uc:Engine.Trigger:OtherTriggerTurnsOff.Trigger", Spec = "spec/devices/triggers.md#enginetrigger")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator) => ((EngineTrigger)self).bInitiallyActive = false;
    }
}
