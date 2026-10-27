using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using UcAsset = OpenScct.Core.Uc.AssetRef;

namespace OpenScctGame.Play.Session;

/// <summary>
/// The animation sequences of a map's skeletal mesh actors (SVMap 1.1 <c>animations</c>) as the devices read them:
/// frame counts, rates and the notify objects (AnimNotify_Trigger fires events, AnimNotify_Sound plays a sound), so
/// SteeE's crane fires <c>tueletube</c> and <c>blast01</c> on time on every machine that loads the map.
/// </summary>
public sealed class ContentMeshAnimations : IMeshAnimations
{
    private readonly MeshAnimations _inner = new();

    /// <summary>The sequences of every animation set of <paramref name="gameplay"/>.</summary>
    public ContentMeshAnimations(GameplayDoc gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        foreach (AnimationSet set in gameplay.Animations)
        {
            var mesh = new UcAsset(set.Mesh);
            foreach (AnimSequenceInfo sequence in set.Sequences)
            {
                if (sequence.Frames < 1 || !(sequence.Rate > 0f))
                {
                    continue;
                }

                var notifies = new List<MeshAnimNotify>(sequence.Notifies.Count);
                foreach (AnimNotifyInfo info in sequence.Notifies)
                {
                    if (CreateNotify(info) is { } notify)
                    {
                        notifies.Add(new MeshAnimNotify(info.Time, notify));
                    }
                }

                _inner.Add(mesh, new MeshAnimSequence(new Name(sequence.Name), sequence.Frames, sequence.Rate, notifies));
                Count++;
            }
        }
    }

    /// <summary>Sequences registered.</summary>
    public int Count { get; }

    public MeshAnimSequence? FindSequence(UcAsset Mesh, Name Sequence) => _inner.FindSequence(Mesh, Sequence);

    /// <summary>The notify object of one notify record, with the props the map stores over the class defaults.</summary>
    private static UAnimNotify? CreateNotify(AnimNotifyInfo info)
    {
        PropBag props = info.Props;
        switch (info.Class)
        {
            case "Engine.AnimNotify_Trigger":
                return new UAnimNotify_Trigger { EventName = props.GetName("EventName") };
            case "Engine.AnimNotify_Sound":
            {
                var sound = new UAnimNotify_Sound();
                if (props.GetAsset("Sound") is { } asset)
                {
                    sound.Sound = new UcAsset(asset.Path);
                }

                sound.Volume = props.GetFloat("Volume", sound.Volume);
                sound.Radius = props.GetInt("Radius", sound.Radius);
                sound.BoneName = props.GetName("BoneName", sound.BoneName);
                return sound;
            }

            case "Engine.AnimNotify_Script":
                return new UAnimNotify_Script { NotifyName = props.GetName("NotifyName") };
            default:
                return null;
        }
    }
}
