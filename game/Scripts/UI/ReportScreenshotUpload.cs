using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Project;

namespace OpenScctGame.UI;

internal static class ReportScreenshotUpload
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 4096,
    };

    internal static async Task<string> Send(byte[] png, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using HttpResponseMessage response = await Client.PostAsync(ProjectLinks.Website + "/help/report-bug/capture", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        string? token = json.RootElement.GetProperty("capture").GetString();
        if (token is null || token.Length != 64) throw new JsonException("Invalid capture response.");
        foreach (char c in token) if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) throw new JsonException("Invalid capture response.");
        return token;
    }
}
