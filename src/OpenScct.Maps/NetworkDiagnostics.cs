using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OpenScct.Maps;

/// <summary>Useful diagnostics for caught download failures, including TLS inner exceptions.</summary>
public static class NetworkDiagnostics
{
    private static readonly object LogGate = new();
    private const long MaxLogBytes = 1024 * 1024;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSCCT", "logs", "network.log");

    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var messages = new List<string>();
        for (Exception? current = error is AggregateException aggregate ? aggregate.GetBaseException() : error;
             current is not null; current = current.InnerException)
        {
            string message = current.Message.Trim();
            if (message.Length > 0 && !messages.Exists(existing => existing.Contains(message, StringComparison.Ordinal)))
            {
                messages.Add(message);
            }
        }
        return string.Join(" — ", messages);
    }

    /// <summary>Writes stderr and a bounded persistent log. Never replaces the original download failure.</summary>
    public static void Report(string operation, Exception error)
    {
        string text = string.Create(CultureInfo.InvariantCulture,
            $"[{DateTimeOffset.UtcNow:O}] OpenSCCT {operation}\n{error}\n" +
            $"TLS trust: SSL_CERT_FILE={Environment.GetEnvironmentVariable("SSL_CERT_FILE") ?? "(default)"}; " +
            $"SSL_CERT_DIR={Environment.GetEnvironmentVariable("SSL_CERT_DIR") ?? "(default)"}\n");
        lock (LogGate)
        {
            try
            {
                Console.Error.WriteLine(text);
                Console.Error.Flush();
            }
            catch (IOException) { }
            try
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
                {
                    File.Move(path, Path.ChangeExtension(path, "previous.log"), overwrite: true);
                }
                File.AppendAllText(path, text + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
