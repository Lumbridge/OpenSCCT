namespace OpenScct.Core.Engine;

/// <summary>Engine.Info: base of non-physical actors (game rules, zones, replication info).</summary>
public abstract partial class Info : Actor
{
    protected Info()
    {
        bHidden = true;
    }
}
