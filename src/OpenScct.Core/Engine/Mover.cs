using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.Mover: a static-mesh actor that travels between up to 8 keyframes under PHYS_MovingBrush, written from
/// spec/devices/mover.md. Its InitialState decides how it answers triggers, bumps and standing pawns, and
/// MoverEncroachType what it does when its motion runs into an actor. States are in the Mover.&lt;State&gt;.cs files.
/// <para>
/// The moving-brush step (<see cref="physMovingBrush"/>) runs at the end of the mover's own tick. Its moves go
/// through the level's <see cref="IActorNatives"/> service (encroachment against the collision world); without the
/// service the mover moves freely.
/// </para>
/// </summary>
public partial class Mover
{
    // ---- interpolation ------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a leg to <paramref name="NewKeyNum"/> lasting <paramref name="Seconds"/>; reverses the leg in progress
    /// when it targets the key the leg left.
    /// </summary>
    [Uc("Engine.Mover.InterpolateTo", Sha1 = "2a90b15a36b833d207cc5ee7e445f791ac6480d0")]
    [PortOf("uc:Engine.Mover:InterpolateTo", Spec = "spec/devices/mover.md#interpolation")]
    public virtual void InterpolateTo(byte NewKeyNum, float Seconds)
    {
        NewKeyNum = (byte)SvmMath.Clamp(NewKeyNum, 0, MoverK.MaxKeys - 1);
        if (NewKeyNum == PrevKeyNum && KeyNum != PrevKeyNum)
        {
            PhysAlpha = 1f - PhysAlpha;
            OldPos = BasePos + KeyPos[KeyNum];
            OldRot = BaseRot + KeyRot[KeyNum];
        }
        else
        {
            OldPos = Location;
            OldRot = Rotation;
            PhysAlpha = 0f;
        }

        SetPhysics(EPhysics.PHYS_MovingBrush);
        bInterpolating = true;
        PrevKeyNum = KeyNum;
        KeyNum = NewKeyNum;
        PhysRate = 1f / SvmMath.Max(Seconds, MoverK.MinLegTime);
        ClientUpdate++;
        SimOldPos = OldPos;
        SimInterpolate = new FVector(100f * PhysAlpha, 100f * SvmMath.Max(0.01f, PhysRate), 256f * PrevKeyNum + KeyNum);
    }

    /// <summary>The leg reached its key: continue through the keys in the same direction, or stop.</summary>
    [Uc("Engine.Mover.KeyFrameReached", Sha1 = "0bcd272adc3fb0c00dbcec79ca1bbf7d10aab8dc")]
    [PortOf("uc:Engine.Mover:KeyFrameReached", Spec = "spec/devices/mover.md#interpolation")]
    public virtual void KeyFrameReached()
    {
        byte OldKeyNum = PrevKeyNum;
        PrevKeyNum = KeyNum;
        PhysAlpha = 0f;
        ClientUpdate--;
        if (KeyNum > 0 && KeyNum < OldKeyNum)
        {
            InterpolateTo((byte)(KeyNum - 1), MoveTime);
        }
        else if (KeyNum < NumKeys - 1 && KeyNum > OldKeyNum)
        {
            InterpolateTo((byte)(KeyNum + 1), MoveTime);
        }
        else
        {
            StopSound(AmbientSound);
            AmbientSound = AssetRef.None;
            if (Level.NetMode != ENetMode.NM_Client && ClientUpdate == 0)
            {
                RealPosition = Location;
            }
        }
    }

    /// <summary>
    /// The moving-brush step (native): alpha advances by dt x PhysRate, the eased (MV_GlideByTime) or linear alpha
    /// places the mover between OldPos/OldRot and the target key, and the move is made with encroachment checks. A
    /// refused move leaves alpha where it was; reaching 1 ends the leg (KeyFrameReached), and the rest of the tick's
    /// time carries into the next leg.
    /// </summary>
    [UcNative("AActor::physMovingBrush")]
    [PortOf("nat:AActor::physMovingBrush", Spec = "spec/devices/mover.md#interpolation")]
    [Provenance(ProvenanceKind.Estimated, "Mover spec rule 2: glide curve 3a^2 - 2a^3 and leftover time carry, UE2 lineage")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a21970: after the legs, Velocity = (Location - start) / DeltaTime on every tick, and fLastTimeMoverMoves is stamped from the Z speed (mover.md rule 2a)")]
    public void physMovingBrush(float DeltaTime)
    {
        FVector start = Location;
        MovingBrushLegs(DeltaTime);
        if (DeltaTime > 0f && !bDeleteMe)
        {
            Velocity = (Location - start) * (1f / DeltaTime);
            if (SvmMath.Abs(Velocity.Z) > MoverK.VerticalMoveSpeed)
            {
                fLastTimeMoverMoves = Level.TimeSeconds;
            }
        }
    }

    private void MovingBrushLegs(float DeltaTime)
    {
        float dt = DeltaTime;
        for (int legs = 0; bInterpolating && dt > 0f && legs < MoverK.MaxKeys && !bDeleteMe; legs++)
        {
            float alpha = PhysAlpha;
            float newAlpha = alpha + dt * PhysRate;
            if (newAlpha > 1f)
            {
                dt *= (newAlpha - 1f) / (newAlpha - alpha);
                newAlpha = 1f;
            }
            else
            {
                dt = 0f;
            }

            float s = MoverGlideType == EMoverGlideType.MV_GlideByTime
                ? 3f * newAlpha * newAlpha - 2f * newAlpha * newAlpha * newAlpha
                : newAlpha;
            FVector target = OldPos + (BasePos + KeyPos[KeyNum] - OldPos) * s;
            FRotator rotation = OldRot + (BaseRot + KeyRot[KeyNum] - OldRot) * s;
            if (!MoveBrush(target - Location, rotation))
            {
                return;
            }

            PhysAlpha = newAlpha;
            if (newAlpha >= 1f)
            {
                bInterpolating = false;
                KeyFrameReached();
            }
        }
    }

    /// <summary>Runs the moving-brush step after the rest of the native tick (physics comes last, as in UE2).</summary>
    protected internal override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (!bDeleteMe && Physics == EPhysics.PHYS_MovingBrush && Role != ENetRole.ROLE_AutonomousProxy)
        {
            physMovingBrush(DeltaSeconds);
        }
    }

    private bool MoveBrush(FVector delta, FRotator rotation)
    {
        if (Natives is { } natives)
        {
            return natives.MoveEncroacher(this, delta, rotation);
        }

        // No collision world: nothing to encroach on. Riders move with the mover.
        Location += delta;
        Rotation = rotation;
        for (int i = 0; i < Attached.Count; i++)
        {
            Attached[i].Location += delta;
        }

        return true;
    }

    /// <summary>
    /// Client: the native net receive of a mover whose SimInterpolate changed (rep-map-actors.md rule 3, decoded
    /// editor 0x110b3020 and 0x110a70e0) starts the server's leg here: from SimOldPos and the current rotation, with
    /// the alpha and rate packed in SimInterpolate.X and .Y (x 0.01) and the keys in .Z (high byte the key left, low
    /// byte the target).
    /// </summary>
    [UcNative("AMover::PostNetReceive", Editor = 0x110b3020)]
    [PortOf("eng:AMover::PostNetReceive", Spec = "spec/net/rep-map-actors.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The client's map Rotation and its replicated BaseRot (sent as bytes, 0..65280) can name the same orientation whole turns apart (-32768 and 32768); the start is moved by whole turns onto the key left so the leg turns as the server's does")]
    public virtual void ClientStartInterpolation()
    {
        OldPos = SimOldPos;
        PhysAlpha = SimInterpolate.X * 0.01f;
        PhysRate = SimInterpolate.Y * 0.01f;
        int keys = (int)SimInterpolate.Z;
        KeyNum = (byte)SvmMath.Clamp(keys & 0xff, 0, MoverK.MaxKeys - 1);
        PrevKeyNum = (byte)SvmMath.Clamp((keys >> 8) & 0xff, 0, MoverK.MaxKeys - 1);
        OldRot = NearestTurn(Rotation, BaseRot + KeyRot[PrevKeyNum]);
        SetPhysics(EPhysics.PHYS_MovingBrush);
        bInterpolating = true;
    }

    /// <summary><paramref name="r"/> with each axis moved by whole turns to the nearest of <paramref name="near"/>'s (same orientation).</summary>
    private static FRotator NearestTurn(FRotator r, FRotator near) => new(
        near.Pitch + FRotator.NormalizeAxis(r.Pitch - near.Pitch),
        near.Yaw + FRotator.NormalizeAxis(r.Yaw - near.Yaw),
        near.Roll + FRotator.NormalizeAxis(r.Roll - near.Roll));

    /// <summary>Matinee hook (unused by the versus maps): leave the state and let interpolation drive the mover.</summary>
    [Uc("Engine.Mover.StartInterpolation", Sha1 = "0b0531f27602e722bac85380a4a07cf45e739666")]
    [PortOf("uc:Engine.Mover:StartInterpolation", Spec = "spec/devices/mover.md#client-side-bookkeeping")]
    public void StartInterpolation()
    {
        GotoState(Name.None);
        bInterpolating = true;
        SetPhysics(EPhysics.PHYS_None);
    }

    /// <summary>Stores key <paramref name="NewKeyNum"/> and makes it the current key.</summary>
    [Uc("Engine.Mover.SetKeyframe", Sha1 = "5d2cf7d26a6508a3209214e8e783bec05580662d")]
    [PortOf("uc:Engine.Mover:SetKeyframe", Spec = "spec/devices/mover.md#client-side-bookkeeping")]
    public void SetKeyframe(byte NewKeyNum, FVector NewLocation, FRotator NewRotation)
    {
        KeyNum = (byte)SvmMath.Clamp(NewKeyNum, 0, MoverK.MaxKeys - 1);
        KeyPos[KeyNum] = NewLocation;
        KeyRot[KeyNum] = NewRotation;
    }

    // ---- open and close -----------------------------------------------------------------------------------

    /// <summary>Plays one of the mover's sounds with its volume, radius and pitch.</summary>
    private void PlayMoverSound(AssetRef sound) =>
        PlaySound(sound, ESoundSlot.SLOT_None, SoundVolume / 255f, false, SoundRadius, SoundPitch / 64f);

    [Uc("Engine.Mover.DoOpen", Sha1 = "ba307e3071c00a3034c1ccfcadadd3cedc7692ee")]
    [PortOf("uc:Engine.Mover:DoOpen", Spec = "spec/devices/mover.md#interpolation")]
    public void DoOpen()
    {
        bOpening = true;
        bDelaying = false;
        InterpolateTo(1, MoveTime);
        MakeNoise(1f);
        PlayMoverSound(OpeningSound);
        AmbientSound = MoveAmbientSound;
        TriggerEvent(OpeningEvent, this, Instigator);
        Follower?.DoOpen();
    }

    [Uc("Engine.Mover.DoClose", Sha1 = "2d38198d1d1617fb0f7e345ac53bb232280c32be")]
    [PortOf("uc:Engine.Mover:DoClose", Spec = "spec/devices/mover.md#interpolation")]
    public void DoClose()
    {
        bOpening = false;
        bDelaying = false;
        InterpolateTo((byte)SvmMath.Max(0, KeyNum - 1), MoveTime);
        MakeNoise(1f);
        PlayMoverSound(ClosingSound);
        UntriggerEvent(Event, this, Instigator);
        AmbientSound = MoveAmbientSound;
        TriggerEvent(ClosingEvent, this, Instigator);
        Follower?.DoClose();
    }

    /// <summary>Fully open: the opened sound, Event triggered, the OpenedEvent (each group member's for a leader).</summary>
    [Uc("Engine.Mover.FinishedOpening", Sha1 = "6ffb4f5174ad8898aa65cc7e06df56108ab0e961")]
    [PortOf("uc:Engine.Mover:FinishedOpening", Spec = "spec/devices/mover.md#interpolation")]
    public void FinishedOpening()
    {
        PlayMoverSound(OpenedSound);
        TriggerEvent(Event, this, Instigator);
        if (bIsLeader)
        {
            for (Mover? m = Leader; m is not null; m = m.Follower)
            {
                m.TriggerEvent(m.OpenedEvent, m, Instigator);
            }
        }
        else
        {
            TriggerEvent(OpenedEvent, this, Instigator);
        }

        // myMarker.MoverOpened() is an empty bot navigation hook in this build.
        FinishNotify();
    }

    /// <summary>Fully closed: the closed sound, the ClosedEvent, EndEvent to the saved trigger, forget the instigator.</summary>
    [Uc("Engine.Mover.FinishedClosing", Sha1 = "b397b7d9e0ed541f4ab6512fa17b9703342256eb")]
    [PortOf("uc:Engine.Mover:FinishedClosing", Spec = "spec/devices/mover.md#interpolation")]
    public void FinishedClosing()
    {
        PlayMoverSound(ClosedSound);
        if (bIsLeader)
        {
            for (Mover? m = Leader; m is not null; m = m.Follower)
            {
                m.TriggerEvent(m.ClosedEvent, m, Instigator);
            }
        }
        else
        {
            TriggerEvent(ClosedEvent, this, Instigator);
        }

        SavedTrigger?.eventEndEvent();
        SavedTrigger = null;
        Instigator = null;

        // myMarker.MoverClosed() is an empty bot navigation hook in this build.
        bClosed = true;
        FinishNotify();
    }

    /// <summary>A leg sequence finished: locks are allowed again, and a pending lock is taken now.</summary>
    [Uc("Engine.Mover.FinishNotify", Sha1 = "95bbbd10f9609abf4dfe646c69f3f3e94f16b677")]
    [PortOf("uc:Engine.Mover:FinishNotify", Spec = "spec/devices/mover.md#interpolation")]
    public void FinishNotify()
    {
        // Controllers waiting on this mover get MoverFinished, an empty bot navigation hook in this build.
        bAllowLock = true;
        if (bShouldLock)
        {
            GotoState(Name.None);
            eventLock();
        }
    }

    // ---- initialisation -----------------------------------------------------------------------------------

    [Uc("Engine.Mover.BeginPlay", Sha1 = "62ea62d1844b8fc95a18342b9cce114717880525")]
    [PortOf("uc:Engine.Mover:BeginPlay", Spec = "spec/devices/mover.md#initialisation")]
    protected internal override void BeginPlay()
    {
        OriginalDelayTime = DelayTime;
        OriginalDelayTimeLastKey = DelayTimeLastKey;
        OriginalMoveTime = MoveTime;
        OriginalStayOpenTime = StayOpenTime;
        OriginalMoverEncroachType = MoverEncroachType;
        PrevKeyNum = KeyNum;
        if (!AntiPortalTag.IsNone)
        {
            foreach (AntiPortalActor antiPortal in XLevel.AllActors<AntiPortalActor>(AntiPortalTag))
            {
                AntiPortals.Add(antiPortal);
            }
        }

        if (Level.NetMode != ENetMode.NM_Standalone)
        {
            SetTimer(Level.NetMode == ENetMode.NM_Client ? MoverK.ClientTimer : MoverK.ServerTimer, true);
            if (Role < ENetRole.ROLE_Authority)
            {
                return;
            }
        }

        base.BeginPlay();
        KeyNum = (byte)SvmMath.Clamp(KeyNum, 0, MoverK.MaxKeys - 1);
        PhysAlpha = 0f;
        Location = BasePos + KeyPos[KeyNum];
        Rotation = BaseRot + KeyRot[KeyNum];
        if (Level.NetMode != ENetMode.NM_Client)
        {
            RealPosition = Location;
        }

        if (ReturnGroup.IsNone)
        {
            bIsLeader = false;
            Leader = this;
            Follower = null;
        }
        else
        {
            Leader = null;
            Follower = null;
        }
    }

    /// <summary>Adopts slave movers with the same Tag and links return groups behind their leader.</summary>
    [Uc("Engine.Mover.PostBeginPlay", Sha1 = "da473b0b77e760410c44afbb6b2682c5fb00e9f1")]
    [PortOf("uc:Engine.Mover:PostBeginPlay", Spec = "spec/devices/mover.md#initialisation")]
    protected internal override void PostBeginPlay()
    {
        if (!bSlave)
        {
            foreach (Mover m in XLevel.DynamicActors<Mover>(Tag))
            {
                if (m.bSlave)
                {
                    m.GotoState(Name.None);
                    m.SetBase(this);
                }
            }
        }

        if (bIsLeader)
        {
            Leader = this;
            foreach (Mover m in XLevel.DynamicActors<Mover>())
            {
                if (!ReferenceEquals(m, this) && m.ReturnGroup == ReturnGroup)
                {
                    m.Leader = this;
                    m.Follower = Follower;
                    Follower = m;
                }
            }
        }
        else if (Leader is null)
        {
            foreach (Mover m in XLevel.DynamicActors<Mover>())
            {
                if (!ReferenceEquals(m, this) && m.ReturnGroup == ReturnGroup)
                {
                    return;
                }
            }

            Leader = this;
        }
    }

    // ---- client-side bookkeeping --------------------------------------------------------------------------

    /// <summary>Servers keep RealPosition current; clients snap to it when they rest more than 3 uu away.</summary>
    [Uc("Engine.Mover.Timer", Sha1 = "d91cd465b8e410c950f0b7485262f17ba82d5378")]
    [PortOf("uc:Engine.Mover:Timer", Spec = "spec/devices/mover.md#client-side-bookkeeping")]
    protected internal override void Timer()
    {
        if (!Velocity.IsZero())
        {
            bClientPause = false;
            return;
        }

        if (Level.NetMode == ENetMode.NM_Client)
        {
            if (ClientUpdate == 0)
            {
                if (bClientPause)
                {
                    if ((RealPosition - Location).Size() > MoverK.ClientSnapDistance)
                    {
                        SetLocation(RealPosition);
                    }
                    else
                    {
                        RealPosition = Location;
                    }

                    bClientPause = false;
                }
                else if (RealPosition != Location)
                {
                    bClientPause = true;
                }
            }
            else
            {
                bClientPause = false;
            }
        }
        else
        {
            RealPosition = Location;
        }
    }

    // ---- triggering ---------------------------------------------------------------------------------------

    /// <summary>
    /// Picks the behaviour for the sender (a SpecialBehaviors entry or InitialState with the original times), takes
    /// the instigator from the sender when none is given, then SubTrigger.
    /// </summary>
    [Uc("Engine.Mover.Trigger", Sha1 = "faa937d3b714f62b4aa96f659b94b164861c57c8")]
    [PortOf("uc:Engine.Mover:Trigger", Spec = "spec/devices/mover.md#triggering")]
    protected internal override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        Actor? triggerActor = IsInteractObject(Other) ? Other!.Owner : Other;
        int i;
        for (i = 0; i < SpecialBehaviors.Count; i++)
        {
            FSpecialBehavior b = SpecialBehaviors[i];
            if (ReferenceEquals(triggerActor, b.EventFromActor))
            {
                DelayTime = b.DelayTime;
                DelayTimeLastKey = b.DelayTimeLastKey;
                MoveTime = b.MoveTime;
                StayOpenTime = b.StayOpenTime;
                MoverEncroachType = b.MoverEncroachType;
                GotoState(b.InitialState);
                break;
            }
        }

        if (i == SpecialBehaviors.Count)
        {
            GotoState(InitialState);
            DelayTime = OriginalDelayTime;
            DelayTimeLastKey = OriginalDelayTimeLastKey;
            MoveTime = OriginalMoveTime;
            StayOpenTime = OriginalStayOpenTime;
            MoverEncroachType = OriginalMoverEncroachType;
        }

        if (EventInstigator is null && Other is not null)
        {
            EventInstigator = Other.Instigator;
        }

        eventSubTrigger(Other, EventInstigator);
    }

    /// <summary>Picks the behaviour for the sender (compared directly, without loading times), then SubUnTrigger.</summary>
    [Uc("Engine.Mover.UnTrigger", Sha1 = "25cbf922a3a9eb42b67e45dc5256d7fd79376a35")]
    [PortOf("uc:Engine.Mover:UnTrigger", Spec = "spec/devices/mover.md#triggering")]
    protected internal override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        int i;
        for (i = 0; i < SpecialBehaviors.Count; i++)
        {
            if (ReferenceEquals(Other, SpecialBehaviors[i].EventFromActor))
            {
                GotoState(SpecialBehaviors[i].InitialState);
                break;
            }
        }

        if (i == SpecialBehaviors.Count)
        {
            GotoState(InitialState);
        }

        eventSubUnTrigger(Other, EventInstigator);
    }

    /// <summary>Global version: does nothing (the states that answer triggers override it).</summary>
    [Uc("Engine.Mover.SubTrigger", Sha1 = "77a289389792f6e18beb2fb4b712cf42a8eb74eb")]
    [PortOf("uc:Engine.Mover:SubTrigger", Spec = "spec/devices/mover.md#triggering")]
    public virtual void SubTrigger(Actor? Other, Pawn? EventInstigator) => base.Trigger(Other, EventInstigator);

    /// <summary>Global version: does nothing.</summary>
    [Uc("Engine.Mover.SubUnTrigger", Sha1 = "7927e34d57e30c907181828bd724af380b8ec4aa")]
    [PortOf("uc:Engine.Mover:SubUnTrigger", Spec = "spec/devices/mover.md#triggering")]
    public virtual void SubUnTrigger(Actor? Other, Pawn? EventInstigator) => base.UnTrigger(Other, EventInstigator);

    /// <summary>
    /// True at rest. While moving, only when the sender forces it, or the sender's SpecialBehaviors entry or the
    /// InitialState is a once-only state. As in the original loop, the InitialState test applies whenever the scan
    /// reaches the end, even after an entry for the sender matched with another state.
    /// </summary>
    [Uc("Engine.Mover.IsTriggerable", Sha1 = "f0f378620fb12417ac8f89960f0bd8593cac89f3")]
    [PortOf("uc:Engine.Mover:IsTriggerable", Spec = "spec/devices/mover.md#triggering")]
    public bool IsTriggerable(Actor? Other)
    {
        if (KeyNum == PrevKeyNum)
        {
            return true;
        }

        if (Other is not null && Other.ForceTriggerableEvenIfMoving())
        {
            return true;
        }

        int i;
        for (i = 0; i < SpecialBehaviors.Count; i++)
        {
            if (ReferenceEquals(Other, SpecialBehaviors[i].EventFromActor) && IsOnceOnlyState(SpecialBehaviors[i].InitialState))
            {
                return true;
            }
        }

        return i == SpecialBehaviors.Count && IsOnceOnlyState(InitialState);
    }

    private static bool IsOnceOnlyState(Name state) =>
        state == NAME_TriggerOpenOnceOnly || state == NAME_TriggerCloseOnceOnly;

    /// <summary>Damage triggers the mover when it is damage-triggered and the damage reaches the threshold.</summary>
    [Uc("Engine.Mover.TakeDamage", Sha1 = "4342bc215ac8e91fdab6ba822afd8d5e037db23d")]
    [PortOf("uc:Engine.Mover:TakeDamage", Spec = "spec/devices/mover.md#triggering")]
    protected internal override void TakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (bDamageTriggered && Damage >= DamageThreshold)
        {
            eventTrigger(this, instigatedBy);
        }
    }

    /// <summary>
    /// A use-triggered mover bumped by a bot opens; then the BumpType filter, BumpEvent and, for a player pawn,
    /// PlayerBumpEvent.
    /// </summary>
    [Uc("Engine.Mover.Bump", Sha1 = "a259445b3878a872ad3b2f04514658c29f2303e1")]
    [PortOf("uc:Engine.Mover:Bump", Spec = "spec/devices/mover.md#triggering")]
    protected internal override void Bump(Actor Other)
    {
        var p = Other as Pawn;
        if (bUseTriggered && p is not null && !IsHumanControlled(p) && IsPlayerPawn(p))
        {
            eventTrigger(p, p);

            // P.Controller.WaitForMover(self) is an empty bot navigation hook in this build.
        }

        if (BumpType != EBumpType.BT_AnyBump && p is null)
        {
            return;
        }

        if (BumpType == EBumpType.BT_PlayerBump && !IsPlayerPawn(p))
        {
            return;
        }

        if (BumpType == EBumpType.BT_PawnBump && p is { bAmbientCreature: true })
        {
            return;
        }

        TriggerEvent(BumpEvent, this, p);
        if (p is not null && IsPlayerPawn(p))
        {
            TriggerEvent(PlayerBumpEvent, this, p);
        }
    }

    // ---- locking ------------------------------------------------------------------------------------------

    /// <summary>Locks the mover in s_TemporaryInactive now, or at the next FinishNotify while a sequence runs.</summary>
    [Uc("Engine.Mover.Lock", Sha1 = "b41db4b5dddf82b330cf04007f8a061ac8210444")]
    [PortOf("uc:Engine.Mover:Lock", Spec = "spec/devices/mover.md#interpolation")]
    public virtual void Lock()
    {
        if (bAllowLock)
        {
            GotoState(NAME_s_TemporaryInactive);
        }
        else
        {
            bShouldLock = true;
        }
    }

    /// <summary>Does nothing outside s_TemporaryInactive.</summary>
    [Uc("Engine.Mover.UnLock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Mover:UnLock", Spec = "spec/devices/mover.md#interpolation")]
    public virtual void UnLock()
    {
    }

    // ---- encroachment -------------------------------------------------------------------------------------

    /// <summary>
    /// The mover's step would leave it overlapping <paramref name="Other"/>: controllerless pawns and decorations
    /// are destroyed by damage, debris is removed, encroach damage is dealt, then the MoverEncroachType decides.
    /// True refuses the step.
    /// </summary>
    [Uc("Engine.Mover.EncroachingOn", Sha1 = "850b9a1609fe003be01864fd567e456a71d33840")]
    [PortOf("uc:Engine.Mover:EncroachingOn", Spec = "spec/devices/mover.md#encroachment")]
    protected internal override bool EncroachingOn(Actor? Other)
    {
        if (Other is null)
        {
            return false;
        }

        if ((Other is Pawn { Controller: null }) || Other is Decoration)
        {
            Other.eventTakeDamage(MoverK.CrushDamage, null, Other.Location, FVector.Zero, SDamageType.Crushed);
            return false;
        }

        if (Other is Fragment or Projectile || Other.ClassName == NAME_Gib)
        {
            Other.Destroy();
            return false;
        }

        if (EncroachDamage != 0 && MoverEncroachType != EMoverEncroachType.ME_CrushWhenEncroach)
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

        if (Other is Pawn { Controller: not null } p && IsPlayerPawn(p))
        {
            if (!PlayerBumpEvent.IsNone)
            {
                eventBump(Other);
            }

            // UnderLift(self) for a controller waiting on this mover is an empty bot navigation hook in this build.
        }

        switch (MoverEncroachType)
        {
            case EMoverEncroachType.ME_StopWhenEncroach:
                (Leader ?? this).MakeGroupStop();
                return true;
            case EMoverEncroachType.ME_ReturnWhenEncroach:
                (Leader ?? this).MakeGroupReturnNoEncroach();

                // Pawn.PlayMoverHitSound is empty in this build.
                return true;
            case EMoverEncroachType.ME_CrushWhenEncroach:
                Other.CrushedBy(Instigator);
                return false;
            case EMoverEncroachType.ME_IgnoreWhenEncroach:
                return false;
            case EMoverEncroachType.ME_WaitAndRetryWhenEncroach:
                CurrentState = GetStateName();
                GotoState(NAME_s_WaitAndRetry);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Freezes the group where it is: motion and ambient sound stop, state code stops.</summary>
    [Uc("Engine.Mover.MakeGroupStop", Sha1 = "c171385a7b3606cdcf8ea5d85859dab4e1ef4537")]
    [PortOf("uc:Engine.Mover:MakeGroupStop", Spec = "spec/devices/mover.md#encroachment")]
    public void MakeGroupStop()
    {
        bInterpolating = false;
        StopSound(AmbientSound);
        AmbientSound = AssetRef.None;
        GotoState(GetStateName(), Name.None);
        Follower?.MakeGroupStop();
    }

    /// <summary>Stops the group and sends the leader back the way it came (Open when closing, Close when opening).</summary>
    [Uc("Engine.Mover.MakeGroupReturn", Sha1 = "0b0c4a7c043ecb60ca6a812888216412fd509679")]
    [PortOf("uc:Engine.Mover:MakeGroupReturn", Spec = "spec/devices/mover.md#encroachment")]
    public void MakeGroupReturn()
    {
        bInterpolating = false;
        StopSound(AmbientSound);
        AmbientSound = AssetRef.None;
        if (bIsLeader || ReferenceEquals(Leader, this))
        {
            GotoState(GetStateName(), KeyNum < PrevKeyNum ? NAME_Open : NAME_Close);
        }

        Follower?.MakeGroupReturn();
    }

    /// <summary>Sends every group member to s_ReturnNoEncroach, remembering its state.</summary>
    [Uc("Engine.Mover.MakeGroupReturnNoEncroach", Sha1 = "8bf2eb95bfd2791402f75c9910e3ab88f6d66733")]
    [PortOf("uc:Engine.Mover:MakeGroupReturnNoEncroach", Spec = "spec/devices/mover.md#encroachment")]
    public void MakeGroupReturnNoEncroach()
    {
        CurrentState = GetStateName();
        GotoState(NAME_s_ReturnNoEncroach);
        Follower?.MakeGroupReturnNoEncroach();
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    /// <summary>Pawn.IsPlayerPawn: the pawn has a controller that is a player.</summary>
    internal static bool IsPlayerPawn(Pawn? p) => p is { Controller.bIsPlayer: true };

    /// <summary>Pawn.IsHumanControlled: the pawn's controller is a PlayerController.</summary>
    internal static bool IsHumanControlled(Pawn? p) => p?.Controller is PlayerController;

    private static bool IsInteractObject(Actor? actor)
    {
        for (UcClassInfo? c = actor?.GetUcClass(); c is not null; c = c.Super)
        {
            if (c.Name == "SInteractObject")
            {
                return true;
            }
        }

        return false;
    }

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        SavedTrigger = Live(SavedTrigger);
        Leader = Live(Leader);
        Follower = Live(Follower);
    }
}
