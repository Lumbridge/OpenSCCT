using System;
using System.IO;
using System.Security.Cryptography;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// The master's ticket key: an ECDsa P-256 private key in a PEM file, made on the first start. Servers check
/// <see cref="PlayerTicket"/>s with its public half (<see cref="PublicKey"/>, sent with every heartbeat answer).
/// </summary>
internal sealed class TicketSigner : IDisposable
{
    private readonly ECDsa _key;

    private TicketSigner(ECDsa key)
    {
        _key = key;
        PublicKey = PlayerTicket.ExportPublicKey(key);
    }

    /// <summary>The public key as base64 SubjectPublicKeyInfo.</summary>
    public string PublicKey { get; }

    /// <summary>Reads the key at <paramref name="path"/>, or makes and saves a new one (readable by the owner only).</summary>
    public static TicketSigner LoadOrCreate(string path)
    {
        var key = ECDsa.Create();
        try
        {
            if (File.Exists(path))
            {
                key.ImportFromPem(File.ReadAllText(path));
                if (key.KeySize != 256)
                {
                    throw new InvalidOperationException($"The ticket key in '{path}' is not a P-256 key.");
                }
            }
            else
            {
                key.GenerateKey(ECCurve.NamedCurves.nistP256);
                string? folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(file))
                {
                    if (OperatingSystem.IsLinux())
                    {
                        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }

                    writer.Write(key.ExportPkcs8PrivateKeyPem());
                }
            }

            return new TicketSigner(key);
        }
        catch (CryptographicException e)
        {
            key.Dispose();
            throw new InvalidOperationException($"The ticket key '{path}' could not be read: {e.Message}", e);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Signs <paramref name="ticket"/>.</summary>
    public string Sign(PlayerTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_key)
        {
            return ticket.Sign(_key);
        }
    }

    public void Dispose() => _key.Dispose();
}
