namespace OpenScct.Core.Engine;

/// <summary>
/// The level's hearing of MakeNoise noises (AActor::CheckNoiseHearing, spec/native/make-noise.md rules 4 to 6). The
/// original game has no listener (no SBase controller implements HearNoise); the rewrite's bots install one as a level
/// service. Without it a noise reaches nobody, as in the original.
/// </summary>
public interface INoiseHearing
{
    /// <summary>
    /// A noise of <paramref name="Loudness"/> (already dampened, rules 2 and 3) made by <paramref name="NoiseMaker"/>
    /// for its <paramref name="Instigator"/>.
    /// </summary>
    void CheckNoiseHearing(Actor NoiseMaker, Pawn Instigator, float Loudness);
}
