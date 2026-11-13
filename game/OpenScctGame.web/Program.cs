using Godot;
using System.IO.Compression;
using HttpClient = System.Net.Http.HttpClient;
using Engine = twodog.Engine;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try { return await Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("OPENSCCT_BROWSER_BOOT_FAILED " + error);
            if (OperatingSystem.IsBrowser()) OpenScctGame.Web.BrowserExports.BootFailed(error.Message);
            return 1;
        }
    }

    private static async Task<int> Run(string[] args)
    {
        Console.WriteLine("OpenScctGame (web) starting...");
        string prefix = "--svm-browser-content-url=";
        string contentUrl = args.Single(a => a.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
        args = args.Where(a => !a.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        const string lessonPrefix = "--svm-browser-lesson=";
        string? lessonArg = args.FirstOrDefault(a => a.StartsWith(lessonPrefix, StringComparison.Ordinal));
        if (lessonArg is not null) OpenScctGame.BrowserPlatform.Lesson = lessonArg[lessonPrefix.Length..];
        args = args.Where(a => !a.StartsWith(lessonPrefix, StringComparison.Ordinal)).ToArray();
        Console.WriteLine("OPENSCCT_BROWSER_CONTENT_DOWNLOADING");
        using (var http = new HttpClient())
        using (var content = new MemoryStream(await http.GetByteArrayAsync(contentUrl)))
        {
            Console.WriteLine("OPENSCCT_BROWSER_CONTENT_EXTRACTING " + content.Length);
            ZipFile.ExtractToDirectory(content, "/content", overwriteFiles: true);
        }
        Console.WriteLine("OPENSCCT_BROWSER_CONTENT_READY");
        OpenScctGame.Online.ContentPacks.ConfigureStore(new OpenScct.Maps.ContentStore("/content", new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        }) { Source = new Uri(new Uri(contentUrl), "content/index.json").AbsoluteUri, AllowRelativeDownloads = true });
        await OpenScctGame.Web.BrowserAccount.Fetch(contentUrl);
        OpenScctGame.BrowserPlatform.RestoreSettings = () =>
        {
            OpenScctGame.Web.BrowserStorage.Restore();
            OpenScctGame.Web.BrowserAccount.Apply();
        };
        OpenScctGame.BrowserPlatform.FlushSettings = () =>
        {
            OpenScctGame.Web.BrowserStorage.Flush();
            OpenScctGame.Web.BrowserAccount.Flush();
        };
        if (OperatingSystem.IsBrowser())
        {
            OpenScctGame.Web.BrowserScoreboardCodec.Prepare();
            OpenScctGame.BrowserPlatform.ReportState = OpenScctGame.Web.BrowserExports.ReportState;
            OpenScctGame.BrowserPlatform.OpenAccount = OpenScctGame.Web.BrowserExports.OpenAccount;
            OpenScctGame.BrowserPlatform.OpenMultiplayer = OpenScctGame.Web.BrowserExports.OpenMultiplayer;
        }

        // The Godot project's assembly owns the source-generated plugins
        // initializer; register it before Start() (there is no
        // GodotPlugins.dll on web).
        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());

        // args come from the page's GODOT_CONFIG.args plus the
        // '--main-pack godot.pck' the engine loader prepends.
        var engine = new Engine("OpenScctGame", args: args);
        try
        {
            engine.Start();
            if (OperatingSystem.IsBrowser()) OpenScctGame.Web.BrowserExports.Ready();
            GD.Print("2dog is running in the browser!");
            GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);
            // The browser loop owns the lifetime after Run() returns.
            engine.Run();
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }

        return 0;
    }
}
