using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.PlayerController sound calls from the server: a sound the server played that this player hears
// (ClientHearSound, spec/native/audio.md rule 5) and an interface sound for this player (ClientPlaySound, rule 29).
// Both run on the player's own machine and raise the presentation event there.
public partial class PlayerController
{
    /// <summary>
    /// The server played <paramref name="S"/> where this player hears it: a PlaySound event on this machine, at the
    /// actor when it is relevant here, else at <paramref name="SoundLocation"/>. <paramref name="Parameters"/> carries
    /// the volume and the pitch in thousandths and the radius (0 is heard as 80); <paramref name="Id"/> the slot
    /// and bNoOverride (ActorIndex x 16 + Slot x 2 + bNoOverride). The event is marked <see cref="SimEvent.Heard"/>.
    /// <paramref name="BoneIdx"/> (the bone of the actor's mesh the sound follows) is not used: the sound follows the actor.
    /// </summary>
    [UcNative("APlayerController::execClientHearSound", Exe = 0x10a22ee0, Editor = 0x11189020)]
    [PortOf("nat:Engine.PlayerController.ClientHearSound", Spec = "spec/native/audio.md#actor-natives")]
    public void ClientHearSound(Actor? Actor, int Id, AssetRef S, FVector SoundLocation, FVector Parameters, bool Attenuate,
        int BoneIdx = 0)
    {
        if (S.IsNone || XLevel is null)
        {
            return;
        }

        Actor? source = Actor is { bDeleteMe: false } ? Actor : null;
        XLevel.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.PlaySound,
            ActorId = source?.ActorId ?? 0,
            What = new Name(S.Path),
            Location = source?.Location ?? SoundLocation,
            Rotation = source?.Rotation ?? default,
            Slot = (Id >> 1) & 7,
            NoOverride = (Id & 1) != 0,
            Volume = Parameters.X / SoundK.HearParameterScale,
            Radius = Parameters.Y == 0f ? SoundK.HearRadiusForZero : Parameters.Y,
            Pitch = Parameters.Z / SoundK.HearParameterScale,
            NoAttenuate = !Attenuate,
            Heard = true,
            Flat = !Attenuate && source is not null && ReferenceEquals(source, ViewTarget ?? this),
        });
    }

    /// <summary>
    /// An interface sound for this player (the spawn sound, the shock sounds): played at the view target, unattenuated,
    /// at 0.9 or, with <paramref name="bVolumeControl"/>, at <paramref name="inAtten"/> clamped to [0, 1].
    /// </summary>
    [Uc("Engine.PlayerController.ClientPlaySound", Sha1 = "b7f1b9e6c512047383671164ab280ab13b5cae4b")]
    [PortOf("uc:Engine.PlayerController:ClientPlaySound", Spec = "spec/native/audio.md#controller-sound-calls")]
    public virtual void ClientPlaySound(AssetRef ASound, bool bVolumeControl = false, float inAtten = 0f,
        ESoundSlot Slot = ESoundSlot.SLOT_None)
    {
        float atten = SoundK.ClientPlaySoundVolume;
        if (bVolumeControl)
        {
            atten = System.Math.Clamp(inAtten, 0f, 1f);
        }

        // PlayerController.PostBeginPlay makes the controller its own view target until it has another; that port
        // is not written, so a controller without one plays the sound at itself, as the original's would.
        (ViewTarget ?? this).PlaySound(ASound, Slot, atten, Attenuate: false);
    }

    /// <summary>
    /// The server's music for this player: with no transition the song plays once as an interface sound
    /// (ClientPlaySound at volume 1 on SLOT_Talk); otherwise it becomes the music, fading in over 3 s, and is
    /// remembered as <see cref="Song"/> and <see cref="Transition"/>.
    /// </summary>
    [Uc("Engine.PlayerController.ClientSetMusic", Sha1 = "f26ba03876a39d1222fb6cb117a283c13e06f969")]
    [PortOf("uc:Engine.PlayerController:ClientSetMusic", Spec = "spec/native/audio.md#controller-sound-calls")]
    public virtual void ClientSetMusic(AssetRef NewSong, EMusicTransition NewTransition)
    {
        if (NewTransition == EMusicTransition.MTRAN_None)
        {
            ClientPlaySound(NewSong, true, 1f, ESoundSlot.SLOT_Talk);
            return;
        }

        PlayMusic(NewSong, SoundK.MusicFadeSeconds);
        Song = NewSong;
        Transition = NewTransition;
    }
}