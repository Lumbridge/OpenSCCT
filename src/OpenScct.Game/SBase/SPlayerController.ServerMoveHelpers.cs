using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController server side of the client's action and traversal calls (reliable client to server):
// the actions a client queued after its move, and the attach points and repositioning targets it proposes.
// Written from spec/net/move-protocol.md (rules 9, 12 to 14).
public partial class SPlayerController
{
    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rules 12 and 13: MSA match tolerance 0.01 s")]
    private const float ActionMsaTolerance = 0.01f;

    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 14: directions are sent x 1000")]
    private const float WireDirectionScale = 0.001f;

    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 13: the velocity is sent x 100")]
    private const float WireVelocityScale = 0.01f;

    /// <summary>
    /// The client's queued actions (rule 12): executed on the server's pawn when the client's acknowledgement time
    /// matches the server's within 0.01 s.
    /// </summary>
    [Uc("SBase.SPlayerController.ReplicateActionToServer", Sha1 = "543ce9fbfb69fe87f48728338b993e6d7532f413")]
    [PortOf("uc:SBase.SPlayerController:ReplicateActionToServer", Spec = "spec/net/move-protocol.md#server-side")]
    [Provenance(ProvenanceKind.Estimated, "The replay of a newer stored big move's actions (BSMS) is not ported")]
    public void ReplicateActionToServer(float BSTTime, float MSATime, int Val0, int Val1)
    {
        if (SvmMath.Abs(MSATime - MirrorStateAcknowledgementTime) < ActionMsaTolerance)
        {
            NativeExecuteReceivedActionForServer(Val0, Val1);
        }
    }

    /// <summary>The same with the client's velocity x 100 (rule 13): applied unless in root motion, where X carries the special yaw.</summary>
    [Uc("SBase.SPlayerController.ReplicateActionToServerWithVelocity", Sha1 = "291ca817863622974c9ea080574b1b2d13d74c52")]
    [PortOf("uc:SBase.SPlayerController:ReplicateActionToServerWithVelocity", Spec = "spec/net/move-protocol.md#server-side")]
    [Provenance(ProvenanceKind.Estimated, "The replay of a newer stored big move's actions (BSMS) is not ported")]
    public void ReplicateActionToServerWithVelocity(float BSTTime, float MSATime, int Val0, int Val1, FVector Vel)
    {
        if (!(SvmMath.Abs(MSATime - MirrorStateAcknowledgementTime) < ActionMsaTolerance))
        {
            return;
        }

        NativeExecuteReceivedActionForServer(Val0, Val1);
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.Physics != EPhysics.PHYS_RootMotion)
        {
            pawn.Velocity = Vel * WireVelocityScale;
        }
        else
        {
            pawn.SpecialYaw = SvmMath.Trunc(Vel.X / 100f);
        }
    }

    [Uc("SBase.SPlayerController.ServerSetWallJumpDir", Sha1 = "e2a0997ef78d9053e05c5def48aab2d91fdf0458")]
    [PortOf("uc:SBase.SPlayerController:ServerSetWallJumpDir", Spec = "spec/net/move-protocol.md#server-side")]
    public void ServerSetWallJumpDir(FVector wallDir)
    {
        if (SBPawn is { } pawn)
        {
            pawn.vWallJumpDir = wallDir * WireDirectionScale;
        }
    }

    [Uc("SBase.SPlayerController.ServerSetInfoForVandamme", Sha1 = "1e34234d2a64b40bc2e5f9b0fa8a62016eb07588")]
    [PortOf("uc:SBase.SPlayerController:ServerSetInfoForVandamme", Spec = "spec/net/move-protocol.md#server-side")]
    public void ServerSetInfoForVandamme(FVector vPos, FVector wallDir)
    {
        if (SBPawn is { } pawn)
        {
            pawn.vVandammePos = vPos;
            pawn.vWallJumpDir = wallDir * WireDirectionScale;
            pawn.fLastRootMotionTimeCalled = Level.TimeSeconds;
        }
    }

    [Uc("SBase.SPlayerController.ServerGetZipP", Sha1 = "8d57cb3ff26f1bb5f950b13d2dbdd7b3dd93140e")]
    [PortOf("uc:SBase.SPlayerController:ServerGetZipP", Spec = "spec/net/move-protocol.md#server-side")]
    public void ServerGetZipP(FVector vZipP1, FVector vZipP2)
    {
        if (SBPawn is { } pawn)
        {
            pawn.ZipP1 = vZipP1;
            pawn.ZipP2 = vZipP2;
            pawn.NewZipP1 = vZipP1;
            pawn.NewZipP2 = vZipP2;
        }
    }

    [Uc("SBase.SPlayerController.ServerGetNewZipP", Sha1 = "9e86a511345224a808c868cdd477b554b0b55e3c")]
    [PortOf("uc:SBase.SPlayerController:ServerGetNewZipP", Spec = "spec/net/move-protocol.md#server-side")]
    public void ServerGetNewZipP(FVector vZipP1, FVector vZipP2)
    {
        if (SBPawn is { } pawn)
        {
            pawn.NewZipP1 = vZipP1;
            pawn.NewZipP2 = vZipP2;
        }
    }

    [Uc("SBase.SPlayerController.ServerGetZipPAndNormal", Sha1 = "94f107d6c7a811d8acf8426f6cb5b7bd64e0e36d")]
    [PortOf("uc:SBase.SPlayerController:ServerGetZipPAndNormal", Spec = "spec/net/move-protocol.md#server-side")]
    public void ServerGetZipPAndNormal(FVector vZipP1, FVector vZipP2, FVector Normal)
    {
        if (SBPawn is { } pawn)
        {
            pawn.NewZipP1 = vZipP1;
            pawn.NewZipP2 = vZipP2;
            pawn.ZipP1 = vZipP1;
            pawn.ZipP2 = vZipP2;
            pawn.m_geoNormal = Normal * WireDirectionScale;
        }
    }

    /// <summary>
    /// NativeExecuteReceivedActionForServer: unpacks the action pair (rule 9's CompressActions: three 9-bit codes
    /// and a 3-bit count in the first value, three more codes in the second) and executes each on the pawn.
    /// </summary>
    [UcNative("ASBasePawn::execNativeExecuteReceivedActionForServer")]
    [PortOf("nat:SBase.SBasePawn.NativeExecuteReceivedActionForServer", Spec = "spec/net/move-protocol.md#server-side")]
    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 9: a1 | a2 << 9 | a3 << 18 | count << 27, a4 | a5 << 9 | a6 << 18")]
    public void NativeExecuteReceivedActionForServer(int Val0, int Val1)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        int count = Math.Min((Val0 >> 27) & 7, 6);
        Span<int> actions = stackalloc int[6];
        for (int i = 0; i < count; i++)
        {
            int packed = i < 3 ? Val0 : Val1;
            actions[i] = (packed >> (9 * (i % 3))) & 0x1FF;
        }

        OpenScct.Game.Animation.ProxyAnimation.ExecuteReceivedActionsForServer(pawn, actions, count);
    }
}
