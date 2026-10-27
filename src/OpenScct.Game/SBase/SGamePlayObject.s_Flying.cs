using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SGamePlayObject
{
    /// <summary>Pushed or thrown: falling with spin, bouncing off what it hits, hurting pawns it runs into fast.</summary>
    [Uc("SBase.SGamePlayObject.s_Flying")]
    public class s_Flying : UcActorState
    {
        public static readonly s_Flying Instance = new(UcMeta.s_Flying, null);

        protected s_Flying(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SGamePlayObject.s_Flying.BeginState", Sha1 = "ba069a86b552e62be2db759f9e55d27a46d2a4a3")]
        [PortOf("uc:SBase.SGamePlayObject:s_Flying.BeginState", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
        public override void BeginState(Actor self)
        {
            var gpo = (SGamePlayObject)self;
            gpo.bCollideWorld = true;
            gpo.bBounce = true;
            gpo.bFixedRotationDir = true;
            if (SvmMath.Abs(gpo.RotationRate.Pitch) < DeviceK.FlyingSpinFloor)
            {
                gpo.RotationRate.Pitch = DeviceK.FlyingSpinFloor;
            }

            if (SvmMath.Abs(gpo.RotationRate.Roll) < DeviceK.FlyingSpinFloor)
            {
                gpo.RotationRate.Roll = DeviceK.FlyingSpinFloor;
            }

            gpo.SetCollision(true, true, gpo.InitialBlockPlayers);
            gpo.SetPhysics(EPhysics.PHYS_Falling);
        }

        [Uc("SBase.SGamePlayObject.s_Flying.Landed", Sha1 = "edd881a7d7d0060d9715201503f9c4ce5e8256c8")]
        [PortOf("uc:SBase.SGamePlayObject:s_Flying.Landed", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
        public override void Landed(Actor self, FVector HitNormal) => self.eventHitWall(HitNormal, null);

        /// <summary>A non-pawn sends it back with some jitter; a pawn hit fast enough takes bump damage.</summary>
        [Uc("SBase.SGamePlayObject.s_Flying.Bump", Sha1 = "b0369a952578f38c47b9345b2ccd903ce1e00180")]
        [PortOf("uc:SBase.SGamePlayObject:s_Flying.Bump", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
        public override void Bump(Actor self, Actor Other)
        {
            var gpo = (SGamePlayObject)self;
            if (Other is not Pawn pawn)
            {
                gpo.Velocity = -gpo.Velocity;
                gpo.Velocity.X += DeviceK.BumpJitter * gpo.FRand() - DeviceK.BumpJitter / 2f;
                gpo.Velocity.Y += DeviceK.BumpJitter * gpo.FRand() - DeviceK.BumpJitter / 2f;
                return;
            }

            float speed = gpo.Velocity.Size();
            if (speed < DeviceK.BumpMinSpeed)
            {
                return;
            }

            float bumpDamage = speed * gpo.Mass / DeviceK.BumpDamageDivisor;
            SBasePawn? damageInstigator = gpo.Owner as SBasePawn ?? (gpo.Owner as Controller)?.Pawn as SBasePawn;
            if (bumpDamage > DeviceK.BumpDamageMin)
            {
                (pawn.Controller as SPlayerController)?.PawnTakeDamage(SvmMath.Trunc(bumpDamage), damageInstigator, gpo.Location,
                    gpo.Velocity, SDamageType.SDTBase);
            }
        }

        [Uc("SBase.SGamePlayObject.s_Flying.HitWall", Sha1 = "7bf45a733f732b062ef6d874ceac5274cb7f52a2")]
        [PortOf("uc:SBase.SGamePlayObject:s_Flying.HitWall", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
        public override void HitWall(Actor self, FVector HitNormal, Actor? Wall) => ((SGamePlayObject)self).NativeHitWall(HitNormal, Wall);
    }
}
