using System;
using System.Globalization;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace OpenScct.Server.Online;

/// <summary>
/// A player's signed key (FEATURES rl:ac.bans): each install makes an Ed25519 key pair once and keeps it. The public
/// key is the player's id for bans; joining a server, the game signs the server's challenge with the private key
/// (<see cref="AnswerChallenge"/>), so nobody can play under another player's key. Nothing about the PC is in it (no
/// hardware fingerprints); a player can make a new key, as he can reset his install id.
/// Keys and signatures are written as base64url without padding (43 and 86 characters).
/// </summary>
public sealed class PlayerKey
{
    private readonly Ed25519PrivateKeyParameters _private;

    private PlayerKey(Ed25519PrivateKeyParameters key)
    {
        _private = key;
        PublicKey = Encode(key.GeneratePublicKey().GetEncoded());
    }

    /// <summary>The public key, base64url: the player's id on servers.</summary>
    public string PublicKey { get; }

    /// <summary>The private key, base64url, to keep in the player's settings.</summary>
    public string PrivateKey => Encode(_private.GetEncoded());

    /// <summary>A new key pair from the system's random source.</summary>
    public static PlayerKey Create() => new(new Ed25519PrivateKeyParameters(new SecureRandom()));

    /// <summary>The key pair of a saved private key, or null when the text is not one.</summary>
    public static PlayerKey? FromPrivate(string? text)
    {
        byte[]? bytes = Decode(text);
        return bytes is { Length: Ed25519.KeySize } ? new PlayerKey(new Ed25519PrivateKeyParameters(bytes)) : null;
    }

    /// <summary>The text a player signs to join: the server's challenge and the connection's salt, so a proof is good once.</summary>
    public static string ChallengeText(string nonce, uint salt) =>
        string.Create(CultureInfo.InvariantCulture, $"OpenSCCT join|{nonce}|{salt:x8}");

    /// <summary>The answer to a server's join challenge: "publickey signature".</summary>
    public string AnswerChallenge(string nonce, uint salt) => PublicKey + " " + Sign(ChallengeText(nonce, salt));

    public string Sign(string message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _private);
        byte[] data = Encoding.UTF8.GetBytes(message);
        signer.BlockUpdate(data, 0, data.Length);
        return Encode(signer.GenerateSignature());
    }

    /// <summary>True when <paramref name="signature"/> is <paramref name="publicKey"/>'s signature of <paramref name="message"/>.</summary>
    public static bool Verify(string? publicKey, string message, string? signature)
    {
        byte[]? key = Decode(publicKey);
        byte[]? sig = Decode(signature);
        if (key is not { Length: Ed25519.KeySize } || sig is not { Length: Ed25519.SignatureSize })
        {
            return false;
        }

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(key));
            byte[] data = Encoding.UTF8.GetBytes(message);
            verifier.BlockUpdate(data, 0, data.Length);
            return verifier.VerifySignature(sig);
        }
        catch (ArgumentException)
        {
            // Not a point on the curve.
            return false;
        }
    }

    /// <summary>
    /// Checks a join challenge's answer (<see cref="AnswerChallenge"/>): the proven public key, or null with the reason.
    /// An empty answer is a client without a key: null with an empty reason.
    /// </summary>
    public static string? CheckAnswer(string? answer, string nonce, uint salt, out string problem)
    {
        problem = string.Empty;
        string text = answer?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        int space = text.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || !Verify(text[..space], ChallengeText(nonce, salt), text[(space + 1)..]))
        {
            problem = "The player key check failed.";
            return null;
        }

        return text[..space];
    }

    /// <summary>A short form of a public key for logs and the admin's lists (its first 12 characters).</summary>
    public static string Short(string publicKey) => publicKey.Length > 12 ? publicKey[..12] : publicKey;

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Decode(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 200)
        {
            return null;
        }

        string b64 = text.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static class Ed25519
    {
        public const int KeySize = 32;

        public const int SignatureSize = 64;
    }
}
