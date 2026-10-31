using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SCamNetwork: a fixed globe camera of the merc camera ring (spec/devices/cam-network.md). The controller
/// states that view through it belong to the player-controller port.
/// </summary>
public partial class SCamNetwork
{
    public const float ShotDisableDuration = 30f;

    /// <summary>Server-driven countdown, replicated even to mercs who enter the feed later.</summary>
    public int DisabledSeconds;
    private float _shotDisabledUntil;
    private bool _disabledLens;
    private OpenScct.Core.Uc.AssetRef _enabledLens;

    public bool IsShotDisabled => DisabledSeconds > 0;

    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (Role != ENetRole.ROLE_Authority || EventInstigator is not SBasePawn { SPawnType: PawnType.SPAWNOFFENSE } ||
            DamageType != SDamageType.SDTStickyShocker)
        {
            return;
        }

        _shotDisabledUntil = Level.TimeSeconds + ShotDisableDuration;
        DisabledSeconds = (int)ShotDisableDuration;
        UpdateDisabledLens();
    }

    /// <summary>
    /// The ring's entry camera: the first network camera in list order with bFirstCam, else the last one in the list
    /// (the gadget's search, rule 3).
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:UseCamNetwork", Spec = "spec/devices/cam-network.md#ring-and-entry")]
    public static SCamNetwork? FindFirstCamera(Level level)
    {
        SCamNetwork? first = null;
        foreach (SCamNetwork cam in level.AllActors<SCamNetwork>())
        {
            first = cam;
            if (cam.bFirstCam)
            {
                break;
            }
        }

        return first;
    }

    /// <summary>
    /// Changes the lens skin for a vision mode (3 cycles motion, EMF, on); asking for the current mode resets to
    /// on, or off for -1, and clears the mode.
    /// </summary>
    [Uc("SBase.SCamNetwork.SwitchToVision", Sha1 = "eac9a50df42a9597ca16ceb4bc2750dcb843c0bf")]
    [PortOf("uc:SBase.SCamNetwork:SwitchToVision", Spec = "spec/devices/cam-network.md#vision-skins-and-fire")]
    public void SwitchToVision(int NewMode)
    {
        if (NewMode == 3)
        {
            NewMode = iCurrentVisionMode switch
            {
                1 => 2,
                2 => -2,
                _ => 1,
            };
        }

        if (iCurrentVisionMode != NewMode)
        {
            switch (NewMode)
            {
                case 1:
                    SetLensSkin(mMotion);
                    break;
                case 2:
                    SetLensSkin(mEMF);
                    break;
                case -2:
                    SetLensSkin(mOn);
                    break;
                case -1:
                case 0:
                    SetLensSkin(mOff);
                    break;
            }

            iChangeToThisVisionMode = NewMode;
            iReplicatedVisionMode = NewMode;
            iCurrentVisionMode = NewMode;
        }
        else
        {
            SetLensSkin(NewMode == -1 ? mOff : mOn);
            iChangeToThisVisionMode = 0;
            iReplicatedVisionMode = 0;
            iCurrentVisionMode = 0;
        }
    }

    /// <summary>Counts a shot for the muzzle flash (replicated).</summary>
    [Uc("SBase.SCamNetwork.AddMuzzleCount", Sha1 = "8958e0a46f35e8459de88587a5f66d2eaf1a6199")]
    [PortOf("uc:SBase.SCamNetwork:AddMuzzleCount", Spec = "spec/devices/cam-network.md#vision-skins-and-fire")]
    public void AddMuzzleCount() => MuzzleCount++;

    private void SetLensSkin(OpenScct.Core.Uc.AssetRef skin)
    {
        if (_disabledLens)
        {
            _enabledLens = skin;
            skin = mOff;
        }

        while (Skins.Count < 2)
        {
            Skins.Add(OpenScct.Core.Uc.AssetRef.None);
        }

        Skins[1] = skin;
    }

    // Runs on clients too: the replicated countdown drives the world model as well as the feed overlay.
    private void UpdateDisabledLens()
    {
        if (IsShotDisabled == _disabledLens)
        {
            return;
        }

        if (IsShotDisabled)
        {
            _enabledLens = Skins.Count > 1 ? Skins[1] : OpenScct.Core.Uc.AssetRef.None;
            SetLensSkin(mOff);
            _disabledLens = true;
        }
        else
        {
            _disabledLens = false;
            SetLensSkin(_enabledLens);
        }
    }
}
