using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Map load support. In a map file Base, Owner and Attached are ordinary serialized properties, so a load sets them
// with no GainedChild, Attach or BaseChange event, unlike SetOwner and SetBase. For map loaders only, before the
// level's begin-play passes.
public abstract partial class Actor
{
    /// <summary>Sets <see cref="Owner"/> as a map load does: no LostChild or GainedChild event.</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 map load: Owner is a serialized property; no events run")]
    public void SetLoadedOwner(Actor? LoadedOwner) => Owner = LoadedOwner;

    /// <summary>
    /// Sets <see cref="Base"/> as a map load does: no Attach, Detach or BaseChange event, with the base's
    /// <see cref="Attached"/> list kept in step (the file stores it). A base that would make a loop is refused and
    /// returns false.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 map load: Base and Attached are serialized properties; no events run")]
    public bool SetLoadedBase(Actor? LoadedBase)
    {
        for (Actor? loop = LoadedBase; loop is not null; loop = loop.Base)
        {
            if (ReferenceEquals(loop, this))
            {
                return false;
            }
        }

        if (ReferenceEquals(LoadedBase, Base))
        {
            return true;
        }

        if (Base is not null && !Base.bWorldGeometry)
        {
            Base.Attached.Remove(this);
        }

        Base = LoadedBase;
        if (LoadedBase is not null && !LoadedBase.bWorldGeometry && !LoadedBase.Attached.Contains(this))
        {
            LoadedBase.Attached.Add(this);
        }

        return true;
    }
}
