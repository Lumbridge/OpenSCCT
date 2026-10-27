using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Game.Movement;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// Reads the local golden runs of the movement scenarios (local/difftest/golden, recorded from the original; never
/// tracked) and replays them in a <see cref="PawnRig"/> on the converted map (.content/svmap). Tests that need them
/// return early when either is missing.
/// </summary>
internal static class GoldenRuns
{
    /// <summary>One recorded frame: the fields the movement tests compare.</summary>
    internal sealed record GoldenFrame(int F, float Dt, FVector Loc, FVector Vel, FRotator Rot, FRotator CRot, int Phys,
        string Cs, bool Crouch, int Hp, float Gs, float Fwd, float ZMax, IReadOnlyDictionary<string, int> Buttons, bool Jump,
        bool? Fast, (int Pitch, int Yaw)? View, string Anim0, bool FastSpeed, float Ch);

    public static string GoldenDir => Path.Combine(MovementTestKit.RepoRoot, "local", "difftest", "golden");

    public static string SvMapDir(string map) => Path.Combine(MovementTestKit.RepoRoot, ".content", "svmap", map);

    /// <summary>The frames of run <paramref name="run"/> of a scenario (frame -1 first), or null when not recorded here.</summary>
    public static List<GoldenFrame>? Load(string scenarioId, int run = 1)
    {
        string path = Path.Combine(GoldenDir, scenarioId, $"run{run}.jsonl.gz");
        if (!File.Exists(path))
        {
            return null;
        }

        var frames = new List<GoldenFrame>();
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            if (!e.TryGetProperty("f", out JsonElement f))
            {
                continue;
            }

            var buttons = new Dictionary<string, int>(StringComparer.Ordinal);
            bool jump = false;
            bool? fast = null;
            (int, int)? view = null;
            if (e.TryGetProperty("in", out JsonElement input) && input.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in input.EnumerateObject())
                {
                    switch (p.Name)
                    {
                        case "jump":
                            jump = p.Value.ValueKind == JsonValueKind.True || (p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() != 0);
                            break;
                        case "fastSpeed":
                            fast = p.Value.ValueKind == JsonValueKind.True || (p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() != 0);
                            break;
                        case "view":
                            view = (p.Value.GetProperty("pitch").GetInt32(), p.Value.GetProperty("yaw").GetInt32());
                            break;
                        default:
                            if (p.Value.ValueKind == JsonValueKind.Number)
                            {
                                buttons[p.Name] = p.Value.GetInt32();
                            }

                            break;
                    }
                }
            }

            string anim0 = "";
            if (e.TryGetProperty("anim", out JsonElement anim) && anim.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement c in anim.EnumerateArray())
                {
                    if (c[0].GetInt32() == 0)
                    {
                        anim0 = c[1].GetString() ?? "";
                    }
                }
            }

            frames.Add(new GoldenFrame(
                f.GetInt32(),
                e.GetProperty("dt").GetSingle(),
                Vec(e.GetProperty("loc")),
                Vec(e.GetProperty("vel")),
                Rot(e.GetProperty("rot")),
                Rot(e.GetProperty("crot")),
                e.GetProperty("phys").GetInt32(),
                e.GetProperty("cs").GetString() ?? "",
                e.GetProperty("crouch").GetInt32() != 0,
                e.GetProperty("hp").GetInt32(),
                e.GetProperty("gs").GetSingle(),
                e.GetProperty("fwd").GetSingle(),
                e.GetProperty("zmax").GetSingle(),
                buttons,
                jump,
                fast,
                view,
                anim0,
                e.TryGetProperty("fast", out JsonElement fs) && fs.GetInt32() != 0,
                e.TryGetProperty("ch", out JsonElement ch) ? ch.GetSingle() : 0f));
        }

        return frames;
    }

    /// <summary>
    /// Replays a run in a rig on <paramref name="map"/>: the start of frame -1 (location, rotations, stance, speed
    /// mode, health), then every frame with its recorded time step and inputs. Returns the rig's samples, one per
    /// recorded frame from 0; null when the map is not converted here.
    /// </summary>
    public static List<PawnSample>? Replay(string map, PawnTeam team, List<GoldenFrame> frames, PawnRig? rig = null)
    {
        if (rig is null)
        {
            string dir = SvMapDir(map);
            if (!File.Exists(Path.Combine(dir, "manifest.json")))
            {
                return null;
            }

            rig = PawnRig.ForMap(team, dir);
        }

        GoldenFrame start = frames[0];
        rig.Place(start.Loc, start.Rot, start.CRot, start.Crouch);
        rig.SetHealth(start.Hp);
        var samples = new List<PawnSample>(frames.Count);
        rig.SetFastSpeed(start.FastSpeed);
        for (int i = 1; i < frames.Count; i++)
        {
            GoldenFrame g = frames[i];
            PlayerInputFrame input = PlayerInputFrame.FromButtons(g.Buttons, g.Jump, g.View, g.Fast);
            rig.Step(input, g.Dt);
            samples.Add(rig.Sample());
        }

        return samples;
    }

    private static FVector Vec(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());

    private static FRotator Rot(JsonElement a) => new(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32());
}
