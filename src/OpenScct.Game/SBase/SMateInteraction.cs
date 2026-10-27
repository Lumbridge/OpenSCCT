using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The fixed team-mate entries (heal, unspy, refill), written from spec/objectives/interaction-system.md and
// spec/objectives/team-interactions.md (the native mate search of their CanUse).
public partial class SMateInteraction
{
    private static readonly Name CNAME_ReplicateMate = new("ReplicateMate");

    /// <summary>The trace flags of the mate search's line from the player to the mate (movers, BSP, world geometry).</summary>
    [Provenance(ProvenanceKind.Decoded, "Mate CanUse (game 0x10ab97f0): ULevel::SingleLineCheck flags 0x86, zero extent")]
    public const ETraceFlags MateLineFlags = ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry;

    /// <summary>The mate search range (3D).</summary>
    [Provenance(ProvenanceKind.Decoded, "Mate CanUse: (mate - player).Size() < 200.0 (game 0x10c10af0)")]
    public const float MateRange = 200f;

    /// <summary>The mate search's facing threshold, both ways.</summary>
    [Provenance(ProvenanceKind.Decoded, "Mate CanUse: both facing dots compared with 0.75 (game 0x10c10e70)")]
    public const float MateFacingDot = 0.75f;

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Mate = Live(Mate);
    }

    [Uc("SBase.SMateInteraction.PostBeginPlay", Sha1 = "286f296aee214ab68fbbf5759f8a286d5990539b")]
    [PortOf("uc:SBase.SMateInteraction:PostBeginPlay", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        ObjectiveNatives.SetCollisionSize(this, (Owner?.CollisionRadius ?? 0f) + Objectives.K.InteractionPadding,
            (Owner?.CollisionHeight ?? 0f) + Objectives.K.InteractionPadding);
    }

    /// <summary>Reliable client-to-server: the client's mate search result reaches the server copy.</summary>
    [Uc("SBase.SMateInteraction.ReplicateMate", Sha1 = "bba22d56205abb390673d8bf2208a25025745a5f")]
    [PortOf("uc:SBase.SMateInteraction:ReplicateMate", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SMateInteraction.ReplicateMate", Spec = "spec/objectives/interaction-system.md#network")]
    public void ReplicateMate(SBasePawn? TheMate) => Mate = TheMate;

    /// <summary>
    /// The native's call of the ReplicateMate event: on a client it is a reliable function to the server's copy of
    /// this entry (owned by the player's controller, so it travels on that player's connection); on the server it
    /// runs here.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Mate CanUse (game 0x10ab9a70): ProcessEvent(ReplicateMate) when the found mate changed")]
    public void eventReplicateMate(SBasePawn? TheMate)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            if (Owner is SPlayerController owner)
            {
                CombatServices.Net(this).ServerCall(owner, new CombatCall(CNAME_ReplicateMate) { Target = this, Actor1 = TheMate });
            }

            return;
        }

        ReplicateMate(TheMate);
    }

    [Uc("SBase.SMateInteraction.Touch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SMateInteraction:Touch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void Touch(Actor Other)
    {
    }

    [Uc("SBase.SMateInteraction.UnTouch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SMateInteraction:UnTouch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void UnTouch(Actor Other)
    {
    }

    /// <summary>
    /// The base mate entry's CanUse: a team mate found by the mate search (no extra condition). The heal, unspy and
    /// backpack entries add theirs.
    /// </summary>
    [UcNative("ASMateInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111CA3F0)]
    [PortOf("nat:SBase.SMateInteraction.CanUse", Spec = "spec/objectives/team-interactions.md#mate-search")]
    [Provenance(ProvenanceKind.Decoded, "Editor ASMateInteraction CanUse virtual 0x111ca490 (vtable +0x14c): the mate search with no extra test")]
    public override bool CanUse(SPlayerController? TheController) => FindMate(TheController, static (_, _) => true);

    /// <summary>
    /// The mate search shared by the mate entries' CanUse (spec/objectives/team-interactions.md, mate search): the
    /// level's pawns in order; the first one that is alive, not lying on the ground, passes <paramref name="wants"/>,
    /// is not the player's own pawn, is of the player's side, is closer than 200 uu, is in front of the player
    /// (direction dot facing over 0.75), faces the player (facing dot facing under -0.75) and is in a clear line
    /// (movers, BSP and world geometry) becomes <see cref="Mate"/>. A new mate raises ReplicateMate. Nothing in the
    /// entry's own bUsable, UsableBy or reuse fields is read.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Game mate CanUse virtuals 0x10ab97f0 (heal), 0x10ae8050 (unspy), 0x10abc920 (backpack); editor 0x111ca490 (base)")]
    protected bool FindMate(SPlayerController? TheController, Func<SPlayerController, SBasePawn, bool> wants)
    {
        if (TheController?.Pawn is not SBasePawn me)
        {
            return false;
        }

        foreach (SBasePawn mate in GadgetWorld.Pawns(XLevel))
        {
            if (mate.bDeleteMe || mate.Health <= 0 || mate.bLyingOnGround)
            {
                continue;
            }

            if (ReferenceEquals(mate, me) || !wants(TheController, mate) || mate.SPawnType != me.SPawnType)
            {
                continue;
            }

            if (!MateInReach(me, mate) || GadgetWorld.Line(this, mate.Location, me.Location, MateLineFlags, out _))
            {
                continue;
            }

            if (!ReferenceEquals(Mate, mate))
            {
                Mate = mate;
                eventReplicateMate(mate);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The mate search's reach: closer than 200 uu (3D), in front of the player (the normalised direction to the
    /// mate dot the player's facing over 0.75) and facing the player (the mate's facing dot the player's under -0.75).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Mate CanUse: Size < 200.0 (0x10c10af0), dots against 0.75 (0x10c10e70)")]
    public static bool MateInReach(SBasePawn me, SBasePawn mate)
    {
        FVector to = mate.Location - me.Location;
        if (to.Size() >= MateRange)
        {
            return false;
        }

        FVector facing = me.Rotation.Vector();
        return (to.SafeNormal() | facing) > MateFacingDot && -(mate.Rotation.Vector().SafeNormal() | facing) > MateFacingDot;
    }
}
