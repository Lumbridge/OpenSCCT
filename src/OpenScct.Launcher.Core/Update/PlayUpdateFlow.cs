using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;

namespace OpenScct.Launcher.Update;

public enum PlayUpdateChoice
{
    Update,
    PlayCurrent,
    Cancel,
}

/// <summary>Checks for a release before Play without making internet access a requirement to launch.</summary>
public static class PlayUpdateFlow
{
    /// <summary>
    /// Returns true to launch the installed game. An accepted update returns to the launcher when it finishes.
    /// The check only reads release information; downloads start only after the player chooses Update.
    /// </summary>
    public static async Task<bool> RunAsync(
        Func<CancellationToken, Task<UpdateCheck>> check,
        Func<UpdateCheck, PlayUpdateChoice> choose,
        Func<CancellationToken, Task> update,
        CancellationToken cancel,
        TimeSpan? checkTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(choose);
        ArgumentNullException.ThrowIfNull(update);
        cancel.ThrowIfCancellationRequested();

        UpdateCheck result;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            timeout.CancelAfter(checkTimeout ?? TimeSpan.FromSeconds(5));
            try
            {
                // Keep the caller's context: the choice and update callbacks may show UI.
                result = await check(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancel.IsCancellationRequested)
            {
                cancel.ThrowIfCancellationRequested();
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                cancel.ThrowIfCancellationRequested();
                return true;
            }
        }

        cancel.ThrowIfCancellationRequested();
        if (result.Feed.Source is ReleaseFeedSource.Cache or ReleaseFeedSource.None || !result.GameUpdateAvailable)
        {
            return true;
        }

        PlayUpdateChoice choice = choose(result);
        cancel.ThrowIfCancellationRequested();
        switch (choice)
        {
            case PlayUpdateChoice.PlayCurrent:
                return true;
            case PlayUpdateChoice.Cancel:
                return false;
            case PlayUpdateChoice.Update:
                // Update failures must reach the launcher; they are not a failed release check.
                await update(cancel);
                cancel.ThrowIfCancellationRequested();
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(choice));
        }
    }
}
