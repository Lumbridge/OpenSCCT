using System;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class PawnSoundReplication
{
    private const string DemoSoundNamePrefix = "#demo-checkpoint-sound-name ";
    private const int SoundNameBytes = 256;

    internal bool WriteDemoCheckpointNames(NetConnection source, NetConnection bootstrap)
    {
        if (bootstrap.ControlChannel is not { } control) return false;
        if (!_viewers.TryGetValue(source, out Viewer? viewer)) return true;
        foreach (var pair in _outNames)
            if (viewer.Names[pair.Value])
                control.Send(ControlMessage.TextLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DemoSoundNamePrefix}{pair.Value} {pair.Key}")));
        return bootstrap.State == EConnectionState.USOCK_Open;
    }

    internal static bool RestoreDemoCheckpointName(NetDriver driver, string text)
    {
        if (!text.StartsWith(DemoSoundNamePrefix, StringComparison.Ordinal)) return false;
        ReadOnlySpan<char> encoded = text.AsSpan(DemoSoundNamePrefix.Length);
        int separator = encoded.IndexOf(' ');
        if (separator <= 0 || !ushort.TryParse(encoded[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out ushort id) ||
            id is 0 or >= Capacity || Encoding.UTF8.GetByteCount(encoded[(separator + 1)..]) > SoundNameBytes)
            return true;
        Of(driver)._inNames[id] = Name.FromSpan(encoded[(separator + 1)..]);
        return true;
    }
}
