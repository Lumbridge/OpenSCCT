using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.Launch;
using OpenScct.Maps;

internal static class MapLinkChecks
{
    public static void Run(Assembly app, string output)
    {
        using var handler = new Catalogue();
        using var http = new HttpClient(handler);
        string packs = Path.Combine(output, "map-link-" + Guid.NewGuid().ToString("N"));
        Type type = app.GetType("OpenScct.Launcher.App.MapsForm", true)!;
        using var form = (Form)Activator.CreateInstance(type, http, packs, null, null)!;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-24000, -24000);
        form.ShowInTaskbar = false;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var review = type.GetMethod("ReviewMap", flags)!;
        var list = (ListView)type.GetField("_list", flags)!.GetValue(form)!;
        var search = (TextBox)type.GetField("_search", flags)!.GetValue(form)!;
        var status = (Label)type.GetField("_status", flags)!.GetValue(form)!;
        var download = (Button)type.GetField("_download", flags)!.GetValue(form)!;
        if (!WebMapRequest.TryParse("openscct://maps/testb", out string? cold)) throw new InvalidDataException("Map URI rejected.");
        review.Invoke(form, [cold]);
        form.Show();
        PumpUntil(() => list.SelectedItems.Count == 1 && download.Enabled);
        if (list.SelectedItems[0].Text != "Map B") throw new InvalidDataException("Cold link selected the wrong row.");

        search.Text = "Map B";
        using var lifetime = new CancellationTokenSource();
        string pipe = WebJoinPipe.MapsName + ".Smoke." + Guid.NewGuid().ToString("N");
        Task listener = WebJoinPipe.ListenAsync(code => form.BeginInvoke(() => review.Invoke(form, [code])), lifetime.Token, pipe);
        try
        {
            if (!WebJoinPipe.SendAsync(pipe, "testA").GetAwaiter().GetResult()) throw new IOException("Map handoff failed.");
            PumpUntil(() => list.SelectedItems.Count == 1 && list.SelectedItems[0].Text == "Map A");
            review.Invoke(form, ["Unavailable"]);
            if (download.Enabled || list.SelectedItems.Count != 0 || !status.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unknown map left an actionable selection.");
            if (handler.Reads != 1 || Directory.Exists(Path.Combine(packs, "svmap")))
                throw new InvalidDataException("Reviewing a map attempted installation.");
            Console.WriteLine("Map review passed: cold selection, filtered warm IPC handoff, unknown code, zero installs.");
        }
        finally
        {
            lifetime.Cancel();
            listener.GetAwaiter().GetResult();
            form.Close();
        }
    }

    private static void PumpUntil(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        if (!done()) throw new TimeoutException("Map review did not reach the expected state.");
    }

    private sealed class Catalogue : HttpMessageHandler
    {
        public int Reads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (request.RequestUri?.AbsoluteUri != ContentStore.IndexUrl) throw new IOException("Unexpected map download.");
            Reads++;
            string hash = new('a', 64);
            object Map(string code, string title) => new
            {
                code, title, modes = new[] { "Story" },
                full = new { url = "https://example.org/map.zip", size = 128, sha256 = hash },
                distribution = new { kind = "community-authored", credits = "Fixture", license = "CC0", evidence = "https://example.org/credits", fullSha256 = hash, simSha256 = hash }
            };
            var catalogue = new { format = ContentIndex.Format, formatVersion = 1, maps = new[] { Map("testA", "Map A"), Map("testB", "Map B") } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(catalogue)) });
        }
    }
}
