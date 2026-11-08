using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Json;
using OpenScct.Core.Match;

namespace OpenScct.Content;

/// <summary>
/// <c>sim/match.json</c> (SVMap 1.2, optional; spec/svmap/1.2/match.md): the map's player-count rules and win targets
/// by team size, as a <see cref="MatchRuleSet"/>. A map without the file plays everything at every count.
/// </summary>
public static class MatchDoc
{
    public const string RelativePath = "sim/match.json";

    public const string FormatId = "svm.match/1";

    /// <summary>Native path resource guard; the original game retains its fixed-array limit.</summary>
    public const int MostKeys = OpenScct.Core.Engine.MoverPath.MaxKeys;

    /// <summary>Reads the file of the SVMap folder <paramref name="mapRoot"/>; null when it has none.</summary>
    public static MatchRuleSet? Load(string mapRoot)
    {
        string path = Path.Combine(mapRoot, "sim", "match.json");
        return File.Exists(path) ? FromJson(SvJson.Parse(File.ReadAllBytes(path), RelativePath)) : null;
    }

    public static MatchRuleSet FromJson(SvJsonNode node, string file = RelativePath)
    {
        ArgumentNullException.ThrowIfNull(node);
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(node, "");
        string format = j.StringMember(root, "format", "");
        if (format != FormatId)
        {
            throw j.Error("/format", $"'{format}' is not {FormatId}");
        }

        var rules = new List<MatchRule>();
        var ruled = new HashSet<string>(AsciiCaseComparer.Instance);
        if (root.TryGetValue("rules", out SvJsonNode rulesNode))
        {
            SvJsonArray list = j.Array(rulesNode, "/rules");
            for (int i = 0; i < list.Count; i++)
            {
                string at = $"/rules/{i}";
                SvJsonObject r = j.Object(list[i], at);
                string actor = j.StringMember(r, "actor", at);
                if (!ruled.Add(actor))
                {
                    throw j.Error(at, $"{actor} has a second rule");
                }

                SvJsonArray players = j.ArrayMember(r, "players", at);
                if (players.Count != 2)
                {
                    throw j.Error($"{at}/players", "expected [min, max]");
                }

                var range = new PlayerRange(
                    (int)j.Integer(players[0], $"{at}/players/0", 0, MatchRuleSet.MostPlayers),
                    (int)j.Integer(players[1], $"{at}/players/1", 0, MatchRuleSet.MostPlayers));
                if (range.Problem() is { } problem)
                {
                    throw j.Error($"{at}/players", problem);
                }

                Hold? outside = r.TryGetValue("outside", out SvJsonNode o) ? ReadHold(j, o, $"{at}/outside") : null;
                Hold? inside = r.TryGetValue("inside", out SvJsonNode n) ? ReadHold(j, n, $"{at}/inside") : null;
                if (outside is { Kind: HoldKind.Normal })
                {
                    throw j.Error($"{at}/outside", "outside its range an actor cannot work as normal");
                }

                rules.Add(new MatchRule(actor, range, outside, inside));
            }
        }

        var win = new List<WinTargets>();
        if (root.TryGetValue("win", out SvJsonNode winNode))
        {
            SvJsonArray list = j.Array(winNode, "/win");
            for (int i = 0; i < list.Count; i++)
            {
                string at = $"/win/{i}";
                SvJsonObject w = j.Object(list[i], at);
                string mission = j.StringMember(w, "mission", at);
                SvJsonArray sizes = j.ArrayMember(w, "bySize", at);
                if (sizes.Count != MatchRuleSet.TeamSizes)
                {
                    throw j.Error($"{at}/bySize", $"expected {MatchRuleSet.TeamSizes} counts (1v1 to 12v12)");
                }

                var bySize = new int[MatchRuleSet.TeamSizes];
                for (int s = 0; s < bySize.Length; s++)
                {
                    bySize[s] = (int)j.Integer(sizes[s], $"{at}/bySize/{s}", 0, 255);
                }

                win.Add(new WinTargets(mission, bySize));
            }
        }

        return new MatchRuleSet(rules, win);
    }

    /// <summary>The rules as the file's JSON; <see cref="CanonicalJson"/> turns it into file bytes.</summary>
    public static SvJsonObject ToJson(MatchRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var list = new SvJsonArray();
        foreach (MatchRule r in rules.Rules)
        {
            var o = new SvJsonObject();
            o.Add("actor", SvJsonNode.String(r.Actor));
            o.Add("players", new SvJsonArray([SvJsonNode.Integer(r.Players.Min), SvJsonNode.Integer(r.Players.Max)]));
            if (r.Outside is { } outside)
            {
                o.Add("outside", WriteHold(outside));
            }

            if (r.Inside is { } inside)
            {
                o.Add("inside", WriteHold(inside));
            }

            list.Add(o);
        }

        var win = new SvJsonArray();
        foreach (WinTargets w in rules.Win)
        {
            var o = new SvJsonObject();
            o.Add("mission", SvJsonNode.String(w.Mission));
            var sizes = new SvJsonArray();
            for (int s = 0; s < MatchRuleSet.TeamSizes; s++)
            {
                sizes.Add(SvJsonNode.Integer(s < w.BySize.Count ? w.BySize[s] : 0));
            }

            o.Add("bySize", sizes);
            win.Add(o);
        }

        var root = new SvJsonObject();
        root.Add("format", SvJsonNode.String(FormatId));
        root.Add("rules", list);
        root.Add("win", win);
        return root;
    }

    private static Hold ReadHold(JsonRead j, SvJsonNode node, string at)
    {
        if (node is SvJsonString s)
        {
            return s.Value switch
            {
                "normal" => Hold.Normal,
                "hidden" => Hold.Hidden,
                _ => throw j.Error(at, $"'{s.Value}' is not normal, hidden or a key"),
            };
        }

        SvJsonObject o = j.Object(node, at);
        return Hold.AtKey((int)j.IntegerMember(o, "key", at, 0, MostKeys - 1));
    }

    private static SvJsonNode WriteHold(Hold hold) => hold.Kind switch
    {
        HoldKind.Normal => SvJsonNode.String("normal"),
        HoldKind.Hidden => SvJsonNode.String("hidden"),
        _ => new SvJsonObject { { "key", SvJsonNode.Integer(hold.Key) } },
    };
}
