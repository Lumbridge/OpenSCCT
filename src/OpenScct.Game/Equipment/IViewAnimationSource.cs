using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Game.Equipment;

public interface IViewAnimationSource
{
    ClipData Resolve(Actor owner, Name sequence, ClipData fallback);
    void Advance(Actor owner, Name sequence, float from, float to, bool includeStart, bool backward, bool loop);
}
