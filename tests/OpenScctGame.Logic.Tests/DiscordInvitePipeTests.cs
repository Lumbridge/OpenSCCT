using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DiscordRPC;
using DiscordRPC.IO;
using DiscordRPC.Logging;
using OpenScctGame.Discord;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class DiscordInvitePipeTests
{
    [Fact]
    public async Task RealClientPresenceKeepsJoinablePartyWithoutRetiredMatchSecret()
    {
        using var transport = new FakePipe();
        using var client = new DiscordRpcClient("123456789", 0, null, true, new DiscordInvitePipe(transport));
        // The fake transport never opens Discord or registers a launch command on this machine.
        typeof(DiscordRpcClient).GetProperty(nameof(DiscordRpcClient.HasRegisteredUriScheme))!
            .SetValue(client, true);
        Assert.True(client.Initialize());

        client.SetPresence(new RichPresence
        {
            Details = "In the lobby of Café",
            Assets = new Assets { LargeImageKey = "logo" },
            Party = new Party { ID = "test-party", Size = 1, Max = 4, Privacy = Party.PrivacySetting.Public },
            Secrets = new Secrets { JoinSecret = "oscct1|m|test-master-id" },
        });

        PipeFrame written = await transport.Activity.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        JsonObject payload = JsonNode.Parse(written.Data)!.AsObject();
        JsonObject activity = payload["args"]!["activity"]!.AsObject();
        Assert.Equal("SET_ACTIVITY", payload["cmd"]!.GetValue<string>());
        Assert.Equal("In the lobby of Café", activity["details"]!.GetValue<string>());
        Assert.Equal("logo", activity["assets"]!["large_image"]!.GetValue<string>());
        Assert.Equal("test-party", activity["party"]!["id"]!.GetValue<string>());
        Assert.Equal(1, activity["party"]!["size"]![0]!.GetValue<int>());
        Assert.Equal(4, activity["party"]!["size"]![1]!.GetValue<int>());
        Assert.Equal(1, activity["party"]!["privacy"]!.GetValue<int>());
        JsonObject secrets = activity["secrets"]!.AsObject();
        Assert.False(secrets.ContainsKey("MatchSecret"));
        Assert.False(secrets.ContainsKey("match"));
        Assert.Equal("oscct1|m|test-master-id", secrets["join"]!.GetValue<string>());
        Assert.Equal((uint)Encoding.UTF8.GetByteCount(written.Message), written.Length);
    }

    [Fact]
    public void RetiredFieldsAreRemovedOnlyFromActivitySecrets()
    {
        using var transport = new FakePipe();
        using var pipe = new DiscordInvitePipe(transport);
        PipeFrame frame = Frame(Opcode.Frame,
            """{"cmd":"SET_ACTIVITY","nonce":"8","args":{"activity":{"details":"match","match":"keep","secrets":{"MatchSecret":null,"match":"old","join":"join-me","spectate":"keep"}}}}""");

        Assert.True(pipe.WriteFrame(frame));

        JsonObject sent = JsonNode.Parse(transport.LastWritten.Data)!.AsObject();
        Assert.Equal("8", sent["nonce"]!.GetValue<string>());
        Assert.Equal("keep", sent["args"]!["activity"]!["match"]!.GetValue<string>());
        Assert.Equal("""{"join":"join-me","spectate":"keep"}""",
            sent["args"]!["activity"]!["secrets"]!.ToJsonString());
    }

    [Theory]
    [InlineData(Opcode.Frame, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"activity\":null}}")]
    [InlineData(Opcode.Frame, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"activity\":{\"details\":\"Menus\"}}}")]
    [InlineData(Opcode.Frame, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"activity\":{\"secrets\":{\"join\":\"valid\"}}}}")]
    [InlineData(Opcode.Frame, "{\"cmd\":\"SUBSCRIBE\",\"args\":{\"MatchSecret\":null}}")]
    [InlineData(Opcode.Handshake, "{\"v\":1,\"client_id\":\"123456789\"}")]
    [InlineData(Opcode.Ping, "ping")]
    [InlineData(Opcode.Frame, "unexpected non-JSON frame")]
    public void OtherFramesKeepTheirExactBytes(Opcode opcode, string message)
    {
        using var transport = new FakePipe();
        using var pipe = new DiscordInvitePipe(transport);
        PipeFrame frame = Frame(opcode, message);

        Assert.True(pipe.WriteFrame(frame));

        Assert.Equal(opcode, transport.LastWritten.Opcode);
        Assert.Same(frame.Data, transport.LastWritten.Data);
    }

    [Fact]
    public void ConnectionReadAndWriteFailureAreDelegated()
    {
        using var transport = new FakePipe();
        using var pipe = new DiscordInvitePipe(transport);
        var logger = new NullLogger();
        pipe.Logger = logger;
        Assert.Same(logger, transport.Logger);
        Assert.True(pipe.Connect(3));
        Assert.True(pipe.IsConnected);
        Assert.Equal(3, pipe.ConnectedPipe);
        PipeFrame incoming = Frame(Opcode.Frame, "{\"evt\":\"ACTIVITY_JOIN\",\"data\":{\"secret\":\"join-me\"}}");
        transport.Incoming.Enqueue(incoming);
        Assert.True(pipe.ReadFrame(out PipeFrame read));
        Assert.Same(incoming.Data, read.Data);
        transport.WriteSucceeds = false;
        Assert.False(pipe.WriteFrame(Frame(Opcode.Pong, "pong")));
        pipe.Close();
        Assert.False(pipe.IsConnected);
        pipe.Dispose();
        Assert.True(transport.Disposed);
    }

    private static PipeFrame Frame(Opcode opcode, string message) => new() { Opcode = opcode, Message = message };

    private sealed class FakePipe : INamedPipeClient
    {
        public ILogger Logger { get; set; } = new NullLogger();
        public bool IsConnected { get; private set; }
        public int ConnectedPipe { get; private set; } = -1;
        public bool Disposed { get; private set; }
        public bool WriteSucceeds { get; set; } = true;
        public PipeFrame LastWritten { get; private set; }
        public ConcurrentQueue<PipeFrame> Incoming { get; } = new();
        public TaskCompletionSource<PipeFrame> Activity { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Connect(int pipe)
        {
            IsConnected = true;
            ConnectedPipe = pipe;
            return true;
        }

        public bool ReadFrame(out PipeFrame frame) => Incoming.TryDequeue(out frame);

        public bool WriteFrame(PipeFrame frame)
        {
            LastWritten = frame;
            if (frame.Opcode == Opcode.Handshake)
            {
                Incoming.Enqueue(Frame(Opcode.Frame,
                    """{"cmd":"DISPATCH","evt":"READY","data":{"v":1,"config":{"cdn_host":"cdn.discordapp.com","api_endpoint":"//discord.com/api","environment":"production"},"user":{"id":"123456789","username":"fixture","discriminator":"0001"}}}"""));
            }
            else if (frame.Opcode == Opcode.Frame && frame.Message.Contains("\"SET_ACTIVITY\"", StringComparison.Ordinal) &&
                JsonNode.Parse(frame.Data)?["args"]?["activity"]?["secrets"]?["join"] is not null)
            {
                Activity.TrySetResult(frame);
            }

            return WriteSucceeds;
        }

        public void Close() => IsConnected = false;
        public void Dispose()
        {
            Disposed = true;
            Close();
        }
    }
}
