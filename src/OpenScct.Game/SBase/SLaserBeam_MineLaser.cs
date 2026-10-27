using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SLaserBeam_MineLaser: the laser mine's beam (spec/equipment/mine-launcher.md rules 16 and 17).</summary>
public partial class SLaserBeam_MineLaser
{
    private static readonly AssetRef LasDetectBeam = new("SGameplayObjects.Objects.LasDetectBeam");

    /// <summary>The beam mesh without collision; the server copies the mine's beam length.</summary>
    [Uc("SBase.SLaserBeam_MineLaser.PostNetBeginPlay", Sha1 = "3d90709bb3280aa888c44d2da787115f980dc429")]
    [PortOf("uc:SBase.SLaserBeam_MineLaser:PostNetBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    [Provenance(ProvenanceKind.Estimated, "The TexScaler skin built at run time is a view effect; the beam length drives the view")]
    protected override void PostNetBeginPlay()
    {
        SetStaticMesh(LasDetectBeam);
        SetCollision(false);
        SetDrawType(EDrawType.DT_StaticMesh);
        if (Role == ENetRole.ROLE_Authority)
        {
            // A spy trap's beam reads the laser mine's field through a failed cast: 0.
            fTailleFaisceau = (Owner as SMineLaser)?.fTailleFaisceau ?? 0f;
        }
    }
}
