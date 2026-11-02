using System.Text.Json;
using OpenScct.Content.Loading;
using OpenScct.Game.AI;
using OpenScct.Physics.Collision;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>Compatibility entry points for the shared server map-light loader.</summary>
public static class AiMapLight
{
    public static AiLightField Load(string? mapFolder, ICollisionWorld? collision, MapLoadResult? loaded = null) =>
        MapAiLight.Load(mapFolder, collision, loaded);

    public static void BindSwitches(AiLightField field, MapLoadResult loaded) => MapAiLight.BindSwitches(field, loaded);

    public static AiLightField Parse(JsonElement root, ICollisionWorld? collision) => MapAiLight.Parse(root, collision);
}
