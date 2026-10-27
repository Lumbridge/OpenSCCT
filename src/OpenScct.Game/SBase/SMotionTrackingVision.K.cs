using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>Constants of the motion tracker (spec/equipment/motion-tracking.md), with their evidence.</summary>
public static class SMotionTrackingVisionK
{
    // ---- scan (GetActors) ---------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SMotionTrackingVision.s_Active.BeginState SetTimer(0.5, true)")]
    public const float ScanPeriod = 0.5f;

    /// <summary>The Reloaded sight line: movers, level, level geometry, every hit (0x20086).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded GetActors PUSH 0x20086 (Reloaded.Core 0x100ca89a)")]
    public const ETraceFlags SightFlags = ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
                                          ETraceFlags.TRACE_Sight;

    /// <summary>The stock sight line, made by Reloaded only when the first finds nothing (0x86).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded GetActors PUSH 0x86 (Reloaded.Core 0x100ca8c8); stock GetActors 0x86")]
    public const ETraceFlags StockSightFlags = ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry;

    [Provenance(ProvenanceKind.Decoded, "GetActors detect sound: radius 64 (0x42800000), volume 1, pitch 1")]
    public const float DetectSoundRadius = 64f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded GetActors CMP byte [ESI + 0xab5], 0xff (SBasePawn.DesiredGlassEffect)")]
    public const byte CloakedGlass = 255;

    // ---- screen box (ASMotionTrackingVision::Tick 0x10ac0470) -------------------------------------------------

    /// <summary>A lock's box is at least this radius and half height (Max(CollisionRadius, 30), Max(CollisionHeight, 30)).</summary>
    [Provenance(ProvenanceKind.Decoded, "Tick 0x10ac0470: FUN_1090c860 (max) of CollisionRadius and CollisionHeight with 30.0 (0x41f00000)")]
    public const float BoxMinExtent = 30f;

    /// <summary>The projected depth below which a lock is in front of the camera and drawn.</summary>
    [Provenance(ProvenanceKind.Decoded, "Tick and DrawHUD: ScreenPosition1.Z < 1.0")]
    public const float FrontDepth = 1f;

    /// <summary>Reloaded's widescreen centre of the 640 x 480 HUD frame (0x10361004, 0x10361008, never written).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a7400 / 0x100a7480 / 0x100a9620: (x - 320) k + 320, (y - 240) k + 240")]
    public const float HudCentreX = 320f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core data 0x10361008 = 240.0")]
    public const float HudCentreY = 240f;

    // ---- drawing (FUN_10a31fb0, UCanvas::RenderMotionTracking) -------------------------------------------------

    /// <summary>The 640 x 480 frame the canvas tiles are given in (FUN_109ff300 scales by SizeX / 640, SizeY / 480).</summary>
    [Provenance(ProvenanceKind.Decoded, "FUN_109ff300: X SizeX 0.0015625, Y SizeY 0.0020833334")]
    public const float FrameWidth = 640f;

    [Provenance(ProvenanceKind.Decoded, "FUN_109ff300: 1 / 480")]
    public const float FrameHeight = 480f;

    /// <summary>A bracket corner: 32 x 32 at scale 1, texels 0..31 of SHudTextures.HUD.Vision_lock.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD corner tiles: size scale x 32 (0x10c10b1c), UV 0, 0, 31, 31 (0x41f80000)")]
    public const float CornerSize = 32f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD corner tiles UL, VL 31.0")]
    public const float CornerTexels = 31f;

    /// <summary>The corner scale goes from 4 to 1 while focusing and from 1 to 4 while fading: 4 - 3 x fraction.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: 4.0 (0x10c10ab8) - fraction x 3.0 (0x10c10a34)")]
    public const float ZoomScale = 4f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD: x 3.0")]
    public const float ZoomRange = 3f;

    /// <summary>Corner alpha while focusing and while held.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: Canvas.DrawColor.A = 0x40 before focus, 0x80 after")]
    public const byte FocusAlpha = 0x40;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD: 0x80 while the lock is held")]
    public const byte HeldAlpha = 0x80;

    /// <summary>Fading alpha: trunc(max(0, remaining fraction x 63) + 1).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: x 63.0 (0x10c10b30), + 1.0")]
    public const float FadeAlphaRange = 63f;

    /// <summary>Echo spacing (remanencetime) and count (remanenceecho, i = 1..4).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: now - i x 0.05 (0x10bff1a8), i < 5")]
    public const float EchoSpacing = 0.05f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD: echo loop i = 1 to 4")]
    public const int EchoCount = 4;

    /// <summary>Focus echoes are drawn until 0.2 s after the focus ends; after that only the fade's echoes.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: FocusTime + 0.2 + StartTime <= now")]
    public const float EchoWindow = 0.2f;

    /// <summary>Echo alpha numerator: trunc(128 / i + 1) while focusing, trunc(max(0, remaining x 128 / i / OutTime) + 1) while fading.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: 128.0 (0x10c11358) / i")]
    public const float EchoAlpha = 128f;

    /// <summary>The sweep: SHudTextures.HUD.balayage_H, 16 high, texels 0..15, inset 4 at each side.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD sweep tiles: X + 4, width - 8, height +-16, UV 0, 0, 15, 15")]
    public const float SweepHeight = 16f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD sweep UV 15.0")]
    public const float SweepTexels = 15f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD sweep: X + 4.0, width - 8.0, travel (half height - 8.0)")]
    public const float SweepInset = 4f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD sweep travel stops 8 short of the top")]
    public const float SweepMargin = 8f;

    /// <summary>Sweep alpha: trunc(51 - frac x 50).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: 51.0 (0x10c10f6c) - frac x 50.0 (0x10c10bb0)")]
    public const float SweepAlphaTop = 51f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD: x 50.0")]
    public const float SweepAlphaRange = 50f;

    /// <summary>The friend marks: SHudTextures.HUD.Vision_friend, 32 x 32 (texels 0..32), at the top right and bottom left.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD friend tiles: +-32 scale, UV 0, 0, 32 (0x42000000), 32")]
    public const float FriendTexels = 32f;

    /// <summary>"Hold Fire" blinks: shown while trunc(2 now) is odd.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: ftol(now + now) & 0x80000001 (odd)")]
    public const float BlinkRate = 2f;

    /// <summary>The distance label sits 10 below the speed label.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: CurY = ScreenPosition2.Y + 10.0 (0x10c10af4)")]
    public const float LabelLine = 10f;

    /// <summary>The brackets turn toward grey for 0.4 s after the merc shot a pawn: v = max(0, (now - shot) x 5 - 1).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: fDateLastShootedPawn + 0.4 (double 0x10c11030) > now; x 5.0 (0x10c10b4c) - 1.0")]
    public const float ShotFlashSeconds = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "DrawHUD: x 5.0")]
    public const float ShotFlashRate = 5f;

    /// <summary>Flash base: channel x v + (1 - v) x 128.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: (1.0 - v) x 128.0 (double 0x10c11360)")]
    public const float ShotFlashGrey = 128f;

    /// <summary>Corner colour of an enemy lock (FColor B 0x22, G 0x5b, R 0x80, A 0xff).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: DAT_10c73c78 = 22 5b 80 ff")]
    public static readonly (byte R, byte G, byte B, byte A) LockColour = (0x80, 0x5b, 0x22, 0xff);

    /// <summary>Sweep colour of an enemy lock (grey).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: DAT_10c73c98 = 80 80 80 ff")]
    public static readonly (byte R, byte G, byte B, byte A) SweepColour = (0x80, 0x80, 0x80, 0xff);

    /// <summary>Label colour of an enemy lock: (128, 93, 0) with alpha 0 (drawn with STY_Alpha: invisible).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: DAT_10c73c94 = 00 5d 80 00; canvas Style 5 (FinalBlend FB_AlphaBlend with alpha test, FUN_10a04010)")]
    public static readonly (byte R, byte G, byte B, byte A) LabelColour = (0x80, 0x5d, 0x00, 0x00);

    /// <summary>Friend colour alpha (HUD.iColorFriend_R/G/B with A 0x80); the Hold Fire text draws it at A 0xff.</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawHUD: CONCAT13(0x80, iColorFriend_R/G/B); text alpha 0xff")]
    public const byte FriendAlpha = 0x80;
}
