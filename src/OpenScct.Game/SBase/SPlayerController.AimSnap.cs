using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// The pad aim assist (spec/equipment/spy-shocker.md rule 17): the global PlayerTick's target snap, which the armed
// spy's view rotation applies (UpdateCamRotation, SPlayerController.Movement.Rotation.cs).
public partial class SPlayerController
{
    private static readonly Name NAME_BSpine1 = new("B Spine1");

    /// <summary>A state that replaces the global ComputeTargetDistance (s_PlayerCharging: the charge target lock).</summary>
    public interface IComputeTargetDistance
    {
        bool ComputeTargetDistance(SPlayerController self);
    }

    /// <summary>
    /// The target snap of the global PlayerTick (rule 17): for a merc, and for a spy with the pistol aimed
    /// (WeaponLoaded 2), bSnappedToTarget is ComputeTargetDistance's answer every tick.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/spy-shocker.md#rules")]
    private void SnapPlayerTick()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (SPawnType == PawnType.SPAWNDEFENSE || pawn.WeaponLoaded == 2)
        {
            bSnappedToTarget = ComputeTargetDistance();
        }
    }

    /// <summary>The global ComputeTargetDistance is the native snap; the charge state has its own.</summary>
    [Uc("SBase.SPlayerController.ComputeTargetDistance", Sha1 = "e9ad5cc98d24f6a44d9892b2ff664ffbd9c81fda")]
    [PortOf("uc:SBase.SPlayerController:ComputeTargetDistance", Spec = "spec/equipment/spy-shocker.md#rules")]
    public bool ComputeTargetDistance() =>
        StateNode is IComputeTargetDistance s ? s.ComputeTargetDistance(this) : NativeFindTargetToSnap();

    /// <summary>
    /// NativeFindTargetToSnap (rule 17): with the look input part way (0.1 to 0.82 of full), the first pawn of the
    /// other side, moving or seen while moving, whose B Spine1 lies within the 0.98 cone of the aim with a clear line,
    /// becomes TargetPawn, and fSnapAngleX/Y become the yaw and pitch toward it, clamped to 100 units scaled down from
    /// 400 uu to nothing at 2000 uu. A merc aims from his Location along MyDesiredRotation (the line lowered 10 uu), a
    /// spy from his pistol along the pistol's rotation.
    /// </summary>
    [UcNative("ASPlayerController::execNativeFindTargetToSnap", Exe = 0x10aab540, Editor = 0x111ee780)]
    [PortOf("nat:SBase.SPlayerController.NativeFindTargetToSnap", Spec = "spec/equipment/spy-shocker.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10aab540: window, side and speed tests, B Spine1 (name 0x220), the 0.98 cone, trace 0x286, fSnapCoef, the +-100 clamp, the vertical speed gate")]
    [Provenance(ProvenanceKind.Estimated, "The bone and the pistol come from the estimated poses (K.SnapSpineUp, CombatPorts.BoneCoords); the yaw and pitch differences are wrapped to a half turn")]
    public bool NativeFindTargetToSnap()
    {
        TargetPawn = null;
        if (!(fNormePadCam < K.SnapLookMax) || !(fNormePadCam > K.SnapLookMin) || Pawn is not { } own || XLevel is null)
        {
            return false;
        }

        bool merc = SPawnType == PawnType.SPAWNDEFENSE;
        FVector start;
        FRotator aim;
        if (merc)
        {
            start = own.Location;
            aim = MyDesiredRotation;
        }
        else if (SBPawn?.MyWeapon is { } weapon)
        {
            FCoords pistol = CombatServices.Ports(this).BoneCoords(weapon, NAME_BSpine1);
            start = pistol.Origin;
            aim = pistol.XAxis.Rotation();
        }
        else
        {
            return false;
        }

        FVector axis = aim.Vector();
        bool ownMoving = own.Velocity.SizeSquared() > K.SnapMovingSpeedSquared;
        ICollisionWorld? world = TraceHitZones.CollisionOf(this);
        foreach (Pawn p in XLevel.DynamicActors<Pawn>())
        {
            if (p is not SBasePawn target || target.SPawnType == SPawnType ||
                (!(target.Velocity.SizeSquared() > K.SnapMovingSpeedSquared) && !ownMoving))
            {
                continue;
            }

            FVector bone = target.Location + new FVector(0f, 0f, target.CollisionHeight * K.SnapSpineUp);
            FVector line = bone - start;
            if (merc)
            {
                line.Z -= K.SnapMercDrop;
            }

            if (!((axis | line.SafeNormal()) > K.SnapCone))
            {
                continue;
            }

            // Flags 0x286 trace no pawns: a hit is the level or a mover, never the target.
            if (world is not null && !world.FastTrace(bone, start))
            {
                continue;
            }

            float d = SvmMath.Clamp(line.Size(), K.SnapNear, K.SnapFar);
            fSnapCoef = 1f - ((d - K.SnapNear) * K.SnapFalloff);
            float step = K.SnapMaxStep * fSnapCoef;
            FRotator toward = line.Rotation();
            fSnapAngleX = SvmMath.Clamp((short)(toward.Yaw - aim.Yaw), -step, step);
            fSnapAngleY = SvmMath.Abs(SvmMath.Trunc(target.Velocity.Z)) >= K.SnapVerticalSpeed
                ? SvmMath.Clamp((short)(toward.Pitch - aim.Pitch), -step, step)
                : 0f;
            TargetPawn = target;
            return true;
        }

        return false;
    }
}
