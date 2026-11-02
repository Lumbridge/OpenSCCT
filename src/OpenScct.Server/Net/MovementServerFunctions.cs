using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The server end of the movement port's client-to-server calls (<c>IMovementNet</c>): the action stream and the
/// traversal data of spec/movement/state-and-network.md, run on the server's copy of the sending player's controller
/// and pawn when they arrive. The movement port sends them; nothing in the controller port receives them, so the
/// network layer applies their effects here, as the original's Server* functions do.
/// </summary>
public static class MovementServerFunctions
{
    private const string Controller = "SBase.SPlayerController";

    /// <summary>The acknowledgement tolerance of a state or action batch (|MSATime - MSA| below it).</summary>
    [Provenance(ProvenanceKind.Decoded, "state-and-network.md constants: state acknowledgement tolerance 0.01 s")]
    public const float AckTolerance = 0.01f;

    /// <summary>Registers the handlers on a server driver; <paramref name="endpoints"/> gives each controller's move protocol.</summary>
    public static void Register(NetDriver driver, Func<Actor, MoveEndpoint?> endpoints)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(endpoints);
        driver.Functions.Register(Controller, "ReplicateActionToServer", (self, f, a, _) =>
        {
            if (self is SPlayerController pc)
            {
                ReplicateActionToServer(pc, endpoints(pc)?.Server, Arg(f, a, "BSTTime").F, Arg(f, a, "MSATime").F, Arg(f, a, "Val0").I,
                    Arg(f, a, "Val1").I, null);
            }
        });
        driver.Functions.Register(Controller, "ReplicateActionToServerWithVelocity", (self, f, a, _) =>
        {
            if (self is SPlayerController pc)
            {
                ReplicateActionToServer(pc, endpoints(pc)?.Server, Arg(f, a, "BSTTime").F, Arg(f, a, "MSATime").F, Arg(f, a, "Val0").I,
                    Arg(f, a, "Val1").I, Arg(f, a, "Vel").V);
            }
        });
        driver.Functions.Register(Controller, "ServerGetZipP", (self, f, a, _) =>
        {
            if (Pawn(self) is { } p)
            {
                p.ZipP1 = p.NewZipP1 = GEPointCheck.Exact(p, Arg(f, a, "vZipP1").V);
                p.ZipP2 = p.NewZipP2 = GEPointCheck.Exact(p, Arg(f, a, "vZipP2").V);
            }
        });
        driver.Functions.Register(Controller, "ServerGetNewZipP", (self, f, a, _) =>
        {
            if (Pawn(self) is { } p)
            {
                p.NewZipP1 = GEPointCheck.Exact(p, Arg(f, a, "vZipP1").V);
                p.NewZipP2 = GEPointCheck.Exact(p, Arg(f, a, "vZipP2").V);
            }
        });
        driver.Functions.Register(Controller, "ServerGetZipPAndNormal", (self, f, a, _) =>
        {
            if (Pawn(self) is { } p)
            {
                p.ZipP1 = p.NewZipP1 = GEPointCheck.Exact(p, Arg(f, a, "vZipP1").V);
                p.ZipP2 = p.NewZipP2 = GEPointCheck.Exact(p, Arg(f, a, "vZipP2").V);
                p.m_geoNormal = Arg(f, a, "Normal").V * 0.001f;
            }
        });
        driver.Functions.Register(Controller, "ServerSetWallJumpDir", (self, f, a, _) =>
        {
            if (Pawn(self) is { } p)
            {
                p.vWallJumpDir = Arg(f, a, "wallDir").V * 0.001f;
            }
        });
        driver.Functions.Register(Controller, "ServerSetInfoForVandamme", (self, f, a, _) =>
        {
            if (Pawn(self) is { } p)
            {
                p.vVandammePos = Arg(f, a, "vPos").V;
                p.vWallJumpDir = Arg(f, a, "wallDir").V * 0.001f;
                p.fLastRootMotionTimeCalled = p.Level.TimeSeconds;
            }
        });
        driver.Functions.Register(Controller, "UpdateDesiredRotation", (self, f, a, _) =>
        {
            if (self is SPlayerController pc)
            {
                pc.MyDesiredRotation = Arg(f, a, "Rot").R;
            }
        });
    }

    /// <summary>
    /// ReplicateActionToServer and its velocity form (rule 4 of the actions): a batch the client sent with the current
    /// acknowledgement runs through the pawn's action code; when the batch is older than the last big move, that
    /// move's actions run again. The velocity form also sets the pawn's velocity (outside root motion) or its
    /// SpecialYaw (in root motion).
    /// </summary>
    [Uc("SBase.SPlayerController.ReplicateActionToServer", Sha1 = "543ce9fbfb69fe87f48728338b993e6d7532f413")]
    [Uc("SBase.SPlayerController.ReplicateActionToServerWithVelocity", Sha1 = "291ca817863622974c9ea080574b1b2d13d74c52")]
    [PortOf("uc:SBase.SPlayerController:ReplicateActionToServer", Spec = "spec/movement/state-and-network.md#actions-changeenumaction-and-executeaction")]
    [PortOf("uc:SBase.SPlayerController:ReplicateActionToServerWithVelocity", Spec = "spec/movement/state-and-network.md#actions-changeenumaction-and-executeaction")]
    public static void ReplicateActionToServer(SPlayerController pc, ServerMoves? moves, float BSTTime, float MSATime, int Val0, int Val1,
        FVector? Vel)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (MathF.Abs(MSATime - pc.MirrorStateAcknowledgementTime) >= AckTolerance || pc.SBPawn is not { } pawn)
        {
            return;
        }

        Execute(pawn, Val0, Val1, Vel);
        if (Vel is { } v)
        {
            ApplyVelocity(pawn, v);
        }

        if (moves is not null && BSTTime < moves.BSMS.TimeStamp)
        {
            Execute(pawn, moves.BSMS.Val0, moves.BSMS.Val1, moves.BSMS.Vel);
            if (pawn.Physics == EPhysics.PHYS_RootMotion)
            {
                pawn.SpecialYaw = SvmMath.Trunc(moves.BSMS.Vel.X / 100f);
            }
            else if (Vel is { } again)
            {
                ApplyVelocity(pawn, again);
            }
        }
    }

    // NativeExecuteReceivedActionForServer: the batch, in order, through the pawn's action code.
    private static void Execute(SBasePawn pawn, int val0, int val1, FVector? velocity)
    {
        Span<int> actions = stackalloc int[6];
        int count = MovePacking.UnCompressActions(val0, val1, actions);
        // The knockback action locks its root-motion direction while it executes. Applying the
        // accompanying yaw afterwards turns the server's victim along a different path until wake-up.
        if (velocity is { } value && actions[..count].Contains(SBasePawn.SA_CHARGE_ATTACKED_GOBACK))
            pawn.SpecialYaw = SvmMath.Trunc(value.X / MoveK.VelocityWireScale);
        OpenScct.Game.Animation.ProxyAnimation.ExecuteReceivedActionsForServer(pawn, actions, count);
    }

    private static void ApplyVelocity(SBasePawn pawn, FVector vel)
    {
        if (pawn.Physics == EPhysics.PHYS_RootMotion)
        {
            pawn.SpecialYaw = SvmMath.Trunc(vel.X / 100f);
        }
        else
        {
            pawn.Velocity = vel * 0.01f;
        }
    }

    private static SBasePawn? Pawn(Actor self) => (self as SPlayerController)?.SBPawn;

    private static NetValue Arg(RepFunction f, NetCallArgs args, string name)
    {
        foreach (RepParam p in f.Parameters)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return args.Values[p.ValueOffset];
            }
        }

        return default;
    }
}
