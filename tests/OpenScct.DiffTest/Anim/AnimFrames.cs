using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenScct.Game.Movement;

namespace OpenScct.DiffTest.Anim;

/// <summary>
/// One animation channel of a pawn in one frame, with the recorder's fields (spec/native/ANSWERS-anim.md A7): the
/// sequence, its normalised frame, AnimRate, the raw blend alpha (0 for channel 0 in a full record) and, when
/// recorded, the tween weight and the tween rate.
/// </summary>
public sealed record AnimChannel(int Channel, string Sequence, double Frame, double Rate, double Alpha, double? Tween, double? TweenRate);

/// <summary>
/// One frame of channel states. <see cref="Full"/> frames (the traversal extra <c>ach</c>) list every channel 0 to 15
/// that has a sequence whatever its alpha; the others (<c>anim</c>) list channel 0 and the channels drawn (alpha above 0).
/// </summary>
public sealed record AnimFrame(int F, bool Full, IReadOnlyList<AnimChannel> Channels)
{
    /// <summary>The channel record, or null.</summary>
    public AnimChannel? Of(int channel)
    {
        foreach (AnimChannel c in Channels)
        {
            if (c.Channel == channel)
            {
                return c;
            }
        }

        return null;
    }
}

/// <summary>Reads the channel records of a golden run, and samples the rewrite's pawn channels the same way.</summary>
public static class AnimFrames
{
    /// <summary>The channels a full record holds (the recorder reads 0 to 15).</summary>
    public const int FullChannels = 16;

    /// <summary>Every frame of an svm.trace/1 file with its channel records, frame -1 first.</summary>
    public static IReadOnlyList<AnimFrame> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var frames = new List<AnimFrame>();
        using FileStream file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        bool header = true;
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (header)
            {
                header = false;
                continue;
            }

            JsonObject o = JsonNode.Parse(line)!.AsObject();
            if (o["end"] is not null)
            {
                continue;
            }

            int f = o["f"]!.GetValue<int>();
            if (o["ach"] is JsonArray ach)
            {
                var list = new List<AnimChannel>(ach.Count);
                foreach (JsonNode? n in ach)
                {
                    JsonArray c = n!.AsArray();
                    list.Add(new AnimChannel(c[0]!.GetValue<int>(), c[1]!.GetValue<string>(), Num(c[2]), Num(c[3]), Num(c[4]),
                        c.Count > 5 ? Num(c[5]) : null, c.Count > 6 ? Num(c[6]) : null));
                }

                frames.Add(new AnimFrame(f, true, list));
            }
            else if (o["anim"] is JsonArray anim)
            {
                var list = new List<AnimChannel>(anim.Count);
                foreach (JsonNode? n in anim)
                {
                    JsonArray c = n!.AsArray();
                    list.Add(new AnimChannel(c[0]!.GetValue<int>(), c[1]!.GetValue<string>(), Num(c[2]), Num(c[3]), Num(c[4]),
                        c.Count > 5 ? Num(c[5]) : null, null));
                }

                frames.Add(new AnimFrame(f, false, list));
            }
        }

        return frames;
    }

    /// <summary>The pawn's channels as a record of the given kind (full: channels 0 to 15 with a sequence; else drawn ones).</summary>
    public static AnimFrame Sample(PawnAnimation anim, int f, bool full)
    {
        ArgumentNullException.ThrowIfNull(anim);
        var list = new List<AnimChannel>();
        for (int ch = 0; ch < PawnAnimation.ChannelCount; ch++)
        {
            PawnAnimChannel c = anim[ch];
            if (c.Sequence.IsNone)
            {
                continue;
            }

            if (full)
            {
                if (ch >= FullChannels)
                {
                    continue;
                }

                list.Add(new AnimChannel(ch, c.Sequence.ToString(), c.Frame, c.Rate, c.BlendAlpha, c.TweenWeight, c.TweenRate));
            }
            else if (ch == 0 || c.BlendAlpha > 0f)
            {
                list.Add(new AnimChannel(ch, c.Sequence.ToString(), c.Frame, c.Rate, ch == 0 ? 1.0 : c.BlendAlpha,
                    ch == 0 ? c.TweenWeight : null, null));
            }
        }

        return new AnimFrame(f, full, list);
    }

    private static double Num(JsonNode? n) => n is null ? 0.0 : n.GetValue<double>();
}
