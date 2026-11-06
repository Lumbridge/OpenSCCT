using System;
using Godot;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>Fits the stock two-handed grip to the camera-relative P190 once at load.</summary>
internal static class SpyFirstPersonRig
{
    private static readonly Vector3 s_supportUpperArm = new Vector3(-.83f, -.52f, -.20f).Normalized();

    public static bool TryPose(SkeletalModel model, Transform3D camera, Transform3D ready,
        out Transform3D weapon, out float supportError)
    {
        weapon = ready;
        supportError = -1f;
        var skeleton = model.Skeleton;
        var left = new Arm(skeleton, "L");
        var right = new Arm(skeleton, "R");
        if (!left.Valid || !right.Valid || !model.Sampler.Play("WaitStSpFd2", loop: false, frame: .25f))
            return false;

        model.Pose();
        // The world P190 mounts on the right hand. Capture the stock support wrist and elbow in that frame.
        Transform3D toGun = model.CurrentBonePose(right.Wrist).AffineInverse();
        Transform3D supportWrist = toGun * model.CurrentBonePose(left.Wrist);
        Vector3 supportElbow = toGun * model.CurrentBonePose(left.Forearm).Origin;
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string name = skeleton.GetBoneName(i);
            Quaternion rotation = skeleton.GetBonePoseRotation(i);
            skeleton.ResetBonePose(i);
            if (name.StartsWith("B L Finger", StringComparison.Ordinal) || name.StartsWith("B R Finger", StringComparison.Ordinal))
                skeleton.SetBonePoseRotation(i, rotation);
        }

        Transform3D toSkeleton = skeleton.GlobalTransform.AffineInverse() * camera;
        Transform3D toCamera = toSkeleton.AffineInverse();
        weapon = toCamera * right.Solve(toSkeleton * new Vector3(-.105f, -.265f, .195f),
            toSkeleton.Basis * new Vector3(-.32f, .58f, .74f), toSkeleton * ready);
        Transform3D wrist = weapon * supportWrist;
        Vector3 elbow = wrist.Origin + ((weapon * supportElbow) - wrist.Origin).Normalized() * left.ForeLength;
        left.Solve(toSkeleton * (elbow + s_supportUpperArm * left.UpperLength), toSkeleton.Basis * -s_supportUpperArm,
            toSkeleton * wrist);
        supportError = (toCamera * model.CurrentBonePose(left.Wrist).Origin).DistanceTo(wrist.Origin);
        return true;
    }

    private static void SetPose(Skeleton3D skeleton, int bone, Transform3D pose)
    {
        skeleton.SetBonePosePosition(bone, pose.Origin);
        skeleton.SetBonePoseRotation(bone, pose.Basis.GetRotationQuaternion().Normalized());
    }

    private sealed class Arm(Skeleton3D skeleton, string side)
    {
        private readonly int _clavicle = skeleton.FindBone($"B {side} Clavicle");
        private readonly int _upper = skeleton.FindBone($"B {side} UpperArm");

        public int Forearm { get; } = skeleton.FindBone($"B {side} ForeArm");
        public int Wrist { get; } = skeleton.FindBone($"B {side} Hand");
        public bool Valid => _clavicle >= 0 && _upper >= 0 && Forearm >= 0 && Wrist >= 0;
        public float UpperLength => skeleton.GetBoneRest(Forearm).Origin.Length();
        public float ForeLength => skeleton.GetBoneRest(Wrist).Origin.Length();

        public Transform3D Solve(Vector3 shoulder, Vector3 pole, Transform3D target)
        {
            Transform3D clavicleRest = skeleton.GetBoneGlobalRest(_clavicle);
            Basis upperRest = skeleton.GetBoneGlobalRest(_upper).Basis.Orthonormalized();
            Basis foreRest = skeleton.GetBoneRest(Forearm).Basis.Orthonormalized();
            Basis handRest = skeleton.GetBoneRest(Wrist).Basis.Orthonormalized();
            float upperLength = UpperLength;
            float foreLength = ForeLength;
            Vector3 delta = target.Origin - shoulder;
            Vector3 direction = delta.Normalized();
            float distance = Mathf.Clamp(delta.Length(), .04f, upperLength + foreLength - .003f);
            Vector3 bend = (pole - direction * pole.Dot(direction)).Normalized();
            float along = (upperLength * upperLength - foreLength * foreLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 elbow = shoulder + direction * along + bend * height;
            Vector3 wrist = shoulder + direction * distance;
            Basis upper = Aim(upperRest, (elbow - shoulder).Normalized());
            Basis fore = Aim(upper * foreRest, (wrist - elbow).Normalized());

            // Carry most of the wrist's roll through the forearm to keep the sleeve from twisting sharply.
            Vector3 axis = fore.X;
            Basis wristBasis = target.Basis.Orthonormalized();
            Vector3 from = (fore * handRest).Y.Slide(axis).Normalized();
            Vector3 to = wristBasis.Y.Slide(axis).Normalized();
            if (from.LengthSquared() > .5f && to.LengthSquared() > .5f)
                fore = new Basis(axis, from.SignedAngleTo(to, axis) * .70f) * fore;

            var clavicle = new Transform3D(clavicleRest.Basis,
                shoulder - clavicleRest.Basis * skeleton.GetBoneRest(_upper).Origin);
            var upperPose = new Transform3D(upper, shoulder);
            var forePose = new Transform3D(fore, elbow);
            var handPose = new Transform3D(wristBasis, wrist);
            int parent = skeleton.GetBoneParent(_clavicle);
            Transform3D spine = parent >= 0 ? skeleton.GetBoneGlobalRest(parent) : Transform3D.Identity;
            SetPose(skeleton, _clavicle, spine.AffineInverse() * clavicle);
            SetPose(skeleton, _upper, clavicle.AffineInverse() * upperPose);
            SetPose(skeleton, Forearm, upperPose.AffineInverse() * forePose);
            SetPose(skeleton, Wrist, forePose.AffineInverse() * handPose);
            return handPose;
        }

        private static Basis Aim(Basis basis, Vector3 direction) => new Basis(new Quaternion(basis.X, direction)) * basis;
    }
}
