using System.IO;
using OpenScct.Physics.Collision;

namespace OpenScct.Server;

public sealed partial class MapCatalog
{
    private GadgetCollision? _gadgetMeshes;

    /// <summary>The same converted mine hit geometry on clients and dedicated servers, shared across maps.</summary>
    public GadgetCollision? GadgetMeshes
    {
        get
        {
            lock (_gate)
            {
                if (_gadgetMeshes is not null) return _gadgetMeshes;
                if (ContentPaths.LocalBaseOnly)
                {
                    return _gadgetMeshes = LoadConfiguredMeshes();
                }
                foreach (string root in _roots)
                {
                    foreach (string shared in new[] { Path.Combine(root, "..", "shared"), Path.Combine(root, "..") })
                    {
                        string path = Path.Combine(shared, "render", "gameplay", "collision.json");
                        if (File.Exists(path)) return _gadgetMeshes = GadgetCollision.Load(path);
                    }
                }
                return _gadgetMeshes = LoadConfiguredMeshes();
            }
        }
    }

    private static GadgetCollision? LoadConfiguredMeshes()
    {
        string? file = ContentPaths.Shared is { } shared ? Path.Combine(shared, "render", "gameplay", "collision.json") : null;
        return File.Exists(file) ? GadgetCollision.Load(file) : null;
    }

}
