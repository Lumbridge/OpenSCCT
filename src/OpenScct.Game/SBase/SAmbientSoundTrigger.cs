using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SAmbientSoundTrigger: a positional loop (the replicated AmbientSound) switched on and off by events
/// (spec/devices/sound-music.md).
/// </summary>
public partial class SAmbientSoundTrigger
{
    /// <summary>Remembers the loop; silent until triggered unless initially on.</summary>
    [Uc("SBase.SAmbientSoundTrigger.PreBeginPlay", Sha1 = "58ad74b19f26599c9fc7d1ad89f7a773a20a6b00")]
    [PortOf("uc:SBase.SAmbientSoundTrigger:PreBeginPlay", Spec = "spec/devices/sound-music.md#rules")]
    protected override void PreBeginPlay()
    {
        SavedAmbientSound = AmbientSound;
        if (!bInitialyOn)
        {
            AmbientSound = AssetRef.None;
        }
    }

    /// <summary>Starts the loop when off; stops it when on and toggling.</summary>
    [Uc("SBase.SAmbientSoundTrigger.Trigger", Sha1 = "6bc2f864c89a7a8fbe723cd656f4f19b95d1e82d")]
    [PortOf("uc:SBase.SAmbientSoundTrigger:Trigger", Spec = "spec/devices/sound-music.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (AmbientSound.IsNone)
        {
            AmbientSound = SavedAmbientSound;
        }
        else if (bToggle)
        {
            AmbientSound = AssetRef.None;
        }
    }

    /// <summary>Stops the loop.</summary>
    [Uc("SBase.SAmbientSoundTrigger.UnTrigger", Sha1 = "d8370b270ae824359bc342f572786f6f71fd8cc8")]
    [PortOf("uc:SBase.SAmbientSoundTrigger:UnTrigger", Spec = "spec/devices/sound-music.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        StopSound(AmbientSound);
        AmbientSound = AssetRef.None;
    }
}
