using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn in the rain: the start of ASBasePawn::Tick (which rain volume the pawn stands in, the cooling heat on
// the server, the wet flag and skin events) and the head splash emitter PostBeginPlay spawns. Written from
// spec/equipment/rain.md.
public partial class SBasePawn
{
    private static readonly ClassRef FlicFlocClass = new("SBase.SFlicFlocEmitter");

    /// <summary>
    /// The rain part at the top of ASBasePawn::Tick, on every machine: a rain volume found last tick is dropped, then
    /// CurrentRainVolume is the first of the level's rain volumes whose box holds the pawn's location. The server
    /// cools the pawn in rain and warms it back outside (rule 2). On entering rain BeginWetSkin and bWet; once out of
    /// the rain and back at full heat (HeatIntensity is replicated) RestoreSkins, then bWet is cleared.
    /// </summary>
    [UcNative("ASBasePawn::Tick", Editor = 0x11205fb0)]
    [PortOf("eng:ASBasePawn::Tick", Spec = "spec/equipment/rain.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::Tick editor 0x11205fb0: CurrentRainVolume cleared when it is an ERainVolume, then UERainPrimitive::CheckPointCollision(Location) over the level's rain list; bWet is bit 0x8 of the bSpyBulletOn group")]
    [Provenance(ProvenanceKind.Decoded, "Leaving: the event (RestoreSkins, name 0x2832, the one PhosphoSkinTime's end also raises) is raised before bWet is cleared")]
    internal void TickRain(float DeltaSeconds)
    {
        if (CurrentRainVolume is ERainVolume)
        {
            CurrentRainVolume = null;
        }

        if (XLevel?.GetService<RainVolumeList>()?.At(Location) is { } volume)
        {
            CurrentRainVolume = volume;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            HeatIntensity = RainHeat(HeatIntensity, CurrentRainVolume, DeltaSeconds, HeatTransitionTime, InRainHeatIntensity);
        }

        if (CurrentRainVolume is null)
        {
            if (bWet && HeatIntensity == 1f)
            {
                RestoreSkins();
                bWet = false;
            }
        }
        else if (!bWet)
        {
            BeginWetSkin();
            bWet = true;
        }
    }

    /// <summary>
    /// The server's heat step (rule 2), with T = HeatTransitionTime / (1 - InRainHeatIntensity): outside rain the heat
    /// rises by dt / T; in an ERainVolume it falls by m_RainFalloff x dt / (100 T); in any other rain actor by 2 dt / T.
    /// The result is kept between InRainHeatIntensity and 1.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::Tick editor 0x11205fb0 (Role == ROLE_Authority); the volume's float at 0x304 is m_RainFalloff")]
    public static float RainHeat(float Heat, Actor? RainVolume, float DeltaSeconds, float HeatTransitionTime, float InRainHeatIntensity)
    {
        float period = HeatTransitionTime / (1f - InRainHeatIntensity);
        float next = RainVolume switch
        {
            null => Heat + (DeltaSeconds / period),
            ERainVolume rain => Heat - (rain.m_RainFalloff * DeltaSeconds / (period * 100f)),
            _ => Heat - ((DeltaSeconds + DeltaSeconds) / period),
        };

        return next < InRainHeatIntensity ? InRainHeatIntensity : next >= 1f ? 1f : next;
    }

    /// <summary>
    /// The wet skin (SPersoTextures HEAT_SN02_SI_wet or HEAT_sn02_wet and heat_sn02_2_wet_head on the spy, the one
    /// chosen by whether this machine's player is a spy; DEF01_heat_bodu_sv on the merc's Skins 0 and 2). The Skins
    /// array is not modelled: the pawn view draws the wet skins from bWet (rule 3).
    /// </summary>
    [Uc("SBase.SBasePawn.BeginWetSkin", Sha1 = "d7f4817e6f9e81e4bd323ae73873e8a38b92d2c3")]
    [PortOf("uc:SBase.SBasePawn:BeginWetSkin", Spec = "spec/equipment/rain.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The Skins array is not modelled: the view reads bWet, which the tick sets right after this event")]
    public void BeginWetSkin()
    {
    }

    /// <summary>
    /// SBasePawn.PostBeginPlay's own part on every machine: the head splash emitter (SFlicFlocEmitter, owned by the
    /// pawn, based on it). The spy's energy and refill pool start values are set by SPawnAttaque.PostBeginPlay.
    /// </summary>
    [Uc("SBase.SBasePawn.PostBeginPlay", Sha1 = "08637dbcdf555ce83c7b957ee2dfd6ca4a66196a")]
    [PortOf("uc:SBase.SBasePawn:PostBeginPlay", Spec = "spec/equipment/rain.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        FlicFloc = DeviceClasses.Spawn(this, FlicFlocClass, this, Name.None, Location, Rotation) as Emitter;
        FlicFloc?.SetBase(this);
    }
}
