using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.STriggerLight: a map light that events switch on, off or over, that a gameplay object's ObjectLights put
/// out when it breaks or is chaffed (a half-second flicker with a spark sound first), written from
/// spec/devices/trigger-light.md. The native tick keeps the corona and the associated shadow meshes in step with
/// the light type; the render reads LightType (DeviceViews switches the map's light by the actor's id).
/// </summary>
public partial class STriggerLight : IObjectLight
{
    /// <summary>Radius of the break sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "STriggerLight.LightOff: PlaySound(GzzzitSound, SLOT_Misc, , , 50)")]
    public const float BreakSoundRadius = 50f;

    /// <summary>How long a broken light flickers before it goes dark.</summary>
    [Provenance(ProvenanceKind.Decoded, "STriggerLight.LightOff: SetTimer(0.5, false)")]
    public const float BreakFlickerTime = 0.5f;

    /// <summary>EMF opacity while the light shows in EMF vision, and while it does not.</summary>
    [Provenance(ProvenanceKind.Decoded, "STriggerLight.SetEMFDLightToClient / UnSetEMFDLightToClient")]
    public const float EmfOnOpacity = 1f;

    [Provenance(ProvenanceKind.Decoded, "STriggerLight.UnSetEMFDLightToClient")]
    public const float EmfOffOpacity = 0.3f;

    /// <summary>Shows the light in EMF vision (a reliable server-to-client call in the original; nothing calls it).</summary>
    [Uc("SBase.STriggerLight.SetEMFDLightToClient", Sha1 = "dba9e6d595c3c362c5c086449cc08f777ca5f27d")]
    [PortOf("uc:SBase.STriggerLight:SetEMFDLightToClient", Spec = "spec/devices/trigger-light.md#rules")]
    public void SetEMFDLightToClient()
    {
        bEMFIsOn = true;
        EMFOpacity = EmfOnOpacity;
    }

    /// <summary>Takes the light out of EMF vision (faded to 0.3).</summary>
    [Uc("SBase.STriggerLight.UnSetEMFDLightToClient", Sha1 = "8f593dadee3bdbf54798b5cf5f88c8b301710693")]
    [PortOf("uc:SBase.STriggerLight:UnSetEMFDLightToClient", Spec = "spec/devices/trigger-light.md#rules")]
    public void UnSetEMFDLightToClient()
    {
        bEMFIsOn = false;
        EMFOpacity = EmfOffOpacity;
    }

    /// <summary>
    /// Remembers the placed light type and corona; a light not initially on starts dark (no type, no corona, its
    /// shadow meshes undrawn); a hidden light is shown with no draw type; the light type is sent once.
    /// </summary>
    [Uc("SBase.STriggerLight.PostBeginPlay", Sha1 = "211bf6d144b6f9332ae2c1a405e8ad2a65a9c4d3")]
    [PortOf("uc:SBase.STriggerLight:PostBeginPlay", Spec = "spec/devices/trigger-light.md#rules")]
    protected override void PostBeginPlay()
    {
        InitialLightType = LightType;
        bInitialCorona = bCorona;
        if (!bInitialyOn)
        {
            LightType = ELightType.LT_None;
            foreach (Actor? shadow in AssociatedShadowActors)
            {
                shadow?.SetDrawType(EDrawType.DT_None);
            }

            bCorona = false;
        }
        else
        {
            LightType = InitialLightType;
            bCorona = bInitialCorona;
        }

        if (bHidden)
        {
            bHidden = false;
            SetDrawType(EDrawType.DT_None);
        }

        bForceReplicationForCurrentLightType = true;
    }

    /// <summary>TriggerControl lights it, TriggerToggle flips it, UnTriggerOnly ignores a trigger.</summary>
    [Uc("SBase.STriggerLight.Trigger", Sha1 = "0eb0938e7643c8ea473b6843076b16dc8980b832")]
    [PortOf("uc:SBase.STriggerLight:Trigger", Spec = "spec/devices/trigger-light.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        switch (TriggerMethode)
        {
            case TriggerType.TriggerControl:
                LightOn();
                break;
            case TriggerType.TriggerToggle:
                LightToggle();
                break;
        }
    }

    /// <summary>Any untrigger puts the light out.</summary>
    [Uc("SBase.STriggerLight.UnTrigger", Sha1 = "7bbf00fefbc719928ece91e7b56f66ca466f9944")]
    [PortOf("uc:SBase.STriggerLight:UnTrigger", Spec = "spec/devices/trigger-light.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => LightOff(false);

    /// <summary>The end of a break flicker: dark.</summary>
    [Uc("SBase.STriggerLight.Timer", Sha1 = "2d96a888da22c7bf9daa6f336f46dedf22c80934")]
    [PortOf("uc:SBase.STriggerLight:Timer", Spec = "spec/devices/trigger-light.md#rules")]
    protected override void Timer()
    {
        if (LightType == ELightType.LT_Flicker)
        {
            LightOff(false);
        }
    }

    /// <summary>Back to the placed type (and beam) when it differs from it, else dark (keeping the beam flag).</summary>
    [Uc("SBase.STriggerLight.LightToggle", Sha1 = "2faaed230f51e9c870f5daf4e36cb715e1463554")]
    [PortOf("uc:SBase.STriggerLight:LightToggle", Spec = "spec/devices/trigger-light.md#rules")]
    public void LightToggle()
    {
        if (LightType != InitialLightType)
        {
            LightType = InitialLightType;
            if (bInitialUsesLightBeam)
            {
                UsesLightBeam = true;
            }
        }
        else
        {
            LightType = ELightType.LT_None;
            bInitialUsesLightBeam = UsesLightBeam;
            UsesLightBeam = false;
        }
    }

    /// <summary>The placed light type, and its beam when it had one.</summary>
    [Uc("SBase.STriggerLight.LightOn", Sha1 = "e6cf804f53ce3d86a0a597cc3289e21177637d09")]
    [PortOf("uc:SBase.STriggerLight:LightOn", Spec = "spec/devices/trigger-light.md#rules")]
    public virtual void LightOn()
    {
        LightType = InitialLightType;
        if (bInitialUsesLightBeam)
        {
            UsesLightBeam = true;
        }
    }

    /// <summary>
    /// Broken: flicker with the spark sound for half a second, then dark. Otherwise dark at once, remembering
    /// whether the beam was drawn.
    /// </summary>
    [Uc("SBase.STriggerLight.LightOff", Sha1 = "61ffe96801d7dea0e768d929fe53a62a76a7cb89")]
    [PortOf("uc:SBase.STriggerLight:LightOff", Spec = "spec/devices/trigger-light.md#rules")]
    public virtual void LightOff(bool bBroken = false)
    {
        if (bBroken)
        {
            LightType = ELightType.LT_Flicker;
            PlaySound(GzzzitSound, ESoundSlot.SLOT_Misc, null, false, BreakSoundRadius);
            SetTimer(BreakFlickerTime, false);
        }
        else
        {
            LightType = ELightType.LT_None;
            bInitialUsesLightBeam = UsesLightBeam;
            UsesLightBeam = false;
        }
    }

    /// <summary>
    /// ASTriggerLight::Tick (every machine): when the light type changed since the last tick, a dark light loses its
    /// corona and hides its shadow meshes; a lit one gets its placed corona back and draws them as static meshes.
    /// </summary>
    [UcNative("ASTriggerLight::Tick", Exe = 0x10ab9470)]
    [PortOf("eng:ASTriggerLight::Tick", Spec = "spec/devices/trigger-light.md#native-tick")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab9470: LightType (+0x2d1) against CurrentLightType (+0x326), bCorona bit 0x10 of +0x2e8 from bInitialCorona, SetDrawType 0 / 8 on AssociatedShadowActors")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || CurrentLightType == LightType)
        {
            return;
        }

        CurrentLightType = LightType;
        bool lit = LightType != ELightType.LT_None;
        bCorona = lit && bInitialCorona;
        foreach (Actor? shadow in AssociatedShadowActors)
        {
            shadow?.SetDrawType(lit ? EDrawType.DT_StaticMesh : EDrawType.DT_None);
        }
    }

    // A gameplay object's ObjectLights (SGamePlayObject): a malfunction is the broken flicker.
    void IObjectLight.LightOn() => LightOn();

    void IObjectLight.LightOff(bool bMalfunction) => LightOff(bMalfunction);

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        for (int i = 0; i < AssociatedShadowActors.Count; i++)
        {
            AssociatedShadowActors[i] = Live(AssociatedShadowActors[i]);
        }
    }
}
