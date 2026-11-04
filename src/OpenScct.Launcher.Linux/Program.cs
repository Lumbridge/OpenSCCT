using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Update;

namespace OpenScct.Launcher.Linux;

internal static class Program
{
    public const string AfterUpdateArg = "--after-update";
    internal static bool SmokeUi { get; private set; }
    internal static string? Join { get; private set; }
    internal static string? Executable { get; private set; }
    internal static string? SmokeRoot { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(["--version"]))
        {
            Console.WriteLine(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "dev");
            return 0;
        }
        if (args.SequenceEqual(["--smoke"])) return LauncherSmoke.RunAsync().GetAwaiter().GetResult();
        bool afterUpdate = false;
        foreach (string arg in args)
        {
            if (arg == "--smoke-ui" && args.Length == 1) SmokeUi = true;
            else if (arg == AfterUpdateArg && !afterUpdate) afterUpdate = true;
            else if (Join is null && WebJoinRequest.TryParse(arg, out string? id)) Join = id;
            else { Console.Error.WriteLine("Usage: OpenSCCT.Launcher-linux-x64 [openscct://join/<server-id>] [--version|--smoke|--smoke-ui]"); return 2; }
        }
        if (!SmokeUi && !OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This launcher is for Linux. Use OpenSCCT.Launcher.exe on Windows.");
            return 2;
        }
        Executable = SmokeUi ? null : Environment.ProcessPath;
        if (Executable is not null)
        {
            var swap = new LauncherSwap(Executable, LauncherPlatform.LinuxX64);
            swap.CleanupAfterStart(afterUpdate ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        string root = LauncherLayout.GetDefaultRoot(LauncherPlatform.LinuxX64);
        string mutexName = "OpenSCCT.Launcher." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..20];
        using var single = new Mutex(true, SmokeUi ? mutexName + Guid.NewGuid().ToString("N") : mutexName, out bool owned);
        if (!owned && afterUpdate)
        {
            try { owned = single.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { owned = true; }
        }
        if (!owned)
        {
            if (Join is not null && !WebJoinPipe.SendAsync(WebJoinPipe.LauncherName, Join).GetAwaiter().GetResult())
            {
                Console.Error.WriteLine("The running launcher could not receive the Join request. Close it and try again.");
                return 1;
            }
            return 0;
        }
        try
        {
            if (SmokeUi) SmokeRoot = Path.Combine(Path.GetTempPath(), "openscct-launcher-ui-" + Guid.NewGuid().ToString("N"));
            return AppBuilder.Configure<LauncherApp>().UsePlatformDetect()
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Glx, X11RenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("OpenSCCT launcher could not start: " + ex.Message);
            Console.Error.WriteLine("A graphical X11 session or XWayland, fontconfig, libX11, libICE and libSM are required.");
            return 1;
        }
        finally
        {
            single.ReleaseMutex();
            if (SmokeRoot is { } temporary && Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }
}

internal sealed class LauncherApp : Application
{
    private HttpClient? _http;
    private LauncherController? _controller;
    private readonly CancellationTokenSource _lifetime = new();

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Resources["SystemAccentColor"] = Avalonia.Media.Color.Parse("#B5D866");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _http = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            }) { Timeout = TimeSpan.FromSeconds(60) };
            var layout = new LauncherLayout(Program.SmokeRoot ?? LauncherLayout.GetDefaultRoot(LauncherPlatform.LinuxX64), LauncherPlatform.LinuxX64);
            _controller = new LauncherController(layout, _http, Program.Executable);
            var window = new MainWindow(_controller, Program.SmokeUi);
            desktop.MainWindow = window;
            if (Program.Join is { } join) _controller.QueueJoin(join);
            if (!Program.SmokeUi)
                _ = WebJoinPipe.ListenAsync(id => Dispatcher.UIThread.Post(() => window.QueueJoin(id)), _lifetime.Token);
            desktop.Exit += (_, _) =>
            {
                _lifetime.Cancel();
                _controller.Dispose();
                _http.Dispose();
                _lifetime.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
