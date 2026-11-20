using System.Net;
using System.Text.Json;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;

if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "index.html")))
{
    Console.Error.WriteLine("Usage: browser-lab <browser AppBundle directory>");
    return 2;
}
string bundle = Path.GetFullPath(args[0]);
using var store = AccountStore.Open(":memory:");
long account = store.GetOrCreate("local-browser-fixture").Id;
store.ClaimName(account, "BrowserFixture", false, out _);
store.PutProfile(account, new ProfileData { Updated = DateTimeOffset.UtcNow,
    Data = "{\"version\":1,\"settings\":{\"player_name\":\"Browser Fixture\",\"invert_y\":false}}" });
long other = store.GetOrCreate("local-browser-other").Id;
store.ClaimName(other, "BrowserOther", false, out _);
store.PutProfile(other, new ProfileData { Updated = DateTimeOffset.UtcNow,
    Data = "{\"version\":1,\"settings\":{\"player_name\":\"Other Fixture\",\"invert_y\":false}}" });
string listen = Environment.GetEnvironmentVariable("SVM_BROWSER_LAB_LISTEN") ?? "http://127.0.0.1:8878";
if (!Uri.TryCreate(listen, UriKind.Absolute, out var listenUri) || listenUri.Scheme != "http" || !listenUri.IsLoopback)
    throw new ArgumentException("The disposable browser lab must listen on loopback HTTP.");
var options = new MasterOptions { PublicHost = "127.0.0.1", HttpUrl = listen,
    PublicUrl = Environment.GetEnvironmentVariable("SVM_BROWSER_LAB_ORIGIN") ?? listen };
var registry = new ServerRegistry(options);
string labRoot = Path.GetFullPath(Path.Combine(bundle, "../../../local/browser-lab-content"));
Directory.CreateDirectory(labRoot);
System.IO.Compression.ZipFile.ExtractToDirectory(Path.Combine(bundle, "content.zip"), labRoot, overwriteFiles: true);
using var signer = TicketSigner.LoadOrCreate(Path.Combine(labRoot, "lab-ticket.pem"));
var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls(options.HttpUrl);
builder.Logging.ClearProviders();
await using var app = builder.Build();
var players = new PlayerPortal(options, store, registry, _ => IPAddress.Loopback,
    new ReleaseChangelog(), browserGameRoot: bundle, browserTickets: signer,
    browserContentRoot: Path.GetFullPath(Path.Combine(bundle, "../../../local/browser-content")), enableBrowserPrototype: true);
players.Map(app);
new AccountApi(options, store, signer, registry, _ => IPAddress.Loopback, TimeProvider.System)
{
    BrowserAuthenticate = players.AuthenticateGameApi,
    BrowserSignOut = players.SignOutGameApi,
}.Map(app);
app.MapPost(MasterApi.HeartbeatPath, async (HttpContext context) =>
{
    var request = (await context.Request.ReadFromJsonAsync<HeartbeatRequest>(MasterApi.Json, context.RequestAborted))!;
    var record = registry.Heartbeat(request, IPAddress.Loopback, out string? error, out int status);
    if (record is null) return Results.Json(new { error }, statusCode: status);
    record.TrustedName = "Local browser lab";
    return Results.Json(new HeartbeatResponse { ServerId = record.Id, HostToken = OpenScct.Net.RelayFrames.TokenText(record.HostToken),
        PublicAddress = "127.0.0.1", HeartbeatSeconds = 1, Listed = true, Trusted = true, TicketKey = signer.PublicKey }, MasterApi.Json);
});
WebHtml.MapAssets(app);
app.MapGet("/fixture", () => Results.Content("<!doctype html><meta charset=utf-8><title>OpenSCCT browser lab</title><h1>Local browser test account</h1>" +
    "<p>This is a synthetic account in an in-memory database.</p><form method=post action=/fixture/login><button>Sign in as BrowserFixture</button></form>" +
    "<form method=post action=/fixture/other><button>Sign in as BrowserOther</button></form>" +
    "<form method=post action=/fixture/guest><button>Continue as guest</button></form>" +
    "<p><a href=/play/>Play as guest</a> · <a href=/fixture/profile>Inspect saved test profile</a></p>", "text/html"));
app.MapPost("/fixture/login", (HttpContext context) =>
{
    context.Response.Cookies.Append(PlayerPortal.SessionCookie, store.CreatePlayerWebSession(account),
        new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromHours(1) });
    return Results.Redirect("/play/");
});
app.MapPost("/fixture/other", (HttpContext context) =>
{
    context.Response.Cookies.Append(PlayerPortal.SessionCookie, store.CreatePlayerWebSession(other),
        new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromHours(1) });
    return Results.Redirect("/play/");
});
app.MapPost("/fixture/guest", (HttpContext context) =>
{
    context.Response.Cookies.Delete(PlayerPortal.SessionCookie);
    return Results.Redirect("/play/");
});
app.MapGet("/fixture/profile", () => Results.Json(store.Profile(account), MasterApi.Json));
string evidence = Path.GetFullPath(Path.Combine(bundle, "../../../local/browser-lab-evidence"));
Directory.CreateDirectory(evidence);
var logGate = new object();
app.MapPost("/__log", async (HttpContext context) =>
{
    if (context.Request.ContentLength > 65536) return Results.BadRequest();
    using var record = await JsonDocument.ParseAsync(context.Request.Body);
    lock (logGate) File.AppendAllText(Path.Combine(evidence, "console.ndjson"), record.RootElement.GetRawText() + "\n");
    return Results.NoContent();
});
Console.WriteLine($"Browser lab: {options.PublicUrl}/fixture (synthetic local account only)");
await using var match = new BrowserLabMatch(labRoot, options.HttpUrl, app.Lifetime.ApplicationStopping);
app.MapGet("/fixture/match", () => Results.Json(new { native = match.Native.State.ToString(), nativeError = match.Native.Error,
    nativeSession = match.Native.Session is { } s ? new { state = s.State.ToString(), s.ReportedWaiting, tick = s.LocalTick,
        controller = s.LocalPlayer?.GetStateName().ToString(), pawn = s.LocalPlayer?.Pawn?.Location.ToString(),
        error = s.Connection?.CloseReason } : null, server = match.Server.Status(),
    players = match.Server.Players.Select(p => new { p.Name, p.Verified, p.AccountId, p.Team,
        waiting = p.Slot?.Controller?.PlayerReplicationInfo?.bWaitingPlayer,
        state = p.Slot?.Controller?.GetStateName().ToString(), pawn = p.Slot?.Controller?.Pawn?.Location.ToString() }).ToArray() }));
await app.RunAsync();
return 0;
