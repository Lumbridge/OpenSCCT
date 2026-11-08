using System;
using System.Collections.Generic;
using OpenScct.Content.Json;
using OpenScct.Core.Engine;

namespace OpenScct.Content;

/// <summary>
/// <c>sim/movers.json</c> (SVMap 1.2, optional; spec/svmap/1.2/movers.md): each mover's own time per leg and easing per
/// key, as <see cref="MoverLegs"/> by actor id. A map without the file moves every mover with the game's timing.
/// </summary>
public static class MoversDoc
{
    public const string RelativePath = "sim/movers.json";

    public const string FormatId = "svm.movers/1";

    /// <summary>The longest leg a key may ask for, in seconds (an hour: anything longer is a typo).</summary>
    public const float LongestLeg = 3600f;

    public static IReadOnlyDictionary<string, MoverLegs> FromJson(SvJsonNode node, string file = RelativePath)
    {
        ArgumentNullException.ThrowIfNull(node);
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(node, "");
        string format = j.StringMember(root, "format", "");
        if (format != FormatId)
        {
            throw j.Error("/format", $"'{format}' is not {FormatId}");
        }

        var movers = new Dictionary<string, MoverLegs>(AsciiCaseComparer.Instance);
        if (!root.TryGetValue("movers", out SvJsonNode listNode))
        {
            return movers;
        }

        SvJsonArray list = j.Array(listNode, "/movers");
        for (int i = 0; i < list.Count; i++)
        {
            string at = $"/movers/{i}";
            SvJsonObject m = j.Object(list[i], at);
            string actor = j.StringMember(m, "actor", at);
            SvJsonArray keys = j.ArrayMember(m, "keys", at);
            if (keys.Count > MoverPath.MaxKeys)
            {
                throw j.Error($"{at}/keys", $"a mover has at most {MoverPath.MaxKeys} keys");
            }

            var timings = new MoverKeyTiming[keys.Count];
            for (int k = 0; k < keys.Count; k++)
            {
                string keyAt = $"{at}/keys/{k}";
                SvJsonObject key = j.Object(keys[k], keyAt);
                float? time = null;
                if (key.TryGetValue("time", out SvJsonNode t))
                {
                    if (k == 0)
                    {
                        throw j.Error($"{keyAt}/time", "key 0 has no leg before it, so it takes no time");
                    }

                    time = j.Number(t, $"{keyAt}/time");
                    if (!(time >= MoverK.MinLegTime && time <= LongestLeg))
                    {
                        throw j.Error($"{keyAt}/time", $"a leg takes {MoverK.MinLegTime} to {LongestLeg} seconds");
                    }
                }

                bool? smooth = key.TryGetValue("smooth", out SvJsonNode s)
                    ? s.Kind switch
                    {
                        SvJsonKind.True => true,
                        SvJsonKind.False => false,
                        _ => throw j.Error($"{keyAt}/smooth", $"expected true or false, found {JsonRead.Describe(s)}"),
                    }
                    : null;
                timings[k] = new MoverKeyTiming(time, smooth);
            }

            if (!movers.TryAdd(actor, new MoverLegs(timings)))
            {
                throw j.Error(at, $"{actor} is listed twice");
            }
        }

        return movers;
    }

    /// <summary>The file's JSON for <paramref name="movers"/> (in the order given); <see cref="CanonicalJson"/> turns it
    /// into file bytes. Keys after the last one with a setting are left out.</summary>
    public static SvJsonObject ToJson(IEnumerable<(string Actor, MoverLegs Legs)> movers)
    {
        ArgumentNullException.ThrowIfNull(movers);
        var list = new SvJsonArray();
        foreach ((string actor, MoverLegs legs) in movers)
        {
            int count = legs.Keys.Count;
            while (count > 0 && legs.Keys[count - 1].IsDefault)
            {
                count--;
            }

            var keys = new SvJsonArray();
            for (int k = 0; k < count; k++)
            {
                var key = new SvJsonObject();
                if (legs.Keys[k].Time is { } time)
                {
                    key.Add("time", SvJsonNode.Float(time));
                }

                if (legs.Keys[k].Smooth is { } smooth)
                {
                    key.Add("smooth", SvJsonNode.Bool(smooth));
                }

                keys.Add(key);
            }

            var o = new SvJsonObject();
            o.Add("actor", SvJsonNode.String(actor));
            o.Add("keys", keys);
            list.Add(o);
        }

        var root = new SvJsonObject();
        root.Add("format", SvJsonNode.String(FormatId));
        root.Add("movers", list);
        return root;
    }
}
