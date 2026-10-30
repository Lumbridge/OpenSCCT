using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGamePlayObject: breakable, swappable and explosive props (doors, extinguishers, lamps, the crane wall and
/// collision swaps, hidden explosives), written from spec/devices/gameplay-object.md. Damage goes through
/// <see cref="ProcessDamage"/> (native contract), Trigger toggles TriggeredMesh / TriggeredSkin or destroys the
/// object for a sender named in DestroyOnEventFrom.
/// </summary>
public partial class SGamePlayObject
{
    public static new readonly StateTable ClassStates = new(SBasicGamePlayObject.ClassStates,
        s_Flying.Instance, s_LockedByChaff.Instance, s_PickedUp.Instance, s_WaitForDamageReset.Instance);

    public override StateTable GetStateTable() => ClassStates;

    // ---- start-up -----------------------------------------------------------------------------------------

    /// <summary>A prop that is neither pickable nor pushable counts as world geometry.</summary>
    [Uc("SBase.SGamePlayObject.BeginPlay", Sha1 = "636c49f13bf55158e304e30ae32eff6e70fdb37d")]
    [PortOf("uc:SBase.SGamePlayObject:BeginPlay", Spec = "spec/devices/gameplay-object.md#start-up")]
    protected override void BeginPlay()
    {
        base.BeginPlay();
        if (!bPickable && !bPushable)
        {
            bWorldGeometry = true;
        }
    }

    /// <summary>Stores the initial hit points, mesh, skin and blocking, and fires OnResetEvent.</summary>
    [Uc("SBase.SGamePlayObject.PostBeginPlay", Sha1 = "0034f9c063a7201b22f65ac8770dd2130eef646b")]
    [PortOf("uc:SBase.SGamePlayObject:PostBeginPlay", Spec = "spec/devices/gameplay-object.md#start-up")]
    protected override void PostBeginPlay()
    {
        OriginalColHeight = SvmMath.Trunc(CollisionHeight);
        if (bPickable)
        {
            // Server only, as the interaction of SBasicGamePlayObject.PostBeginPlay.
            if (Role == ENetRole.ROLE_Authority)
            {
                PickupInteraction = DeviceClasses.Spawn(this, DeviceK_PickupInteraction, this, Name.None, Location, Rotation) as SPickupInteraction;
            }

            bMovable = true;
            bReplicateMovement = true;
            bSkipActorPropertyReplication = false;
            bUpdateSimulatedPosition = true;
        }

        if (bPushable)
        {
            bMovable = true;
            bReplicateMovement = true;
            bSkipActorPropertyReplication = false;
            bUpdateSimulatedPosition = true;
            bBlockZeroExtentTraces = true;
            bBlockNonZeroExtentTraces = true;
        }

        InitialHitPoints = HitPoints;
        InitialStaticMesh = StaticMesh;
        if (Skins.Count > 0)
        {
            InitialSkin = Skins[0];
        }

        RepSkin = InitialSkin;
        InitialBlockActors = bBlockActors;
        InitialBlockPlayers = bBlockPlayers;
        if (ObjectLights.Count > 0)
        {
            bCanBeChaffed = true;
        }

        if (!TriggeredMesh.IsNone || !TriggeredSkin.IsNone || DamagedMeshes.Count > 0)
        {
            bSkipActorPropertyReplication = false;
        }

        FireOnResetEvent();
        base.PostBeginPlay();
    }

    private static readonly ClassRef DeviceK_PickupInteraction = new("SBase.SPickupInteraction");

    /// <summary>Fires OnResetEvent by its type; the alternating types flip after firing.</summary>
    private void FireOnResetEvent()
    {
        EEventType type = OnResetEventType;
        FireTypedEvent(OnResetEvent, ref type, null);
        OnResetEventType = type;
    }

    /// <summary>Fires <paramref name="eventName"/> from this object by <paramref name="type"/> (alternating types flip).</summary>
    private void FireTypedEvent(Name eventName, ref EEventType type, Pawn? instigator)
    {
        if (eventName.IsNone)
        {
            return;
        }

        switch (type)
        {
            case EEventType.EVT_Trigger:
                TriggerEvent(eventName, this, instigator);
                break;
            case EEventType.EVT_Untrigger:
                UntriggerEvent(eventName, this, instigator);
                break;
            case EEventType.EVT_TriggerUntrigger:
                TriggerEvent(eventName, this, instigator);
                type = EEventType.EVT_UntriggerTrigger;
                break;
            case EEventType.EVT_UntriggerTrigger:
                UntriggerEvent(eventName, this, instigator);
                type = EEventType.EVT_TriggerUntrigger;
                break;
        }
    }

    /// <summary>Sticky gadgets attach only to props that are neither pickable nor pushable.</summary>
    [Uc("SBase.SGamePlayObject.AcceptStickableGPO", Sha1 = "22c4386d8121b3c14daba96c50a545b4e195444a")]
    [PortOf("uc:SBase.SGamePlayObject:AcceptStickableGPO", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public override bool AcceptStickableGPO() => !bPickable && !bPushable;

    // ---- trigger and untrigger ----------------------------------------------------------------------------

    /// <summary>
    /// A damageable object dies by a DestroyOnEventFrom sender (and ignores triggers once dead); otherwise the
    /// trigger toggles the skin or the mesh, and is passed on to the object's lights.
    /// </summary>
    [Uc("SBase.SGamePlayObject.Trigger", Sha1 = "f59b192f4df2d1ad250d655ab6028a6abac4df6a")]
    [PortOf("uc:SBase.SGamePlayObject:Trigger", Spec = "spec/devices/gameplay-object.md#trigger-and-untrigger")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (EventInstigator is null && Other is not null)
        {
            EventInstigator = Other.Instigator;
        }

        if (bDamageable)
        {
            if (HitPoints <= 0)
            {
                return;
            }

            foreach (Actor? sender in DestroyOnEventFrom)
            {
                if (ReferenceEquals(sender, Other) && sender is not null)
                {
                    SDamageType type = TakeDamageTypes.Count > 0 ? TakeDamageTypes[0] : SDamageType.SDTBase;
                    eventTakeDamage(HitPoints, EventInstigator, Location, new FVector(0f, 0f, DeviceK.DestroyOnEventMomentumZ), type);
                    return;
                }
            }
        }

        if (!TriggeredSkin.IsNone)
        {
            SetSkin(RepSkin != InitialSkin ? InitialSkin : TriggeredSkin);
        }
        else if (StaticMesh != InitialStaticMesh)
        {
            SetStaticMesh(InitialStaticMesh);
        }
        else if (!TriggeredMesh.IsNone)
        {
            SetStaticMesh(TriggeredMesh);
        }

        foreach (STriggerLight? light in ObjectLights.ToArray())
        {
            light?.eventTrigger(Other, EventInstigator);
        }
    }

    /// <summary>Puts a swapped skin or mesh back and passes the untrigger to the lights (nothing once dead).</summary>
    [Uc("SBase.SGamePlayObject.UnTrigger", Sha1 = "c0c5719fecc5797ff4bff9136c99168fd741af46")]
    [PortOf("uc:SBase.SGamePlayObject:UnTrigger", Spec = "spec/devices/gameplay-object.md#trigger-and-untrigger")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        if (bDamageable && HitPoints <= 0)
        {
            return;
        }

        if (!TriggeredSkin.IsNone)
        {
            if (RepSkin != InitialSkin)
            {
                SetSkin(InitialSkin);
            }
        }
        else if (StaticMesh != InitialStaticMesh)
        {
            SetStaticMesh(InitialStaticMesh);
        }

        foreach (STriggerLight? light in ObjectLights.ToArray())
        {
            light?.eventUnTrigger(Other, EventInstigator);
        }
    }

    /// <summary>Sets Skins[0] and the replicated RepSkin.</summary>
    private void SetSkin(AssetRef skin)
    {
        RepSkin = skin;
        if (Skins.Count == 0)
        {
            Skins.Add(skin);
        }
        else
        {
            Skins[0] = skin;
        }
    }

    // ---- damage -------------------------------------------------------------------------------------------

    /// <summary>
    /// Only the listed damage types count; the damage sound; a sticky shot locks a sticky-as-chaff object; then the
    /// momentum is normalised (a pushable object is pushed) and the damage is processed with the instigator
    /// recorded as SGameInfo.GPODamageInstigator, so everything the damage causes is credited to it.
    /// </summary>
    [Uc("SBase.SGamePlayObject.TakeDamage", Sha1 = "1fe4f245c8aae33a63a6fa61f576d7fe5652be6e")]
    [PortOf("uc:SBase.SGamePlayObject:TakeDamage", Spec = "spec/devices/gameplay-object.md#damage")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (TakeDamageTypes.Count > 0 && !TakeDamageTypes.Contains(DamageType))
        {
            return;
        }

        PlaySound(TakeDamageSound, ESoundSlot.SLOT_Misc, 1f, false, DeviceK.DamageSoundRadius, 1f);
        if (bStickyDamageIsLikeChaff && DamageType == SDamageType.SDTStickyShocker)
        {
            DeviceClasses.Spawn(this, DeviceK.SparkShockerClass, this, Name.None, HitLocation, Rotation);
            eventSetLockedByChaff(StickyNeutralizationTime);
            return;
        }

        Instigator = EventInstigator;
        float size = Momentum.Size();
        if (size > 0f)
        {
            Momentum = DeviceK.MomentumLength * Momentum / size;
            Momentum.X += DeviceK.MomentumJitter * FRand() - DeviceK.MomentumJitter / 2f;
            Momentum.Y += DeviceK.MomentumJitter * FRand() - DeviceK.MomentumJitter / 2f;
            Momentum.Z = SvmMath.Max(SvmMath.Trunc(SvmMath.Abs(Momentum.Z)), DeviceK.MomentumMinZ);
        }

        if (bPushable)
        {
            Velocity += Momentum * DeviceK.PushImpulse / Mass;
            GotoState(nameof(s_Flying));
        }

        var game = Level.Game as SGameInfo;
        Pawn? previous = game?.GPODamageInstigator;
        if (game is not null && EventInstigator is not null)
        {
            game.GPODamageInstigator = EventInstigator;
        }

        eventProcessDamage(Damage);
        if (game is not null)
        {
            game.GPODamageInstigator = previous;
        }
    }

    /// <summary>
    /// The native damage processing (natives2-damage.md rule 1): hit points down; at 0 the destruction (sound, noise,
    /// the radius blast and shake, lights off, the reset wait); then, on every call, the spawn bands and the damage
    /// stage of the current damage percent.
    /// </summary>
    [UcNative("ASGamePlayObject::execProcessDamage", Exe = 0x10a8fc40)]
    [PortOf("nat:SBase.SGamePlayObject.ProcessDamage", Spec = "spec/native/natives2-damage.md#1-sgameplayobjectprocessdamagedamage")]
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1 (game 0x10a8fc40): order, percent formula, bands, stage and collision")]
    public void ProcessDamage(int Damage)
    {
        if (!bDamageable || bAlreadyDestroyed)
        {
            return;
        }

        // Damage 0 still runs the rest (the stage is re-applied and its event fired again).
        HitPoints = SvmMath.Max(HitPoints - Damage, 0);
        if (HitPoints == 0)
        {
            bAlreadyDestroyed = true;
            PlaySound(DestroySound, ESoundSlot.SLOT_Misc, 1f, false, DeviceK.DamageSoundRadius, 1f);
            if (!DestroySound.IsNone)
            {
                NotifyNoise(DeviceK.DestroyNoise);
            }

            // The blast and the shake run only for an object that damages actors in a positive range (0x10a8fd86).
            if (bCanDamageActor && DamageActorRange > 0)
            {
                DamageActorsInRange();
            }

            foreach (STriggerLight? light in ObjectLights)
            {
                (light as IObjectLight)?.LightOff();
            }

            if (ResetDamageTime > 0)
            {
                eventWaitReset();
            }
        }

        int percent = DamagePercent();
        UpdateDamageBands(percent);
        ApplyDamageStage(percent);
    }

    /// <summary>
    /// <c>int((1 - HitPoints / InitialHitPoints) x 100)</c>, 100 without initial hit points. The game computes it on
    /// the x87 stack; the ratio is taken in double before truncating (natives2-damage.md rule 1.4).
    /// </summary>
    private int DamagePercent() =>
        InitialHitPoints > 0 ? (int)((1.0 - ((double)HitPoints / InitialHitPoints)) * 100.0) : 100;

    /// <summary>Dispatches ProcessDamage through the current state (s_WaitForDamageReset ignores it).</summary>
    public void eventProcessDamage(int Damage)
    {
        if (StateNode is IProcessDamage s)
        {
            s.ProcessDamage(this, Damage);
        }
        else
        {
            ProcessDamage(Damage);
        }
    }

    /// <summary>
    /// The damage stage, re-applied on every call (natives2-damage.md rule 1.6): the DamagedMeshes entry with the
    /// highest Percent not above <paramref name="percent"/> (the first on a tie). A Skin stage only changes the skin;
    /// otherwise a None mesh stops drawing the object (DrawType none, the mesh is kept) and a mesh is swapped in.
    /// Collision goes off for a stage that leaves nothing at 0 hit points or that disables it (attached actors are
    /// destroyed), else back to the initial blocking. The stage's event is fired with no instigator.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1.6 (game 0x10a8fc40)")]
    private void ApplyDamageStage(int percent)
    {
        int best = -1;
        for (int i = 0; i < DamagedMeshes.Count; i++)
        {
            int stagePercent = DamagedMeshes[i].Percent;
            if (stagePercent <= percent && (best < 0 || stagePercent > DamagedMeshes[best].Percent))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return;
        }

        FDamagedMesh entry = DamagedMeshes[best];
        if (!entry.Skin.IsNone)
        {
            SetSkin(entry.Skin);
        }
        else if (entry.StaticMesh.IsNone)
        {
            SetDrawType(EDrawType.DT_None);

            // This port's collision is the mesh's own colliders: an object no longer drawn has none, on every machine
            // (a client does the same when the draw type arrives, PawnProxies NetReceive).
            Natives?.SetStaticMesh(this, default);
        }
        else
        {
            SetDrawType(EDrawType.DT_StaticMesh);
            SetStaticMesh(entry.StaticMesh);
        }

        if (entry.Skin.IsNone && entry.StaticMesh.IsNone && HitPoints == 0)
        {
            SetCollision(false, false, false);
            DestroyAttached(keepPawns: true);
        }
        else if (entry.bDisableCollision)
        {
            SetCollision(false, false, false);
            DestroyAttached(keepPawns: false);
        }
        else
        {
            SetCollision(true, InitialBlockActors, InitialBlockPlayers);
        }

        if (!bBlockActors && !bBlockPlayers)
        {
            bBlockZeroExtentTraces = false;
            bBlockNonZeroExtentTraces = false;
        }

        EEventType type = entry.EventType;
        FireTypedEvent(entry.Event, ref type, null);
        entry.EventType = type;
        DamagedMeshes[best] = entry;
    }

    /// <summary>Destroys the actors attached to the object (pawns kept when <paramref name="keepPawns"/>).</summary>
    private void DestroyAttached(bool keepPawns)
    {
        foreach (Actor attached in Attached.ToArray())
        {
            if (!(keepPawns && attached is Pawn))
            {
                attached.Destroy();
            }
        }
    }

    /// <summary>
    /// The SpawnableObjects bands, on every call (natives2-damage.md rule 1.5): an entry with nothing spawned whose band
    /// holds <paramref name="percent"/> spawns (its particle effect on every machine, else its class with the object's
    /// owner and instigator); an entry whose percent went past its band removes what it spawned.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1.5 (game 0x10a8fc40)")]
    private void UpdateDamageBands(int percent)
    {
        for (int i = 0; i < SpawnableObjects.Count; i++)
        {
            FSpawnInfo info = SpawnableObjects[i];
            if (info.SpawnedActor is null)
            {
                if (percent < info.MinPercent || percent > info.MaxPercent)
                {
                    continue;
                }

                if (!info.SpawnParticle.IsNone)
                {
                    SpawnDamageEmitter(i);
                }
                else if (!info.SpawnClass.IsNone)
                {
                    info.SpawnedActor = DeviceClasses.Spawn(this, info.SpawnClass, Owner, Name.None, Location, Rotation);
                    SpawnableObjects[i] = info;
                }
            }
            else if (percent > info.MaxPercent)
            {
                if (!info.SpawnParticle.IsNone)
                {
                    KillDamageEmitter(i);
                }
                else
                {
                    info.SpawnedActor.Destroy();
                    info.SpawnedActor = null;
                    SpawnableObjects[i] = info;
                }
            }
        }
    }

    /// <summary>
    /// The destruction blast (natives2-damage.md rule 1.3.4): for every actor of a copy of the level's list (no hidden
    /// actors, not the object) with <c>int(d2) &lt; DamageActorRange^2</c> and a clear 0x286 line from the object to
    /// it, <c>int((DamageActorRange - dist) DamageActorPoints / DamageActorRange)</c> of DamageActorType with no
    /// instigator and momentum <c>A - object</c>: pawns through their player controller's PawnTakeDamage (spy hold
    /// rule, the spy first), other actors through TakeDamage (other gameplay objects chain). No team test. A pawn out
    /// of reach but within ShakeRange is shaken.
    /// </summary>
    [UcNative("ASGamePlayObject::execProcessDamage", Exe = 0x10a8fc90)]
    [PortOf("nat:SBase.SGamePlayObject.ProcessDamage", Spec = "spec/native/natives2-damage.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ProcessDamage body 0x10a8fda1..0x10a900cb: reach, sight 0x286, linear falloff, PawnTakeDamage for SBasePawn, shake law")]
    private void DamageActorsInRange()
    {
        var actors = new System.Collections.Generic.List<Actor>();
        foreach (Actor a in XLevel.AllActors<Actor>())
        {
            if (!ReferenceEquals(a, this) && !a.bDeleteMe && !a.bHidden)
            {
                actors.Add(a);
            }
        }

        int range2 = DamageActorRange * DamageActorRange;
        foreach (Actor a in actors)
        {
            if (a.bDeleteMe || a.XLevel is null)
            {
                continue;
            }

            int d2 = SvmMath.Trunc(FVector.DistSquared(a.Location, Location));
            if (d2 >= range2)
            {
                if (a is SBasePawn shaken && d2 < ShakeRange * ShakeRange)
                {
                    GadgetBlast.Shake(shaken, d2, ShakeRange);
                }

                continue;
            }

            if (GadgetWorld.Line(this, a.Location, Location, GadgetWorld.SightFlags, out FCheckResult hit) &&
                !ReferenceEquals(hit.Actor, a))
            {
                continue;
            }

            float dist = FVector.Dist(a.Location, Location);
            int damage = SvmMath.Trunc((DamageActorRange - dist) * DamageActorPoints / DamageActorRange);
            FVector momentum = a.Location - Location;
            if (a is SBasePawn pawn)
            {
                // Only a pawn with a player controller takes the blast (0x10a8ffa5), then the hold rule, spy first.
                if (pawn.Controller is SPlayerController)
                {
                    GadgetBlast.HurtPawn(pawn, damage, null, a.Location, momentum, DamageActorType,
                        PercentDamageRefillable, bTargetFirst: false);
                }

                continue;
            }

            a.eventTakeDamage(damage, null, a.Location, momentum, DamageActorType);
        }
    }

    /// <summary>Starts the reset wait after destruction, unless the object is flying.</summary>
    [Uc("SBase.SGamePlayObject.WaitReset", Sha1 = "d5f73366707a177c7b64dda1cf080017ef69ae64")]
    [PortOf("uc:SBase.SGamePlayObject:WaitReset", Spec = "spec/devices/gameplay-object.md#damage")]
    public virtual void WaitReset()
    {
        if (GetStateName() != nameof(s_Flying))
        {
            GotoState(nameof(s_WaitForDamageReset));
        }
    }

    public void eventWaitReset() => WaitReset();

    // ---- chaff --------------------------------------------------------------------------------------------

    /// <summary>An object with lights is not locked again while all of them are already off.</summary>
    [Uc("SBase.SGamePlayObject.SetLockedByChaff", Sha1 = "0eac4e7d8bab5c463dbb89c3646550128d7d5d9d")]
    [PortOf("uc:SBase.SGamePlayObject:SetLockedByChaff", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
    public override void SetLockedByChaff(float Length)
    {
        if (bCanBeChaffed && ObjectLights.Count > 0)
        {
            bool allLightsAreOff = true;
            foreach (STriggerLight? light in ObjectLights)
            {
                if (light is not null && light.CurrentLightType != ELightType.LT_None)
                {
                    allLightsAreOff = false;
                    break;
                }
            }

            if (allLightsAreOff)
            {
                return;
            }
        }

        base.SetLockedByChaff(Length);
    }

    // ---- pushable and pickable props ----------------------------------------------------------------------

    /// <summary>A heavy pawn walking into a pushable object kicks it into flight.</summary>
    [Uc("SBase.SGamePlayObject.Touch", Sha1 = "6f9b0136b6eb47b4fc0654a79c9bf8cfe2b62fc0")]
    [PortOf("uc:SBase.SGamePlayObject:Touch", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
    protected override void Touch(Actor Other)
    {
        if (!bPushable || Other is not SBasePawn || !(Other.Mass > DeviceK.TouchPushMass))
        {
            return;
        }

        float oldZ = Velocity.Z;
        if (Other.Velocity.Size() > 0f)
        {
            Velocity += Other.Velocity;
        }

        Velocity.Z = Physics != EPhysics.PHYS_Falling ? DeviceK.TouchPushZ : oldZ;
        GotoState(nameof(s_Flying));
    }

    /// <summary>A controller picks the object up: no physics, owned by it, the pickup entry disabled.</summary>
    [Uc("SBase.SGamePlayObject.NotifyPickup", Sha1 = "fe19bc3f4ce7822eeff315696d2c15ee378262dd")]
    [PortOf("uc:SBase.SGamePlayObject:NotifyPickup", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
    protected internal virtual bool NotifyPickup(Controller Instigator)
    {
        SetPhysics(EPhysics.PHYS_None);
        SetOwner(Instigator);
        if (PickupInteraction is not null)
        {
            PickupInteraction.SetBase(null);
            PickupInteraction.bCanPickup = false;
        }

        GotoState(nameof(s_PickedUp));
        SetCollision(false, false, false);
        return false;
    }

    /// <summary>Thrown by its carrier: velocity, instigator, released from the hand, flying.</summary>
    [Uc("SBase.SGamePlayObject.Throw", Sha1 = "8382c76204391a7532852626f4af1126a51eda84")]
    [PortOf("uc:SBase.SGamePlayObject:Throw", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
    [Provenance(ProvenanceKind.Estimated, "Hand bone placement needs the animation system: thrown from the carrier's location")]
    protected internal virtual void Throw(Controller? Thrower, FVector ThrowVelocity)
    {
        Velocity = ThrowVelocity;
        Instigator = Thrower?.Pawn;
        if (Thrower?.Pawn is SBasePawn carrier && ReferenceEquals(carrier.HandItem, this))
        {
            carrier.HandItem = null;
        }

        if (Thrower?.Pawn is { } pawn)
        {
            FVector start = pawn.Location;
            SetBase(null);
            SetLocation(start);
            PickupInteraction?.SetLocation(start);
            PickupInteraction?.SetBase(this);
        }

        GotoState(nameof(s_Flying));
    }

    /// <summary>The object came to rest: pickup re-enabled, owner cleared, blocking restored, reset wait if dead.</summary>
    [Uc("SBase.SGamePlayObject.StoppedMoving", Sha1 = "905f548524f293fe34e2de55f7bb2bf8d4f20147")]
    [PortOf("uc:SBase.SGamePlayObject:StoppedMoving", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
    protected internal virtual void StoppedMoving()
    {
        if (bPickable && PickupInteraction is not null)
        {
            PickupInteraction.bCanPickup = true;
        }

        SetOwner(null);
        SetCollision(true, InitialBlockActors, InitialBlockPlayers);
        GotoState(HitPoints == 0 && ResetDamageTime > 0 ? (Name)nameof(s_WaitForDamageReset) : Name.None);
    }

    /// <summary>
    /// Native bounce: the object stops where it hit and the StoppedMoving event follows (bounce damping is open
    /// question 2). The BounceSound plays as the original's does when the hit would bounce the object on.
    /// </summary>
    [UcNative("ASGamePlayObject::execNativeHitWall", Exe = 0x10a90870)]
    [PortOf("nat:SBase.SGamePlayObject.NativeHitWall", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
    [Provenance(ProvenanceKind.Estimated, "Bounce rules not decoded (gameplay-object.md open question 2): stop at the first hit")]
    public void NativeHitWall(FVector HitNormal, Actor? Wall)
    {
        PlayBounceSound(HitNormal, Wall);
        Velocity = FVector.Zero;
        SetPhysics(EPhysics.PHYS_None);
        eventStoppedMoving();
    }

    /// <summary>The share of the mirrored velocity a bounce keeps.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeHitWall game 0x10a90a08: (V - 2 (V.N) N) x 0.5")]
    private const float BounceKeep = 0.5f;

    /// <summary>Below this bounce speed on a floor (normal Z above 0.7) the object settles instead of bouncing.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeHitWall game 0x10a90b32: speed < 60 and HitNormal.Z > 0.7 stop it")]
    private const float BounceSettleSpeed = 60f;

    [Provenance(ProvenanceKind.Decoded, "NativeHitWall game 0x10a90b3f: HitNormal.Z > 0.7")]
    private const float BounceFloorNormalZ = 0.7f;

    /// <summary>A bounce slower than this makes no sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeHitWall game 0x10a90d26: speed > 40")]
    private const float BounceSoundSpeed = 40f;

    /// <summary>The bounce sound's radius (volume 1, pitch 1).</summary>
    [Provenance(ProvenanceKind.Decoded, "NativeHitWall game 0x10a90d60: HearSound(self, BounceSound, (1.0, 100.0, 1.0)) to every bIsPlayer controller")]
    private const float BounceSoundRadius = 100f;

    /// <summary>
    /// NativeHitWall's sound (game 0x10a90d26): the object bounces with half its mirrored velocity; when it bounces on
    /// (it hit a pawn, or it is not settling on a floor below 60 uu/s) faster than 40 uu/s and has a static mesh,
    /// every player hears its BounceSound at radius 100. The native then raises a noise event (not ported).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The class the native tests the wall against (0x10cc1568) is read as Pawn")]
    private void PlayBounceSound(FVector HitNormal, Actor? Wall)
    {
        if (BounceSound.IsNone || StaticMesh.IsNone)
        {
            return;
        }

        FVector bounce = (Velocity - (HitNormal * (2f * (Velocity | HitNormal)))) * BounceKeep;
        float speed = bounce.Size();
        bool settles = speed < BounceSettleSpeed && HitNormal.Z > BounceFloorNormalZ;
        if ((Wall is Pawn || !settles) && speed > BounceSoundSpeed)
        {
            PlaySound(BounceSound, ESoundSlot.SLOT_None, 1f, false, BounceSoundRadius);
        }
    }

    // ---- effects ------------------------------------------------------------------------------------------

    /// <summary>
    /// Spawns the particle effect of SpawnableObjects entry <paramref name="i"/> on the object (sent to every
    /// client by the authority): an SSpawnableEmitter based on the object carrying the entry's emitter.
    /// </summary>
    [Uc("SBase.SGamePlayObject.SpawnDamageEmitter", Sha1 = "bcd21e99400377078866b991c656fbae71e0e694")]
    [PortOf("uc:SBase.SGamePlayObject:SpawnDamageEmitter", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public void SpawnDamageEmitter(int i)
    {
        FSpawnInfo info = SpawnableObjects[i];
        if (info.SpawnParticle.IsNone)
        {
            return;
        }

        SSpawnableEmitter? emitter = XLevel.Spawn<SSpawnableEmitter>(Location, Rotation, this, Instigator);
        if (emitter is null)
        {
            return;
        }

        emitter.SetBase(this);
        emitter.DeleteParticleEmitters = false;
        emitter.Emitters.Insert(0, info.SpawnParticle);
        info.SpawnedActor = emitter;
        SpawnableObjects[i] = info;
    }

    /// <summary>Destroys the effect of entry <paramref name="i"/>.</summary>
    [Uc("SBase.SGamePlayObject.KillDamageEmitter", Sha1 = "1343c0491ab16fe6cc165cb4685136f647feaa8b")]
    [PortOf("uc:SBase.SGamePlayObject:KillDamageEmitter", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public void KillDamageEmitter(int i)
    {
        FSpawnInfo info = SpawnableObjects[i];
        if (info.SpawnedActor is null)
        {
            return;
        }

        info.SpawnedActor.Destroy();
        info.SpawnedActor = null;
        SpawnableObjects[i] = info;
    }

    /// <summary>The full reset after ResetDamageTime (s_WaitForDamageReset.Timer).</summary>
    private void ResetAfterDamage()
    {
        bAlreadyDestroyed = false;
        bDamageable = true;
        HitPoints = InitialHitPoints;
        RestoreDrawType();
        SetStaticMesh(InitialStaticMesh);
        SetSkin(InitialSkin);
        foreach (STriggerLight? light in ObjectLights)
        {
            if (light is IObjectLight switchable)
            {
                if (light.bInitialyOn)
                {
                    switchable.LightOn();
                }
                else
                {
                    switchable.LightOff();
                }
            }
        }

        if (bPickable && PickupInteraction is not null)
        {
            PickupInteraction.bCanPickup = true;
        }

        SetCollision(true, InitialBlockActors, InitialBlockPlayers);
        bBlockNonZeroExtentTraces = true;
        bBlockZeroExtentTraces = true;
        FireOnResetEvent();
        for (int i = 0; i < SpawnableObjects.Count; i++)
        {
            FSpawnInfo info = SpawnableObjects[i];
            if (info.SpawnedActor is null)
            {
                continue;
            }

            if (!info.SpawnParticle.IsNone)
            {
                KillDamageEmitter(i);
            }
            else
            {
                info.SpawnedActor.Destroy();
                info.SpawnedActor = null;
                SpawnableObjects[i] = info;
            }
        }

        GotoState(Name.None);
    }

    /// <summary>
    /// A None damage stage stops drawing the object (DT_None) and the reset's SetStaticMesh does not change the draw
    /// type, so a reset object would come back solid but unseen (CpusS has two that vanish and reset after 12 s).
    /// It is drawn again here.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "gameplay-object.md open question: does the original draw a reset object whose stage set DT_None")]
    private void RestoreDrawType()
    {
        if (DrawType == EDrawType.DT_None && !InitialStaticMesh.IsNone)
        {
            SetDrawType(EDrawType.DT_StaticMesh);
            Natives?.SetStaticMesh(this, StaticMesh);
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        PickupInteraction = Live(PickupInteraction);
        for (int i = 0; i < SpawnableObjects.Count; i++)
        {
            FSpawnInfo info = SpawnableObjects[i];
            if (info.SpawnedActor is { bDeleteMe: true })
            {
                info.SpawnedActor = null;
                SpawnableObjects[i] = info;
            }
        }
    }

    // ---- state dispatch of the functions this class declares ---------------------------------------------

    /// <summary>A state's version of <see cref="ProcessDamage"/>.</summary>
    public interface IProcessDamage
    {
        void ProcessDamage(SGamePlayObject self, int Damage);
    }

    /// <summary>A state's version of <see cref="NotifyPickup"/>.</summary>
    public interface INotifyPickup
    {
        bool NotifyPickup(SGamePlayObject self, Controller Instigator);
    }

    /// <summary>A state's version of <see cref="Throw"/>.</summary>
    public interface IThrow
    {
        void Throw(SGamePlayObject self, Controller? Thrower, FVector ThrowVelocity);
    }

    /// <summary>A state's version of <see cref="StoppedMoving"/>.</summary>
    public interface IStoppedMoving
    {
        void StoppedMoving(SGamePlayObject self);
    }

    public bool eventNotifyPickup(Controller Instigator) =>
        StateNode is INotifyPickup s ? s.NotifyPickup(this, Instigator) : NotifyPickup(Instigator);

    public void eventThrow(Controller? Thrower, FVector ThrowVelocity)
    {
        if (StateNode is IThrow s)
        {
            s.Throw(this, Thrower, ThrowVelocity);
        }
        else
        {
            Throw(Thrower, ThrowVelocity);
        }
    }

    public void eventStoppedMoving()
    {
        if (StateNode is IStoppedMoving s)
        {
            s.StoppedMoving(this);
        }
        else
        {
            StoppedMoving();
        }
    }
}
