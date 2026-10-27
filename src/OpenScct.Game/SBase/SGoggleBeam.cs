using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGoggleBeam: the lit goggle lenses on a pawn's head (spec/equipment/goggle-glow.md). One per pawn and per
/// machine, never replicated; its mode follows the pawn's goggle actions and its skin changes at the goggle clip's
/// notifies.
/// </summary>
public partial class SGoggleBeam
{
    /// <summary>The skin over the beam's material slot (Skins[0]); none until the first ChangeSkin.</summary>
    public AssetRef Skin => Skins.Count > 0 ? Skins[0] : default;

    /// <summary>The pawn the beam belongs to (its owner).</summary>
    public SBasePawn? BeamPawn => Owner as SBasePawn;

    /// <summary>
    /// True when the machine's local player is a merc and the beam's owner a spy (rules 2 and 5). A machine without a
    /// local player controller reads as a spy, as a None controller's SPawnType reads 0.
    /// </summary>
    public bool HiddenFromLocalMerc()
    {
        PawnType local = (Level?.GetLocalPlayerController() as SPlayerController)?.SPawnType ?? PawnType.SPAWNOFFENSE;
        return local == PawnType.SPAWNDEFENSE && BeamPawn?.SPawnType == PawnType.SPAWNOFFENSE;
    }

    /// <summary>Rule 5: unhides the beam and puts on the skin of its mode (a spy's glow stays green).</summary>
    [Uc("SBase.SGoggleBeam.ChangeSkin", Sha1 = "c662ad7b3e4601fc0305a5a430c782140c19aa22")]
    [PortOf("uc:SBase.SGoggleBeam:ChangeSkin", Spec = "spec/equipment/goggle-glow.md#rules")]
    public void ChangeSkin()
    {
        if (HiddenFromLocalMerc())
        {
            return;
        }

        bHidden = false;
        switch (CurrentMode)
        {
            case K.BeamModeNone:
                if (BeamPawn is { } pawn && pawn.SPawnType != PawnType.SPAWNOFFENSE)
                {
                    SetSkin(K.GoggleSkinOrange);
                }

                break;
            case K.BeamModeThermal:
            case K.BeamModeNight:
                SetSkin(K.GoggleSkinGreen);
                break;
            case K.BeamModeMotion:
                SetSkin(K.GoggleSkinMotion);
                break;
            case K.BeamModeEmf:
                SetSkin(K.GoggleSkinEmf);
                break;
            case K.BeamModeNormal:
                SetSkin(K.GoggleSkinOrange);
                break;
        }
    }

    private void SetSkin(AssetRef skin)
    {
        if (Skins.Count == 0)
        {
            Skins.Add(skin);
        }
        else
        {
            Skins[0] = skin;
        }
    }

    /// <summary>Debug: adds to the offset's pitch and shifts the beam (rule 7).</summary>
    [Uc("SBase.SGoggleBeam.X", Sha1 = "eab93a078d2d702e191bc5322d14d1b90baec75f")]
    [PortOf("uc:SBase.SGoggleBeam:X", Spec = "spec/equipment/goggle-glow.md#rules")]
    public void X(float A)
    {
        offset.Pitch += (int)A;
        Shift(offset);
    }

    /// <summary>Debug: adds to the offset's roll and shifts the beam (rule 7).</summary>
    [Uc("SBase.SGoggleBeam.Y", Sha1 = "34b2df1530dfb1a4116338ab89a195acf11690f6")]
    [PortOf("uc:SBase.SGoggleBeam:Y", Spec = "spec/equipment/goggle-glow.md#rules")]
    public void Y(float A)
    {
        offset.Roll += (int)A;
        Shift(offset);
    }

    /// <summary>Debug: adds to the offset's yaw and shifts the beam (rule 7).</summary>
    [Uc("SBase.SGoggleBeam.Z", Sha1 = "e97015bccca8711138d395396a7b297e38e381ef")]
    [PortOf("uc:SBase.SGoggleBeam:Z", Spec = "spec/equipment/goggle-glow.md#rules")]
    public void Z(float A)
    {
        offset.Yaw += (int)A;
        Shift(offset);
    }

    /// <summary>Debug: the relative rotation here and on the client (the script also logs it).</summary>
    [Uc("SBase.SGoggleBeam.Shift", Sha1 = "f64f6a1061ebeab45791065db2525b074ccf085a")]
    [PortOf("uc:SBase.SGoggleBeam:Shift", Spec = "spec/equipment/goggle-glow.md#rules")]
    public void Shift(FRotator E)
    {
        ShiftOnCLient(E);
        RelativeRotation = E;
    }

    [Uc("SBase.SGoggleBeam.ShiftOnCLient", Sha1 = "596ef62c80aa991245929380bf86e3713b14cfc9")]
    [PortOf("uc:SBase.SGoggleBeam:ShiftOnCLient", Spec = "spec/equipment/goggle-glow.md#rules")]
    public void ShiftOnCLient(FRotator E) => RelativeRotation = E;
}

/// <summary>SBase.SGoggleBeam_ATT: the spy's three green lenses.</summary>
public partial class SGoggleBeam_ATT
{
    /// <summary>Rule 2: gone on a merc's machine, otherwise shown from the spawn.</summary>
    [Uc("SBase.SGoggleBeam_ATT.PostBeginPlay", Sha1 = "09085a765e9d55483c4770e343d2a306d76661c3")]
    [PortOf("uc:SBase.SGoggleBeam_ATT:PostBeginPlay", Spec = "spec/equipment/goggle-glow.md#rules")]
    protected override void PostBeginPlay()
    {
        if (HiddenFromLocalMerc())
        {
            Destroy();
        }

        bHidden = false;
        base.PostBeginPlay();
    }
}
