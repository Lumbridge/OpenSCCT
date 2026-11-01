using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Game.Lighting;
using Xunit;

namespace OpenScct.Game.Tests.Lighting;

public sealed class LightInfluenceIndexTests
{
    [Fact]
    public void Indexed_candidates_preserve_every_positive_score_and_the_original_sorted_result()
    {
        var random = new Random(18473);
        var lights = new Light[512];
        for (int i = 0; i < lights.Length; i++)
        {
            int effect = i % 31 == 0 ? ActorLightRules.LE_Sunlight
                : i % 7 == 0 ? ActorLightRules.LE_StaticSpot : 0;
            lights[i] = new Light(new FVector(Next(30000), Next(30000), Next(6000)),
                new FRotator((int)Next(32768), (int)Next(32768), 0),
                i % 61 == 0 ? -10f : 64f + i % 10, Next(140) + 150f, (byte)(i % 256), effect);
        }
        // Equal-score suns and colocated point lights make tie handling part of the differential check.
        for (int i = 0; i < 24; i++) lights[i] = new Light(default, default, 100f, 100f, 128, 0);
        var index = new LightInfluenceIndex(lights.Select(Influence).ToArray());
        var candidates = new List<int>();
        for (int query = 0; query < 512; query++)
        {
            FVector center = query < 20 ? default : new FVector(Next(30000), Next(30000), Next(6000));
            float radius = query % 29 == 0 ? -100f : random.NextSingle() * 1000f;
            index.Query(center, radius, candidates);
            candidates.Sort();
            Assert.Equal(Rank(Enumerable.Range(0, lights.Length), center, radius), Rank(candidates, center, radius));
        }

        float Next(float size) => (random.NextSingle() * 2f - 1f) * size;

        int[] Rank(IEnumerable<int> source, FVector center, float radius)
        {
            var ranked = new List<(float Score, int Id)>();
            foreach (int i in source)
            {
                Light light = lights[i];
                float score = ActorLightRules.Score(light.Effect, light.Center, light.Rotation, light.Brightness,
                    light.Radius, light.Cone, center, radius);
                if (score > 0f) ranked.Add((score, i));
            }

            ranked.Sort((a, b) =>
            {
                bool sunA = lights[a.Id].Effect == ActorLightRules.LE_Sunlight;
                bool sunB = lights[b.Id].Effect == ActorLightRules.LE_Sunlight;
                return sunA != sunB ? (sunA ? -1 : 1) : b.Score.CompareTo(a.Score);
            });
            return ranked.Select(x => x.Id).ToArray();
        }
    }

    [Fact]
    public void Boundary_rounding_unbounded_lights_and_extreme_queries_remain_conservative()
    {
        foreach (float coordinate in new[] { 0f, 10000f, -10000f, 1e12f })
        foreach (float radius in new[] { 0.01f, 25f, 1000f, -50f })
        {
            var light = new Light(new FVector(coordinate, 0, 0), default, 64f, radius, 128, 0);
            var index = new LightInfluenceIndex(new[] { Influence(light), new LightInfluence(default, 0, true) });
            var candidates = new List<int>();
            float reach = ActorLightRules.WorldRadius(radius) + 50f;
            float at = coordinate + reach;
            foreach (float x in new[] { MathF.BitDecrement(at), at, MathF.BitIncrement(at), coordinate })
            {
                var center = new FVector(x, 0, 0);
                index.Query(center, 50f, candidates);
                Assert.Contains(1, candidates);
                if (ActorLightRules.Score(0, light.Center, default, 64f, radius, 128, center, 50f) > 0f)
                    Assert.Contains(0, candidates);
            }
            index.Query(new FVector(float.NaN, 0, 0), 50f, candidates);
            Assert.Equal(2, candidates.Count);
            index.Query(default, float.MaxValue, candidates);
            Assert.Equal(2, candidates.Count);
        }
    }

    [Fact]
    public void Distant_boxes_are_rejected_and_the_destination_is_reused()
    {
        var influences = new LightInfluence[1000];
        for (int i = 0; i < influences.Length; i++) influences[i] = new LightInfluence(new FVector(i * 1000f, 0, 0), 25f);
        var index = new LightInfluenceIndex(influences);
        var candidates = new List<int>();
        index.Query(default, 25f, candidates);
        Assert.Equal(new[] { 0 }, candidates);
        index.Query(new FVector(500000f, 0, 0), 25f, candidates);
        Assert.Equal(new[] { 500 }, candidates);
    }

    private static LightInfluence Influence(Light light) => new(light.Center, ActorLightRules.WorldRadius(light.Radius),
        light.Effect == ActorLightRules.LE_Sunlight || light.Brightness <= 0f || !float.IsFinite(light.Brightness));

    private readonly record struct Light(FVector Center, FRotator Rotation, float Brightness, float Radius, byte Cone, int Effect);
}
