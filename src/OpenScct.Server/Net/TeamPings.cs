using System;
using System.Buffers.Binary;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;

namespace OpenScct.Server;

public enum TeamPingKind : byte
{
    Generic,
    Watch,
    Check,
    CoopBoost,
    Mine,
}

/// <summary>A temporary team annotation. ServerTick is the tick when the host accepted the request.</summary>
public readonly record struct TeamPingMark(int SenderId, byte Team, TeamPingKind Kind, FVector Location, long ServerTick);

public sealed partial class ServerHost
{
    private SGameInfo? TeamPingGame => World.Level.Info.Game is SGameInfo
        { GameReplicationInfo.GameMode: not EGameMode.GM_MercDeathMatch } game && game is not SHubGameInfo ? game : null;

    private void OfferTeamPings(NetConnection connection)
    {
        if (TeamPingGame is not null && connection is { State: EConnectionState.USOCK_Open, Actor: SPlayerController })
            connection.ControlChannel?.Send(ControlMessage.TextLine(TeamPingWire.Offer));
    }

    private bool ReceiveTeamPing(NetConnection connection, string? text)
    {
        if (!TeamPingWire.IsMessage(text)) return false;
        if (!connection.bJoined || connection.UserData is not PlayerSlot slot ||
            connection.Actor is not SPlayerController player || TeamPingGame is not { } game) return true;

        if (text == TeamPingWire.Ack)
        {
            slot.SupportsTeamPings = true;
            return true;
        }

        long tick = World.TickCount;
        if (!slot.SupportsTeamPings || game.bGameEnded || game.GameReplicationInfo?.bMatchHasBegun != true ||
            tick < slot.NextTeamPingTick || !TeamPingPlacement.TryGetTeam(player, out byte team) ||
            !TeamPingWire.TryReadRequest(text!, out TeamPingKind kind, out uint sourceRef, out FVector location) ||
            !TeamPingPlacement.IsAllowed(kind, team) || !TeamPingPlacement.TryGetSource(player, out Actor source, out _) ||
            sourceRef != TeamPingWire.SourceReference(connection, source)) return true;

        // Bound the rate of collision queries as well as of accepted markers.
        slot.NextTeamPingTick = tick + Sim.Hz;
        if (!TeamPingPlacement.TryValidateTarget(player, source, location)) return true;

        ControlMessage message = ControlMessage.TextLine(TeamPingWire.Event(new TeamPingMark(slot.PlayerId, team, kind, location, tick)));
        foreach (PlayerSlot recipient in _players)
        {
            if (recipient.SupportsTeamPings && recipient.Connection is
                { bJoined: true, State: EConnectionState.USOCK_Open, ReliableBunchesInFlight: < TeamPingWire.MaxInFlight,
                  Actor: SPlayerController teammate } && TeamPingPlacement.TryGetTeam(teammate, out byte recipientTeam) && recipientTeam == team)
                recipient.Connection.ControlChannel?.Send(message);
        }

        return true;
    }
}

public sealed partial class ClientSession
{
    private bool _supportsTeamPings;

    public bool SupportsTeamPings => State == EClientState.Joined && _supportsTeamPings;

    public event Action<TeamPingMark>? TeamPingReceived;

    public void SendTeamPing(TeamPingKind kind, Actor source, FVector location)
    {
        if (!SupportsTeamPings || _connection is not
            { State: EConnectionState.USOCK_Open, ReliableBunchesInFlight: < TeamPingWire.MaxInFlight, Actor: SPlayerController player } connection ||
            !TeamPingWire.IsFinite(location) || !TeamPingPlacement.TryGetTeam(player, out byte team) ||
            !TeamPingPlacement.IsAllowed(kind, team) || !TeamPingPlacement.TryGetSource(player, out Actor currentSource, out _) ||
            !ReferenceEquals(source, currentSource)) return;

        uint sourceRef = TeamPingWire.SourceReference(connection, source);
        if (sourceRef != 0)
            SendText(TeamPingWire.Request(kind, sourceRef, location));
    }

    private bool ReceiveTeamPing(string? text)
    {
        if (!TeamPingWire.IsMessage(text)) return false;
        if (State != EClientState.Joined || _transport.IsPlayback) return true;

        if (text == TeamPingWire.Offer && !_supportsTeamPings)
        {
            SendText(TeamPingWire.Ack);
            _supportsTeamPings = true;
        }
        else if (SupportsTeamPings && TeamPingWire.TryReadEvent(text!, out TeamPingMark mark) &&
            TeamPingPlacement.TryGetTeam(LocalPlayer as SPlayerController, out byte team) && mark.Team == team &&
            TeamPingPlacement.IsAllowed(mark.Kind, team))
        {
            TeamPingReceived?.Invoke(mark);
        }

        return true;
    }
}

// Keep the existing control message IDs and replication hash. Old peers ignore the reserved session command.
internal static class TeamPingWire
{
    private const string Command = SessionProtocol.Prefix + "team-ping";
    private const string Version = Command + " 1 ";
    private const string RequestPrefix = Version + "req ";
    private const string EventPrefix = Version + "event ";
    internal const string Offer = Version + "offer";
    internal const string Ack = Version + "ack";
    internal const int MaxInFlight = 64;

    internal static bool IsMessage(string? text) =>
        text == Command || text?.StartsWith(Command + " ", StringComparison.Ordinal) == true;

    internal static uint SourceReference(NetConnection connection, Actor source)
    {
        // As on the actor channels, the low bit distinguishes placed objects from dynamic actor channels.
        int mapIndex = connection.Driver.MapBinding.IndexOf(source);
        if (mapIndex >= 0) return (uint)mapIndex * 2 + 1;
        return connection.FindActorChannel(source) is { Closing: false, NetGuid: > 0 } channel ? channel.NetGuid * 2 + 2 : 0;
    }

    // Request: kind, source reference, XYZ. Event: sender, team, kind, XYZ, server tick. Little-endian throughout.
    internal static string Request(TeamPingKind kind, uint sourceRef, FVector location)
    {
        Span<byte> data = stackalloc byte[17];
        data[0] = (byte)kind;
        BinaryPrimitives.WriteUInt32LittleEndian(data[1..], sourceRef);
        WriteLocation(data[5..], location);
        return RequestPrefix + Convert.ToBase64String(data);
    }

    internal static bool TryReadRequest(string text, out TeamPingKind kind, out uint sourceRef, out FVector location)
    {
        kind = default;
        sourceRef = 0;
        location = default;
        Span<byte> data = stackalloc byte[17];
        if (!TryRead(text, RequestPrefix, data)) return false;
        kind = (TeamPingKind)data[0];
        sourceRef = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
        location = ReadLocation(data[5..]);
        return sourceRef != 0 && IsFinite(location);
    }

    internal static string Event(TeamPingMark mark)
    {
        Span<byte> data = stackalloc byte[26];
        BinaryPrimitives.WriteInt32LittleEndian(data, mark.SenderId);
        data[4] = mark.Team;
        data[5] = (byte)mark.Kind;
        WriteLocation(data[6..], mark.Location);
        BinaryPrimitives.WriteInt64LittleEndian(data[18..], mark.ServerTick);
        return EventPrefix + Convert.ToBase64String(data);
    }

    internal static bool TryReadEvent(string text, out TeamPingMark mark)
    {
        mark = default;
        Span<byte> data = stackalloc byte[26];
        if (!TryRead(text, EventPrefix, data)) return false;
        mark = new TeamPingMark(BinaryPrimitives.ReadInt32LittleEndian(data), data[4], (TeamPingKind)data[5],
            ReadLocation(data[6..]), BinaryPrimitives.ReadInt64LittleEndian(data[18..]));
        return mark.SenderId > 0 && mark.ServerTick >= 0 && IsFinite(mark.Location);
    }

    private static bool TryRead(string text, string prefix, Span<byte> data) =>
        text.Length == prefix.Length + (data.Length + 2) / 3 * 4 && text.StartsWith(prefix, StringComparison.Ordinal) &&
        Convert.TryFromBase64Chars(text.AsSpan(prefix.Length), data, out int written) && written == data.Length;

    private static void WriteLocation(Span<byte> data, FVector value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(data, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(data[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(data[8..], value.Z);
    }

    private static FVector ReadLocation(ReadOnlySpan<byte> data) => new(BinaryPrimitives.ReadSingleLittleEndian(data),
        BinaryPrimitives.ReadSingleLittleEndian(data[4..]), BinaryPrimitives.ReadSingleLittleEndian(data[8..]));

    internal static bool IsFinite(FVector value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
