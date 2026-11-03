using System.Collections.Generic;
using System.Text.Json;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Data;

public sealed record MaterialSequenceFrame(MaterialSequenceItem Timing, MaterialLayer Layer, FlipbookEntry? Flipbook,
    MaterialEntry? Material = null);
public sealed record MaterialSequenceEntry(string Source, bool Loop, bool Paused, int TriggerAction, MaterialSequenceFrame[] Items,
    bool SwitchMaterial = false)
{
    public static Dictionary<string, MaterialSequenceEntry> From(JsonElement flags)
    {
        var result = new Dictionary<string, MaterialSequenceEntry>();
        if (!flags.TryGetProperty("sequences", out JsonElement sequences) || sequences.ValueKind != JsonValueKind.Object) return result;
        foreach (JsonProperty sequence in sequences.EnumerateObject())
        {
            JsonElement s = sequence.Value;
            if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array) continue;
            var frames = new List<MaterialSequenceFrame>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (MaterialsDoc.Str(item, "texture") is not { } texture) continue;
                frames.Add(new(new(item.TryGetProperty("time", out JsonElement t) ? t.GetSingle() : 0f,
                    item.TryGetProperty("action", out JsonElement a) && a.GetInt32() == 1),
                    new(texture, item.TryGetProperty("anim", out JsonElement anim) ? UvAnim.From(anim) : null,
                        item.TryGetProperty("matrix", out JsonElement matrix) ? UvAffine.From(matrix) : null,
                        item.TryGetProperty("post", out JsonElement post) ? UvAffine.From(post) : null),
                    item.TryGetProperty("flipbook", out JsonElement fb) ? FlipbookEntry.From(fb) : null,
                    item.TryGetProperty("material", out JsonElement state) && state.ValueKind == JsonValueKind.Object
                        ? MaterialsDoc.FromFlags((MaterialsDoc.Str(s, "source") ?? "sequence") + "#" + frames.Count,
                            state, MaterialsDoc.Str(s, "source")) : null));
            }
            if (frames.Count > 0) result[sequence.Name] = new(MaterialsDoc.Str(s, "source") ?? "", !s.TryGetProperty("loop", out JsonElement loop) || loop.ValueKind != JsonValueKind.False,
                MaterialsDoc.Bool(s, "paused"), s.TryGetProperty("trigger_action", out JsonElement trigger) ? trigger.GetInt32() : 0,
                frames.ToArray(), MaterialsDoc.Bool(s, "switch_material"));
        }
        return result;
    }
}
