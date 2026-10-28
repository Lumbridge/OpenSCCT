using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// The merc's motion tracking on the HUD (spec/equipment/motion-tracking.md, "Screen box" and "DrawHUD"): every
/// frame the goggles' tracker of the local merc gets its locks' screen boxes from the drawn camera
/// (<see cref="SMotionTrackingVision.ProjectLocks"/>), then its draw list (<see cref="SMotionTrackingVision.BuildHud"/>)
/// is turned into HUD quads with SHudTextures.HUD.Vision_lock, balayage_H and Vision_friend, in the HUD's canvas
/// colours (doubled, HudK.CanvasColour). The merc page draws it after the Enhanced Reality markers, as
/// ASHud::execHudDef calls RenderMotionTracking after ERDrawObjectives (0x10af8060); the camera network's HUD state
/// draws it too (s_Hud_UseObject_CamNetwork.DrawHUD). Locks exist only while the tracker is active (motion vision).
/// The HUD's own tracker (SHud.DrawTargetName, spec/hud/merc-reticle.md rule 3e) holds the laser sight's target, with
/// its typed name over the bracket and the friend mark for a teammate.
/// </summary>
public sealed class HudMotionTracking
{
    public const string Package = "SHudTextures";

    private static readonly string[] TextureNames = ["Vision_lock", "balayage_H", "Vision_friend"];
    private static readonly Vector2[] TextureSizes = [new(32f, 32f), new(16f, 16f), new(32f, 32f)];

    private readonly MotionHudDrawList _list = new();
    private readonly MotionHudInputs _inputs = new();

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The clean view rectangles of this frame, in screen pixels (for the motion vision renderer).</summary>
    public List<Rect2> CleanRects { get; } = new();

    /// <summary>The tracker drawn last frame, or null.</summary>
    public SMotionTrackingVision? Tracker { get; private set; }

    /// <summary>Locks drawn last frame (in front of the camera).</summary>
    public int DrawnLocks { get; private set; }

    /// <summary>The frame the last draw used.</summary>
    public MotionHudFrame Frame { get; private set; }

    /// <summary>The tracker the local controller's HUD draws: the goggles' tracker of a merc, else null.</summary>
    public static SMotionTrackingVision? TrackerOf(SPlayerController? pc) =>
        pc is { SPawnType: PawnType.SPAWNDEFENSE } && pc.Goggle?.MotionTrackingVision is { bDeleteMe: false } t ? t : null;

    /// <summary>The HUD's own tracker (the laser's target name) drawn last frame, or null.</summary>
    public SMotionTrackingVision? TargetTracker { get; private set; }

    /// <summary>
    /// Projects and draws the local merc's tracker; clears the list when there is none. With
    /// <paramref name="targetName"/> the laser's target name pass of the frame runs too (SHud.DrawTargetName: the
    /// merc HUD's aim point helper past its gates, or the camera network's HUD state): the laser target's lock on the
    /// HUD's own tracker, that tracker's brackets, the typed name, then the tracker's expiry.
    /// </summary>
    public void Update(SPlayerController? pc, Vector2 screen, Func<FVector, Vector2?>? project, bool stretch, HudAssets? assets,
        bool targetName = false)
    {
        DrawList.Clear();
        CleanRects.Clear();
        _list.Clear();
        DrawnLocks = 0;
        Tracker = TrackerOf(pc);
        TargetTracker = null;
        SHud? hud = targetName ? pc?.myHUD as SHud : null;
        if (hud is not null && hud.TargetNameLocks())
        {
            TargetTracker = hud.TargetTracker;
        }

        bool goggles = Tracker is { LockedObjects.Count: > 0 };
        bool laser = TargetTracker is { LockedObjects.Count: > 0 };
        if (pc is null || project is null || (!goggles && !laser))
        {
            hud?.ExpireTargetLocks();
            return;
        }

        MotionHudFrame frame = MotionHudFrame.For(screen.X, screen.Y, stretch);
        Frame = frame;
        float now = pc.Level.TimeSeconds;
        FRotator boxAxes = pc.Pawn?.Rotation ?? pc.Rotation;
        FVector ToFrame(FVector p)
        {
            Vector2? s = project(p);
            if (s is not { } px)
            {
                return new FVector(0f, 0f, 2f);
            }

            (float fx, float fy) = frame.FromScreen(px.X, px.Y);
            return new FVector(fx, fy, 0.5f);
        }

        Fill(pc, now);
        if (goggles)
        {
            Tracker!.ProjectLocks(ToFrame, boxAxes, SMotionTrackingVisionK.FrameWidth, SMotionTrackingVisionK.FrameHeight, now,
                CleanRects: _list.CleanRects);
            Tracker.BuildHud(_list, _inputs);
        }

        if (laser)
        {
            TargetTracker!.ProjectLocks(ToFrame, boxAxes, SMotionTrackingVisionK.FrameWidth, SMotionTrackingVisionK.FrameHeight, now);
            TargetTracker.BuildHud(_list, _inputs);
            if (hud!.TargetNameLabel(t => _inputs.Measure(t, MotionHudFont.Medium)) is { } name)
            {
                _list.Add(name);
            }
        }

        hud?.ExpireTargetLocks();
        foreach (MotionHudRect r in _list.CleanRects)
        {
            Vector2 a = Pixel(frame, r.X1, r.Y1);
            Vector2 b = Pixel(frame, r.X2, r.Y2);
            CleanRects.Add(new Rect2(a, b - a));
        }

        Emit(frame, assets);
    }

    private void Fill(SPlayerController pc, float now)
    {
        _inputs.Now = now;
        _inputs.LastShotPawn = pc.fDateLastShootedPawn;
        _inputs.ViewerLocation = pc.Pawn?.Location ?? pc.Location;
        if (pc.myHUD is { } hud)
        {
            _inputs.FriendR = (byte)Math.Clamp(hud.iColorFriend_R, 0, 255);
            _inputs.FriendG = (byte)Math.Clamp(hud.iColorFriend_G, 0, 255);
            _inputs.FriendB = (byte)Math.Clamp(hud.iColorFriend_B, 0, 255);
            if (hud is SHud shud && shud.StrDistanceUnit.Length > 0)
            {
                _inputs.DistanceUnit = shud.fDistanceUnit;
                _inputs.DistanceUnitText = shud.StrDistanceUnit;
            }
        }

        if ((Tracker ?? TargetTracker) is { } t && t.TextHold.Length > 0)
        {
            _inputs.TextHold = t.TextHold;
        }
    }

    private void Emit(MotionHudFrame frame, HudAssets? assets)
    {
        foreach ((bool text, int index) in _list.Order)
        {
            if (text)
            {
                MotionHudText t = _list.Texts[index];
                if (t.Colour.A == 0)
                {
                    // An enemy lock's labels: alpha 0 under the canvas's alpha style, so nothing shows.
                    continue;
                }

                float h = MathF.Round((t.Font == MotionHudFont.Tiny ? HudMotionK.TinyFontHeight : HudMotionK.MediumFontHeight) * frame.ScaleY);
                Vector2 at = Pixel(frame, t.X, t.Y);
                DrawList.Items.Add(new MagmaDrawItem
                {
                    Path = "MotionTracking/text",
                    Kind = MagmaWidgetKind.Text,
                    Text = t.Text,
                    Font = HudAssets.DefaultFont,
                    Rect = new Rect2(at.X, at.Y, 400f * frame.ScaleX, h),
                    HeightPx = h,
                    Align = MagmaAlign.Left,
                    Color = Colour(t.Colour),
                    TrackingPx = HudText.Tracking * h,
                });
                continue;
            }

            MotionHudTile tile = _list.Tiles[index];
            if (tile.Colour.A == 0)
            {
                continue;
            }

            int ti = (int)tile.Texture;
            Vector2 size = assets?.TextureSize(Package, TextureNames[ti]) is { } s ? new Vector2(s.X, s.Y) : TextureSizes[ti];
            Vector2 p0 = Pixel(frame, tile.X, tile.Y);
            Vector2 p1 = Pixel(frame, tile.X + tile.XL, tile.Y + tile.YL);
            float u0 = tile.U / size.X, v0 = tile.V / size.Y, u1 = (tile.U + tile.UL) / size.X, v1 = (tile.V + tile.VL) / size.Y;
            Color c = Colour(tile.Colour);
            DrawList.Items.Add(new MagmaDrawItem
            {
                Path = "MotionTracking/" + TextureNames[ti],
                Kind = MagmaWidgetKind.Image,
                Package = Package,
                Texture = TextureNames[ti],
                Points = [p0, new Vector2(p1.X, p0.Y), p1, new Vector2(p0.X, p1.Y)],
                Uvs = [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)],
                Colors = [c, c, c, c],
            });
        }

        DrawnLocks = CountDrawn(Tracker) + CountDrawn(TargetTracker);
    }

    private static int CountDrawn(SMotionTrackingVision? tracker)
    {
        int n = 0;
        if (tracker is null)
        {
            return n;
        }

        foreach (FLockedObject l in tracker.LockedObjects)
        {
            if (l.Actor is not null && l.ScreenPosition1.Z < SMotionTrackingVisionK.FrontDepth)
            {
                n++;
            }
        }

        return n;
    }

    private static Vector2 Pixel(MotionHudFrame frame, float x, float y)
    {
        (float px, float py) = frame.ToScreen(x, y);
        return new Vector2(px, py);
    }

    private static Color Colour(MotionHudColour c) => HudK.CanvasColour(c.R, c.G, c.B, c.A);
}

/// <summary>Sizes of the tracker's HUD that come from the canvas fonts.</summary>
public static class HudMotionK
{
    /// <summary>Canvas.TinyFont (VerySmallGUIFont) line height, in 480-high frame units.</summary>
    [Provenance(ProvenanceKind.Estimated, "VerySmallGUIFont is not converted; the labels are invisible on enemies (alpha 0) and the goggles' tracker never marks friends")]
    public const float TinyFontHeight = 9f;

    /// <summary>Canvas.MedFont (SmallGUIFont) line height, in 480-high frame units.</summary>
    [Provenance(ProvenanceKind.Estimated, "SmallGUIFont is not converted")]
    public const float MediumFontHeight = 12f;
}
