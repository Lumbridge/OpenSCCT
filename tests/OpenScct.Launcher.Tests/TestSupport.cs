using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;

namespace OpenScct.Launcher.Tests;

/// <summary>A temp folder deleted after the test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "openscct-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Answers requests from a function and records them.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

internal sealed class FakeGames : IGameProcessProbe
{
    public List<RunningGame> Games { get; } = [];

    public IReadOnlyList<RunningGame> Running() => Games.ToList();
}

/// <summary>Reversible stand-in for DPAPI.</summary>
internal sealed class XorProtector : ISecretProtector
{
    public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).Prepend((byte)1).ToArray();

    public byte[] Unprotect(byte[] protectedBytes) =>
        protectedBytes.Length > 0 && protectedBytes[0] == 1
            ? protectedBytes.Skip(1).Select(b => (byte)(b ^ 0x5A)).ToArray()
            : throw new CryptographicException("not ours");
}

internal static class Builds
{
    /// <summary>A zip that looks like an exported game build.</summary>
    public static byte[] GameZip(string version, bool wrapInFolder = false)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            string prefix = wrapInFolder ? $"OpenSCCT-{version}/" : string.Empty;
            Write(zip, prefix + "OpenSCCT.exe", "exe " + version);
            Write(zip, prefix + "OpenSCCT.console.exe", "console");
            Write(zip, prefix + "data_OpenScctGame_windows_x86_64/OpenScct.Game.dll", "dll " + version);
        }

        return memory.ToArray();
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static HttpResponseMessage Bytes(byte[] data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(data) };

    public static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text) };

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
