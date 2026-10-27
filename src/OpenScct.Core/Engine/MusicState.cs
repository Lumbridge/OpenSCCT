using OpenScct.Core.Uc;

namespace OpenScct.Core.Engine;

/// <summary>
/// The music this machine is playing (the audio subsystem's current track), read by GetCurrentMusic. One per level,
/// kept as a level service so every actor sees the same value.
/// </summary>
public sealed class MusicState
{
    /// <summary>The track playing, or None.</summary>
    public AssetRef Current;

    /// <summary>Handle of the last PlayMusic.</summary>
    public int Handle;

    /// <summary>The level's music state, created on first use.</summary>
    public static MusicState Of(Level level)
    {
        MusicState? state = level.GetService<MusicState>();
        if (state is null)
        {
            state = new MusicState();
            level.SetService(state);
        }

        return state;
    }
}
