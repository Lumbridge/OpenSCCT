using System;
using System.Collections.Generic;
using OpenScct.Content.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Content;

/// <summary>Native mover poses, independent of the legacy UC fixed arrays.</summary>
public static class MoverPathsDoc
{
    public const string RelativePath = "sim/mover-paths.json";
    public const string FormatId = "svm.mover-paths/1";

    public static IReadOnlyDictionary<string, MoverPath> FromJson(SvJsonNode node)
    {
        var j = new JsonRead(RelativePath);
        var root = j.Object(node, "");
        if (j.StringMember(root, "format", "") != FormatId) throw j.Error("/format", "Unknown mover path format");
        var result = new Dictionary<string, MoverPath>(AsciiCaseComparer.Instance);
        var movers = j.ArrayMember(root, "movers", "");
        for (int i = 0; i < movers.Count; i++)
        {
            string at = $"/movers/{i}";
            var m = j.Object(movers[i], at);
            string actor = j.StringMember(m, "actor", at);
            var keys = j.ArrayMember(m, "keys", at);
            if (keys.Count is < 1 or > MoverPath.MaxKeys) throw j.Error(at + "/keys", "Invalid path length");
            int start = (int)(j.OptionalInteger(m, "start", at, 0, keys.Count - 1) ?? 0);
            var path = new MoverPathKey[keys.Count];
            for (int k = 0; k < path.Length; k++)
            {
                string keyAt = $"{at}/keys/{k}";
                var key = j.Object(keys[k], keyAt);
                var p = j.ArrayMember(key, "offset", keyAt);
                var r = j.ArrayMember(key, "turn", keyAt);
                if (p.Count != 3 || r.Count != 3) throw j.Error(keyAt, "offset and turn must have three components");
                path[k] = new(new FVector(j.Number(p[0], keyAt), j.Number(p[1], keyAt), j.Number(p[2], keyAt)),
                    new FRotator((int)j.Integer(r[0], keyAt, int.MinValue, int.MaxValue), (int)j.Integer(r[1], keyAt, int.MinValue, int.MaxValue),
                        (int)j.Integer(r[2], keyAt, int.MinValue, int.MaxValue)));
            }
            if (!result.TryAdd(actor, new MoverPath(path, start))) throw j.Error(at, "Duplicate mover path");
        }
        return result;
    }

    public static SvJsonObject ToJson(IEnumerable<(string Actor, MoverPath Path)> paths)
    {
        var movers = new SvJsonArray();
        foreach (var (actor, path) in paths)
        {
            var keys = new SvJsonArray();
            foreach (var key in path.Keys)
            {
                var k = new SvJsonObject();
                k.Add("offset", new SvJsonArray([SvJsonNode.Float(key.Offset.X), SvJsonNode.Float(key.Offset.Y), SvJsonNode.Float(key.Offset.Z)]));
                k.Add("turn", new SvJsonArray([SvJsonNode.Integer(key.Turn.Pitch), SvJsonNode.Integer(key.Turn.Yaw), SvJsonNode.Integer(key.Turn.Roll)]));
                keys.Add(k);
            }
            var m = new SvJsonObject();
            m.Add("actor", SvJsonNode.String(actor));
            m.Add("start", SvJsonNode.Integer(path.Start));
            m.Add("keys", keys);
            movers.Add(m);
        }
        var root = new SvJsonObject();
        root.Add("format", SvJsonNode.String(FormatId));
        root.Add("movers", movers);
        return root;
    }
}
