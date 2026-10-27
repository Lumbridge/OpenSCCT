using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenScct.Net.Discovery;

/// <summary>What a ticket's holder may do beyond playing.</summary>
[Flags]
public enum PlayerTicketFlags : byte
{
    None = 0,

    /// <summary>One of the master's admins: a trusted server makes the player a server admin.</summary>
    Admin = 1,
}

/// <summary>
/// A master-signed pass for one join of one server (docs/ONLINE.md, "Accounts"): the signed-in player's account id
/// and claimed name, the server it is for, and a short expiry. The text form is
/// <c>base64url(payload) "." base64url(signature)</c>, about 160 characters, small enough for the Hello's login
/// options. The signature is ECDsa P-256 over the payload bytes (IEEE P1363, 64 bytes).
/// </summary>
public sealed record PlayerTicket
{
    public const byte FormatVersion = 2;

    /// <summary>Tickets without the unlock bytes (still read).</summary>
    private const byte FormatVersion1 = 1;

    /// <summary>How long a ticket is good for after the master signs it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private const int MaxServerIdBytes = 32;
    private const int MaxNameBytes = 48;
    private const int MaxUnlockBytes = 64;

    public long AccountId { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>The master's id of the server the ticket is for.</summary>
    public string ServerId { get; init; } = string.Empty;

    public DateTimeOffset Expires { get; init; }

    /// <summary>Random; a server takes each ticket once.</summary>
    public ulong TicketId { get; init; }

    public PlayerTicketFlags Flags { get; init; }

    public bool IsAdmin => (Flags & PlayerTicketFlags.Admin) != 0;

    /// <summary>The cosmetics the account has unlocked, as a bit set by catalogue bit (OpenScct.Net.Cosmetics).</summary>
    public byte[] Unlocks { get; init; } = [];

    /// <summary>The ticket's text, signed with the master's private key.</summary>
    public string Sign(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        byte[] payload = Pack();
        byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Base64Url(payload) + "." + Base64Url(signature);
    }

    /// <summary>
    /// Reads and checks a ticket: the master's signature, the server it was made for and its expiry. False with a
    /// reason a log can show when any of them fails.
    /// </summary>
    public static bool TryVerify(string? text, ECDsa publicKey, string serverId, DateTimeOffset now, out PlayerTicket? ticket, out string why)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ticket = null;
        if (string.IsNullOrEmpty(text))
        {
            why = "no ticket";
            return false;
        }

        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || !TryFromBase64Url(text[..dot], out byte[]? payload) || !TryFromBase64Url(text[(dot + 1)..], out byte[]? signature))
        {
            why = "not a ticket";
            return false;
        }

        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            why = "bad signature";
            return false;
        }

        if (!TryUnpack(payload, out PlayerTicket? read))
        {
            why = "unknown ticket format";
            return false;
        }

        if (!string.Equals(read.ServerId, serverId, StringComparison.Ordinal))
        {
            why = "ticket for another server";
            return false;
        }

        if (read.Expires < now)
        {
            why = "ticket expired";
            return false;
        }

        ticket = read;
        why = string.Empty;
        return true;
    }

    /// <summary>The master's public key as it publishes it (base64 SubjectPublicKeyInfo).</summary>
    public static string ExportPublicKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>A public key from <see cref="ExportPublicKey"/>, or null when the text is not one.</summary>
    public static ECDsa? ImportPublicKey(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
            return key;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            key.Dispose();
            return null;
        }
    }

    /// <summary>A new random ticket id.</summary>
    public static ulong NewTicketId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    private byte[] Pack()
    {
        byte[] server = Encoding.ASCII.GetBytes(ServerId);
        byte[] name = Encoding.UTF8.GetBytes(Name);
        if (server.Length > MaxServerIdBytes || name.Length > MaxNameBytes || Unlocks.Length > MaxUnlockBytes)
        {
            throw new InvalidOperationException("Ticket server id, name or unlocks too long.");
        }

        byte[] data = new byte[1 + 1 + 8 + 8 + 8 + 1 + server.Length + 1 + name.Length + 1 + Unlocks.Length];
        int at = 0;
        data[at++] = FormatVersion;
        data[at++] = (byte)Flags;
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(at), AccountId);
        at += 8;
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(at), Expires.ToUnixTimeSeconds());
        at += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at), TicketId);
        at += 8;
        data[at++] = (byte)server.Length;
        server.CopyTo(data, at);
        at += server.Length;
        data[at++] = (byte)name.Length;
        name.CopyTo(data, at);
        at += name.Length;
        data[at++] = (byte)Unlocks.Length;
        Unlocks.CopyTo(data, at);
        return data;
    }

    private static bool TryUnpack(byte[] data, out PlayerTicket ticket)
    {
        ticket = new PlayerTicket();
        const int fixedPart = 1 + 1 + 8 + 8 + 8;
        if (data.Length < fixedPart + 2 || (data[0] != FormatVersion && data[0] != FormatVersion1))
        {
            return false;
        }

        int at = 1;
        var flags = (PlayerTicketFlags)data[at++];
        long account = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at));
        at += 8;
        long expires = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at));
        at += 8;
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at));
        at += 8;
        int serverLength = data[at++];
        if (at + serverLength + 1 > data.Length)
        {
            return false;
        }

        string server = Encoding.ASCII.GetString(data, at, serverLength);
        at += serverLength;
        int nameLength = data[at++];
        bool hasUnlocks = data[0] != FormatVersion1;
        if (hasUnlocks ? at + nameLength + 1 > data.Length : at + nameLength != data.Length)
        {
            return false;
        }

        string name = Encoding.UTF8.GetString(data, at, nameLength);
        at += nameLength;
        byte[] unlocks = [];
        if (hasUnlocks)
        {
            // Bytes after the unlocks are fields a newer master added; this reader skips them, so the master can add
            // fields without breaking servers that are not updated yet (new fields go at the end, never in between).
            int unlockLength = data[at++];
            if (at + unlockLength > data.Length)
            {
                return false;
            }

            unlocks = data.AsSpan(at, unlockLength).ToArray();
        }

        ticket = new PlayerTicket
        {
            AccountId = account, Name = name, ServerId = server, Expires = DateTimeOffset.FromUnixTimeSeconds(expires), TicketId = id,
            Flags = flags, Unlocks = unlocks,
        };
        return true;
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? data)
    {
        data = null;
        string b64 = text.Replace('-', '+').Replace('_', '/');
        b64 += (b64.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        try
        {
            data = Convert.FromBase64String(b64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
