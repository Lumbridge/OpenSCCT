using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Settings;

/// <summary>Encrypts secrets for the current Windows user (DPAPI in the app; a fake in tests).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);

    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>
/// Keeps the optional GitHub token in token.dat, encrypted by <see cref="ISecretProtector"/>. The plain token is
/// never written to disk and never shown again after it is saved.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public TokenStore(string path, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(protector);
        _path = path;
        _protector = protector;
    }

    public bool HasToken => File.Exists(_path);

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            byte[] plain = _protector.Unprotect(File.ReadAllBytes(_path));
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Encrypted for another user or machine, or damaged: as if there were no token.
            return null;
        }
    }

    public void Save(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Clear();
            return;
        }

        byte[] plain = Encoding.UTF8.GetBytes(token.Trim());
        try
        {
            AtomicFile.WriteAllBytes(_path, _protector.Protect(plain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
