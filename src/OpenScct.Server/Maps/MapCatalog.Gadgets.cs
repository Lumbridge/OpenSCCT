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
                foreach (string root in _roots)
                {
                    foreach (string shared in new[] { Path.Combine(root, "..", "shared"), Path.Combine(root, "..") })
                    {
                        string path = Path.Combine(shared, "render", "gameplay", "collision.json");
                        if (File.Exists(path)) return _gadgetMeshes = GadgetCollision.Load(path);
                    }
                }
                if (ContentPaths.Shared is { } content)
                {
                    string path = Path.Combine(content, "render", "gameplay", "collision.json");
                    if (File.Exists(path)) return _gadgetMeshes = GadgetCollision.Load(path);
                }
                // A catalog can be used before the shared pack finishes downloading.
                return null;
            }
        }
    }
}
