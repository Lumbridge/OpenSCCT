using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScctGame.Smoke;

/// <summary>Shared lab evidence format for rendered clients and the isolated dedicated server.</summary>
public static class NetworkLabState
{
    private static readonly JsonSerializerOptions Json = new();
    private static readonly ConditionalWeakTable<ServerHost, Queue<object>> Disconnects = new();

    private static Queue<object> ObserveDisconnects(ServerHost server)
    {
        var history = new Queue<object>();
        server.PlayerLeft += slot =>
        {
            if (history.Count == 16) history.Dequeue();
            history.Enqueue(new
            {
                utc = DateTimeOffset.UtcNow, player = slot.Name, reason = slot.Connection.CloseReason,
                movement = slot.Controller is { } pc ? server.Movement.PeekEndpoint(pc)?.Sequenced?.DescribePending() : null,
            });
        };
        return history;
    }
    public static void Atomic(string path, object value)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temp, path, overwrite: true); return; }
            catch (Exception e) when (attempt < 5 && e is IOException or UnauthorizedAccessException)
            {
                // Windows scanners/readers can briefly hold the replaced status file without delete sharing.
                // Keep replacement atomic and bounded; a persistent error must still fail the lab.
                System.Threading.Thread.Sleep(5);
            }
        }
    }

    public static object Server(ServerHost server, string phase) => new
    {
        tick = server.World.TickCount, phase, started = server.StartGate?.Started, pawns = Pawns(server.World),
        grenades = Grenades(server.World),
        ended = server.Game?.bGameEnded, winningTeam = server.Game?.GameReplicationInfo?.WinningTeam,
        lag = server.World.Level.GetService<LagCompensation>() is { } lag ? new { lag.Enabled, lag.CeilingMs, lag.RewoundShots, lag.LastRewindMs } : null,
        disconnects = Disconnects.GetValue(server, ObserveDisconnects).ToArray(),
        players = server.Players.Select(p => new
        {
            name = p.Name, ready = p.Controller?.PlayerReplicationInfo?.bWaitingPlayer, pawn = p.Controller?.Pawn?.ActorId,
            movement = p.Controller is { } pc ? server.Movement.PeekEndpoint(pc)?.Sequenced?.DescribePending() : null,
            inputClock = p.Controller is { } clockPc ? server.Movement.PeekEndpoint(clockPc)?.Sequenced?.DescribeInputClock() : null,
            controller = Controller(p.Controller as SPlayerController),
        }).ToArray(),
    };

    public static object? Controller(SPlayerController? pc) => pc is null ? null : new
    {
        name = pc.PlayerReplicationInfo?.PlayerName, team = pc.PlayerReplicationInfo?.Team?.TeamIndex,
        state = pc.GetStateName().ToString(), pawn = pc.Pawn?.ActorId,
        health = (pc.Pawn as SBasePawn)?.Health, lives = pc.PlayerReplicationInfo?.NumLives,
        deaths = pc.PlayerReplicationInfo?.Deaths, kills = pc.PlayerReplicationInfo?.Kills,
        shots = pc.PlayerReplicationInfo?.Stats?.ShotsFired, hits = pc.PlayerReplicationInfo?.Stats?.ShotsHit,
        spectating = pc.PlayerReplicationInfo?.bIsSpectator, onlySpectator = pc.PlayerReplicationInfo?.bOnlySpectator,
        target = (pc.PC_spectated as SPlayerController)?.PlayerReplicationInfo?.PlayerName ??
            (pc.SpecTarget as SBasePawn)?.PlayerReplicationInfo?.PlayerName,
        view = pc.ViewTarget?.ActorId, targetPawn = pc.SpecTarget?.ActorId,
    };

    public static object[] Pawns(World world, NetConnection? connection = null)
    {
        var pawns = new List<object>();
        foreach (SBasePawn p in world.Level.AllActors<SBasePawn>())
            if (!p.bDeleteMe) pawns.Add(Pawn(p, connection));
        return pawns.ToArray();
    }

    public static object[] Grenades(World world, NetConnection? connection = null)
    {
        var grenades = new List<object>();
        foreach (SProjGrenade g in world.Level.AllActors<SProjGrenade>())
        {
            if (!g.bDeleteMe) grenades.Add(new
            {
                id = g.ActorId, netGuid = connection is null ? (uint)g.ActorId : connection.FindActorChannel(g)?.NetGuid,
                type = g.GetType().Name, temporary = g.bNetTemporary, owner = g.Instigator?.PlayerReplicationInfo?.PlayerName,
                position = new[] { g.Location.X, g.Location.Y, g.Location.Z }, physics = g.Physics.ToString(),
            });
        }
        return grenades.ToArray();
    }

    public static object Pawn(SBasePawn p, NetConnection? connection) => new
    {
        id = p.ActorId, name = p.PlayerReplicationInfo?.PlayerName, role = p.Role.ToString(),
        netGuid = connection is null ? (uint)p.ActorId : connection.FindActorChannel(p)?.NetGuid,
        action = p.sAction,
        position = new[] { p.Location.X, p.Location.Y, p.Location.Z }, velocity = new[] { p.Velocity.X, p.Velocity.Y, p.Velocity.Z },
        yaw = p.Rotation.Yaw, pitch = p.Rotation.Pitch, physics = p.Physics.ToString(), state = p.Controller?.GetStateName().ToString(),
        aim = p.iDesiredYawPitch, view = p.iYawPitchFromView, ik = p.IK_Type.ToString(),
        fullYaw = p.PawnAnim.Aim.FullYaw, fullPitch = p.PawnAnim.Aim.FullPitch,
        grabAllowed = (p.Controller as SPlayerController)?.GameReplicationInfo?.bAvlb_SPY_Grab,
        stateTime = (p.Controller as SPlayerController)?.MirrorStateLastTime,
        stateAck = (p.Controller as SPlayerController)?.MirrorStateAcknowledgementTime,
        crouched = p.bIsCrouched, height = p.CollisionHeight, radius = p.CollisionRadius, grabbable = p.bCanBeGrabbed,
        health = p.Health, sleeping = p.bSleeping, lying = p.bLyingOnGround,
        eyeHeight = CombatServices.Ports(p).EyePosition(p).Z,
        gadget = p.SelectedGadget is { } gadget ? new { type = gadget.GetType().Name, charge = gadget.Charge } : null,
        weapon = p.MyWeapon is { } weapon ? new
        {
            type = weapon.GetType().Name, state = weapon.GetStateName().ToString(), ammo = weapon.Ammo,
            shot = weapon.ShotCounter, clip = (weapon as SWeaponDefense)?.ClipAmmo,
            clipMax = (weapon as SWeaponDefense)?.ClipMaxAmmo, mode = (weapon as SWeaponDefense)?.ROFMode.ToString(),
        } : null,
        pivot = new[] { p.PrePivot.X, p.PrePivot.Y, p.PrePivot.Z }, zone = p.ZoneConfig,
        wakeTime = p.iLastWakeTime, time = p.Level.TimeSeconds, poseReady = p.NetworkPoseReady,
        attacker = p.m_AttackedBy?.ActorId, target = p.m_AttackTarget?.ActorId,
        attackerName = p.m_AttackedBy?.PlayerReplicationInfo?.PlayerName, targetName = p.m_AttackTarget?.PlayerReplicationInfo?.PlayerName,
        channels = Enumerable.Range(0, PawnAnimation.ChannelCount).Select(i => new
        {
            i, sequence = p.PawnAnim[i].Sequence.ToString(), frame = p.PawnAnim[i].Frame,
            alpha = p.PawnAnim[i].BlendAlpha, bone = p.PawnAnim[i].StartBone.ToString(), rate = p.PawnAnim[i].Rate,
        }).ToArray(),
    };
}
