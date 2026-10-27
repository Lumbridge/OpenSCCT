using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>The textures the tracker's HUD draws (SMotionTrackingVision cadre, balayage, TexLaserSentry).</summary>
public enum MotionHudTexture : byte
{
    /// <summary>SHudTextures.HUD.Vision_lock: a bracket corner.</summary>
    Lock,

    /// <summary>SHudTextures.HUD.balayage_H: the sweep band.</summary>
    Sweep,

    /// <summary>SHudTextures.HUD.Vision_friend: the friend mark.</summary>
    Friend,
}

/// <summary>The canvas fonts the tracker's HUD writes with.</summary>
public enum MotionHudFont : byte
{
    /// <summary>Canvas.TinyFont (VerySmallGUIFont): the speed and distance labels.</summary>
    Tiny,

    /// <summary>Canvas.MedFont (SmallGUIFont): "Hold Fire".</summary>
    Medium,
}

/// <summary>A colour as the canvas holds it (0 to 255 a channel).</summary>
public readonly record struct MotionHudColour(byte R, byte G, byte B, byte A)
{
    public MotionHudColour WithAlpha(byte a) => this with { A = a };
}

/// <summary>
/// A canvas tile in the 640 x 480 HUD frame: corner (X, Y), size (XL, YL; negative mirrors), texels U, V, UL, VL.
/// </summary>
public readonly record struct MotionHudTile(MotionHudTexture Texture, float X, float Y, float XL, float YL, float U, float V, float UL,
    float VL, MotionHudColour Colour);

/// <summary>A canvas text at (X, Y) of the HUD frame (its top left).</summary>
public readonly record struct MotionHudText(string Text, MotionHudFont Font, float X, float Y, MotionHudColour Colour);

/// <summary>A lock's clean view rectangle (the renderer redraws the scene inside it in clean monochrome).</summary>
public readonly record struct MotionHudRect(float X1, float X2, float Y1, float Y2);

/// <summary>What the tracker draws in one frame, in draw order, in the 640 x 480 HUD frame.</summary>
public sealed class MotionHudDrawList
{
    private readonly List<MotionHudTile> _tiles = new();
    private readonly List<MotionHudText> _texts = new();
    private readonly List<(bool Text, int Index)> _order = new();

    public IReadOnlyList<MotionHudTile> Tiles => _tiles;

    public IReadOnlyList<MotionHudText> Texts => _texts;

    /// <summary>Tiles and texts in draw order (Text false: <see cref="Tiles"/>, true: <see cref="Texts"/>).</summary>
    public IReadOnlyList<(bool Text, int Index)> Order => _order;

    public List<MotionHudRect> CleanRects { get; } = new();

    public void Clear()
    {
        _tiles.Clear();
        _texts.Clear();
        _order.Clear();
        CleanRects.Clear();
    }

    public void Add(in MotionHudTile tile)
    {
        _order.Add((false, _tiles.Count));
        _tiles.Add(tile);
    }

    public void Add(in MotionHudText text)
    {
        _order.Add((true, _texts.Count));
        _texts.Add(text);
    }
}

/// <summary>What the tracker's HUD reads from the merc's HUD and controller each frame.</summary>
public sealed class MotionHudInputs
{
    /// <summary>LevelInfo.TimeSeconds.</summary>
    public float Now;

    /// <summary>HUD.iColorFriend_R/G/B (defaults 200, 40, 10).</summary>
    public byte FriendR = 200;

    public byte FriendG = 40;

    public byte FriendB = 10;

    /// <summary>The merc controller's fDateLastShootedPawn (when its shot last hit a pawn).</summary>
    public float LastShotPawn = -100f;

    /// <summary>SHud.fDistanceUnit and StrDistanceUnit (metric: 0.01 and "m").</summary>
    public float DistanceUnit = 0.01f;

    public string DistanceUnitText = "m";

    /// <summary>The viewer pawn's location (PlayerOwner.Pawn): the distance label measures from it.</summary>
    public FVector ViewerLocation;

    /// <summary>The localized "Hold Fire" (MotionTrackingVision.TextHold).</summary>
    public string TextHold = "Hold Fire";

    /// <summary>The canvas clip size the zoom and fade run to (640 x 480 on the merc's HUD).</summary>
    public float ClipX = SMotionTrackingVisionK.FrameWidth;

    public float ClipY = SMotionTrackingVisionK.FrameHeight;

    /// <summary>Reloaded's widescreen factor of the draw (1 at 4:3 and wider: min(W, 4/3 H) / H / (4/3)).</summary>
    public float DrawScaleX = 1f;

    /// <summary>The size of a text in a font (WrappedPrint measure), in frame units.</summary>
    public Func<string, MotionHudFont, (float Width, float Height)> Measure = static (s, _) => (s.Length * 6f, 10f);
}

public partial class SMotionTrackingVision
{
    /// <summary>
    /// ASMotionTrackingVision::Tick (0x10ac0470), the part after the actor tick, on the merc's own machine: each lock
    /// with an actor gets the screen box of its actor from the viewer's camera, in the 640 x 480 HUD frame. The box's
    /// corners are the actor's location minus the right axis times max(CollisionRadius, 30) plus the up axis times
    /// max(CollisionHeight, 30), and the opposite, the axes of the viewer pawn's rotation; the width is kept within
    /// MinWidth and MaxWidth of the clip width and the height within MinHeight and MaxHeight of the clip height,
    /// about the box's centre. A box in front of the camera whose focus is done and that is not fading is a clean
    /// view rectangle. Reloaded's widescreen hooks scale x and y about (320, 240) by k before the width test and x
    /// back after it (k is 1 at 4:3 and wider).
    /// </summary>
    /// <param name="Project">A world point to the HUD frame (X, Y) with its depth in Z (below 1 in front of the camera).</param>
    /// <param name="BoxRotation">The viewer pawn's rotation (PlayerOwner.Pawn).</param>
    [UcNative("ASMotionTrackingVision::Tick", Exe = 0x10ac0470)]
    [PortOf("eng:ASMotionTrackingVision::Tick", Spec = "spec/equipment/motion-tracking.md#screen-box")]
    [Provenance(ProvenanceKind.Decoded, "Tick 0x10ac0470; Reloaded hooks 0x10ac0810 (100a7400) and 0x10ac08bd (100a7480)")]
    public void ProjectLocks(Func<FVector, FVector> Project, FRotator BoxRotation, float ClipX, float ClipY, float Now,
        float WidescreenK = 1f, List<MotionHudRect>? CleanRects = null)
    {
        ArgumentNullException.ThrowIfNull(Project);
        BoxRotation.GetAxes(out _, out FVector y, out FVector z);
        for (int i = 0; i < LockedObjects.Count; i++)
        {
            FLockedObject l = LockedObjects[i];
            if (l.Actor is not { } a)
            {
                continue;
            }

            float r = SvmMath.Max(a.CollisionRadius, SMotionTrackingVisionK.BoxMinExtent);
            float h = SvmMath.Max(a.CollisionHeight, SMotionTrackingVisionK.BoxMinExtent);
            FVector p1 = Project(a.Location - y * r + z * h);
            FVector p2 = Project(a.Location + y * r - z * h);
            if (WidescreenK != 1f)
            {
                p1 = ScaleAbout(p1, WidescreenK, true);
                p2 = ScaleAbout(p2, WidescreenK, true);
            }

            ClampSpan(ref p1.X, ref p2.X, MaxWidth * ClipX, MinWidth * ClipX);
            if (WidescreenK != 1f)
            {
                p1.X = (p1.X - SMotionTrackingVisionK.HudCentreX) / WidescreenK + SMotionTrackingVisionK.HudCentreX;
                p2.X = (p2.X - SMotionTrackingVisionK.HudCentreX) / WidescreenK + SMotionTrackingVisionK.HudCentreX;
            }

            ClampSpan(ref p1.Y, ref p2.Y, MaxHeight * ClipY, MinHeight * ClipY);
            l.ScreenPosition1 = p1;
            l.ScreenPosition2 = p2;
            LockedObjects[i] = l;
            if (CleanRects is not null && p1.Z < SMotionTrackingVisionK.FrontDepth && FocusTime + l.StartTime < Now &&
                (!l.bRemove || Now < l.EndTime))
            {
                CleanRects.Add(new MotionHudRect(SvmMath.Min(p1.X, p2.X), SvmMath.Max(p1.X, p2.X), SvmMath.Min(p1.Y, p2.Y),
                    SvmMath.Max(p1.Y, p2.Y)));
            }
        }
    }

    /// <summary>Keeps a span within <paramref name="Max"/> and above <paramref name="Min"/>, about its centre.</summary>
    [Provenance(ProvenanceKind.Decoded, "Tick 0x10ac0470: span > max -> centred max; else span < min -> centred min")]
    private static void ClampSpan(ref float A, ref float B, float Max, float Min)
    {
        float span = B - A;
        float keep;
        if (span > Max)
        {
            keep = Max;
        }
        else if (span < Min)
        {
            keep = Min;
        }
        else
        {
            return;
        }

        A = A + span * 0.5f - keep * 0.5f;
        B = B - span * 0.5f + keep * 0.5f;
    }

    private static FVector ScaleAbout(FVector P, float K, bool Both) => new(
        (P.X - SMotionTrackingVisionK.HudCentreX) * K + SMotionTrackingVisionK.HudCentreX,
        Both ? (P.Y - SMotionTrackingVisionK.HudCentreY) * K + SMotionTrackingVisionK.HudCentreY : P.Y, P.Z);

    /// <summary>
    /// The tracker's HUD (DrawHUD, native 1423, drawn through UCanvas::RenderMotionTracking 0x10a31fb0), for every
    /// lock in list order: the box zooms from the whole clip area to the lock's box over FocusTime with corners from
    /// four times to normal size at alpha 0x40, holds at alpha 0x80 with a sweep band climbing from the box's middle
    /// to its top every second, the speed and distance labels under it, and fades back out to the whole clip area over
    /// OutTime after EndTime; four echoes 0.05 s apart trail the zoom and the fade. A friend's lock (ItsMyFriend) is
    /// drawn in the HUD's friend colour with two friend marks and a blinking "Hold Fire". Locks behind the camera are
    /// not drawn. Labels of an enemy lock are drawn with alpha 0 under the canvas's alpha style: they do not show.
    /// </summary>
    [UcNative("ASMotionTrackingVision::execDrawHUD", Exe = 0x10ac0410)]
    [UcNative("UCanvas::execRenderMotionTracking", Exe = 0x10a33150)]
    [PortOf("nat:SBase.SMotionTrackingVision.DrawHUD", Spec = "spec/equipment/motion-tracking.md#drawhud")]
    [PortOf("nat:Engine.Canvas.RenderMotionTracking", Spec = "spec/equipment/motion-tracking.md#drawhud")]
    [Provenance(ProvenanceKind.Decoded, "FUN_10a31fb0 (game): phases, sizes, alphas, colours, echoes and labels; Reloaded draw hook 0x10a320cb (100a9620) scales x about 320 by its factor")]
    public void BuildHud(MotionHudDrawList Into, MotionHudInputs In)
    {
        ArgumentNullException.ThrowIfNull(Into);
        ArgumentNullException.ThrowIfNull(In);
        float now = In.Now;
        float clipX = In.ClipX;
        float clipY = In.ClipY;
        var amber = new MotionHudColour(SMotionTrackingVisionK.LockColour.R, SMotionTrackingVisionK.LockColour.G, SMotionTrackingVisionK.LockColour.B,
            SMotionTrackingVisionK.LockColour.A);
        var grey = new MotionHudColour(SMotionTrackingVisionK.SweepColour.R, SMotionTrackingVisionK.SweepColour.G, SMotionTrackingVisionK.SweepColour.B,
            SMotionTrackingVisionK.SweepColour.A);
        var label = new MotionHudColour(SMotionTrackingVisionK.LabelColour.R, SMotionTrackingVisionK.LabelColour.G, SMotionTrackingVisionK.LabelColour.B,
            SMotionTrackingVisionK.LabelColour.A);
        var friend = new MotionHudColour(In.FriendR, In.FriendG, In.FriendB, SMotionTrackingVisionK.FriendAlpha);

        // The shot flash: for 0.4 s after the merc's shot hit a pawn the corners go from grey back to their colour.
        bool flash = false;
        float v = 1f;
        if (In.LastShotPawn + SMotionTrackingVisionK.ShotFlashSeconds > now)
        {
            flash = true;
            v = (now - In.LastShotPawn) * SMotionTrackingVisionK.ShotFlashRate - 1f;
            if (v < 0f)
            {
                v = 0f;
            }
        }

        foreach (FLockedObject l in LockedObjects)
        {
            FVector real1 = l.ScreenPosition1;
            FVector real2 = l.ScreenPosition2;
            float width = real2.X - real1.X;
            if (In.DrawScaleX != 1f)
            {
                real1.X = SMotionTrackingVisionK.HudCentreX - (SMotionTrackingVisionK.HudCentreX - real1.X) * In.DrawScaleX;
                real2.X = SMotionTrackingVisionK.HudCentreX - (SMotionTrackingVisionK.HudCentreX - real2.X) * In.DrawScaleX;
            }

            FVector s1 = real1;
            FVector s2 = real2;
            float scale;
            float frac = 0f;
            float focusEnd = FocusTime + l.StartTime;
            if (focusEnd <= now)
            {
                if (l.bRemove && l.EndTime < now)
                {
                    float r = OutTime + l.EndTime - now;
                    float t = now - l.EndTime;
                    s1 = s1 * (r / OutTime);
                    s2 = new FVector(s2.X + (clipX - s2.X) * t / OutTime, s2.Y + (clipY - s2.Y) * t / OutTime, s2.Z - s2.Z * t / OutTime);
                    scale = SMotionTrackingVisionK.ZoomScale - r / OutTime * SMotionTrackingVisionK.ZoomRange;
                }
                else
                {
                    scale = 1f;
                    float since = now - focusEnd;
                    frac = since - MathF.Truncate(since);
                }
            }
            else
            {
                float t = now - l.StartTime;
                float rem = focusEnd - now;
                s1 = s1 * (t / FocusTime);
                s2 = new FVector(s2.X + (clipX - s2.X) * rem / FocusTime, s2.Y + (clipY - s2.Y) * rem / FocusTime, s2.Z - s2.Z * rem / FocusTime);
                scale = SMotionTrackingVisionK.ZoomScale - t / FocusTime * SMotionTrackingVisionK.ZoomRange;
            }

            if (!(s1.Z < SMotionTrackingVisionK.FrontDepth))
            {
                continue;
            }

            float corner = scale * SMotionTrackingVisionK.CornerSize;
            bool odd = ((long)MathF.Truncate(now * SMotionTrackingVisionK.BlinkRate) & 1) != 0;
            if (l.ItsMyFriend && odd)
            {
                (float xl, float yl) = In.Measure(In.TextHold, MotionHudFont.Medium);
                float cx = SvmMath.Max(0f, (s1.X + s2.X) * 0.5f - (int)xl / 2);
                float cy = SvmMath.Max(0f, (s1.Y + s2.Y) * 0.5f - (int)yl / 2);
                Into.Add(new MotionHudText(In.TextHold, MotionHudFont.Medium, MathF.Truncate(cx), MathF.Truncate(cy), friend.WithAlpha(0xff)));
                float f = SMotionTrackingVisionK.FriendTexels;
                Into.Add(new MotionHudTile(MotionHudTexture.Friend, s2.X, s1.Y, -corner, corner, 0f, 0f, f, f, friend));
                Into.Add(new MotionHudTile(MotionHudTexture.Friend, s1.X, s2.Y, corner, -corner, 0f, 0f, f, f, friend));
            }

            if (l.Actor is { } actor && focusEnd < now && (!l.bRemove || now < l.EndTime))
            {
                // The sweep: two mirrored bands on one line, from the box's middle to 8 under its top, fading.
                MotionHudColour sweep = (l.ItsMyFriend ? friend : grey).WithAlpha(Ftol(SMotionTrackingVisionK.SweepAlphaTop - frac * SMotionTrackingVisionK.SweepAlphaRange));
                float half = (real2.Y - real1.Y) * 0.5f;
                float travel = (half - SMotionTrackingVisionK.SweepMargin) * frac;
                float y = half + s1.Y - travel;
                float w = width - 2f * SMotionTrackingVisionK.SweepInset;
                float t = SMotionTrackingVisionK.SweepTexels;
                Into.Add(new MotionHudTile(MotionHudTexture.Sweep, s1.X + SMotionTrackingVisionK.SweepInset, y, w, SMotionTrackingVisionK.SweepHeight, 0f, 0f, t, t, sweep));
                Into.Add(new MotionHudTile(MotionHudTexture.Sweep, s1.X + SMotionTrackingVisionK.SweepInset, y, w, -SMotionTrackingVisionK.SweepHeight, 0f, 0f, t, t, sweep));

                MotionHudColour text = l.ItsMyFriend ? friend : label;
                string speed = string.Format(CultureInfo.InvariantCulture, "{0:F2} {1}/s", actor.Velocity.Size() * In.DistanceUnit, In.DistanceUnitText);
                Into.Add(new MotionHudText(speed, MotionHudFont.Tiny, MathF.Truncate(s1.X), MathF.Truncate(s2.Y), text));
                string distance = string.Format(CultureInfo.InvariantCulture, "{0:F2} {1}", (In.ViewerLocation - actor.Location).Size() * In.DistanceUnit,
                    In.DistanceUnitText);
                Into.Add(new MotionHudText(distance, MotionHudFont.Tiny, MathF.Truncate(s1.X), MathF.Truncate(s2.Y + SMotionTrackingVisionK.LabelLine), text));
            }

            MotionHudColour colour;
            if (l.ItsMyFriend)
            {
                colour = friend;
            }
            else if (flash)
            {
                float grey128 = (1f - v) * SMotionTrackingVisionK.ShotFlashGrey;
                colour = new MotionHudColour(Ftol(amber.R * v + grey128), Ftol(amber.G * v + grey128), Ftol(amber.B * v + grey128), amber.A);
            }
            else
            {
                colour = amber;
            }

            byte alpha;
            if (focusEnd <= now)
            {
                alpha = !l.bRemove || now <= l.EndTime
                    ? SMotionTrackingVisionK.HeldAlpha
                    : Ftol(SvmMath.Max(0f, (OutTime + l.EndTime - now) / OutTime * SMotionTrackingVisionK.FadeAlphaRange) + 1f);
            }
            else
            {
                alpha = SMotionTrackingVisionK.FocusAlpha;
            }

            Corners(Into, s1, s2, corner, colour.WithAlpha(alpha));

            MotionHudColour echoColour = l.ItsMyFriend ? friend : amber;
            if (focusEnd + SMotionTrackingVisionK.EchoWindow <= now)
            {
                if (l.bRemove && l.EndTime < now)
                {
                    float echoScale = SMotionTrackingVisionK.ZoomScale - (OutTime + l.EndTime - now) / OutTime * SMotionTrackingVisionK.ZoomRange;
                    for (int i = 1; i <= SMotionTrackingVisionK.EchoCount; i++)
                    {
                        float ti = now - i * SMotionTrackingVisionK.EchoSpacing;
                        float ri = OutTime + l.EndTime - ti;
                        float gone = ti - l.EndTime;
                        FVector e1 = real1 * (ri / OutTime);
                        FVector e2 = new(real2.X + (clipX - real2.X) * gone / OutTime, real2.Y + (clipY - real2.Y) * gone / OutTime,
                            real2.Z - real2.Z * gone / OutTime);
                        byte a = Ftol(SvmMath.Max(0f, (OutTime + l.EndTime - now) * (SMotionTrackingVisionK.EchoAlpha / i) / OutTime) + 1f);
                        if (e1.X < real1.X)
                        {
                            Corners(Into, e1, e2, echoScale * SMotionTrackingVisionK.CornerSize, echoColour.WithAlpha(a));
                        }
                    }
                }
            }
            else
            {
                for (int i = 1; i <= SMotionTrackingVisionK.EchoCount; i++)
                {
                    float ti = now - i * SMotionTrackingVisionK.EchoSpacing - l.StartTime;
                    float left = FocusTime - ti;
                    FVector e1 = real1 * (ti / FocusTime);
                    FVector e2 = new(real2.X + (clipX - real2.X) * left / FocusTime, real2.Y + (clipY - real2.Y) * left / FocusTime,
                        real2.Z - real2.Z * left / FocusTime);
                    float echoScale = SMotionTrackingVisionK.ZoomScale - ti / FocusTime * SMotionTrackingVisionK.ZoomRange;
                    byte a = Ftol(SMotionTrackingVisionK.EchoAlpha / i + 1f);
                    if (e1.X < real1.X)
                    {
                        Corners(Into, e1, e2, echoScale * SMotionTrackingVisionK.CornerSize, echoColour.WithAlpha(a));
                    }
                }
            }
        }
    }

    /// <summary>The four corners of a box: the lock corner texture mirrored for each corner.</summary>
    private static void Corners(MotionHudDrawList Into, FVector S1, FVector S2, float Size, MotionHudColour Colour)
    {
        float t = SMotionTrackingVisionK.CornerTexels;
        Into.Add(new MotionHudTile(MotionHudTexture.Lock, S1.X, S1.Y, Size, Size, 0f, 0f, t, t, Colour));
        Into.Add(new MotionHudTile(MotionHudTexture.Lock, S2.X, S1.Y, -Size, Size, 0f, 0f, t, t, Colour));
        Into.Add(new MotionHudTile(MotionHudTexture.Lock, S1.X, S2.Y, Size, -Size, 0f, 0f, t, t, Colour));
        Into.Add(new MotionHudTile(MotionHudTexture.Lock, S2.X, S2.Y, -Size, -Size, 0f, 0f, t, t, Colour));
    }

    /// <summary>The engine's float to int (truncation), kept to a byte as the canvas colour bytes take it.</summary>
    private static byte Ftol(float Value) => unchecked((byte)(int)MathF.Truncate(Value));
}
