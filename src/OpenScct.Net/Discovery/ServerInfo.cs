using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace OpenScct.Net.Discovery;

/// <summary>Where a server's session is: in the lobby, loading a map, playing a round, or showing its results.</summary>
public enum SessionPhase : byte
{
    Lobby = 0,
    Loading = 1,
    Match = 2,
    Results = 3,
}

/// <summary>One player as a server's info lists it.</summary>
public readonly record struct ServerInfoPlayer(string Name, byte Team);

/// <summary>
/// What a server tells browsers about itself, in answer to a query on its game port or its LAN port and in its
/// master heartbeat: name, map and mode, the session phase, players, the build it runs and the content hash of the
/// map, so a browser can show at once whether it can join.
/// </summary>
public sealed record ServerInfo
{
    public string Name { get; init; } = "OpenSCCT server";

    /// <summary>The map of the coming or running round (SVMap id).</summary>
    public string Map { get; init; } = string.Empty;

    /// <summary>EGameMode of the coming or running round (5 Story, 4 Disk Hunt, 6 Deathmatch, 7 Merc Deathmatch).</summary>
    public byte Mode { get; init; }

    public SessionPhase Phase { get; init; }

    public int Players { get; init; }

    public int MaxPlayers { get; init; }

    /// <summary>UDP port of the game socket (a LAN beacon answers from another port).</summary>
    public int GamePort { get; init; }

    /// <summary>The protocol hash of the server's build (every replicated class and layout).</summary>
    public ulong ProtocolHash { get; init; }

    /// <summary>The content hash of <see cref="Map"/> on the server.</summary>
    public ulong ContentHash { get; init; }

    /// <summary>The server's build version (for "this server runs a newer build").</summary>
    public string Version { get; init; } = BuildInfo.Version;

    public bool HasPassword { get; init; }

    public bool Dedicated { get; init; }

    /// <summary>Seconds of the round left, or 0 when no round runs or it has no limit.</summary>
    public int SecondsLeft { get; init; }

    public IReadOnlyList<ServerInfoPlayer> PlayerList { get; init; } = [];

    /// <summary>The master's id of the server when it is listed (a direct-address join asks the master for a ticket with it), or empty.</summary>
    public string MasterId { get; init; } = string.Empty;

    /// <summary>Only signed-in players (with a master ticket) may join.</summary>
    public bool RequireAccounts { get; init; }

    /// <summary>A hub (docs/HUB.md), not a game server: flag bit 4, which older readers ignore.</summary>
    public bool IsHub { get; init; }

    /// <summary>Version 2 added <see cref="MasterId"/> and <see cref="RequireAccounts"/> at the end; version 1 still reads.</summary>
    private const byte FormatVersion = 2;
    private const byte FirstVersion = 1;
    private const int MaxText = 64;
    private const int MaxListed = 32;

    /// <summary>Encodes the info into <paramref name="destination"/>; returns the length it needs (more than the span when it did not fit).</summary>
    public int Write(Span<byte> destination)
    {
        var w = new SpanWriter(destination);
        w.Byte(FormatVersion);
        w.Text(Name);
        w.Text(Map);
        w.Byte(Mode);
        w.Byte((byte)Phase);
        w.UInt16((ushort)Math.Clamp(Players, 0, ushort.MaxValue));
        w.UInt16((ushort)Math.Clamp(MaxPlayers, 0, ushort.MaxValue));
        w.UInt16((ushort)Math.Clamp(GamePort, 0, ushort.MaxValue));
        w.UInt64(ProtocolHash);
        w.UInt64(ContentHash);
        w.Text(Version);
        w.Byte((byte)((HasPassword ? 1 : 0) | (Dedicated ? 2 : 0) | (IsHub ? 4 : 0)));
        w.UInt16((ushort)Math.Clamp(SecondsLeft, 0, ushort.MaxValue));
        int count = Math.Min(PlayerList.Count, MaxListed);
        w.Byte((byte)count);
        for (int i = 0; i < count; i++)
        {
            w.Text(PlayerList[i].Name);
            w.Byte(PlayerList[i].Team);
        }

        w.Text(MasterId);
        w.Byte((byte)(RequireAccounts ? 1 : 0));
        return w.Position;
    }

    /// <summary>Decodes an info written by <see cref="Write"/>; false when the bytes are not a valid info.</summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out ServerInfo info)
    {
        info = new ServerInfo();
        var r = new SpanReader(source);
        byte version = r.Byte();
        if (version is < FirstVersion or > FormatVersion)
        {
            return false;
        }

        string name = r.Text();
        string map = r.Text();
        byte mode = r.Byte();
        byte phase = r.Byte();
        int players = r.UInt16();
        int max = r.UInt16();
        int port = r.UInt16();
        ulong protocol = r.UInt64();
        ulong content = r.UInt64();
        string build = r.Text();
        byte flags = r.Byte();
        int left = r.UInt16();
        int count = r.Byte();
        var list = new List<ServerInfoPlayer>(count);
        for (int i = 0; i < count && !r.Error; i++)
        {
            string player = r.Text();
            byte team = r.Byte();
            list.Add(new ServerInfoPlayer(player, team));
        }

        string masterId = string.Empty;
        bool requireAccounts = false;
        if (version >= 2)
        {
            masterId = r.Text();
            requireAccounts = (r.Byte() & 1) != 0;
        }

        if (r.Error || phase > (byte)SessionPhase.Results)
        {
            return false;
        }

        info = new ServerInfo
        {
            Name = name, Map = map, Mode = mode, Phase = (SessionPhase)phase, Players = players, MaxPlayers = max,
            GamePort = port, ProtocolHash = protocol, ContentHash = content, Version = build,
            HasPassword = (flags & 1) != 0, Dedicated = (flags & 2) != 0, IsHub = (flags & 4) != 0, SecondsLeft = left, PlayerList = list,
            MasterId = masterId, RequireAccounts = requireAccounts,
        };
        return true;
    }

    private ref struct SpanWriter(Span<byte> span)
    {
        private readonly Span<byte> _span = span;

        public int Position { get; private set; }

        public void Byte(byte value)
        {
            if (Position < _span.Length)
            {
                _span[Position] = value;
            }

            Position++;
        }

        public void UInt16(ushort value)
        {
            Byte((byte)(value >> 8));
            Byte((byte)value);
        }

        public void UInt64(ulong value)
        {
            if (Position + 8 <= _span.Length)
            {
                BinaryPrimitives.WriteUInt64BigEndian(_span[Position..], value);
            }

            Position += 8;
        }

        public void Text(string? text)
        {
            string clipped = ControlMessage.Clip(text, MaxText);
            int length = Encoding.UTF8.GetByteCount(clipped);
            Byte((byte)length);
            if (Position + length <= _span.Length)
            {
                Encoding.UTF8.GetBytes(clipped, _span[Position..]);
            }

            Position += length;
        }
    }

    private ref struct SpanReader(ReadOnlySpan<byte> span)
    {
        private readonly ReadOnlySpan<byte> _span = span;
        private int _position;

        public bool Error { get; private set; }

        public byte Byte()
        {
            if (_position >= _span.Length)
            {
                Error = true;
                return 0;
            }

            return _span[_position++];
        }

        public int UInt16() => (Byte() << 8) | Byte();

        public ulong UInt64()
        {
            if (_position + 8 > _span.Length)
            {
                Error = true;
                return 0;
            }

            ulong v = BinaryPrimitives.ReadUInt64BigEndian(_span[_position..]);
            _position += 8;
            return v;
        }

        public string Text()
        {
            int length = Byte();
            if (_position + length > _span.Length)
            {
                Error = true;
                return string.Empty;
            }

            string s = Encoding.UTF8.GetString(_span.Slice(_position, length));
            _position += length;
            return s;
        }
    }
}
