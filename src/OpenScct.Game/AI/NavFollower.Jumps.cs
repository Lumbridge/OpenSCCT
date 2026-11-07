using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

public sealed partial class NavFollower
{
    private NavCommand TraverseJump(SBasePawn pawn, float now, int viewYaw, NavStep step, NavLink link)
    {
        var cmd = new NavCommand { Busy = true, Crouched = false, Tolerance = 1500, Yaw = link.Yaw };
        FVector direction = YawVector(link.Yaw);
        if (_phase == TPhase.None)
        {
            _crossed = false;
            Enter(TPhase.Approach, now);
        }
        if (_phase == TPhase.Approach)
        {
            FVector runup = link.Start - direction * 80f;
            if (Horizontal(runup - pawn.Location) > 12f)
            {
                cmd.Yaw = YawTo(pawn.Location, runup);
                cmd.Keys.BT_Up = 1;
                if (now - _phaseAt > 6f) return Fail(pawn, link, step);
                return cmd;
            }
            Enter(TPhase.Face, now);
        }
        if (_phase == TPhase.Face)
        {
            if (pawn.bIsCrouched || !Still(pawn) || YawError(viewYaw, link.Yaw) > 300)
            {
                if (now - _phaseAt > 4f) return Fail(pawn, link, step);
                return cmd;
            }
            Enter(TPhase.RunUp, now);
        }
        if (_phase == TPhase.RunUp)
        {
            if (PawnNear(pawn, AiK.PawnNoJumpGap) || TeammateBelow(pawn, link.End))
            {
                if (now - _phaseAt > 4f) return Fail(pawn, link, step);
                return cmd;
            }
            cmd.Keys.BT_Up = 1;
            cmd.Keys.FastSpeed = true;
            if (((pawn.Location - link.Start) | direction) >= -12f)
            {
                if (pawn.Physics != EPhysics.PHYS_Walking || Speed(pawn) < Graph.Profile.JumpSpeed * 0.7f)
                    return Fail(pawn, link, step);
                cmd.Keys.Press(AiCommand.Jump);
                Enter(TPhase.Air, now);
            }
            else if (now - _phaseAt > 3f) return Fail(pawn, link, step);
            return cmd;
        }
        if (_phase == TPhase.Air)
        {
            cmd.Tolerance = 65536;
            cmd.Keys.FastSpeed = true;
            // Keep the checked heading through flight, then absorb momentum on the supported run-out.
            cmd.Keys.BT_Up = 1;
            _crossed |= pawn.Physics == EPhysics.PHYS_Falling;
            if (pawn.Physics == EPhysics.PHYS_Walking && now - _phaseAt > 0.25f)
            {
                bool reached = ((pawn.Location - link.Start) | direction) >=
                    ((link.End - link.Start) | direction) - Graph.Profile.Radius;
                return _crossed && reached ? Done(pawn, link, step) : Fail(pawn, link, step);
            }
            if (now - _phaseAt > 2f) return Fail(pawn, link, step);
        }
        return cmd;
    }
}
