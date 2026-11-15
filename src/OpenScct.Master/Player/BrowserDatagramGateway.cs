using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenScct.Net;

namespace OpenScct.Master.Player;

/// <summary>Bridges a granted browser connection to one registered server. Never accepts a client-selected address.</summary>
internal static class BrowserDatagramGateway
{
    internal static async Task Run(WebSocket socket, string address, CancellationToken aborted)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        using var udp = LiteNetLibTransport.CreateClient();
        EndpointId server = udp.Connect(address);
        var frames = System.Threading.Channels.Channel.CreateBounded<byte[]>(256);
        Task receive = Read(socket, frames.Writer, stop.Token);
        try
        {
            var buffer = new byte[WebSocketTransport.DatagramLimit];
            while (!stop.IsCancellationRequested && !receive.IsCompleted && socket.State == WebSocketState.Open)
            {
                while (frames.Reader.TryRead(out byte[]? frame)) udp.Send(server, frame);
                for (int i = 0; i < 128 && udp.TryReceive(buffer, out _, out int size); i++)
                    await socket.SendAsync(buffer.AsMemory(0, size), WebSocketMessageType.Binary, true, stop.Token).ConfigureAwait(false);
                await Task.Delay(2, stop.Token).ConfigureAwait(false);
            }
            if (receive.IsCompleted) await receive.ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
        finally
        {
            stop.Cancel();
            socket.Abort();
            try { await receive.ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException or InvalidOperationException) { }
        }
    }

    private static async Task Read(WebSocket socket, ChannelWriter<byte[]> frames, CancellationToken stop)
    {
        var buffer = new byte[WebSocketTransport.DatagramLimit + 1];
        long window = Environment.TickCount64;
        int messages = 0;
        while (!stop.IsCancellationRequested)
        {
            int size = 0;
            ValueWebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer.AsMemory(size), stop).ConfigureAwait(false);
                if (part.MessageType == WebSocketMessageType.Close) return;
                size += part.Count;
                if (part.MessageType != WebSocketMessageType.Binary || size > WebSocketTransport.DatagramLimit)
                    throw new WebSocketException("Invalid game datagram.");
            } while (!part.EndOfMessage);
            long now = Environment.TickCount64;
            if (now - window >= 1000) { window = now; messages = 0; }
            if (++messages > 240 || size == 0 || !frames.TryWrite(buffer.AsSpan(0, size).ToArray()))
                throw new WebSocketException("Game connection exceeded its queue or rate limit.");
        }
    }
}
