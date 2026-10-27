using System;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play.Session;

namespace OpenScct.MapSweep;

/// <summary>
/// <c>--place=Map:x,y,z:yaw:keys:seconds[:merc]</c>: the local player of an offline match (inline host, loopback, no
/// bots) is put at a point facing a yaw, as a respawn puts him (the server moves the pawn and reinitialises the
/// client), then holds the listed input buttons (<c>BT_Up+bCrouch</c>, or <c>-</c> for none) for the given seconds,
/// printing the client's and the server's state four times a second. For looking at one place of a map the sweep
/// flagged.
/// </summary>
internal static class PlaceRun
{
    public static int Run(string arg)
    {
        string[] parts = arg.Split(':');
        string map = parts[0];
        string[] xyz = parts[1].Split(',');
        var at = new FVector(float.Parse(xyz[0], CultureInfo.InvariantCulture), float.Parse(xyz[1], CultureInfo.InvariantCulture),
            float.Parse(xyz[2], CultureInfo.InvariantCulture));
        int yaw = int.Parse(parts[2], CultureInfo.InvariantCulture);
        string[] keys = parts[3] == "-" ? [] : parts[3].Split('+');
        float seconds = float.Parse(parts[4], CultureInfo.InvariantCulture);
        bool merc = parts.Length > 5 && parts[5] == "merc";
        var clock = new SweepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = map,
            Side = merc ? PlaySide.Merc : PlaySide.Spy,
            Mode = MapSweepOptions.ModeOf(map),
            Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline,
        }, clock);
        void Tick()
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.EndTick();
        }

        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++)
        {
            Tick();
        }

        // Stand up (the spy spawns crouched) before moving him.
        if (!merc)
        {
            session.Input.Set("bCrouch", true);
            Tick();
            session.Input.Set("bCrouch", false);
        }

        for (int i = 0; i < 90; i++)
        {
            Tick();
        }

        SPlayerController server = session.ServerController ?? throw new InvalidOperationException("no server controller");
        var pawn = (SBasePawn)server.Pawn!;
        if (session.Host.World.Level.GetService<IMovementNet>() is not NetMovementBridge bridge || bridge.EndpointOf(server)?.Server is not { } moves)
        {
            throw new InvalidOperationException("no move endpoint");
        }

        MovementNatives.FarMoveActor(pawn, at);
        pawn.Velocity = default;
        pawn.Acceleration = default;
        pawn.Rotation = new FRotator(0, yaw, 0);
        server.Rotation = pawn.Rotation;
        moves.ReinitPawn();
        for (int i = 0; i < 30; i++)
        {
            Tick();
        }

        if (session.LocalController is { } local)
        {
            local.SetViewRotation(new FRotator(0, yaw, 0));
        }

        foreach (string k in keys)
        {
            session.Input.Set(k, true);
        }

        int ticks = (int)(seconds * Sim.Hz);
        for (int t = 0; t <= ticks; t++)
        {
            if (t % (Sim.Hz / 4) == 0)
            {
                SPlayerController? c = session.LocalController;
                SBasePawn? cp = session.LocalPawn;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t / (float)Sim.Hz,5:F2} s client [up {c?.BT_Up} fwd {c?.aForward:F0} iMove {c?.iMove} speed {c?.GetSpeedMode():F1}] {c?.GetStateName()} {cp?.Physics} {MapSweeper.Fmt(cp?.Location ?? default)} vz {cp?.Velocity.Z:F0} | server {server.GetStateName()} {pawn.Physics} {MapSweeper.Fmt(pawn.Location)} yaw {pawn.Rotation.Yaw & 65535} iMove {server.iMove} canStop {server.bCanStop} ceiling {server.CheckCeilingForLadder()} zip {MapSweeper.Fmt(pawn.ZipP1)} {MapSweeper.Fmt(pawn.ZipP2)} accel {MapSweeper.Fmt(pawn.Acceleration)}"));
            }

            Tick();
        }

        return 0;
    }
}
