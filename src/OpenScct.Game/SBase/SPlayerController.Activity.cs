using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

public partial class SPlayerController
{
    // The owner decides when its cough becomes a fall. The server independently observes gas exposure so a
    // generic fall RPC cannot turn an old smoke attribution into a fresh sleep event. Use world time here:
    // accepted network commands temporarily install their older input clock on Level.TimeSeconds.
    private const float GasSleepDeliveryGrace = 0.75f;
    private SBasePawn? _gasActivityPawn;
    private PlayerReplicationInfo? _gasActivityInstigator;
    private float _gasActivityLastExposure;
    private float _gasActivityExpires;
    private float _gasActivityFaintAt;
    private int _gasActivitySwitch;

    private readonly record struct GasSleepNotice(int Switch, PlayerReplicationInfo? Instigator, PlayerReplicationInfo? Victim);

    private bool CanObserveGasSleep() => Role == ENetRole.ROLE_Authority &&
        SBPawn is { Health: > 0, bGasMask: false, bSleeping: false, bLyingOnGround: false } pawn &&
        (pawn.SPawnType == PawnType.SPAWNDEFENSE || StateNode is s_GrabbingPlayer) &&
        StateNode is not s_Inactive && StateNode is not s_GrabbedByPlayer &&
        !CombatDispatch.IgnoresSmoke(GetStateName()) &&
        GetStateName() != CNAME_s_FallDropAttacked && GetStateName() != DNAME_s_FallFallDropAttacked &&
        XLevel.WorldTimeSeconds - fLastTimeWakeUp > K.SmokeWakeGrace;

    /// <summary>Records actual server-observed exposure without driving the owner's cough, input or state.</summary>
    internal void ObserveGasActivity(Actor source, float coughTime, float faintTime, bool stickyCamera)
    {
        if (!CanObserveGasSleep())
        {
            ClearGasActivity();
            return;
        }

        float now = XLevel.WorldTimeSeconds;
        float deadline = now + MathF.Max(0f, faintTime);
        bool newEpisode = !ReferenceEquals(_gasActivityPawn, SBPawn) || now > _gasActivityExpires;
        if (newEpisode || deadline < _gasActivityFaintAt ||
            (deadline == _gasActivityFaintAt && stickyCamera && _gasActivitySwitch != K.MsgStickyCameraSleep))
        {
            // Credit the source that set the effective faint deadline, regardless of emitter iteration order.
            _gasActivityFaintAt = deadline;
            _gasActivityInstigator = source.Instigator?.PlayerReplicationInfo;
            _gasActivitySwitch = stickyCamera ? K.MsgStickyCameraSleep : K.MsgSmokeSleep;
        }

        _gasActivityPawn = SBPawn;
        _gasActivityLastExposure = now;
        _gasActivityExpires = now + MathF.Max(0f, coughTime);
        if (stickyCamera)
        {
            StickyCamInstigator = source.Instigator;
            StickyCamInstigatorTime = now;
        }
        else
        {
            SmokeInstigator = source.Instigator;
            SmokeInstigatorTime = now;
        }
    }

    private GasSleepNotice? TakeGasSleepNotice()
    {
        float now = XLevel.WorldTimeSeconds;
        if (!CanObserveGasSleep() || !ReferenceEquals(_gasActivityPawn, SBPawn) ||
            now < _gasActivityFaintAt || now - _gasActivityLastExposure > GasSleepDeliveryGrace)
        {
            return null;
        }

        var notice = new GasSleepNotice(_gasActivitySwitch, _gasActivityInstigator, PlayerReplicationInfo);
        ClearGasActivity();
        return notice;
    }

    private void PublishGasSleepNotice(GasSleepNotice? notice)
    {
        if (notice is { } sleep && SBPawn is { Health: > 0 })
        {
            Level.Game?.BroadcastLocalizedMessage(SBasePawn.GameMessagesClass, sleep.Switch, sleep.Instigator, sleep.Victim);
        }
    }

    private void ClearGasActivity()
    {
        _gasActivityPawn = null;
        _gasActivityInstigator = null;
        _gasActivitySwitch = 0;
    }

    private static bool IsGasFallTransition(Name state, Name label) =>
        (state == CNAME_s_FallDown && label == CNAME_DIE_InPlace) || state == CNAME_s_FallDropAttacked;

    private void ResetGasActivityAfterStateChange(Name state)
    {
        if (CombatDispatch.IgnoresSmoke(state) || state == CNAME_s_FallDropAttacked || state == DNAME_s_FallFallDropAttacked ||
            SBPawn is not { Health: > 0 })
        {
            ClearGasActivity();
        }
    }
}
