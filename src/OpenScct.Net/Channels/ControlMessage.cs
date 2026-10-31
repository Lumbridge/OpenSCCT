using System;
using System.Text;

namespace OpenScct.Net;

/// <summary>Control channel message types. All travel reliably and in order on channel 0.</summary>
public enum EControlMessage : byte
{
    /// <summary>Client to server: protocol and content hashes, player name and requested net speed.</summary>
    Hello = 0,

    /// <summary>Server to client: accepted; player id, map, server tick and replication rate.</summary>
    Welcome = 1,

    /// <summary>Server to client: refused, with the reason and (for version mismatches) the differing file.</summary>
    Reject = 2,

    /// <summary>Client to server: the map is loaded, spawn me.</summary>
    Join = 3,

    /// <summary>Either way: a line of text (admin, tests).</summary>
    Text = 4,

    /// <summary>
    /// Either way: a chat line (<see cref="ChatLine"/>). Client to server: what the player typed, Say or TeamSay.
    /// Server to client: a line to show, with the sender's player id, name and team.
    /// </summary>
    Chat = 5,

    /// <summary>
    /// Server to client, in answer to a Hello on a server that checks player keys: a random challenge (Text) for the
    /// client to sign. The Welcome or Reject comes after the client's <see cref="Proof"/>.
    /// </summary>
    Challenge = 6,

    /// <summary>Client to server: the signed challenge, "publickey signature" (Text), or empty without a key.</summary>
    Proof = 7,

    MAX = 8,
}

/// <summary>One control channel message. Which fields are used depends on <see cref="Type"/>.</summary>
public readonly record struct ControlMessage
{
    /// <summary>The original wording for a version mismatch.</summary>
    public const string VersionMismatchText = "Your game's version is not the same as the server's";

    /// <summary>Longest player name in UTF-8 bytes; longer names are cut.</summary>
    public const int MaxNameBytes = 64;

    /// <summary>
    /// Longest Hello text in UTF-8 bytes: the login URL, the player's name and its options (an online session's
    /// token, install id, side, password, and a signed-in player's master ticket of about 165 characters).
    /// </summary>
    public const int MaxLoginBytes = 1024;

    public const int MaxMapBytes = 128;

    public const int MaxReasonBytes = 256;

    /// <summary>Longest text line in UTF-8 bytes; longer lines are cut.</summary>
    public const int MaxTextBytes = 512;

    /// <summary>Longest file digest listing in a Welcome, in UTF-8 bytes.</summary>
    public const int MaxFilesBytes = 1024;

    public EControlMessage Type { get; init; }

    public ulong ProtocolHash { get; init; }

    public ulong ContentHash { get; init; }

    /// <summary>Player name (Hello), map (Welcome), reason (Reject) or the text (Text).</summary>
    public string? Text { get; init; }

    /// <summary>File that differs (Reject).</summary>
    public string? File { get; init; }

    public int PlayerId { get; init; }

    public uint ServerTick { get; init; }

    /// <summary>Server replication rate in flushes per second (Welcome).</summary>
    public int NetRate { get; init; }

    /// <summary>How the server's proxy pawns are to be shown (Welcome): the client follows the server's mode.</summary>
    public PawnProxyMode PawnProxies { get; init; }

    /// <summary>Requested net speed in bytes per second (Hello).</summary>
    public int NetSpeed { get; init; }

    /// <summary>Say or TeamSay (Chat).</summary>
    public EChatScope Scope { get; init; }

    /// <summary>The sender's name (Chat, server to client).</summary>
    public string? Sender { get; init; }

    /// <summary>The sender's team, <see cref="ChatK.NoTeam"/> without one (Chat, server to client).</summary>
    public byte Team { get; init; }

    public static ControlMessage Hello(ulong protocolHash, ulong contentHash, string playerName, int netSpeed) =>
        new() { Type = EControlMessage.Hello, ProtocolHash = protocolHash, ContentHash = contentHash, Text = playerName, NetSpeed = netSpeed };

    /// <summary>
    /// The server accepts: the player's id, the map, the server tick and replication rate, and the map's content hash
    /// with its sim file digests (<paramref name="files"/>, <c>path=sha256;...</c>) so the client can name the file
    /// that differs from its own copy.
    /// </summary>
    public static ControlMessage Welcome(int playerId, string map, uint serverTick, int netRate, ulong contentHash = 0,
        string? files = null, PawnProxyMode pawnProxies = PawnProxyMode.DeadReckoning) =>
        new()
        {
            Type = EControlMessage.Welcome, PlayerId = playerId, Text = map, ServerTick = serverTick, NetRate = netRate,
            ContentHash = contentHash, File = files, PawnProxies = pawnProxies,
        };

    public static ControlMessage Reject(string reason, string? file = null) =>
        new() { Type = EControlMessage.Reject, Text = reason, File = file };

    public static ControlMessage Join() => new() { Type = EControlMessage.Join };

    public static ControlMessage Challenge(string nonce) => new() { Type = EControlMessage.Challenge, Text = nonce };

    public static ControlMessage Proof(string? answer) => new() { Type = EControlMessage.Proof, Text = answer ?? string.Empty };

    public static ControlMessage TextLine(string text) => new() { Type = EControlMessage.Text, Text = text };

    /// <summary>A chat line a player typed (client to server); the server fills in the sender.</summary>
    public static ControlMessage Chat(EChatScope scope, string text) =>
        new() { Type = EControlMessage.Chat, Scope = scope, Text = text, Team = ChatK.NoTeam };

    /// <summary>A chat line to show (server to client).</summary>
    public static ControlMessage Chat(in ChatLine line) =>
        new()
        {
            Type = EControlMessage.Chat, Scope = line.Scope, PlayerId = line.SenderId, Sender = line.SenderName,
            Team = line.SenderTeam, Text = line.Text,
        };

    /// <summary>The chat line this message carries (Chat).</summary>
    public ChatLine ToChatLine() => new(Scope, PlayerId, Sender ?? string.Empty, Team, Text ?? string.Empty);

    internal void Write(FBitWriter w)
    {
        w.WriteInt((uint)Type, (uint)EControlMessage.MAX);
        switch (Type)
        {
            case EControlMessage.Hello:
                w.WriteUInt64(ProtocolHash);
                w.WriteUInt64(ContentHash);
                w.WriteString(Clip(Text, MaxLoginBytes), MaxLoginBytes);
                w.WriteIntPacked((uint)NetSpeed);
                break;
            case EControlMessage.Welcome:
                w.WriteIntPacked((uint)PlayerId);
                w.WriteString(Clip(Text, MaxMapBytes), MaxMapBytes);
                w.WriteUInt32(ServerTick);
                w.WriteIntPacked((uint)NetRate);
                w.WriteUInt64(ContentHash);
                w.WriteString(Clip(File, MaxFilesBytes), MaxFilesBytes);
                w.WriteIntPacked((uint)PawnProxies);
                break;
            case EControlMessage.Reject:
                w.WriteString(Clip(Text, MaxReasonBytes), MaxReasonBytes);
                w.WriteString(Clip(File, MaxReasonBytes), MaxReasonBytes);
                break;
            case EControlMessage.Text or EControlMessage.Challenge or EControlMessage.Proof:
                w.WriteString(Clip(Text, MaxTextBytes), MaxTextBytes);
                break;
            case EControlMessage.Chat:
                w.WriteInt((uint)Scope, (uint)EChatScope.MAX);
                w.WriteIntPacked((uint)Math.Max(0, PlayerId));
                w.WriteString(Clip(Sender, MaxNameBytes), MaxNameBytes);
                w.WriteByte(Team);
                w.WriteString(Clip(Text, MaxTextBytes), MaxTextBytes);
                break;
        }
    }

    /// <summary>Cuts <paramref name="text"/> to at most <paramref name="maxBytes"/> UTF-8 bytes, on a character boundary.</summary>
    internal static string Clip(string? text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text ?? string.Empty;
        }

        int length = text.Length;
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > maxBytes)
        {
            length--;
            if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            {
                length--;
            }
        }

        return text[..length];
    }

    internal static ControlMessage Read(FBitReader r)
    {
        var type = (EControlMessage)r.ReadInt((uint)EControlMessage.MAX);
        switch (type)
        {
            case EControlMessage.Hello:
            {
                ulong protocol = r.ReadUInt64();
                ulong content = r.ReadUInt64();
                string name = r.ReadString(MaxLoginBytes);
                int netSpeed = (int)r.ReadIntPacked();
                return Hello(protocol, content, name, netSpeed);
            }

            case EControlMessage.Welcome:
            {
                int playerId = (int)r.ReadIntPacked();
                string map = r.ReadString(MaxMapBytes);
                uint tick = r.ReadUInt32();
                int rate = (int)r.ReadIntPacked();
                ulong content = r.ReadUInt64();
                string files = r.ReadString(MaxFilesBytes);

                // Last and optional (one message per bunch): a server without it shows its pawns the Stock way.
                var proxies = r.AtEnd ? PawnProxyMode.DeadReckoning : (PawnProxyMode)Math.Min(r.ReadIntPacked(), (uint)PawnProxyMode.Snapshots);
                return Welcome(playerId, map, tick, rate, content, files.Length == 0 ? null : files, proxies);
            }

            case EControlMessage.Reject:
            {
                string reason = r.ReadString(MaxReasonBytes);
                string file = r.ReadString(MaxReasonBytes);
                return Reject(reason, file.Length == 0 ? null : file);
            }

            case EControlMessage.Join:
                return Join();
            case EControlMessage.Challenge:
                return Challenge(r.ReadString(MaxTextBytes));
            case EControlMessage.Proof:
                return Proof(r.ReadString(MaxTextBytes));
            case EControlMessage.Chat:
            {
                var scope = (EChatScope)r.ReadInt((uint)EChatScope.MAX);
                int sender = (int)r.ReadIntPacked();
                string name = r.ReadString(MaxNameBytes);
                byte team = r.ReadByte();
                string text = r.ReadString(MaxTextBytes);
                return Chat(new ChatLine(scope, sender, name, team, text));
            }

            default:
                return TextLine(r.ReadString(MaxTextBytes));
        }
    }
}
