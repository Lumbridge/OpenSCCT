using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Launcher.Launch;

/// <summary>Bounded, current-user-only handoff of validated server ids between local processes.</summary>
public static class WebJoinPipe
{
    public const string LauncherName = "OpenSCCT.WebJoin.Launcher";
    public const string GameName = "OpenSCCT.WebJoin.Game";

    public static async Task<bool> SendAsync(string pipeName, string serverId)
    {
        if (!WebJoinRequest.IsValidServerId(serverId)) return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(1000, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.ASCII.GetBytes(serverId + "\n"), timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            byte[] reply = new byte[3];
            int read = 0;
            while (read < reply.Length)
            {
                int count = await pipe.ReadAsync(reply.AsMemory(read), timeout.Token).ConfigureAwait(false);
                if (count == 0) return false;
                read += count;
            }
            return reply[0] == 'O' && reply[1] == 'K' && reply[2] == '\n';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task ListenAsync(Action<string> receive, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(LauncherName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 256, 256);
                await pipe.WaitForConnectionAsync(cancel).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                byte[] value = new byte[129];
                int count = 0;
                while (count < value.Length)
                {
                    int read = await pipe.ReadAsync(value.AsMemory(count, 1), timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (value[count] == '\n')
                    {
                        string serverId = Encoding.ASCII.GetString(value, 0, count);
                        if (WebJoinRequest.IsValidServerId(serverId))
                        {
                            receive(serverId);
                            await pipe.WriteAsync("OK\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
                            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                        }
                        break;
                    }
                    count++;
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                try { await Task.Delay(250, cancel).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
