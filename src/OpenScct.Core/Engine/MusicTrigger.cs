using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.MusicTrigger: starts (Trigger) or stops (UnTrigger) a music track on every machine
/// (spec/devices/sound-music.md).
/// </summary>
public partial class MusicTrigger
{
    [Uc("Engine.MusicTrigger.PreBeginPlay", Sha1 = "12c2aa385057837ca84d279648175aa2677b2646")]
    [PortOf("uc:Engine.MusicTrigger:PreBeginPlay", Spec = "spec/devices/sound-music.md#rules")]
    protected internal override void PreBeginPlay() => SetDrawType(EDrawType.DT_None);

    /// <summary>
    /// Plays the song when it is not the current music and this is a Trigger; otherwise stops it on an UnTrigger,
    /// or on a Trigger when toggling. With bInstigatorOnly only the instigator's own machine reacts. Replicated to
    /// every client from the authority (the "to all clients" function flag); here it runs where it is called.
    /// </summary>
    [Uc("Engine.MusicTrigger.ClientPlayStopMusic", Sha1 = "10ca9275d261fc9d1d18e6308da92c3b5a8c630c")]
    [PortOf("uc:Engine.MusicTrigger:ClientPlayStopMusic", Spec = "spec/devices/sound-music.md#rules")]
    public void ClientPlayStopMusic(bool bTrigger, Pawn? EventInstigator)
    {
        if (bInstigatorOnly && !ReferenceEquals(Level.LocalPlayerController?.Pawn, EventInstigator))
        {
            return;
        }

        if (GetCurrentMusic() != Song && bTrigger)
        {
            PlayMusic(Song, FadeInTime);
        }
        else if (bToggle || !bTrigger)
        {
            StopMusic(Song, 0, FadeOutTime);
        }
    }

    [Uc("Engine.MusicTrigger.Trigger", Sha1 = "97217e1ba2c306e327246cff2544e462344a47e2")]
    [PortOf("uc:Engine.MusicTrigger:Trigger", Spec = "spec/devices/sound-music.md#rules")]
    protected internal override void Trigger(Actor? Other, Pawn? EventInstigator) => ClientPlayStopMusic(true, EventInstigator);

    [Uc("Engine.MusicTrigger.UnTrigger", Sha1 = "f025d006bd8f6997a9d0135b227ddccfd73e94d1")]
    [PortOf("uc:Engine.MusicTrigger:UnTrigger", Spec = "spec/devices/sound-music.md#rules")]
    protected internal override void UnTrigger(Actor? Other, Pawn? EventInstigator) => ClientPlayStopMusic(false, EventInstigator);
}
