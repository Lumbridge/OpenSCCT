using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the spy's quick launch (QuickUse, native 1313) and of the quick-launched grenade's pop
// (spec/equipment/grenade-throw.md rules 6a and 10a).
public static partial class K
{
    /// <summary>The quick launch starts this far under the right hand bone.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa0743: FSUB 10.0 on the B R Hand origin's Z")]
    public const float QuickThrowHandDrop = 10f;

    /// <summary>Half size of the box swept from the pawn to that start; when it is blocked the throw starts at the weapon.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa0768: extent (15, 15, 15), flags 0x86, blocked when Time < 1")]
    public const float QuickThrowClearExtent = 15f;

    /// <summary>The trace flags of that box (movers, level, world geometry).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa0794: PUSH 0x86")]
    public const int QuickThrowClearFlags = 0x86;

    /// <summary>The fallback start (and a merc's quick launch) is the weapon's FX bone raised by this.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa0844 and 0x10aa09ab: FADD 5.0 on the FX origin's Z")]
    public const float QuickThrowFxRaise = 5f;

    /// <summary>The quick launch's rotation pitch (about 82 degrees down); the yaw and roll are the bone's.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa089d and 0x10aa098c: MOV EDX,0xffffc568")]
    public const int QuickThrowPitch = -15000;

    /// <summary>The spy's quick launch force while the quick throw clip is running (bQuickGadgetHasBegun): downward.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa08c7: (bQuickGadgetHasBegun ? 0xffffff24 : 0) + 0x14")]
    public const int QuickThrowForce = -200;

    /// <summary>The spy's quick launch force without the clip flag.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa08d8: ADD EAX,0x14")]
    public const int QuickThrowForceNoClip = 20;

    /// <summary>The force of a merc's quick launch (the native has the branch; no merc grenade can be quick launched).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execQuickUse game 0x10aa09b6: MOV EAX,0x1")]
    public const int QuickThrowForceMerc = 1;

    /// <summary>A quick launch from a pawn whose base rises faster than this keeps a tenth of the base's velocity.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjGrenade::execLaunch game 0x10ab8a90: 100.0 < Base.Velocity.Z, then Velocity = Base.Velocity * 0.1")]
    public const float QuickLaunchRisingBase = 100f;

    [Provenance(ProvenanceKind.Decoded, "ASProjGrenade::execLaunch game 0x10ab8a90: Base.Velocity * 0.1")]
    public const float QuickLaunchBaseShare = 0.1f;

    /// <summary>
    /// The right hand bone (B R Hand) at the quick throw clip's launch notify, from the pawn's Location in its yaw frame
    /// (forward, right, up). The simulation has no skeleton.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "B R Hand at 0.4 of ThroStAlMix not measured: beside the right hip, 10 forward, 15 right, level with Location")]
    public const float QuickThrowHandForward = 10f;

    [Provenance(ProvenanceKind.Estimated, "as QuickThrowHandForward")]
    public const float QuickThrowHandRight = 15f;

    [Provenance(ProvenanceKind.Estimated, "as QuickThrowHandForward")]
    public const float QuickThrowHandUp = 0f;

    /// <summary>
    /// The server accepts an owner's ThrowGren this much before RefireRate has passed on its own clock, so jitter does
    /// not drop a throw the client timed correctly.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "port choice: the original trusts the client's UseGren; 0.25 s covers ordinary jitter")]
    public const float RemoteRefireSlack = 0.25f;
}
