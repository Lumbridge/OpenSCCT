using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// How a client animates the pawns it does not control (spec/net/proxy-pawns.md, spec/native/net-proxy.md): the
/// walk set a proxy starts with (InitializeBasicActions), the action batches the server relays (GetNewActions, then
/// played by the proxy tick through the pawn's own action code, so the clips, the walk sets and the stance follow the
/// owner's), and the zone configuration (crouched, zip speed or legs-over cylinder with its mesh offset). The walk
/// blender (<see cref="PawnMovementAnimation"/>) then runs on the proxy from its smoothed velocity as on any pawn.
/// Also the server's side of the relay: the actions a remote player sent, recorded for the other viewers, and the
/// per-tick batch bookkeeping of a player controller.
/// </summary>
public static class ProxyAnimation
{
    /// <summary>The action batch time a forced reinit sets (DistantClientReinit), never played.</summary>
    [Provenance(ProvenanceKind.Decoded, "proxy-pawns.md constants: reset batch time -1.0")]
    public const float ReinitBatchTime = -1f;

    /// <summary>
    /// SBasePawn.InitializeBasicActions on a pawn without a controller (a proxy): the walk set of its stance through
    /// AnimateWalking: crouched SA_WALK_CR_0H (75) or SA_WALK_CR_2H (76), standing SA_WALK_ST_0H (85) or SA_WALK_ST_2H
    /// (86), the unarmed set only for a spy (SPawnType 0) with WeaponLoaded 0. A pawn on a wall (BTW) or in a pipe
    /// fire pose keeps those poses' own sets.
    /// </summary>
    [UcNative("ASBasePawn::execInitializeBasicActions", Exe = 0x10ae4720, Editor = 0x111feb80)]
    [PortOf("nat:SBase.SBasePawn.InitializeBasicActions", Spec = "spec/net/proxy-pawns.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111feb80 default case (physics outside 6 to 14): BTW, PipHFire, then bIsCrouched x (SPawnType 0 and WeaponLoaded 0) to AnimateWalking 0x4b, 0x4c, 0x55, 0x56")]
    public static void InitializeBasicActions(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);

        // The traversal modes (OnPipe, fence, ledge, ladder, hand over hand) set their own sets; not ported (a new
        // proxy is walking or falling as it appears).
        if (pawn.Physics is >= EPhysics.PHYS_OnPipe and <= EPhysics.PHYS_OnPipeH &&
            pawn.Physics is not (EPhysics.PHYS_Linear or EPhysics.PHYS_Vandamme or EPhysics.PHYS_Repositionnal or EPhysics.PHYS_FollowParent))
        {
            return;
        }

        if (pawn.BTW != default || pawn.PipHFire != default)
        {
            return;
        }

        bool unarmed = pawn.SPawnType == PawnType.SPAWNOFFENSE && pawn.WeaponLoaded == 0;
        int action = pawn.bIsCrouched
            ? (unarmed ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_CR_2H)
            : (unarmed ? SBasePawn.SA_WALK_ST_0H : SBasePawn.SA_WALK_ST_2H);
        AnimateWalking(pawn, action);
    }

    /// <summary>
    /// ASBasePawn::AnimateWalking(Action) as the action ports split it: the resting loop on channel 0
    /// (<see cref="PawnActions.AnimateWalking"/>) and the walk blender's set (<see cref="ActionAnimation.WalkSet"/>).
    /// </summary>
    [UcNative("ASBasePawn::AnimateWalking", Editor = 0x111fd360)]
    private static void AnimateWalking(SBasePawn pawn, int action)
    {
        Name wait = action switch
        {
            SBasePawn.SA_WALK_CR_0H => pawn.AWaitCr0,
            SBasePawn.SA_WALK_CR_2H => pawn.AWaitCr2,
            SBasePawn.SA_WALK_ST_2H => pawn.AWaitSt2,
            _ => pawn.AWaitSt0,
        };
        PawnActions.AnimateWalking(pawn, wait);
        ActionAnimation.WalkSet(pawn, action);
    }

    /// <summary>
    /// SBasePawn.GetNewActions: the batch the server sent (already unpacked by UnCompressActions) becomes the pawn's
    /// AllActions and count, with its server time and the owner's zone configuration.
    /// </summary>
    [Uc("SBase.SBasePawn.GetNewActions", Sha1 = "5008609b003b6f80b5e155cbdad5bcd1e2077fa8")]
    [PortOf("uc:SBase.SBasePawn:GetNewActions", Spec = "spec/net/proxy-pawns.md#rules")]
    public static void GetNewActions(SBasePawn pawn, ReadOnlySpan<int> actions, int count, float fTime, byte zc)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        int n = Math.Clamp(count, 0, Math.Min(actions.Length, pawn.AllActions.Length));
        for (int i = 0; i < n; i++)
        {
            pawn.AllActions[i] = actions[i];
        }

        pawn.iNbActionForDistantClient = n;
        pawn.fRepTimeOfLastActionChange = fTime;
        pawn.ZoneConfigForClient = zc;
    }

    /// <summary>
    /// The animation part of the proxy tick (net-proxy.md rules 8 and 9), before the velocity and the smoothing: a
    /// batch newer than the last one played runs in order through the pawn's action code, then a changed zone
    /// configuration moves the proxy by its ZCDecal offset (the pending smoothing error kept in world terms) and sets
    /// the cylinder height and PrePivot of the new configuration.
    /// </summary>
    [UcNative("ASBasePawn::TickSimulated", Editor = 0x1121dc30)]
    [PortOf("eng:ASBasePawn::TickSimulated", Spec = "spec/native/net-proxy.md#client-proxy-tick-ticksimulated-every-frame-for-a-simulated-pawn")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x1121dc30 to 0x1121debb: the batch loop over 0x744, sAction from AllActions[iNbAction - 1], fOld = fRep; the ZCDecal move, ErrorDistance.Z, NormErrDist, the four configurations")]
    public static void TickSimulated(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        PlayNewActions(pawn);
        if (!pawn.bDeleteMe && pawn.ZoneConfigForClient != pawn.ZoneConfig)
        {
            ApplyZoneConfig(pawn);
        }
    }

    /// <summary>Rule 8: the new batch, in order, through the engine's action code.</summary>
    public static void PlayNewActions(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (!(pawn.fRepTimeOfLastActionChange > pawn.fOldTimeOfLastActionChange))
        {
            return;
        }

        int count = Math.Min(pawn.iNbActionForDistantClient, pawn.AllActions.Length);
        for (int i = 0; i < count && !pawn.bDeleteMe; i++)
        {
            pawn.ExecuteAction(pawn.AllActions[i]);
        }

        // sAction takes AllActions[iNbAction - 1]; with iNbAction 0 that slot is sAction itself (unchanged).
        if (pawn.iNbAction > 0 && pawn.iNbAction <= pawn.AllActions.Length)
        {
            pawn.sAction = pawn.AllActions[pawn.iNbAction - 1];
        }

        pawn.fOldTimeOfLastActionChange = pawn.fRepTimeOfLastActionChange;
    }

    /// <summary>Rule 9: the proxy's cylinder and mesh offset follow the owner's zone configuration.</summary>
    public static void ApplyZoneConfig(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        SBasePawn d = pawn.DefaultObject;
        int from = pawn.ZoneConfig & 3;
        int to = pawn.ZoneConfigForClient & 3;
        FVector prePivot = d.PrePivot;
        float decal = pawn.ZCDecal[(from * 4) + to];
        MovementNatives.FarMoveActor(pawn, pawn.Location - new FVector(0f, 0f, decal));
        FVector error = pawn.ErrorDistance;
        error.Z += decal;
        pawn.ErrorDistance = error;
        pawn.NormErrDist = error.SafeNormal();
        float height;
        switch (to)
        {
            case 1:
                prePivot.Z += d.CollisionHeight - pawn.CrouchHeight;
                height = pawn.CrouchHeight;
                break;
            case 2:
                prePivot.Z += pawn.ZipHeight - d.CollisionHeight;
                height = pawn.ZipHeight;
                break;
            case 3:
                prePivot.Z -= pawn.Pipe4DecalCol;
                height = pawn.Pipe4Height;
                break;
            default:
                height = d.CollisionHeight;
                break;
        }

        pawn.PrePivot = prePivot;
        MovementNatives.SetCollisionSize(pawn, pawn.CollisionRadius, height);
        pawn.ZoneConfig = pawn.ZoneConfigForClient;
    }

    /// <summary>
    /// The server's run of the actions a remote player sent (NativeExecuteReceivedActionForServer): each through the
    /// pawn's action code and into AllActions, then sAction is the last, and both counts take the batch's size so
    /// the controller's tick relays it to the other viewers (<see cref="ServerUpdateActionCounts"/>).
    /// </summary>
    [UcNative("ASBasePawn::execNativeExecuteReceivedActionForServer", Exe = 0x10ae4030, Editor = 0x1121dba0)]
    [PortOf("nat:SBase.SBasePawn.NativeExecuteReceivedActionForServer", Spec = "spec/net/move-protocol.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x1121db40: ExecuteAction and AllActions[i] per action, sAction = the last, iNbActionForDistantClient = iNbAction = count")]
    public static void ExecuteReceivedActionsForServer(SBasePawn pawn, ReadOnlySpan<int> actions, int count)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        int n = Math.Clamp(count, 0, Math.Min(actions.Length, pawn.AllActions.Length));
        if (n == 0)
        {
            return;
        }

        for (int i = 0; i < n && !pawn.bDeleteMe; i++)
        {
            pawn.ExecuteAction(actions[i]);
            pawn.AllActions[i] = actions[i];
        }

        pawn.sAction = actions[n - 1];
        pawn.iNbActionForDistantClient = n;
        pawn.iNbAction = n;
    }

    /// <summary>
    /// The end of a player controller's tick for a pawn with authority (net-proxy.md rule 6): a batch played this
    /// tick gets the server time and its relayed count (iNbAction for a remote player's pawn, the table for one
    /// played here), then the table counts as sent.
    /// </summary>
    [UcNative("ASBasePawn::ServerUpdateActionCounts", Editor = 0x111f8c80)]
    [PortOf("eng:ASBasePawn::ServerUpdateActionCounts", Spec = "spec/native/net-proxy.md#server-what-each-viewer-gets-per-update-of-a-pawn-to-a-non-owning-connection")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111f8c80 from ASPlayerController::Tick 0x111f41b0 when the pawn's Role is 4")]
    public static void ServerUpdateActionCounts(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.iNbAction > 0)
        {
            pawn.fRepTimeOfLastActionChange = pawn.Level.TimeSeconds;
            pawn.iNbActionForDistantClient = pawn.Controller is { } c && c.RemoteRole != ENetRole.ROLE_AutonomousProxy
                ? pawn.iNbActionInTable
                : pawn.iNbAction;
        }

        pawn.iNbActionsSent = pawn.iNbActionInTable;
        pawn.iNbAction = 0;
    }
}
