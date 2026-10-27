using System;
using System.Text;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>The chat control message: both directions round trip, long fields are cut, and it leaves the others alone.</summary>
public sealed class ChatMessageTests
{
    [Fact]
    public void ChatLinesRoundTripInBothDirections()
    {
        ControlMessage typed = ControlMessage.Chat(EChatScope.TeamSay, "cover the lab");
        ControlMessage relayed = ControlMessage.Chat(new ChatLine(EChatScope.Say, 12, "Shadownet", 1, "gg"));
        var w = new FBitWriter(8 * 1024);
        typed.Write(w);
        ControlMessage.TextLine("after").Write(w);
        relayed.Write(w);

        var r = new FBitReader(w.Buffer, w.NumBits);
        ControlMessage a = ControlMessage.Read(r);
        Assert.Equal(EControlMessage.Chat, a.Type);
        Assert.Equal(EChatScope.TeamSay, a.Scope);
        Assert.Equal("cover the lab", a.Text);
        Assert.Equal(ChatK.NoTeam, a.Team);
        Assert.Equal("after", ControlMessage.Read(r).Text);
        ChatLine b = ControlMessage.Read(r).ToChatLine();
        Assert.Equal(new ChatLine(EChatScope.Say, 12, "Shadownet", 1, "gg"), b);
        Assert.Equal("Shadownet: gg", b.Display);
        Assert.False(r.IsError);
        Assert.True(r.AtEnd);
    }

    [Fact]
    public void LongNamesAndTextsAreCutOnCharacterBoundaries()
    {
        string name = string.Concat(System.Linq.Enumerable.Repeat("Sam\U0001F600", 40));
        string text = new('y', 4000);
        var w = new FBitWriter(16 * 1024);
        ControlMessage.Chat(new ChatLine(EChatScope.Say, 3, name, 0, text)).Write(w);
        ControlMessage back = ControlMessage.Read(new FBitReader(w.Buffer, w.NumBits));
        Assert.True(Encoding.UTF8.GetByteCount(back.Sender!) <= ControlMessage.MaxNameBytes);
        Assert.StartsWith(back.Sender!, name, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(back.Sender![^1]));
        Assert.Equal(ControlMessage.MaxTextBytes, back.Text!.Length);
    }

    [Fact]
    public void ChatTravelsOnAHarshLinkAndArrivesOnceInOrder()
    {
        var harsh = new SimLinkProfile("harsh", 80, 30, 20f, DuplicatePercent: 5f, ReorderPercent: 10f, ReorderDelayMs: 40);
        using var rig = new NetRig(harsh, 7, 1);
        for (int wait = 0; wait < 600 && (rig.Server.ClientConnections.Count == 0 || rig.ServerSide(0).State != EConnectionState.USOCK_Open); wait++)
        {
            rig.Step();
        }

        for (int i = 0; i < 20; i++)
        {
            rig.Clients[0].ToServer.ControlChannel!.Send(ControlMessage.Chat(EChatScope.Say, "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            rig.Step();
        }

        rig.Step(60 * 10);
        Assert.Equal(20, rig.ServerNotify.Chats.Count);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal("line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), rig.ServerNotify.Chats[i].Text);
        }
    }
}
