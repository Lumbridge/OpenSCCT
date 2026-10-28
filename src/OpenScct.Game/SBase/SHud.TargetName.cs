using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// DrawTargetName (native 1809, game 0x10af7800, body 0x10af7920; spec/hud/merc-reticle.md rule 3e): the merc's laser
// target on the HUD's own tracker, its typed-out name and the friend mark. The merc HUD's aim point helper (game
// 0x10af7cf0) runs the same body, and the camera network's HUD state calls the native. The view draws the tracker
// and the name (game/Scripts/Hud/HudMotionTracking.cs).
public partial class SHud
{
    private static readonly ClassRef CREF_SMotionTrackingVision = new("SBase.SMotionTrackingVision");

    /// <summary>SHud.PostRender's merc colour (Hud_R, Hud_G, Hud_B) before HudDef.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.PostRender: Hud_R/G/B = 128, 93, 34 for a merc, 72, 117, 54 for a spy")]
    public static readonly (byte R, byte G, byte B) MercHudColour = (128, 93, 34);

    /// <summary>The cursor drawn after the part of the name typed so far.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7c48: character 0x5f after nbCarName characters")]
    public const char TargetNameCursor = '_';

    /// <summary>
    /// Whether the merc HUD's aim point helper reaches the target name pass this frame (rule 3e): the viewed merc has a
    /// weapon drawn and the reticle is not switched off, with a player.
    /// </summary>
    [UcNative("ASHud::execHudDef (aim point helper)", Exe = 0x10af7cf0)]
    [PortOf("nat:SBase.SHud.DrawTargetName", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7cf0: P.MyWeapon, bReticuleOff (+0x498 m2), P.WeaponLoaded (+0xab4) and PlayerOwner (+0x3e0) before the target name body")]
    public bool TargetNameInAimHelper() =>
        P is SPAWNDEFENSE { MyWeapon: not null, WeaponLoaded: not 0 } && !bReticuleOff && PlayerOwner is not null;

    /// <summary>The HUD's own tracker (HUD.MyTrackingVision), the one the laser's target name uses.</summary>
    public SMotionTrackingVision? TargetTracker => MyTrackingVision as SMotionTrackingVision;

    /// <summary>
    /// The lock part of DrawTargetName (rule 3e), once per HUD frame. Nothing while the viewed merc's laser sight is
    /// off (false). Otherwise the HUD's tracker is spawned the first time; when the laser has a target, its lock is
    /// held for TimeToStayLock more seconds, or a new lock goes first in the list (StartTime now, EndTime now +
    /// TimeToStayLock, flags clear, the name typing restarts) with Snd_TeamateInSight when the target is a merc; then
    /// MyTargetName is the target's player name.
    /// </summary>
    [UcNative("ASHud::execDrawTargetName", Exe = 0x10af7800)]
    [PortOf("nat:SBase.SHud.DrawTargetName", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7800 (P.TorcheLaser.bInUse gate, MyTrackingVision spawn) and 0x10af7920 (find or insert the lock, TimeToStayLock, the merc sound, MyTargetName, nbCarName)")]
    [Provenance(ProvenanceKind.Estimated, "The sight sound plays from the controller (the native plays it through the audio subsystem at the HUD's location, radius 100)")]
    public bool TargetNameLocks()
    {
        if (P is not SPAWNDEFENSE { TorcheLaser: { bInUse: true } laser } || Level is null)
        {
            return false;
        }

        if (MyTrackingVision is null &&
            DeviceClasses.Spawn(this, CREF_SMotionTrackingVision, this, default, Location, Rotation) is SMotionTrackingVision spawned)
        {
            spawned.PlayerOwner = PlayerOwner;
            MyTrackingVision = spawned;
        }

        if (TargetTracker is not { } tracker || laser.TargetPawn is not { } target)
        {
            return true;
        }

        float now = Level.TimeSeconds;
        int found = tracker.FindLock(target);
        if (found >= 0)
        {
            FLockedObject l = tracker.LockedObjects[found];
            l.EndTime = now + TimeToStayLock;
            tracker.LockedObjects[found] = l;
        }
        else
        {
            if (target is SPAWNDEFENSE)
            {
                Owner?.PlaySound(Snd_TeamateInSight, ESoundSlot.SLOT_None, 1f, false, TargetNameSoundRadius, 1f, false);
            }

            tracker.LockedObjects.Insert(0, new FLockedObject { Actor = target, StartTime = now, EndTime = now + TimeToStayLock });
            nbCarName = 0;
        }

        MyTargetName = target.PlayerReplicationInfo?.PlayerName ?? "";
        return true;
    }

    /// <summary>The radius the native gives the sight sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7a1b: the audio call's radius 100.0 (0x42c80000), volume 1, pitch 1")]
    public const float TargetNameSoundRadius = 100f;

    /// <summary>
    /// The name part of DrawTargetName (rule 3e), after the tracker's boxes are placed and its HUD drawn: the laser
    /// target's lock, once TimeToScanName has passed since it began, gets its name over the box, centred on it and
    /// standing on its top edge, in the friend colour (and the lock's ItsMyFriend set) for a teammate, else in the
    /// side's HUD colour. The name types out one character a frame with a cursor until one character is left, then
    /// shows whole. Null when nothing is written. <paramref name="Measure"/> is the font's size of a text in frame
    /// units.
    /// </summary>
    [UcNative("ASHud::execDrawTargetName", Exe = 0x10af7800)]
    [PortOf("nat:SBase.SHud.DrawTargetName", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7b12..0x10af7cb1: TimeToScanName gate, the team test and ItsMyFriend (flag 2), colours, X = (SP1.X + SP2.X) / 2 - XL / 2, Y = SP1.Y - YL, the nbCarName typing")]
    public MotionHudText? TargetNameLabel(Func<string, (float Width, float Height)> Measure)
    {
        ArgumentNullException.ThrowIfNull(Measure);
        if (P is not SPAWNDEFENSE { TorcheLaser: { bInUse: true, TargetPawn: { } target } } merc || TargetTracker is not { } tracker ||
            Level is null)
        {
            return null;
        }

        float now = Level.TimeSeconds;
        MotionHudText? label = null;
        for (int i = 0; i < tracker.LockedObjects.Count; i++)
        {
            FLockedObject l = tracker.LockedObjects[i];
            if (!ReferenceEquals(l.Actor, target) || !(TimeToScanName + l.StartTime < now))
            {
                continue;
            }

            MotionHudColour colour;
            if (target.SPawnType == merc.SPawnType)
            {
                l.ItsMyFriend = true;
                tracker.LockedObjects[i] = l;
                colour = new MotionHudColour(Channel(iColorFriend_R), Channel(iColorFriend_G), Channel(iColorFriend_B), 255);
            }
            else
            {
                colour = new MotionHudColour(Hud_R, Hud_G, Hud_B, 255);
            }

            (float w, float h) = Measure(MyTargetName);
            float x = ((l.ScreenPosition2.X + l.ScreenPosition1.X) * 0.5f) - (SvmMath.Trunc(w) * 0.5f);
            float y = l.ScreenPosition1.Y - SvmMath.Trunc(h);
            int last = MyTargetName.Length == 0 ? 0 : MyTargetName.Length - 1;
            string text;
            if (nbCarName < last)
            {
                nbCarName++;
                text = MyTargetName[..nbCarName] + TargetNameCursor;
            }
            else
            {
                text = MyTargetName;
            }

            label = new MotionHudText(text, MotionHudFont.Medium, SvmMath.Trunc(x), SvmMath.Trunc(y), colour);
        }

        return label;
    }

    /// <summary>
    /// The HUD tracker's expiry (rule 3e): the first lock, in list order, whose EndTime has passed is removed; one a
    /// frame.
    /// </summary>
    [UcNative("ASHud::execDrawTargetName", Exe = 0x10af7800)]
    [PortOf("nat:SBase.SHud.DrawTargetName", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7896 and the aim point helper 0x10af7ee9: the first lock with EndTime < TimeSeconds is removed")]
    public void ExpireTargetLocks()
    {
        if (TargetTracker is not { } tracker || Level is null)
        {
            return;
        }

        float now = Level.TimeSeconds;
        for (int i = 0; i < tracker.LockedObjects.Count; i++)
        {
            if (tracker.LockedObjects[i].EndTime < now)
            {
                tracker.LockedObjects.RemoveAt(i);
                return;
            }
        }
    }

    private static byte Channel(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
