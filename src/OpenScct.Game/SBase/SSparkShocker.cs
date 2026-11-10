namespace OpenScct.Game.SBase;

public partial class SSparkShocker
{
    // Eight sparks at eight a second, each living 0.8 s at most: all are gone 1.8 s after the hit.
    private const float EffectLifetime = 2f;

    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();

        // The renderer runs the particles, so AutoDestroy alone cannot retire the actor in the simulation. A device
        // hit by the Shocker spawns this as an actor (SBasicGamePlayObject.TakeDamage); left alive, its blinking
        // light stayed on the device (a shot tripwire emitter) for the rest of the round.
        LifeSpan = EffectLifetime;
    }
}
