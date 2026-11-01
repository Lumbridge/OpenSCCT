using System;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Script functions and natives that Engine.Actor declares and the map devices (spec/devices) use: the event
// dispatch hooks, damage and encroachment, sound and music, and the collision, mesh and physics setters.
//
// Script functions follow the event pattern of Actor.Events.cs: a virtual with the UnrealScript name is the
// class-level (global) version, and callers use the eventX dispatcher so the current state's version wins. States
// override these functions by implementing the nested interfaces below.
public abstract partial class Actor
{
    /// <summary>A state's version of <see cref="TakeDamage"/>.</summary>
    public interface ITakeDamage
    {
        void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag);
    }

    /// <summary>A state's version of <see cref="EncroachingOn"/>.</summary>
    public interface IEncroachingOn
    {
        bool EncroachingOn(Actor self, Actor? Other);
    }

    /// <summary>A state's version of <see cref="BeginEvent"/>.</summary>
    public interface IBeginEvent
    {
        void BeginEvent(Actor self);
    }

    /// <summary>A state's version of <see cref="EndEvent"/>.</summary>
    public interface IEndEvent
    {
        void EndEvent(Actor self);
    }

    /// <summary>The level's collision natives, or null when no physics side is installed.</summary>
    protected IActorNatives? Natives => XLevel?.GetService<IActorNatives>();

    // ---- event dispatch -----------------------------------------------------------------------------------

    /// <summary>True when a mover should accept this actor's trigger even while it moves (SInterrupteur overrides).</summary>
    [Uc("Engine.Actor.ForceTriggerableEvenIfMoving", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:Engine.Actor:ForceTriggerableEvenIfMoving", Spec = "spec/devices/events.md#rules")]
    public virtual bool ForceTriggerableEvenIfMoving() => false;

    /// <summary>A receiver started acting on this actor's event (only Mover's BumpButton gives it meaning).</summary>
    [Uc("Engine.Actor.BeginEvent", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Actor:BeginEvent", Spec = "spec/devices/events.md#rules")]
    protected internal virtual void BeginEvent()
    {
    }

    /// <summary>A receiver finished acting on this actor's event.</summary>
    [Uc("Engine.Actor.EndEvent", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Actor:EndEvent", Spec = "spec/devices/events.md#rules")]
    protected internal virtual void EndEvent()
    {
    }

    public void eventBeginEvent()
    {
        if (StateNode is IBeginEvent s)
        {
            s.BeginEvent(this);
        }
        else
        {
            BeginEvent();
        }
    }

    public void eventEndEvent()
    {
        if (StateNode is IEndEvent s)
        {
            s.EndEvent(this);
        }
        else
        {
            EndEvent();
        }
    }

    /// <summary>
    /// Tells every player controller about a loud noise at this actor (a HUD noise marker). A loudness of 0 counts
    /// as 1.
    /// </summary>
    [Uc("Engine.Actor.NotifyNoise", Sha1 = "80999d8e514b117c4baefe3220ad3c461d87c26e")]
    [PortOf("uc:Engine.Actor:NotifyNoise", Spec = "spec/devices/events.md#rules")]
    public virtual void NotifyNoise(float Loudness = 0f)
    {
        if (Loudness == 0f)
        {
            Loudness = 1f;
        }

        foreach (PlayerController pc in XLevel.AllActors<PlayerController>())
        {
            pc.ClientNotifyNoise(this, Loudness);
        }
    }

    /// <summary>Round reset hook; empty on Actor.</summary>
    [Uc("Engine.Actor.Reset", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    public virtual void Reset()
    {
    }

    // ---- damage and encroachment --------------------------------------------------------------------------

    /// <summary>Damage dealt to this actor. Empty on Actor; callers use <see cref="eventTakeDamage"/>.</summary>
    [Uc("Engine.Actor.TakeDamage", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Actor:TakeDamage", Spec = "spec/devices/gameplay-object.md#damage")]
    protected internal virtual void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
    }

    public void eventTakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (XLevel?.World.IsReplaying == true) return;
        using var clock = WorldEventTime();
        if (StateNode is ITakeDamage s)
        {
            s.TakeDamage(this, Damage, EventInstigator, HitLocation, Momentum, DamageType, PillTag);
        }
        else
        {
            TakeDamage(Damage, EventInstigator, HitLocation, Momentum, DamageType, PillTag);
        }
    }

    /// <summary>
    /// This actor's move would leave it overlapping <paramref name="Other"/> (called by the encroachment natives).
    /// True refuses the move. False on Actor. Movers dispatch it through their state (Mover.eventEncroachingOn).
    /// </summary>
    [Uc("Engine.Actor.EncroachingOn", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Actor:EncroachingOn", Spec = "spec/devices/mover.md#encroachment")]
    protected internal virtual bool EncroachingOn(Actor? Other) => false;

    /// <summary>An encroacher's move went ahead while overlapping this actor. Empty on Actor.</summary>
    [Uc("Engine.Actor.EncroachedBy", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Actor:EncroachedBy", Spec = "spec/devices/mover.md#encroachment")]
    public virtual void EncroachedBy(Actor? Other)
    {
    }

    /// <summary>A crushing mover ran into this actor; Actor treats it as being killed by the instigator.</summary>
    [Uc("Engine.Actor.CrushedBy", Sha1 = "091ddb4a76acc002cf47c93010e8e8aba51b089a")]
    [PortOf("uc:Engine.Actor:CrushedBy", Spec = "spec/devices/mover.md#encroachment")]
    public virtual void CrushedBy(Pawn? EventInstigator) => KilledBy(EventInstigator);

    /// <summary>Killed by the environment on behalf of <paramref name="EventInstigator"/>. Empty on Actor.</summary>
    [Uc("Engine.Actor.KilledBy", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    public virtual void KilledBy(Pawn? EventInstigator)
    {
    }

    // ---- sound and music natives --------------------------------------------------------------------------

    /// <summary>
    /// Plays a sound at this actor (a <see cref="SimEventKind.PlaySound"/> event). A None sound plays nothing. Omitted
    /// volume and radius are the actor's TransientSoundVolume and TransientSoundRadius, an omitted pitch is 1.
    /// </summary>
    [UcNative("AActor::execPlaySound", Exe = 0x10a25e50)]
    [PortOf("nat:Engine.Actor.PlaySound", Spec = "spec/devices/sound-music.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 execPlaySound defaults for omitted volume, radius and pitch")]
    public void PlaySound(AssetRef Sound, ESoundSlot Slot = ESoundSlot.SLOT_None, float? Volume = null,
        bool bNoOverride = false, float? Radius = null, float? Pitch = null, bool Attenuate = true, Name BoneName = default)
    {
        if (Sound.IsNone || XLevel is null)
        {
            return;
        }

        XLevel.World.Events.PlaySound(this, new Name(Sound.Path), (int)Slot, Volume ?? TransientSoundVolume,
            Radius ?? TransientSoundRadius, Pitch ?? 1f, bNoOverride, Attenuate, BoneName, flat: IsViewedUnattenuated(Attenuate));
    }

    /// <summary>
    /// Rule 5: an unattenuated sound of the actor the local player views plays without 3D. A controller without a
    /// view target views itself (PlayerController.PostBeginPlay sets it so).
    /// </summary>
    private bool IsViewedUnattenuated(bool attenuate) =>
        !attenuate && Level?.GetLocalPlayerController() is { } viewer && ReferenceEquals(this, viewer.ViewTarget ?? viewer);

    /// <summary>
    /// PlaySound for a sound the owning client already played locally: a PlaySound event marked
    /// <see cref="SimEvent.Owned"/>, so views can tell it from the owner's own call (spec/native/audio.md rule 4).
    /// Attenuate defaults to false (rule 1).
    /// </summary>
    [UcNative("AActor::execPlayOwnedSound", Exe = 0x10a26120, Editor = 0x11189fb0)]
    [PortOf("nat:Engine.Actor.PlayOwnedSound", Spec = "spec/native/audio.md#actor-natives")]
    public void PlayOwnedSound(AssetRef Sound, ESoundSlot Slot = ESoundSlot.SLOT_None, float? Volume = null,
        bool bNoOverride = false, float? Radius = null, float? Pitch = null, bool Attenuate = false, Name BoneName = default)
    {
        if (Sound.IsNone || XLevel is null)
        {
            return;
        }

        XLevel.World.Events.PlaySound(this, new Name(Sound.Path), (int)Slot, Volume ?? TransientSoundVolume,
            Radius ?? TransientSoundRadius, Pitch ?? 1f, bNoOverride, Attenuate, BoneName, owned: true,
            flat: IsViewedUnattenuated(Attenuate));
    }

    /// <summary>
    /// Sets the surface type the next switch sound plays (UNIAudio keeps one value for all sounds and the next switch
    /// sound consumes it): a <see cref="SimEventKind.SetSwitch"/> event.
    /// </summary>
    [UcNative("AActor::execSetSwitch")]
    [PortOf("nat:Engine.Actor.SetSwitch", Spec = "spec/native/audio.md#actor-natives")]
    public void SetSwitch(byte Surface) => XLevel?.World.Events.SetSwitch(this, Surface);

    /// <summary>Stops a sound playing on this actor (a <see cref="SimEventKind.StopSound"/> event).</summary>
    [UcNative("AActor::execStopSound", Exe = 0x10a25720)]
    [PortOf("nat:Engine.Actor.StopSound", Spec = "spec/devices/sound-music.md#rules")]
    public void StopSound(AssetRef Sound)
    {
        if (Sound.IsNone || XLevel is null)
        {
            return;
        }

        XLevel.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.StopSound,
            ActorId = ActorId,
            What = new Name(Sound.Path),
            Location = Location,
            Rotation = Rotation,
        });
    }

    /// <summary>Starts a music track on this machine; it becomes the current music. Returns a song handle.</summary>
    [UcNative("AActor::execPlayMusic", Exe = 0x10a25c60)]
    [PortOf("nat:Engine.Actor.PlayMusic", Spec = "spec/devices/sound-music.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Handle numbering is not decoded; the script only stores it")]
    public int PlayMusic(AssetRef Song, float FadeInTime)
    {
        if (Song.IsNone)
        {
            return 0;
        }

        MusicState music = MusicState.Of(XLevel);
        music.Current = Song;
        music.Handle++;
        XLevel.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.PlayMusic,
            ActorId = ActorId,
            What = new Name(Song.Path),
            Radius = FadeInTime,
            Volume = 1f,
            Slot = music.Handle,
        });
        return music.Handle;
    }

    /// <summary>Stops a music track on this machine; the current music becomes None when it was that track.</summary>
    [UcNative("AActor::execStopMusic", Exe = 0x10a25d10)]
    [PortOf("nat:Engine.Actor.StopMusic", Spec = "spec/devices/sound-music.md#rules")]
    public void StopMusic(AssetRef Song, int SongHandle, float FadeOutTime)
    {
        MusicState music = MusicState.Of(XLevel);
        if (music.Current == Song)
        {
            music.Current = AssetRef.None;
        }

        XLevel.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.StopMusic,
            ActorId = ActorId,
            What = Song.IsNone ? Name.None : new Name(Song.Path),
            Radius = FadeOutTime,
            Slot = SongHandle,
        });
    }

    /// <summary>The music track playing on this machine, or None.</summary>
    [UcNative("AActor::execGetCurrentMusic", Exe = 0x10a25e20)]
    [PortOf("nat:Engine.Actor.GetCurrentMusic", Spec = "spec/devices/sound-music.md#rules")]
    public AssetRef GetCurrentMusic() => XLevel?.GetService<MusicState>()?.Current ?? AssetRef.None;

    /// <summary>
    /// A noise for AI hearing (spec/native/make-noise.md): server side only and only with an Instigator, dampened by
    /// the Instigator's SoundDampening when it made the noise itself (or owns the maker) and by 0.3 when its
    /// Visibility is below 2, then handed to the level's <see cref="INoiseHearing"/>. No SBase controller hears it,
    /// so without the bots' listener it has no effect, as in the original.
    /// </summary>
    [UcNative("AActor::execMakeNoise", Exe = 0x109b0150)]
    [PortOf("nat:Engine.Actor.MakeNoise", Spec = "spec/native/make-noise.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "execMakeNoise 0x109b0150: NetMode 3 and no Instigator skip; SoundDampening; Visibility < 2 x 0.3")]
    public void MakeNoise(float Loudness)
    {
        if (XLevel is not { } level || level.Info.NetMode == ENetMode.NM_Client || Instigator is not { } instigator)
        {
            return;
        }

        if (ReferenceEquals(Owner, instigator) || ReferenceEquals(instigator, this))
        {
            Loudness *= instigator.SoundDampening;
        }

        if (instigator.Visibility < NoiseVisibilityCut)
        {
            Loudness *= NoiseLowVisibilityScale;
        }

        level.GetService<INoiseHearing>()?.CheckNoiseHearing(this, instigator, Loudness);
    }

    [Provenance(ProvenanceKind.Decoded, "execMakeNoise 0x109b0150: Instigator.Visibility < 2")]
    private const byte NoiseVisibilityCut = 2;

    [Provenance(ProvenanceKind.Decoded, "execMakeNoise 0x109b0150: loudness x 0.3")]
    private const float NoiseLowVisibilityScale = 0.3f;

    // ---- collision, mesh and physics natives --------------------------------------------------------------

    /// <summary>Changes the physics mode (through the level's natives when installed).</summary>
    [UcNative("AActor::execSetPhysics", Exe = 0x10a2a920)]
    [PortOf("nat:Engine.Actor.SetPhysics", Spec = "spec/devices/mover.md#interpolation")]
    public void SetPhysics(EPhysics newPhysics)
    {
        if (Natives is { } natives)
        {
            natives.SetPhysics(this, newPhysics);
        }
        else
        {
            Physics = newPhysics;
        }
    }

    /// <summary>Changes how the actor is drawn.</summary>
    [UcNative("AActor::execSetDrawType", Exe = 0x10a24ee0)]
    [PortOf("nat:Engine.Actor.SetDrawType", Spec = "spec/devices/animated-mesh.md#rules")]
    public void SetDrawType(EDrawType NewDrawType) => DrawType = NewDrawType;

    /// <summary>Changes the static mesh; the collision follows the new mesh.</summary>
    [UcNative("AActor::execSetStaticMesh", Exe = 0x10a24ea0)]
    [PortOf("nat:Engine.Actor.SetStaticMesh", Spec = "spec/devices/gameplay-object.md#trigger-and-untrigger")]
    public void SetStaticMesh(AssetRef NewStaticMesh)
    {
        if (NewStaticMesh == StaticMesh)
        {
            return;
        }

        StaticMesh = NewStaticMesh;
        Natives?.SetStaticMesh(this, NewStaticMesh);
    }

    /// <summary>Changes the collision flags. An omitted flag keeps its current value.</summary>
    [UcNative("AActor::execSetCollision", Exe = 0x10a24bf0)]
    [PortOf("nat:Engine.Actor.SetCollision", Spec = "spec/devices/triggers.md#enginetrigger")]
    public void SetCollision(bool? NewColActors = null, bool? NewBlockActors = null, bool? NewBlockPlayers = null)
    {
        bool collide = NewColActors ?? bCollideActors;
        bool blockActors = NewBlockActors ?? bBlockActors;
        bool blockPlayers = NewBlockPlayers ?? bBlockPlayers;
        if (Natives is { } natives)
        {
            natives.SetCollision(this, collide, blockActors, blockPlayers);
        }
        else
        {
            bCollideActors = collide;
            bBlockActors = blockActors;
            bBlockPlayers = blockPlayers;
        }
    }

    /// <summary>Teleports the actor; false (and no move) when the spot is blocked.</summary>
    [UcNative("AActor::execSetLocation", Exe = 0x10a26e10)]
    [PortOf("nat:Engine.Actor.SetLocation", Spec = "spec/devices/mover.md#client-side-bookkeeping")]
    public bool SetLocation(FVector NewLocation)
    {
        if (Natives is { } natives)
        {
            return natives.SetLocation(this, NewLocation);
        }

        Location = NewLocation;
        return true;
    }

    /// <summary>When set, only pawns generate touches with this actor.</summary>
    [UcNative("AActor::execOnlyAffectPawns", Exe = 0x10a24ca0)]
    [PortOf("nat:Engine.Actor.OnlyAffectPawns", Spec = "spec/devices/triggers.md#enginetrigger")]
    public void OnlyAffectPawns(bool B) => bOnlyAffectPawns = B;

    /// <summary>
    /// Resolves a class reference with the level's <see cref="UcClassIndex"/> service (installed by the game library
    /// with every generated package), or with the Core and Engine packages when the level has none.
    /// </summary>
    protected UcClassInfo? FindClass(ClassRef Class) =>
        Class.IsNone ? null : (XLevel?.GetService<UcClassIndex>() ?? CoreClasses.Index).Find(Class);

    private static class CoreClasses
    {
        public static readonly UcClassIndex Index = new(CorePackage.Info, EnginePackage.Info);
    }

    /// <summary>True when <paramref name="TestClass"/> is <paramref name="ParentClass"/> or a subclass of it.</summary>
    [UcNative("UObject::execClassIsChildOf", Exe = 0x10949870)]
    [PortOf("nat:Core.Object.ClassIsChildOf", Spec = "spec/devices/triggers.md#enginetrigger")]
    protected static bool ClassIsChildOf(UcClassInfo? TestClass, ClassRef ParentClass)
    {
        if (ParentClass.IsNone)
        {
            return false;
        }

        for (UcClassInfo? c = TestClass; c is not null; c = c.Super)
        {
            if (string.Equals(c.Path, ParentClass.Path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
