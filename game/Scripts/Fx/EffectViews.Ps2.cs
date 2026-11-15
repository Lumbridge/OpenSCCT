using System.Collections.Generic;
using System.Text.Json;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Fx;

public sealed partial class EffectViews
{
    private static readonly JsonSerializerOptions Ps2ParticleJson = new()
    {
        IncludeFields = true,
        IgnoreReadOnlyProperties = true,
    };

    private ParticleSystemView? StartPs2ModemEffect(Ps2ModemEffect effect)
    {
        if (Map?.Emitters is not { } assets) return null;
        var particles = new List<ParticleParams>();
        foreach (USpriteEmitter source in effect.SourceEmitters)
        {
            // Serialize only the requested property, so UObject/runtime links
            // never enter the particle definition. Ranges and colors retain
            // the original field names consumed by the existing simulator.
            JsonElement? Get(string name) => source.GetType().GetField(name) is { } field
                ? JsonSerializer.SerializeToElement(field.GetValue(source), Ps2ParticleJson) : null;
            string? material = Map.Materials.FindSource(source.Texture.Path);
            string? png = material is not null ? Map.Materials.Find(material)?.Texture : null;
            if (png is null || assets.Texture(png) is null)
            {
                Missing.Add(source.Texture.Path ?? nameof(Ps2ModemEffect));
                return null;
            }
            particles.Add(ParticleParams.From("Engine.SpriteEmitter", "Ps2ModemEffect" + particles.Count, Get, png));
        }
        var settings = new ParticleSystemParams(effect.AutoDestroy, effect.AutoReset, default, default);
        var simulation = new ParticleSystemSim(particles, settings, _seed++ * 2246822519u) { Trace = _trace };
        Started[nameof(Ps2ModemEffect)] = Started.GetValueOrDefault(nameof(Ps2ModemEffect)) + 1;
        return new ParticleSystemView(simulation, assets, this, nameof(Ps2ModemEffect), effect.HeatOpacity);
    }
}
