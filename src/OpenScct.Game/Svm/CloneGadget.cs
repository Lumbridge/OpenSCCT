using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Svm;

public sealed class CloneGadget : SpyDeployableGadget
{
    public const string ClassPath = "Svm.CloneGadget";
    public static readonly UcClassInfo ClassInfo = GadgetClass<CloneGadget>(ClassPath);

    private long _lastDeploymentTick = -1;
    public const float RechargeSeconds = 60f;
    private float _rechargeUntil;
    // Remaining time is replicated, so owner HUDs do not subtract clocks from different worlds.
    public float RechargeLeft;
    public float RechargeRemaining => RechargeLeft;
    private bool Ready => Charge > 0 && RechargeRemaining <= 0f;

    public override UcClassInfo GetUcClass() => ClassInfo;

    protected override void SetDefaults()
    {
        base.SetDefaults();
        ItemName = ItemLongName = "Clone";
        ItemDescription = "Send a copy of yourself running forward. A Merc shot makes it flash Mercs who are looking at it. Recharges in 60 seconds.";
        MagmaIconName = "A_capeInvisible";
        IconU = 128;
        IconV = 64;
        Charge = 1;
        CanQuickLaunch = true;
    }

    public override void Use(float value)
    {
        if (!Ready || OwnerPawn?.Controller is not SPlayerController pc) return;

        if (!GadgetNet.ToServer(this, pc, new CombatCall(new Name("DeployClone")) { Target = this, Rotation = pc.Rotation }))
            Deploy(pc, pc.Rotation.Yaw);
    }

    internal void Deploy(SPlayerController pc, int yaw)
    {
        if (!Ready || _lastDeploymentTick == XLevel.TickCount || !CanDeploy(pc) || OwnerPawn is not SPawnAttaque spy ||
            spy.Physics != EPhysics.PHYS_Walking) return;

        var heading = new FRotator(0, yaw & 65535, 0);
        var clone = new SpyClone();
        clone.CopyAppearance(spy);
        FVector standing = spy.Location + new FVector(0f, 0f, clone.CollisionHeight - spy.CollisionHeight);
        if (spy.XLevel.SpawnActor(clone, standing, heading) is null) return;

        // A mouse button can run both alternate fire and the legacy quick-slot command on the same tick.
        _lastDeploymentTick = XLevel.TickCount;
        Charge = 0;
        _rechargeUntil = Level.TimeSeconds + RechargeSeconds;
        RechargeLeft = RechargeSeconds;
        LastUsed = Level.TimeSeconds;
    }

    protected override void Tick(float deltaTime)
    {
        base.Tick(deltaTime);
        if (Role != ENetRole.ROLE_Authority || _rechargeUntil <= 0f) return;
        RechargeLeft = System.MathF.Ceiling(System.Math.Max(0f, _rechargeUntil - Level.TimeSeconds) * 10f) / 10f;
        if (RechargeLeft <= 0f)
        {
            Charge = 1;
            _rechargeUntil = 0f;
        }
    }
}
