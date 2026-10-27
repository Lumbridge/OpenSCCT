using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OpenScct.Master;

/// <summary>
/// svm-master: the server list, NAT punch introducer, relay and account server for a VPS.
/// <code>
/// svm-master [--config svm-master.json]
/// svm-master admin &lt;command&gt; [args] [--config svm-master.json]
/// </code>
/// Settings come from the <c>Master</c> section of the config file (default <c>svm-master.json</c> next to the
/// executable, then in the working folder), then from <c>SVM_</c> environment variables
/// (<c>SVM_Master__PublicHost=203.0.113.7</c>, <c>SVM_Master__Discord__ClientSecret=...</c>). The admin commands
/// (<see cref="AdminCommands"/>) open only the account database.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool admin = args.Length > 0 && args[0] == "admin";
        string? configPath = null;
        var rest = new List<string>();
        for (int i = admin ? 1 : 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Length:
                    configPath = args[++i];
                    break;
                case "--help" or "-h" when !admin:
                    Console.WriteLine("Usage: svm-master [--config <file>]   (see svm-master.example.json and docs/ONLINE.md)");
                    Console.WriteLine("       svm-master admin <command> [args] [--config <file>]   (svm-master admin help)");
                    return 0;
                default:
                    if (!admin)
                    {
                        Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                        return 2;
                    }

                    rest.Add(args[i]);
                    break;
            }
        }

        if (LoadOptions(configPath) is not { } options)
        {
            return 2;
        }

        if (admin)
        {
            return AdminCommands.Run(rest, options, Console.Out, Console.Error);
        }

        try
        {
            await using MasterHost master = await MasterHost.StartAsync(options).ConfigureAwait(false);
            await master.WaitForShutdownAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            return 3;
        }

        return 0;
    }

    /// <summary>The options from the config file and the environment, or null (with a message) when the file is missing.</summary>
    private static MasterOptions? LoadOptions(string? configPath)
    {
        configPath ??= FindDefaultConfig();
        var config = new ConfigurationBuilder();
        string? folder = null;
        if (configPath is not null)
        {
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"Config file '{configPath}' not found.");
                return null;
            }

            string full = Path.GetFullPath(configPath);
            folder = Path.GetDirectoryName(full);
            config.AddJsonFile(full, optional: false);
        }

        config.AddEnvironmentVariables("SVM_");
        var options = new MasterOptions();
        config.Build().GetSection("Master").Bind(options);
        options.DataDirectory ??= folder;
        return options;
    }

    private static string? FindDefaultConfig()
    {
        foreach (string dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            string path = Path.Combine(dir, "svm-master.json");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
