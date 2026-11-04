using System;
using System.IO;
using OpenScct.Content.Json;
using OpenScct.Core;

namespace OpenScct.Content;

/// <summary>Whose look wins on a team that wears a map outfit (spec/additions/map-outfits.md).</summary>
public enum OutfitCosmetics : byte
{
    /// <summary>The map's look wins on the body; the player's light hue and gun skins still apply. The default.</summary>
    Map,

    /// <summary>The player's body cosmetics apply where the outfit allows them: camo on texture outfits, tints on models.</summary>
    Player,

    /// <summary>No player cosmetics on this team: retail lights and guns too.</summary>
    None,
}

/// <summary>A slot's replacement diffuse: a PNG under the map's <c>render/</c>, and the original object it came from.</summary>
public sealed record OutfitSlot(string Texture, string? Source = null);

/// <summary>
/// A team's replacement model: a glTF the map ships (<see cref="Glb"/>, under <c>render/</c>) or a stock skeletal mesh
/// by asset path (<see cref="Stock"/>, such as <c>SPerso.DEF_01</c>). It is drawn with the team's own animations, so it
/// must have every bone they drive. The goggle lights sit at <see cref="GoggleOffset"/> in <see cref="GoggleBone"/>'s
/// axes.
/// </summary>
public sealed record OutfitModel(string? Glb, string? Stock, string GoggleBone, FVector GoggleOffset);

/// <summary>One team's outfit: slot textures, an optional model, and the cosmetics policy.</summary>
public sealed record TeamOutfit(OutfitSlot? Body, OutfitSlot? Head, OutfitModel? Model, OutfitCosmetics Cosmetics = OutfitCosmetics.Map)
{
    public bool IsEmpty => Body is null && Head is null && Model is null;
}

/// <summary>A map's outfits: per team, or null for the stock look.</summary>
public sealed record MapOutfits(TeamOutfit? Spy, TeamOutfit? Merc)
{
    public static MapOutfits None { get; } = new(null, null);

    public bool IsEmpty => (Spy?.IsEmpty ?? true) && (Merc?.IsEmpty ?? true);

    public TeamOutfit? For(bool spy) => spy ? Spy : Merc;
}

/// <summary>
/// <c>render/outfits.json</c> (SVMap 1.2, optional, client only; spec/svmap/1.2/outfits.md). The server never reads
/// it: an outfit changes the look only, never collision or vision rules.
/// </summary>
public static class OutfitsDoc
{
    public const string RelativePath = "render/outfits.json";

    public const string FormatId = "svm.outfits/1";

    /// <summary>The bone RE+ attaches the goggle lights to.</summary>
    public const string DefaultGoggleBone = "B Head";

    /// <summary>Reads the file of the SVMap folder <paramref name="mapRoot"/>; null when it has none.</summary>
    public static MapOutfits? Load(string mapRoot)
    {
        string path = Path.Combine(mapRoot, "render", "outfits.json");
        return File.Exists(path) ? FromJson(SvJson.Parse(File.ReadAllBytes(path), RelativePath)) : null;
    }

    public static MapOutfits FromJson(SvJsonNode node, string file = RelativePath)
    {
        ArgumentNullException.ThrowIfNull(node);
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(node, "");
        string format = j.StringMember(root, "format", "");
        if (format != FormatId)
        {
            throw j.Error("/format", $"'{format}' is not {FormatId}");
        }

        return new MapOutfits(Team(j, root, "spy"), Team(j, root, "merc"));
    }

    public static SvJsonObject ToJson(MapOutfits outfits)
    {
        ArgumentNullException.ThrowIfNull(outfits);
        var root = new SvJsonObject();
        root.Add("format", SvJsonNode.String(FormatId));
        if (outfits.Merc is { IsEmpty: false } merc)
        {
            root.Add("merc", TeamJson(merc));
        }

        if (outfits.Spy is { IsEmpty: false } spy)
        {
            root.Add("spy", TeamJson(spy));
        }

        return root;
    }

    public static string PolicyName(OutfitCosmetics policy) => policy switch
    {
        OutfitCosmetics.Player => "player",
        OutfitCosmetics.None => "none",
        _ => "map",
    };

    public static bool TryParsePolicy(string text, out OutfitCosmetics policy)
    {
        policy = text switch
        {
            "map" => OutfitCosmetics.Map,
            "player" => OutfitCosmetics.Player,
            "none" => OutfitCosmetics.None,
            _ => (OutfitCosmetics)255,
        };
        return (byte)policy != 255;
    }

    private static TeamOutfit? Team(JsonRead j, SvJsonObject root, string team)
    {
        if (!root.TryGetValue(team, out SvJsonNode node))
        {
            return null;
        }

        string at = $"/{team}";
        SvJsonObject t = j.Object(node, at);
        OutfitSlot? body = null, head = null;
        if (t.TryGetValue("slots", out SvJsonNode slotsNode))
        {
            foreach (var slot in j.Object(slotsNode, $"{at}/slots"))
            {
                string sat = $"{at}/slots/{slot.Key}";
                SvJsonObject s = j.Object(slot.Value, sat);
                var value = new OutfitSlot(j.StringMember(s, "texture", sat), j.OptionalString(s, "source", sat));
                switch (slot.Key)
                {
                    case "body":
                        body = value;
                        break;
                    case "head":
                        head = value;
                        break;
                    default:
                        throw j.Error(sat, "is not body or head");
                }
            }
        }

        OutfitModel? model = null;
        if (t.TryGetValue("model", out SvJsonNode modelNode))
        {
            string mat = $"{at}/model";
            SvJsonObject m = j.Object(modelNode, mat);
            string? glb = j.OptionalString(m, "glb", mat);
            string? stock = j.OptionalString(m, "stock", mat);
            if ((glb is null) == (stock is null))
            {
                throw j.Error(mat, "names either a glb or a stock mesh");
            }

            string bone = DefaultGoggleBone;
            FVector offset = default;
            if (m.TryGetValue("goggles", out SvJsonNode g))
            {
                SvJsonObject goggles = j.Object(g, $"{mat}/goggles");
                bone = j.OptionalString(goggles, "bone", $"{mat}/goggles") ?? DefaultGoggleBone;
                if (goggles.TryGetValue("offset", out SvJsonNode o))
                {
                    SvJsonArray v = j.Array(o, $"{mat}/goggles/offset");
                    if (v.Count != 3)
                    {
                        throw j.Error($"{mat}/goggles/offset", "expected [x, y, z]");
                    }

                    offset = new FVector(j.Number(v[0], $"{mat}/goggles/offset/0"), j.Number(v[1], $"{mat}/goggles/offset/1"),
                        j.Number(v[2], $"{mat}/goggles/offset/2"));
                }
            }

            model = new OutfitModel(glb, stock, bone, offset);
        }

        OutfitCosmetics policy = OutfitCosmetics.Map;
        if (j.OptionalString(t, "cosmetics", at) is { } text && !TryParsePolicy(text, out policy))
        {
            throw j.Error($"{at}/cosmetics", $"'{text}' is not map, player or none");
        }

        return new TeamOutfit(body, head, model, policy);
    }

    private static SvJsonObject TeamJson(TeamOutfit team)
    {
        var o = new SvJsonObject();
        o.Add("cosmetics", SvJsonNode.String(PolicyName(team.Cosmetics)));
        if (team.Model is { } model)
        {
            var m = new SvJsonObject();
            if (model.Glb is { } glb)
            {
                m.Add("glb", SvJsonNode.String(glb));
            }
            else
            {
                m.Add("stock", SvJsonNode.String(model.Stock ?? string.Empty));
            }

            var goggles = new SvJsonObject();
            goggles.Add("bone", SvJsonNode.String(model.GoggleBone));
            goggles.Add("offset", new SvJsonArray([SvJsonNode.Float(model.GoggleOffset.X), SvJsonNode.Float(model.GoggleOffset.Y),
                SvJsonNode.Float(model.GoggleOffset.Z)]));
            m.Add("goggles", goggles);
            o.Add("model", m);
        }

        if (team.Body is not null || team.Head is not null)
        {
            var slots = new SvJsonObject();
            if (team.Body is { } body)
            {
                slots.Add("body", SlotJson(body));
            }

            if (team.Head is { } head)
            {
                slots.Add("head", SlotJson(head));
            }

            o.Add("slots", slots);
        }

        return o;
    }

    private static SvJsonObject SlotJson(OutfitSlot slot)
    {
        var s = new SvJsonObject();
        if (slot.Source is { } source)
        {
            s.Add("source", SvJsonNode.String(source));
        }

        s.Add("texture", SvJsonNode.String(slot.Texture));
        return s;
    }
}
