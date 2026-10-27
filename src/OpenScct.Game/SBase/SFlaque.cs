using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFlaque: a water puddle. Grow sets a growth rate of 0.1 scale per second and Reduce a shrink rate of 0.01;
/// every tick (server and clients, the rate replicates) X and Y scale by the rate, the new scale is applied while X
/// is under 1.5, and the puddle destroys itself once X is under 0.1. Placed puddles (PrisD) never change. See
/// spec/devices/sprinkler.md.
/// </summary>
public partial class SFlaque
{
    [Uc("SBase.SFlaque.Tick", Sha1 = "32b5458c0b80888aaf6d863cd0704e88a67cef38")]
    [PortOf("uc:SBase.SFlaque:Tick", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        FVector scale = DrawScale3D;
        if (variation == 0f)
        {
            return;
        }

        scale.X += variation * DeltaTime;
        scale.Y += variation * DeltaTime;
        if (scale.X < 1.5f)
        {
            DrawScale3D = scale;
        }

        SetLocation(Location);
        if (scale.X < 0.1f)
        {
            Destroy();
        }
    }

    /// <summary>Clears the owning sprinkler's puddle reference.</summary>
    [Uc("SBase.SFlaque.Destroyed", Sha1 = "8065f7e236a7e2838dc38b02e6ddbd83fbe1421c")]
    [PortOf("uc:SBase.SFlaque:Destroyed", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void Destroyed()
    {
        if (Owner is SSprinkler sprinkler)
        {
            sprinkler.Flaque = null;
        }
    }

    [Uc("SBase.SFlaque.Grow", Sha1 = "8d0afdedd4f94693dad529d18350713f1dec590a")]
    [PortOf("uc:SBase.SFlaque:Grow", Spec = "spec/devices/sprinkler.md#rules")]
    public void Grow() => variation = 0.1f;

    [Uc("SBase.SFlaque.Reduce", Sha1 = "37746e83d69f90d7cb5eed53c1d78b9db6a9f0a6")]
    [PortOf("uc:SBase.SFlaque:Reduce", Spec = "spec/devices/sprinkler.md#rules")]
    public void Reduce() => variation = -0.01f;
}
