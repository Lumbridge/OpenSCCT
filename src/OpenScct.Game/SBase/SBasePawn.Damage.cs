using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn functions of the combat port: the blast and dying flight, the counter-grab match value and the
// spy bullet mark. Written from spec/combat/blast-and-fall-damage.md, death-and-respawn.md, spy-grab.md and
// spec/equipment/merc-laser.md. TakeDamage, TakeFallingDamage and EncroachedBy stay empty (Actor's versions): every
// player damage goes through SPlayerController.PawnTakeDamage.
public partial class SBasePawn
{
    private static readonly ClassRef CombatGameMessages = new("SBase.SGameMessages");

    /// <summary>
    /// The blast flight (blast spec rule 3): a client owner also sends it to the server at 100 times scale; locally
    /// the pawn takes the velocity and faces back toward the blast.
    /// </summary>
    [Uc("SBase.SBasePawn.SetVelocityAndOrientationForBlast", Sha1 = "0dce2b2515160c0621ecbdbf6eafa6ef2077a12a")]
    [PortOf("uc:SBase.SBasePawn:SetVelocityAndOrientationForBlast", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void SetVelocityAndOrientationForBlast(FVector V)
    {
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController pc)
        {
            pc.ServerSetVelocityAndOrientationForBlast(V * K.BlastVelocityWireScale);
        }

        ApplyBlastVelocity(V);
    }

    /// <summary>Falling physics, the blast velocity and the facing opposite to its horizontal direction (pitch and roll 0).</summary>
    [Provenance(ProvenanceKind.Estimated, "Owner side also switches to PHYS_Falling as the server does (walking would drop Velocity.Z)")]
    public void ApplyBlastVelocity(FVector V)
    {
        SetPhysics(EPhysics.PHYS_Falling);
        Velocity = V;
        FVector flat = V;
        flat.Z = 0f;

        // SetRotation(rotator(-Normal(flat))): pitch and roll 0, and a straight-up blast (no flat part) faces yaw 0.
        Rotation = (-flat.SafeNormal()).Rotation();
    }

    /// <summary>
    /// The push of a pawn that dies on a ladder, pipe or fence (death spec rule 8): the owner takes the velocity and
    /// sends it to the server, which starts the fall.
    /// </summary>
    [Uc("SBase.SBasePawn.SetVelocityForDyingPawn", Sha1 = "39180c647da81e86284b93322ec203420474b037")]
    [PortOf("uc:SBase.SBasePawn:SetVelocityForDyingPawn", Spec = "spec/combat/death-and-respawn.md#rules")]
    public void SetVelocityForDyingPawn(FVector V)
    {
        Velocity = V;
        (Controller as SPlayerController)?.ServerSetVelocityForDyingPawn(V * K.BlastVelocityWireScale);
    }

    /// <summary>
    /// The counter-grab match value of one side (spy-grab.md rules 28 and 29, reliable owner to server): stored on
    /// the spy's controller; once both sides have a value the higher wins (ties go to the spy), the merc's win asks
    /// the spy's owner for a countered release, and both values reset to -1.
    /// </summary>
    [Uc("SBase.SBasePawn.GrabSendServerCounterValue", Sha1 = "865d98f1a5162fc67eccbbe9efde6e3ebeafac49")]
    [PortOf("uc:SBase.SBasePawn:GrabSendServerCounterValue", Spec = "spec/combat/spy-grab.md#counter-grab")]
    [PortOf("rep:SBase.SBasePawn.GrabSendServerCounterValue", Spec = "spec/combat/spy-grab.md#network")]
    public void GrabSendServerCounterValue(int val, PawnType PType)
    {
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController owner)
        {
            CombatServices.Net(this).ServerCall(owner, new CombatCall(CNAME_GrabSendServerCounterValue)
            {
                Target = this,
                Int1 = val,
                Int2 = (int)PType,
            });
            return;
        }

        SPlayerController? spy;
        if (PType == PawnType.SPAWNDEFENSE)
        {
            spy = m_AttackedBy?.Controller as SPlayerController;
            if (spy is null)
            {
                return;
            }

            spy.iGrabMateCounterGrabValue = val;
        }
        else
        {
            spy = Controller as SPlayerController;
            if (spy is null)
            {
                return;
            }

            spy.iGrabMyCounterGrabValue = val;
        }

        if (spy.iGrabMyCounterGrabValue == -1 || spy.iGrabMateCounterGrabValue == -1)
        {
            return;
        }

        var merc = spy.SBPawn?.m_AttackTarget?.Controller as SPlayerController;
        if (spy.iGrabMateCounterGrabValue > spy.iGrabMyCounterGrabValue)
        {
            spy.ReceiveCombatMessage(K.MsgLoseCounterGrab);
            merc?.ReceiveCombatMessage(K.MsgWinCounterGrab);
            spy.GrabServerReleaseRequest(GrabReleaseMethode_.GrabbingCountered);
        }
        else
        {
            spy.ReceiveCombatMessage(K.MsgWinCounterGrab);
            merc?.ReceiveCombatMessage(K.MsgLoseCounterGrab);
        }

        spy.iGrabMyCounterGrabValue = -1;
        spy.iGrabMateCounterGrabValue = -1;
    }

    private static readonly Name CNAME_GrabSendServerCounterValue = new("GrabSendServerCounterValue");

    /// <summary>
    /// Marked by a merc's laser (merc-laser.md rule 11, server): the mark, every controller of the other side may
    /// listen to the marked pawn (and hears it at once while holding the hack-comm key), and EMF visibility.
    /// </summary>
    [Uc("SBase.SBasePawn.HitBySpyBullet", Sha1 = "9026ad81c7e99f6c9b2dc9ddda910a47451c21a4")]
    [PortOf("uc:SBase.SBasePawn:HitBySpyBullet", Spec = "spec/equipment/merc-laser.md#rules")]
    public void HitBySpyBullet(int Team)
    {
        using var clock = WorldEventTime();
        bSpyBulletOn = true;
        fTimeSpy = Level.TimeSeconds;
        iTeamSpying = Team;
        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (pc.SPawnType != SPawnType)
            {
                pc.bCanListenSpyBulletedGuy = true;
            }
        }

        Equipment.PawnVision.SetEMF(this);
    }

    /// <summary>The client-to-server version of <see cref="HitBySpyBullet"/>.</summary>
    [Uc("SBase.SBasePawn.ServerHitBySpyBullet", Sha1 = "506d8d6e081bf1e47fb8e1c21e28e1c4e02fca7f")]
    [PortOf("uc:SBase.SBasePawn:ServerHitBySpyBullet", Spec = "spec/equipment/merc-laser.md#rules")]
    public void ServerHitBySpyBullet(int Team) => HitBySpyBullet(Team);

    /// <summary>The SGameMessages class of the combat messages.</summary>
    internal static ClassRef GameMessagesClass => CombatGameMessages;

    /// <summary>The class default object (UnrealScript <c>default.X</c>) for the combat ports.</summary>
    internal SBasePawn ClassDefaults => Defaults;

    /// <summary>A destroyed pawn deletes all its inventory (inventory-selection.md rule 12).</summary>
    [Uc("SBase.SBasePawn.Destroyed", Sha1 = "38e3bd7c90113f14c59007c1f2384d192432cced")]
    [PortOf("uc:SBase.SBasePawn:Destroyed", Spec = "spec/equipment/inventory-selection.md#rules")]
    protected override void Destroyed()
    {
        Equipment.PawnVision.DestroyGoggleBeam(this);
        Equipment.PawnInventory.DeleteAllInventory(this);
        base.Destroyed();
    }
}
