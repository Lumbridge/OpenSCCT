using System.Collections.Generic;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.Projector natives. The projection itself is drawing: the simulation keeps what the views need, the
/// abandoned projections of this machine (<see cref="AbandonedProjectors"/>).
/// </summary>
public partial class Projector
{
    /// <summary>
    /// The colour a material built at run time draws the projection in (a Combiner taking its colour from a
    /// ConstantColor and multiplying the texture's alpha by the colour's: SFootPrint), or null for the texture's own.
    /// </summary>
    public FColor? ProjectionTint;

    /// <summary>The projection's texture is drawn upside down (a TexScaler with VScale -1: SFootPrint's right foot).</summary>
    public bool ProjectionFlipV;

    /// <summary>AttachProjector (native 1202): attaches the projection to what it covers. Drawing only.</summary>
    [UcNative("AProjector::execAttachProjector")]
    [PortOf("nat:Engine.Projector.AttachProjector", Spec = "spec/equipment/flare.md#rules")]
    public void AttachProjector()
    {
    }

    /// <summary>
    /// AbandonProjector (native 1205): the projection stays drawn for <paramref name="Lifetime"/> seconds without its
    /// actor. Kept on every machine that draws (not on a dedicated server).
    /// </summary>
    [UcNative("AProjector::execAbandonProjector")]
    [PortOf("nat:Engine.Projector.AbandonProjector", Spec = "spec/equipment/flare.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2: the render info outlives the actor until TimeSeconds + Lifetime; the projection is not decoded")]
    public void AbandonProjector(float Lifetime = 0f)
    {
        if (XLevel is null || Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            return;
        }

        AbandonedProjectors list = AbandonedProjectors.Of(XLevel);
        list.Expire(Level.TimeSeconds);
        list.Add(this, Level.TimeSeconds + Lifetime);
    }
}

/// <summary>One abandoned projection: where it was cast from, along its forward axis, with its texture.</summary>
public sealed class AbandonedProjection
{
    public required Name Class { get; init; }

    public required FVector Location { get; init; }

    public required FRotator Rotation { get; init; }

    public required AssetRef ProjTexture { get; init; }

    public required float DrawScale { get; init; }

    public required int FOV { get; init; }

    public required int MaxTraceDistance { get; init; }

    public required EProjectorBlending FrameBufferBlendingOp { get; init; }

    /// <summary>The level time it goes.</summary>
    public required float Expires { get; init; }

    /// <summary>Counts the projections abandoned on this machine, so a view sees each one once.</summary>
    public required int Serial { get; init; }

    /// <summary>The projector's <see cref="Projector.ProjectionTint"/>.</summary>
    public FColor? Tint { get; init; }

    /// <summary>The projector's <see cref="Projector.ProjectionFlipV"/>.</summary>
    public bool FlipV { get; init; }
}

/// <summary>The abandoned projections of a level (one per machine, a level service made on first use).</summary>
public sealed class AbandonedProjectors
{
    private readonly List<AbandonedProjection> _live = new();
    private int _serial;

    /// <summary>The projections not yet expired at the last <see cref="Add"/> or <see cref="Expire"/>.</summary>
    public IReadOnlyList<AbandonedProjection> Live => _live;

    public static AbandonedProjectors Of(Level level)
    {
        AbandonedProjectors? list = level.GetService<AbandonedProjectors>();
        if (list is null)
        {
            list = new AbandonedProjectors();
            level.SetService(list);
        }

        return list;
    }

    public void Add(Projector p, float expires)
    {
        _live.Add(new AbandonedProjection
        {
            Class = p.ClassName,
            Location = p.Location,
            Rotation = p.Rotation,
            ProjTexture = p.ProjTexture,
            DrawScale = p.DrawScale,
            FOV = p.FOV,
            MaxTraceDistance = p.MaxTraceDistance,
            FrameBufferBlendingOp = p.FrameBufferBlendingOp,
            Expires = expires,
            Serial = ++_serial,
            Tint = p.ProjectionTint,
            FlipV = p.ProjectionFlipV,
        });
    }

    /// <summary>Drops the projections whose time is up.</summary>
    public void Expire(float now)
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            if (_live[i].Expires <= now)
            {
                _live.RemoveAt(i);
            }
        }
    }
}
