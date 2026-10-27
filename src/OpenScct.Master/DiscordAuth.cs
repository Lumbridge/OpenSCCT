using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>Discord said no or could not be reached; the message is for the player.</summary>
internal sealed class DiscordAuthException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The OAuth2 code flow with Discord (scope <c>identify</c>): the authorize page the browser goes to, and the code
/// exchange that gives the player's Discord user id. Nothing else of the Discord account is read or kept.
/// </summary>
internal static class DiscordAuth
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("svm-master/" + BuildInfo.Version.Split('+')[0]);
        return http;
    }

    /// <summary>Discord's authorize page for the sign-in <paramref name="state"/> (the link code).</summary>
    public static string AuthorizeUrl(MasterOptions options, string state) =>
        options.Discord.AuthorizeUrl + "?client_id=" + Uri.EscapeDataString(options.Discord.ClientId ?? string.Empty) +
        "&response_type=code&redirect_uri=" + Uri.EscapeDataString(options.DiscordRedirectUrl) +
        "&scope=identify&state=" + Uri.EscapeDataString(state) + "&prompt=none";

    /// <summary>Trades the callback's <paramref name="code"/> for a token and reads the user's id with it.</summary>
    public static async Task<string> IdentifyAsync(MasterOptions options, string code, CancellationToken cancellationToken)
    {
        string api = options.Discord.ApiUrl.TrimEnd('/');
        string accessToken;
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = options.DiscordRedirectUrl,
                ["client_id"] = options.Discord.ClientId ?? string.Empty, ["client_secret"] = options.Discord.ClientSecret ?? string.Empty,
            });
            using HttpResponseMessage tokenResponse = await Http.PostAsync(new Uri(api + "/oauth2/token"), form, cancellationToken).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                throw new DiscordAuthException($"Discord refused the sign-in (HTTP {(int)tokenResponse.StatusCode}). Start again from the game.");
            }

            using JsonDocument tokenJson = await ReadJsonAsync(tokenResponse, cancellationToken).ConfigureAwait(false);
            accessToken = tokenJson.RootElement.TryGetProperty("access_token", out JsonElement t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? string.Empty
                : string.Empty;
            if (accessToken.Length == 0)
            {
                throw new DiscordAuthException("Discord sent no sign-in token.");
            }

            using var me = new HttpRequestMessage(HttpMethod.Get, new Uri(api + "/users/@me"));
            me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage meResponse = await Http.SendAsync(me, cancellationToken).ConfigureAwait(false);
            if (!meResponse.IsSuccessStatusCode)
            {
                throw new DiscordAuthException($"Discord did not say who you are (HTTP {(int)meResponse.StatusCode}).");
            }

            using JsonDocument meJson = await ReadJsonAsync(meResponse, cancellationToken).ConfigureAwait(false);
            string? id = meJson.RootElement.TryGetProperty("id", out JsonElement i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            if (!MasterOptions.IsSnowflake(id))
            {
                throw new DiscordAuthException("Discord sent an account id this master does not understand.");
            }

            return id!;
        }
        catch (HttpRequestException e)
        {
            throw new DiscordAuthException("Discord could not be reached. Try again in a moment.", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DiscordAuthException("Discord took too long to answer. Try again in a moment.", e);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            System.IO.Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException e)
        {
            throw new DiscordAuthException("Discord sent an answer this master does not understand.", e);
        }
    }
}
