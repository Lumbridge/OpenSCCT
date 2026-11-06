namespace OpenScct.Game.SBase;

public partial class SMineProxEmitter
{
    // The smoke particles can live for 8 seconds after the initial burst.
    private const float EffectLifetime = 9f;

    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();

        // The renderer runs the particles, so AutoDestroy alone cannot retire the actor in the simulation.
        // Send the stationary effect once per client and let each copy expire locally.
        bNetTemporary = true;
        LifeSpan = EffectLifetime;
    }
}
