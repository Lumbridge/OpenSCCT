using System;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Game.AI;

public sealed partial class SpyAi
{
    private const float AttentionSeconds = 0.5f;
    private const float AttentionRepeatSeconds = 1.3f;
    private const float HeardAttentionRange = 1200f;
    private const float GlimpseAttentionRange = 1600f;

    private float _attentionUntil = -1f;
    private float _nextAttention = -1f;
    private FVector _attentionPoint;

    private void AttendToContacts(float now, ref AiPlan plan)
    {
        if (Goal is not (GIdle or GHack or GObjective) || AtTerminal || Doors.Busy ||
            Bot.Gadgets.Active != SpyGadgetAct.None || Pawn.Physics != EPhysics.PHYS_Walking ||
            Follower.CurrentKind is not (null or NavEdgeKind.Walk or NavEdgeKind.Crouch) ||
            !CanGlance(plan.Keys))
        {
            _attentionUntil = -1f;
            return;
        }

        if (now >= _attentionUntil && now >= _nextAttention && AttentionContact(now) is { } contact)
        {
            // A quick look at the place we heard or glimpsed, using the remembered estimate rather than the live pawn.
            _attentionPoint = contact.Position;
            _attentionUntil = now + AttentionSeconds;
            _nextAttention = now + AttentionRepeatSeconds;
        }

        if (now < _attentionUntil)
        {
            LookAt(new FVector(_attentionPoint.X, _attentionPoint.Y, AiSight.Eye(Pawn).Z), ref plan);
        }
    }

    private static bool CanGlance(AiInput keys) => (keys.bRoll | keys.bUsing | keys.bFire | keys.bAltFire | keys.bFireMode | keys.bUseBackToWall) == 0 &&
        keys.Command1 == AiCommand.None && keys.Command2 == AiCommand.None && (keys.BT_Up != 0 || !keys.Moving);

    private AiContact? AttentionContact(float now)
    {
        AiContact? best = null;
        float bestScore = 0f;
        foreach (AiContact contact in Bot.Memory.Contacts)
        {
            // A recognized threat already has a tactical response. Let it keep control of the view.
            if (contact.Visible && contact.Confirmed)
            {
                return null;
            }

            bool glimpse = contact.Visible && contact.Spot >= 0.1f;
            bool heard = !contact.Visible && contact.Source == AiContactSource.Heard && contact.Confidence >= 0.42f;
            if ((!glimpse && !heard) || now - contact.Time > AttentionSeconds)
            {
                continue;
            }

            FVector to = contact.Position - Pawn.Location;
            float range = glimpse ? GlimpseAttentionRange : HeardAttentionRange;
            float distance = NavFollower.Horizontal(to);
            if (distance >= range || MathF.Abs(to.Z) > 200f)
            {
                continue;
            }

            float score = glimpse ? 1f + contact.Spot : contact.Confidence * (1f - distance / range);
            if (score > bestScore)
            {
                best = contact;
                bestScore = score;
            }
        }

        return best;
    }
}
