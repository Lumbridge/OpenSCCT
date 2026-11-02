using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Combat;

/// <summary>Presentation of protection from the spy's shocker, independent of the active electric stun.</summary>
public readonly record struct ShockImmunityView(bool Active, bool StateProtected, float RemainingSeconds, float Fraction);

/// <summary>The authoritative blockers and the existing timer; state protection has no invented deadline.</summary>
public readonly record struct ShockImmunityState(bool TimerBlocked, bool StateProtected, float ExpiresAt, float Duration)
{
    public bool Active => TimerBlocked || StateProtected;

    public ShockImmunityView At(float gameSeconds)
    {
        if (!Active) return default;
        float remaining = TimerBlocked ? Math.Clamp(ExpiresAt - gameSeconds, 0f, Math.Max(0f, Duration)) : 0f;
        return new(true, StateProtected, remaining, Duration > 0f ? Math.Clamp(remaining / Duration, 0f, 1f) : 0f);
    }
}

/// <summary>Read-only description of the gates that reject an ordinary spy shock. Never changes combat state.</summary>
public static class MercShockImmunity
{
    /// <summary>Keep the exact subtraction and strict comparison used by Checkshock, including equality.</summary>
    public static bool TimerBlocks(SPlayerController controller) =>
        !(controller.Level.TimeSeconds - controller.TimeShock > controller.RealTimeInvulSticky);

    public static bool VisiblePawn(SBasePawn pawn) => pawn.SPawnType == PawnType.SPAWNDEFENSE &&
        pawn.Health > 0 && !pawn.bDeleteMe && !pawn.bHidden && PlayRules.Of(pawn).PawnsTakeDamage;

    public static ShockImmunityState Capture(SBasePawn pawn)
    {
        if (!VisiblePawn(pawn) || pawn.Controller is not SPlayerController controller ||
            !ReferenceEquals(controller.Pawn, pawn)) return default;
        Name state = controller.GetStateName();
        // Play Level enters gameplay directly without setting bMatchHasBegun. The controller's waiting state,
        // rather than that match flag, distinguishes a lobby pawn from a playable practice pawn.
        if (state == new Name("s_PlayerWaiting") || state == new Name("WaitingForPawn") ||
            state == new Name("GameEnded") || state == new Name("s_Cinematic") ||
            state == new Name("s_BaseSpectating") || state == new Name("s_MultiSpectating") ||
            state == new Name("s_MultiSpectating_WhenKilled") || state == new Name("s_Spectating_Visitor")) return default;
        bool blocked = CombatDispatch.DamageHandlerFor(state) is DamageHandler.Blast or DamageHandler.GrabBlastOnly or
            DamageHandler.FallDown or DamageHandler.Ignored ||
            PracticeRules.Of(controller)?.IgnoresDamage(controller, SDamageType.SDTStickyShocker) == true;
        bool timer = TimerBlocks(controller);
        return new(timer, blocked, timer ? controller.TimeShock + controller.RealTimeInvulSticky : 0f,
            timer ? controller.RealTimeInvulSticky : 0f);
    }
}
