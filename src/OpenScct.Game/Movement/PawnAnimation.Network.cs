using System;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Movement;

public sealed partial class PawnAnimation
{
    private AuthoredChange TrackAuthoredChange(int channel, bool phase = false, bool procedural = false)
    {
        // The walk blender's copies and continuous cycle/rate edits are reconstructed from snapshots.
        // Explicit plays, clears and descriptor changes on those same channels still enter the journal.
        if (!IsValidChannel(channel) || (procedural && channel is >= 2 and <= 13) ||
            Owner is not SBasePawn pawn || pawn.XLevel is not { } level || level.World.IsReplaying ||
            level.GetService<IPawnAnimationTransitions>() is not { } observer) return default;
        return new AuthoredChange(pawn, observer, channel, phase);
    }

    private readonly struct AuthoredChange : IDisposable
    {
        private readonly SBasePawn? _pawn;
        private readonly IPawnAnimationTransitions? _observer;
        private readonly int _channel;
        private readonly bool _phase;
        private readonly NetworkAnimChannel _before;

        public AuthoredChange(SBasePawn pawn, IPawnAnimationTransitions observer, int channel, bool phase)
        {
            _pawn = pawn; _observer = observer; _channel = channel; _phase = phase;
            PawnAnimChannel c = pawn.PawnAnim[channel];
            _before = c.NetworkChangeDepth++ == 0 ? NetworkAnimChannel.Capture(c) : default;
        }

        public void Dispose()
        {
            if (_pawn is not { } pawn || --pawn.PawnAnim[_channel].NetworkChangeDepth != 0) return;
            NetworkAnimChannel after = NetworkAnimChannel.Capture(pawn.PawnAnim[_channel]);
            if (!_before.SamePlay(after) || (_phase && (_before.Frame != after.Frame || _before.Rate != after.Rate)))
                _observer!.Authored(pawn, _channel, after);
        }
    }
}
