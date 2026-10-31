using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net;

// Object references on one connection (UE2 UPackageMapLevel::SerializeObject, spec/native/net-replication.md
// rule 18) and channel lookups.
public sealed partial class NetConnection
{
    public int ReceivedReliableSequence(Actor actor) => FindActorChannel(actor) is { } channel ? InReliable[channel.ChIndex] : 0;
    /// <summary>The open actor channel of <paramref name="actor"/> on this connection, or null.</summary>
    public ActorChannel? FindActorChannel(Actor actor) =>
        ActorChannelByActorId.TryGetValue(actor.ActorId, out ActorChannel? channel) && ReferenceEquals(channel.Actor, actor) ? channel : null;

    /// <summary>True while a channel index is free for a new actor channel.</summary>
    public bool HasFreeChannelIndex => ActorChannelList.Count < K.MAX_CHANNELS - 2;

    /// <summary>
    /// Writes a reference: 0 for None, an odd code for a map actor (2 × SVMap index + 1), an even code for a
    /// dynamic actor (2 × net id + 2). A dynamic actor counts as mapped only when its channel on this connection is
    /// open and acknowledged; otherwise None goes out and <paramref name="mapped"/> is false so the caller retries.
    /// Objects that are not actors cannot be named and go as None.
    /// </summary>
    [UcNative("UPackageMapLevel::SerializeObject", Exe = 0x109ee320)]
    public void WriteObject(FBitWriter writer, UObject? value, out bool mapped)
    {
        if (value is not Actor actor || actor.bDeleteMe)
        {
            writer.WriteIntPacked(0);
            mapped = true;
            return;
        }

        int mapIndex = actor.bStatic || actor.bNoDelete ? _driver.MapBinding.IndexOf(actor) : -1;
        if (mapIndex >= 0)
        {
            writer.WriteIntPacked((uint)(mapIndex * 2 + 1));
            mapped = true;
            return;
        }

        ActorChannel? channel = FindActorChannel(actor);
        if (channel is not null && !channel.Closing && (channel.OpenAcked || !_driver.IsServer))
        {
            writer.WriteIntPacked(channel.NetGuid * 2u + 2u);
            mapped = true;
            return;
        }

        writer.WriteIntPacked(0);
        mapped = !_driver.IsServer;
    }

    public UObject? ReadObject(FBitReader reader)
    {
        uint code = reader.ReadIntPacked();
        if (code == 0 || reader.IsError)
        {
            return null;
        }

        if ((code & 1) != 0)
        {
            return _driver.MapBinding.Get((int)((code - 1) / 2));
        }

        uint guid = (code - 2) / 2;
        return _driver.IsServer ? _driver.World?.Level.FindActor((int)guid) : _driver.FindActorByNetGuid(guid);
    }
}
