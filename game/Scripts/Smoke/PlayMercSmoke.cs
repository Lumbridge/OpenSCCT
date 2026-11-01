using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Controls;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-merc</c>: the merc kit through the player's real keys (Godot input events for the keys the merc
/// layout binds, read by <see cref="PlayInput"/>, applied to the client's controller and sent over loopback to the
/// listen server), checking what the player sees on the client: the laser beam (Q), the torch light (E), the rate of
/// fire key (F) with the burst and auto cadence and the recoil, the reload time (R) with the ammo on the HUD, the scope
/// (B, Page Up and Page Down, a single shot inside), a frag throw (1, held right mouse: launch speed, bounces, the
/// 2 s fuse and the explosion), the gas mask (4, right mouse: worn, air running down, the HUD view) and the camera
/// network (3, right mouse: the camera view, Shift to the next camera, Page Up zoom, F1 motion tracking inside it,
/// right mouse out).
/// A second leg runs the server inline so it can hold a spy bot in front and read the server: the laser marks the spy
/// (server mark, radar blip on the client HUD), the server's shot spread standing and walking against the decoded
/// rule and its recovery, and a frag dropped at the merc's own feet.
/// SVM_MERC_LEGS picks legs by name (keys, server). With a window and SVM_PLAY_SHOTS naming a folder under local/, each
/// step is saved there.
/// </summary>
public sealed class PlayMercSmoke : SmokeTest
{
    public const string TestName = "play-merc";

    /// <summary>The rifle's rate-of-fire screens (SWeaponDefense Skins[3], spec/equipment/merc-rifle.md rule 16).</summary>
    private const string RofBurst = "SWeaponTex.SI_rof_burst";

    private const string RofAuto = "SWeaponTex.SI_rof_auto";

    private const int TeardownFrames = 4;

    private readonly List<string> _report = new();
    private readonly string[] _legs;
    private int _leg;
    private MercLeg? _current;
    private int _teardown = -1;
    private SmokeResult? _final;

    public PlayMercSmoke()
    {
        string? only = System.Environment.GetEnvironmentVariable("SVM_MERC_LEGS");
        _legs = string.IsNullOrWhiteSpace(only) ? ["keys", "server"] : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            if (++_teardown < TeardownFrames)
            {
                return SmokeResult.Running;
            }

            _teardown = -1;
            if (_final is { } done)
            {
                return done;
            }
        }

        if (_current is null)
        {
            _current = _legs[_leg] == "server" ? ServerLeg(context) : KeysLeg(context);
            return SmokeResult.Running;
        }

        SmokeResult? result = _current.Step(context);
        if (result is null)
        {
            return SmokeResult.Running;
        }

        _current.Dispose();
        _current = null;
        _teardown = 0;
        if (result.Value.State == SmokeState.Failed)
        {
            _final = SmokeResult.Fail($"{_legs[_leg]}: {result.Value.Detail} | done: {string.Join("; ", _report)}");
            return SmokeResult.Running;
        }

        _report.Add(result.Value.Detail);
        if (++_leg >= _legs.Length)
        {
            _final = SmokeResult.Pass(string.Join(" || ", _report));
        }

        return SmokeResult.Running;
    }

    // ---- the keys leg: the launchers' settings ----------------------------------------------------------------

    private static MercLeg KeysLeg(SmokeContext context)
    {
        var leg = new MercLeg(context, PlayHostMode.Thread, bots: 3, walkToWall: true);

        leg.Add("laser on (Q)", 240, s => s.Tap("Q", 2, 3), s =>
        {
            if (s.Laser is not { bInUse: true } || s.Pawn is not SPAWNDEFENSE { bLaserOn: true })
            {
                return null;
            }

            ViewProbe view = s.Lasers;
            if (!view.LocalVisible)
            {
                return s.Ticks > 120 ? $"FAIL: the laser is on but no beam is drawn on the client (laser view {(view.Exists ? "present" : "missing")})" : null;
            }

            if (s.Hud is { } hud && !hud.Snapshot.LaserOn)
            {
                return null;
            }

            return s.Detail($"beam {view.LocalStart} -> {view.LocalEnd} ({FVector.Dist(view.LocalStart, view.LocalEnd):F0} uu, pack mesh {view.PackMesh}), HUD laser {s.Hud?.Snapshot.LaserOn}, reticle {s.Hud?.Snapshot.ShowReticle}");
        });
        leg.Add("laser off (Q)", 240, s => s.Tap("Q", 2, 3), s =>
            s.Laser is { bInUse: false } && !s.Lasers.LocalVisible ? s.Detail("beam gone") : null);
        leg.Add("torch on (E)", 240, s => s.Tap("E", 2, 3), s =>
        {
            if (s.Torch is not { bInUse: true } torch || torch.LightType == ELightType.LT_None)
            {
                return null;
            }

            ViewProbe view = s.FlashLight;
            if (!view.On || !ReferenceEquals(view.LitBy, s.Pawn))
            {
                return s.Ticks > 120 ? $"FAIL: the torch is on in the simulation but no light is drawn (torch view {(view.Exists ? "present" : "missing")}, on {view.On})" : null;
            }

            // Half a second lit, so the shadow pass has run and the picture shows the light.
            if (view.Get<int>("LitFrames") < 30)
            {
                return null;
            }

            return s.Detail($"light at {view.Placement.Location} pointing {view.Placement.Rotation} (bone {view.FromBone}), pawn at {s.Pawn!.Location} yaw {s.Pawn.Rotation.Yaw}, " +
                $"aim {s.PC?.MyDesiredRotation}, IK {s.Pawn.IK_Type}, torch actor {torch.Rotation}");
        });
        leg.Add("torch off (E)", 240, s => s.Tap("E", 2, 3), s =>
            s.Torch is { bInUse: false } && !s.FlashLight.On ? s.Detail("light off") : null);

        // Rate of fire: the rifle starts in Auto; F goes to Burst; a tap fires three shots 0.2 s apart and stops.
        leg.Add("rate of fire to burst (F)", 240, s =>
        {
            if (s.Rifle is { } idle && idle.GetStateName().ToString() == "s_Selected" && s.PressedAt < 0 && s.Frame >= 5)
            {
                s.Remember = idle.ROFMode;
                s.Send("F", true);
                s.PressedAt = s.Frame;
            }
            else if (s.PressedAt >= 0 && s.Frame == s.PressedAt + 3)
            {
                s.Send("F", false);
            }
        }, s => s.Rifle is { ROFMode: SRateOfFireMode.ROF_Burst } r && s.Remember is SRateOfFireMode before && s.RofScreen == RofBurst
            ? s.Detail($"{before} -> {r.ROFMode}, HUD {s.Hud?.Snapshot.FireModeName}, rifle screen {s.RofScreen}") : null);
        leg.Add("burst: one tap fires three shots (left mouse)", 300, s =>
        {
            s.WatchShots();
            s.Tap("LeftMouse", 5, 3);
        }, s =>
        {
            s.WatchShots();
            if (s.Ticks < 90)
            {
                return null;
            }

            if (s.ShotTimes.Count != 3)
            {
                return $"FAIL: a burst tap fired {s.ShotTimes.Count} shots ({s.Gaps()})";
            }

            return s.Detail($"3 shots, gaps {s.Gaps()}, clip {s.Clip}");
        });
        leg.Add("rate of fire to auto (F)", 240, s =>
        {
            if (s.Rifle is { } idle && idle.GetStateName().ToString() == "s_Selected" && s.PressedAt < 0 && s.Frame >= 5)
            {
                s.Send("F", true);
                s.PressedAt = s.Frame;
            }
            else if (s.PressedAt >= 0 && s.Frame == s.PressedAt + 3)
            {
                s.Send("F", false);
            }
        }, s => s.Rifle is { ROFMode: SRateOfFireMode.ROF_Auto } && s.RofScreen == RofAuto
            ? s.Detail($"burst -> auto (the key never reaches single), rifle screen {s.RofScreen}") : null);
        leg.Add("auto: held fire and recoil (left mouse 1.2 s)", 400, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = s.PC?.MyDesiredRotation.Pitch ?? 0;
                s.PeakKick = 0;
            }

            s.WatchShots();
            s.WatchRecoil();
            s.HoldFor("LeftMouse", 1.2);
        }, s =>
        {
            s.WatchShots();
            if (s.Held || s.Ticks < 100)
            {
                return null;
            }

            int n = s.ShotTimes.Count;
            int kick = (short)((s.PC?.MyDesiredRotation.Pitch ?? 0) - (int)s.Remember!);
            if (n < 5 || n > 8)
            {
                return $"FAIL: 1.2 s of auto fired {n} shots ({s.Gaps()})";
            }

            // Reloaded's recoil view step leaves 10 % of 35 % of each camera kick (66..230 units) on the aim:
            // about 2 to 8 units a shot, after a 35 % peak that eases back within 0.066 s.
            if (kick < 2 * n || kick > 9 * n)
            {
                return $"FAIL: {n} shots kicked the aim {kick} units (Reloaded keeps 2..8 a shot)";
            }

            if (s.PeakKick < 10)
            {
                return $"FAIL: the view never rose more than {s.PeakKick} units above its resting climb during the burst";
            }

            return s.Detail($"{n} shots, gaps {s.Gaps()}, aim kept {kick} units ({kick / (float)n:F1} a shot), view peak {s.PeakKick} units over the kept climb, clip {s.Clip}, HUD clip {s.Hud?.Snapshot.ClipAmmo} ammo {s.Hud?.Snapshot.Ammo}");
        });
        leg.Add("reload (R) takes the clip's 2.07 s", 400, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = null;
            }

            s.Tap("R", 5, 3);
            if (s.Frame == 5)
            {
                s.Remember = s.ClientTime;
            }
        }, s =>
        {
            if (s.Remember is not float pressed || s.Rifle is not { } r || r.ClipAmmo != r.ClipMaxAmmo)
            {
                return null;
            }

            float took = s.ClientTime - pressed;
            if (took < 1.9f || took > 2.3f)
            {
                return $"FAIL: the reload took {took:F2} s (FirstReloStFd2 is 2.067 s)";
            }

            if (s.Hud is { } hud && (hud.Snapshot.ClipAmmo != r.ClipAmmo || hud.Snapshot.Ammo != r.Ammo - r.ClipAmmo && hud.Snapshot.Ammo != r.Ammo))
            {
                return null;
            }

            return s.Detail($"clip full after {took:F2} s, clip {r.ClipAmmo} ammo {r.Ammo}, HUD {s.Hud?.Snapshot.ClipAmmo}/{s.Hud?.Snapshot.Ammo}");
        });
        bool sawScopeRaise = false;
        leg.Add("scope (B)", 300, s => s.Tap("B", 5, 3), s =>
        {
            if (s.PC is not { } pc || pc.GetStateName().ToString() != "s_Binoculars_Def" ||
                s.Rifle is not { ROFMode: SRateOfFireMode.ROF_Single } rifle)
                return null;
            if (rifle.bDrawWeapon)
            {
                sawScopeRaise |= s.FirstPersonShown;
                return null;
            }
            if (s.FirstPersonShown) return null;
            return sawScopeRaise
                ? s.Detail($"scoped after visible raise, FOV {pc.FovAngle:F0}, single shot, rifle screen {s.RofScreen}")
                : "FAIL: rifle disappeared before the scope raise finished";
        });
        leg.Add("scope zoom in twice (Page Up)", 300, s =>
        {
            s.Tap("PageUp", 10, 3);
            s.Tap("PageUp", 40, 3);
        }, s => s.PC is { } pc && s.Ticks > 50 && pc.fBinocularVisionFOV < 11f ? s.Detail($"zoom FOV {pc.fBinocularVisionFOV:F0}") : null);
        leg.Add("scope zoom out (Page Down)", 300, s => s.Tap("PageDown", 10, 3), s =>
            s.PC is { } pc && s.Ticks > 20 && pc.fBinocularVisionFOV is > 15f and < 25f ? s.Detail($"zoom FOV {pc.fBinocularVisionFOV:F0}") : null);
        leg.Add("one shot in the scope (left mouse held 0.6 s)", 300, s =>
        {
            s.WatchShots();
            s.HoldFor("LeftMouse", 0.6);
        }, s =>
        {
            s.WatchShots();
            if (s.Held || s.Ticks < 80)
            {
                return null;
            }

            return s.ShotTimes.Count == 1 ? s.Detail("1 shot") : $"FAIL: a held trigger in the scope fired {s.ShotTimes.Count} shots";
        });
        leg.Add("leave the scope (B)", 300, s => s.Tap("B", 10, 3), s =>
            s.PC is { } pc && pc.GetStateName().ToString() != "s_Binoculars_Def" && s.Walking && s.Rifle is { ROFMode: SRateOfFireMode.ROF_Auto }
                && s.RofScreen == RofAuto
                ? s.Detail($"back in {pc.GetStateName()}, rate {s.Rifle.ROFMode}, rifle screen {s.RofScreen}") : null);

        leg.Add("select the frag grenade (1)", 120, s => s.Tap("1", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadFragGrenade g ? s.Detail($"selected, {g.Charge} charges") : null);
        leg.Add("throw a frag (right mouse held 0.75 s)", 900, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = s.Pawn?.SelectedGadget?.Charge ?? 0;
            }

            // Thrown into open space: a frag stopped by a wall 250 uu away comes back at a fifth of its speed (wall
            // rebound 0.2) and goes off inside its 500 uu range, which hurts its thrower as in the original
            // (frag-grenade.md rule 3: the native skips no one); within 350 uu that is the flat 200 of an explosion
            // and the merc died in about 1 run in 5.
            if (!s.FaceOpen() && s.Frame < 240 || s.Frame < 30)
            {
                return;
            }

            s.HoldFor("RightMouse", 0.75);
            s.WatchFrag();
        }, s =>
        {
            s.WatchFrag();
            if (s.Frag is not { } f || !f.Gone || !f.Exploded)
            {
                return s.Ticks > 800 && s.Frag is null ? "FAIL: no frag appeared on the client" : null;
            }

            int force = WeaponRules.ThrowForce(s.ThrowValue, 900f);
            float expect = force + K.GrenadeLaunchBonus;
            if (MathF.Abs(f.LaunchSpeed - expect) > 60f)
            {
                return $"FAIL: the frag left at {f.LaunchSpeed:F0} uu/s, the decoded launch gives {expect:F0} (force {force} + 210, charge {s.ThrowValue:F2})";
            }

            if (f.Life < 1.7f || f.Life > 2.4f)
            {
                return $"FAIL: the frag lived {f.Life:F2} s on the client (2 s fuse)";
            }

            // Out of its range (500 uu), so the frag cannot hurt the merc, who is alive for the next steps.
            float away = FVector.Dist(f.LastAt, s.Pawn?.Location ?? f.LastAt);
            if (away < 520f || s.Pawn is not { Health: > 0 })
            {
                return $"FAIL: the frag went off {away:F0} uu from the merc (open space {s.OpenClear:F0} uu at yaw {s.OpenYaw}), merc health {s.Pawn?.Health.ToString(CultureInfo.InvariantCulture) ?? "dead"}, turns [{string.Join("; ", f.Turns)}]";
            }

            return s.Detail($"launch {f.LaunchSpeed:F0} uu/s (force {force} + 210 at charge {s.ThrowValue:F2}), bounces {f.Bounces}, rest speed {f.LastSpeed:F0}, travelled {f.Travel:F0} uu, " +
                $"exploded after {f.Life:F2} s at {f.LastAt} ({FVector.Dist(f.LastAt, s.Pawn?.Location ?? f.LastAt):F0} uu from the merc), explosion actor {f.Exploded}, charges {s.Remember} -> {s.Pawn?.SelectedGadget?.Charge}, " +
                $"thrown from {f.Start} at view {s.PC?.Rotation}, turns [{string.Join("; ", f.Turns)}]");
        });

        leg.Add("select the gas mask (4)", 120, s => s.Tap("4", 0, 3), s =>
        {
            if (s.Pawn?.SelectedGadget is not SGadGasMask || !GadgetHudRules.GasMask(s.PC).Shown)
            {
                return null;
            }

            // The selection hint (SGadGasMask.Load, 2 s on the state area) with its key token turned into the key name.
            if (s.Hud?.ActivePage is { } page && page.DrawList.Items.Find(i => i.Kind == OpenScctGame.Hud.Magma.MagmaWidgetKind.Text && i.Text.Contains("mask", StringComparison.OrdinalIgnoreCase) && i.Text.Contains("activate", StringComparison.OrdinalIgnoreCase)) is { } hint)
            {
                return hint.Text.Contains('\\', StringComparison.Ordinal)
                    ? $"FAIL: the gas mask hint shows a raw key token: '{hint.Text}'"
                    : s.Detail($"selected, HUD air {GadgetHudRules.GasMask(s.PC).AirSeconds} s, hint '{hint.Text}'");
            }

            return s.Ticks > 60 ? "FAIL: no gas mask hint drawn on the state area" : null;
        });
        leg.Add("wear the gas mask (right mouse)", 300, s =>
        {
            s.Tap("RightMouse", 5, 4);
            if (s.Pawn is { bGasMask: true } && s.Remember is null && s.Pawn.SelectedGadget is SGadGasMask m)
            {
                s.Remember = (m.MaskDuration, s.ClientTime);
            }
        }, s =>
        {
            if (s.Remember is not (float air0, float t0) || s.Pawn?.SelectedGadget is not SGadGasMask mask || s.ClientTime - t0 < 1.5f)
            {
                return null;
            }

            GasMaskView view = GadgetHudRules.GasMask(s.PC);
            float used = air0 - mask.MaskDuration;
            if (!view.Worn || used < 1f)
            {
                return $"FAIL: the mask is {(view.Worn ? "worn" : "not worn")} on the HUD and used {used:F2} s of air in {s.ClientTime - t0:F2} s";
            }

            return s.Detail($"worn, air {air0:F1} -> {mask.MaskDuration:F1} s, HUD gauge frame {view.GaugeFrame}");
        });
        leg.Add("take the gas mask off (right mouse)", 300, s => s.Tap("RightMouse", 5, 4), s =>
            s.Pawn is { bGasMask: false } && !GadgetHudRules.GasMask(s.PC).Worn && s.Ticks > 20 ? s.Detail("off") : null);

        leg.Add("select the camera network (3)", 120, s => s.Tap("3", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadCamNetwork ? s.Detail("selected") : null);
        leg.Add("enter the camera network (right mouse)", 600, s => s.Tap("RightMouse", 10, 4), s =>
            s.PC is { } pc && pc.GetStateName().ToString() == "s_UseObject_CamNetwork" && s.Pawn?.UsedObject is SCamNetwork cam && s.Ticks > 30
                ? s.Remember2(cam, s.Detail($"in camera {Id(cam)} at {cam.Location}, FOV {pc.FovAngle:F0}")) : null);
        leg.Add("next camera (Shift)", 300, s => s.Tap("Shift", 10, 4), s =>
            s.Pawn?.UsedObject is SCamNetwork cam && s.Remember is SCamNetwork before && !ReferenceEquals(cam, before)
                ? s.Remember2(cam, s.Detail($"{Id(before)} -> {Id(cam)}")) : null);
        leg.Add("camera zoom (Page Up held)", 300, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = s.PC?.FovAngle ?? 0f;
            }

            s.Tap("PageUp", 5, 40);
        }, s => s.PC is { } pc && s.Remember is float fov && pc.FovAngle < fov - 2f ? s.Detail($"FOV {fov:F0} -> {pc.FovAngle:F0}") : null);
        leg.Add("motion tracking in the camera (F1)", 300, s => s.Tap("F1", 5, 3), s =>
            s.PC is { RendMap: 37 } pc ? s.Detail($"camera vision {pc.RendMap}") : null);
        leg.Add("leave the camera network (right mouse)", 600, s => s.Tap("RightMouse", 10, 4), s =>
            s.PC is { } pc && pc.GetStateName().ToString() != "s_UseObject_CamNetwork" && s.Walking && s.Pawn?.UsedObject is null && s.Ticks > 30
                ? s.Detail($"back in {pc.GetStateName()}, vision {pc.RendMap}") : null);
        return leg;
    }

    // ---- the server leg: inline host, a spy bot held in front -------------------------------------------------

    private static MercLeg ServerLeg(SmokeContext context)
    {
        var leg = new MercLeg(context, PlayHostMode.Inline, bots: 1, walkToWall: false);
        leg.Add("hold a spy bot in front (setup)", 900, s =>
        {
            s.ServerHook ??= PinBot(s, 450f);
            s.Pinning = true;
        }, s =>
        {
            if (s.Target is null && s.PinSpot is { } spot)
            {
                s.Target = NearestEnemy(s, spot);
            }

            return s.Target is { } t && s.PinSpot is { } at && FVector.Dist(t.Location, at) < 80f && s.Ticks > 60
                ? s.Detail($"bot at {t.Location}") : null;
        });
        leg.Add("aim at the bot (mouse)", 600, s =>
        {
            if (s.Target is { } t)
            {
                s.AimAt(t.Location + new FVector(0f, 0f, 10f));
            }
        }, s => s.Target is { } t && s.AimAt(t.Location + new FVector(0f, 0f, 10f)) is var e && Math.Abs(e.Yaw) < 150 && Math.Abs(e.Pitch) < 150
            ? s.Detail($"aim error yaw {e.Yaw} pitch {e.Pitch}") : null);
        leg.Add("laser on the bot marks it (Q, 5 s)", 900, s =>
        {
            if (s.Target is { } t)
            {
                s.AimAt(t.Location + new FVector(0f, 0f, 10f));
            }

            s.Tap("Q", 2, 3);
        }, s =>
        {
            bool marked = s.Session.ServerSnapshot(h =>
            {
                foreach (SPawnAttaque p in h.World.Level.AllActors<SPawnAttaque>())
                {
                    if (p.bSpyBulletOn && p.iTeamSpying == 1f)
                    {
                        return true;
                    }
                }

                return false;
            });
            if (!marked)
            {
                return null;
            }

            bool blip = s.Hud is null || s.Hud.Snapshot.Blips.Any(b => b.Kind == OpenScctGame.Hud.HudBlipKind.Enemy && s.Target is { } t && FVector.Dist(b.Location, t.Location) < 120f);
            if (!blip)
            {
                return s.Ticks > 700 ? "FAIL: the server marked the spy but the client HUD shows no enemy blip" : null;
            }

            return s.Detail($"server mark after {s.ClientTime - s.StepStart:F1} s, client HUD enemy blips {s.Hud?.Snapshot.Blips.Count(b => b.Kind == OpenScctGame.Hud.HudBlipKind.Enemy)}, client sees mark {s.Target?.bSpyBulletOn}");
        });
        leg.Add("laser off (Q)", 240, s => s.Tap("Q", 2, 3), s => s.Laser is { bInUse: false } ? s.Detail("off") : null);
        leg.Add("let the bot go, face a wall (setup)", 300, s =>
        {
            s.Pinning = false;
            s.FaceEmpty();
        }, s => s.Ticks > 120 ? s.Detail("facing away") : null);
        leg.Add("spread standing (auto, left mouse 1 s)", 400, s =>
        {
            s.ServerShots();
            s.HoldFor("LeftMouse", 1.0);
        }, s =>
        {
            s.ServerShots();
            if (s.Held || s.Ticks < 90)
            {
                return null;
            }

            return CheckSpread(s, "standing");
        });
        leg.Add("spread recovers (0.5 s after)", 120, _ => { }, s =>
        {
            int row = s.Session.ServerSnapshot(h => ((s.Session.ServerController?.Pawn as SBasePawn)?.MyWeapon as SWeaponDefense)?.BulletInARow ?? -1);
            return row == 0 && s.Ticks > 30 ? s.Detail("server BulletInARow back to 0") : null;
        });
        leg.Add("spread walking (W held, left mouse 0.8 s)", 400, s =>
        {
            s.ServerShots();
            s.HoldFor("W", 1.3);
            if (s.ClientTime - s.StepStart > 0.4f)
            {
                s.HoldFor("LeftMouse", 0.8);
            }
        }, s =>
        {
            s.ServerShots();
            if (s.Held || s.Ticks < 90)
            {
                return null;
            }

            return CheckSpread(s, "walking", needSpeed: true);
        });
        return leg;
    }

    private static string Id(Actor a) => a.GetType().Name + "#" + a.ActorId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Every server shot's spread must be the decoded GetAccuracy of that moment.</summary>
    private static string? CheckSpread(KeyState s, string label, bool needSpeed = false)
    {
        if (s.Spread.Count < 3)
        {
            return $"FAIL: {label}: only {s.Spread.Count} server shots seen";
        }

        foreach ((float disp, int row, float speed) in s.Spread)
        {
            float want = MathF.Min((speed / 600f) + 0.15f + (0.1f * row), 0.7f);
            if (MathF.Abs(disp - want) > 0.02f)
            {
                return $"FAIL: {label}: a shot's spread {disp:F3} is not min(v/600 + 0.15 + 0.1 x {row}, 0.7) = {want:F3} at {speed:F0} uu/s";
            }
        }

        if (needSpeed && s.Spread.Max(x => x.Speed) < 50f)
        {
            return $"FAIL: {label}: the merc did not move while firing";
        }

        string list = string.Join(" ", s.Spread.Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Disp:F2}@{x.Row}/{x.Speed:F0}")));
        s.Spread.Clear();
        return s.Detail($"server spread (disp@row/speed): {list}");
    }

    private static SBasePawn? NearestEnemy(KeyState s, FVector spot)
    {
        SBasePawn? best = null;
        float bestD = float.MaxValue;
        foreach (SBasePawn p in s.Session.Client.World.Level.AllActors<SBasePawn>())
        {
            if (p.bDeleteMe || ReferenceEquals(p, s.Pawn) || p is not SPawnAttaque)
            {
                continue;
            }

            float d = FVector.Dist(p.Location, spot);
            if (d < bestD)
            {
                bestD = d;
                best = p;
            }
        }

        return best;
    }

    /// <summary>The server hook that holds the spy bot in front of the merc (setup only; the bot keeps its controller).</summary>
    private static Action<PlaySession> PinBot(KeyState state, float distance) => session =>
    {
        if (!state.Pinning || session.Bots is not { } bots || session.ServerController?.Pawn is not SBasePawn merc)
        {
            return;
        }

        SBasePawn? bot = bots.Bots.Select(b => b.Pawn).FirstOrDefault(p => p is SPawnAttaque { bDeleteMe: false, Health: > 0 });
        if (bot is null)
        {
            return;
        }

        if (state.PinSpot is not { } spot)
        {
            FVector eye = merc.Location + new FVector(0f, 0f, merc.EyeHeight);
            for (float d = distance; d >= 200f && state.PinSpot is null; d -= 100f)
            {
                for (int k = 0; k < 16 && state.PinSpot is null; k++)
                {
                    int step = (k + 1) / 2 * ((k & 1) == 0 ? 1 : -1);
                    FVector dir = new FRotator(0, merc.Rotation.Yaw + (step * 4096), 0).Vector();
                    FVector far = merc.Location + (dir * (d + 120f));
                    if (!GadgetWorld.Line(merc, far + new FVector(0f, 0f, merc.EyeHeight), eye, ETraceFlags.TRACE_World, out _) &&
                        !GadgetWorld.Line(merc, far, merc.Location, ETraceFlags.TRACE_World, out _))
                    {
                        state.PinSpot = merc.Location + (dir * d);
                    }
                }
            }

            if (state.PinSpot is not { } found)
            {
                return;
            }

            spot = found;
        }

        float floor = merc.Location.Z - merc.CollisionHeight;
        if (GadgetWorld.Line(merc, new FVector(spot.X, spot.Y, floor - 300f), new FVector(spot.X, spot.Y, floor + 100f), ETraceFlags.TRACE_World, out FCheckResult ground))
        {
            floor = ground.Location.Z;
        }

        spot.Z = floor + bot.CollisionHeight + 1f;
        float dx = bot.Location.X - spot.X;
        float dy = bot.Location.Y - spot.Y;
        // Held within a few units: a creeping bot pulled back from 30 uu away left the client's laser for a tick or two
        // each second, and every miss restarts the decoded mark timer.
        if ((dx * dx) + (dy * dy) > 4f * 4f || System.Math.Abs(bot.Location.Z - spot.Z) > 20f)
        {
            bot.SetLocation(spot);
        }

        bot.Velocity = new FVector(0f, 0f, bot.Velocity.Z);
        bot.Acceleration = default;
    };

    /// <summary>One play of SteeE as the merc: the scripted pilot walks to a wall (setup), then the steps press keys.</summary>
    private sealed class MercLeg : IDisposable
    {
        private readonly List<(string Name, int TimeoutTicks, Action<KeyState> Act, Func<KeyState, string?> Done)> _steps = new();
        private readonly List<string> _results = new();
        private readonly List<string> _failures = new();
        private readonly bool _keepGoing = System.Environment.GetEnvironmentVariable("SVM_MERC_KEEP_GOING") == "1";
        private PlayRoot? _root;
        private ScriptedPilot? _pilot;
        private KeyState? _state;
        private (FVector Spot, FVector Face)? _wall;
        private int _index;
        private bool _hooked;
        private long _stepStartTick;
        private long _lastActTick = -1;
        private int _shots;

        public MercLeg(SmokeContext context, PlayHostMode host, int bots, bool walkToWall)
        {
            _pilot = walkToWall ? new ScriptedPilot(Wall) { UseHoldTicks = 1 } : null;
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = "SteeE",
                    Side = PlaySide.Merc,
                    Rules = PlayRules.PlayLevel,
                    HostMode = host,
                    MapRoots = [.. context.App.Options.MapRoots],
                    Bots = bots,
                    Difficulty = "normal",
                },
                Render = true,
                Hud = true,
                TicksPerFrame = 0,
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
        }

        public void Add(string name, int timeoutTicks, Action<KeyState> act, Func<KeyState, string?> done)
        {
            if (_steps.Count == 0)
            {
                _steps.Add(("settle", 600, _ => { }, s => s.Walking && s.Ticks > 30 && s.Rifle is not null ? s.Detail($"in {s.PC!.GetStateName()}") : null));
            }

            _steps.Add((name, timeoutTicks, act, done));
        }

        public SmokeResult? Step(SmokeContext context)
        {
            if (_root is null)
            {
                return SmokeResult.Fail("no play scene");
            }

            if (_root.LoadError is { } error)
            {
                return SmokeResult.Fail("play scene did not load: " + error);
            }

            if (!_root.IsLoaded || _root.Session is not { } session || _root.Controls is not { } controls)
            {
                return null;
            }

            if (session.ServerFailure is { } failure)
            {
                return SmokeResult.Fail("server failed: " + failure.Message);
            }

            if (!_hooked)
            {
                Action<PlaySession, long>? before = session.BeforeClientTick;
                session.BeforeClientTick = (s, t) =>
                {
                    before?.Invoke(s, t);
                    if (_pilot is { Phase: not PilotPhase.Done and not PilotPhase.Failed })
                    {
                        _pilot.Tick(s);
                    }
                    else if (_state?.ServerHook is { } hook && s.Settings.HostMode == PlayHostMode.Inline)
                    {
                        hook(s);
                    }
                };
                controls.Enabled = true;
                controls.CapturePointer = false;
                controls.RequireCapture = DisplayServer.GetName() != "headless";
                if (controls.RequireCapture)
                {
                    OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
                }

                _hooked = true;
            }

            if (_pilot is { Phase: PilotPhase.Failed })
            {
                return SmokeResult.Fail($"pilot failed: {_pilot.Failure}");
            }

            if (_pilot is { Phase: not PilotPhase.Done })
            {
                if (session.TicksRun <= 60 * 60)
                {
                    return null;
                }

                GD.Print($"SVM_SMOKE {TestName}: the walk to the wall stopped at {session.LocalPawn?.Location} ({_pilot.Phase}); the steps start here");
                _pilot = null;
                session.Input.Clear();
            }

            _state ??= new KeyState(session, _root, _wall);
            if (_state.Frame == 0 && _index == 0 && _stepStartTick == 0)
            {
                _stepStartTick = session.TicksRun;
                _state.StepStart = _state.ClientTime;
            }

            (string name, int timeout, Action<KeyState> act, Func<KeyState, string?> done) = _steps[_index];
            _state.Ticks = (int)(session.TicksRun - _stepStartTick);
            string? detail = _state.Frame == 0 ? null : done(_state);
            string? failed = null;
            if (detail is not null && detail.StartsWith("FAIL: ", StringComparison.Ordinal))
            {
                failed = $"step '{name}': {detail[6..]} ({_state.Describe()})";
            }
            else if (detail is null && _state.Ticks > timeout)
            {
                failed = $"step '{name}' had no result after {timeout} ticks ({_state.Describe()})";
            }

            if (failed is not null)
            {
                Shot(name + " FAILED");
                GD.Print($"SVM_SMOKE {TestName} FAILED {failed}");
                if (!_keepGoing)
                {
                    return SmokeResult.Fail($"{failed} | done: {string.Join("; ", _results)}");
                }

                _failures.Add(failed);
            }
            else if (detail is not null)
            {
                _results.Add($"{name}: {detail}");
                GD.Print($"SVM_SMOKE {TestName} ok {name}: {detail}");
                Shot(name);
            }

            if (failed is not null || detail is not null)
            {
                _state.NextStep();
                _index++;
                _stepStartTick = session.TicksRun;
                if (_index < _steps.Count)
                {
                    return null;
                }

                return _failures.Count == 0
                    ? SmokeResult.Pass("merc: " + string.Join("; ", _results))
                    : SmokeResult.Fail($"{_failures.Count} failed: {string.Join(" || ", _failures)} | done: {string.Join("; ", _results)}");
            }

            // One step action a simulation tick: the steps count their key holds in frames and steer the view with mouse
            // moves from the last tick's rotation; at a few hundred frames a second a three-frame tap could press and
            // release between two ticks, and several moves a tick overshot the aim.
            if (session.TicksRun == _lastActTick)
            {
                return null;
            }

            _lastActTick = session.TicksRun;
            act(_state);
            _state.Frame++;
            return null;
        }

        private void Shot(string step)
        {
            string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
            if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
            {
                return;
            }

            System.IO.Directory.CreateDirectory(folder);
            Image image = _root.GetViewport().GetTexture().GetImage();
            string safe = string.Concat(step.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
            image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_shots++:D2}-{safe}.png")));
        }

        private (FVector Spot, FVector Face)? Wall(SPlayerController pc)
        {
            if (_wall is { } known)
            {
                return known;
            }

            if (pc.Pawn is not SBasePawn pawn)
            {
                return null;
            }

            const float standoff = 300f;
            float best = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                FVector dir = new FRotator(0, k * 4096, 0).Vector();
                FVector start = pawn.Location;
                if (!GadgetWorld.Line(pawn, start + dir * 1500f, start, ETraceFlags.TRACE_World, out FCheckResult hit) || MathF.Abs(hit.Normal.Z) > 0.3f)
                {
                    continue;
                }

                float d = FVector.Dist(hit.Location, start);
                if (d > standoff + 60f && d < best)
                {
                    best = d;
                    FVector spot = hit.Location - dir * standoff;
                    spot.Z = start.Z;
                    _wall = (spot, hit.Location);
                }
            }

            return _wall;
        }

        public void Dispose()
        {
            _state?.ReleaseAll();
            if (DisplayServer.GetName() != "headless")
            {
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
            }

            _root?.QueueFree();
            _root = null;
        }
    }

    /// <summary>Reads a view node's public properties by name (null when the node or the property is missing).</summary>
    private readonly struct ViewProbe
    {
        private readonly Node? _node;

        public ViewProbe(Node? node) => _node = node;

        public bool Exists => _node is not null;

        public T? Get<T>(string name) => _node?.GetType().GetProperty(name)?.GetValue(_node) is T v ? v : default;

        public bool LocalVisible => Get<bool>("LocalVisible");

        public FVector LocalStart => Get<FVector>("LocalStart");

        public FVector LocalEnd => Get<FVector>("LocalEnd");

        public bool PackMesh => Get<bool>("PackMesh");

        public bool On => Get<bool>("On");

        public SBasePawn? LitBy => Get<SBasePawn>("LitBy");

        public bool FromBone => Get<bool>("FromBone");

        public (FVector Location, FRotator Rotation) Placement => Get<(FVector, FRotator)>("Placement");
    }

    /// <summary>A frag followed on the client from its first frame to its end.</summary>
    private sealed class FragTrack
    {
        public SProjFrag? Actor;
        public float Born;
        public float LaunchSpeed;
        public float LastVz;
        public float LastSpeed;
        public int Bounces;
        public FVector Start;
        public FVector LastAt;
        public float Travel;
        public float Life;
        public bool Gone;
        public bool Exploded;
        public FVector LastVelocity;

        /// <summary>Each turn of the frag's path (a bounce off the floor, a wall or a pawn): where, the speed before and after, the nearest other pawn.</summary>
        public List<string> Turns { get; } = new();
    }

    /// <summary>What a step sees, and the keys it presses as Godot input events.</summary>
    private sealed class KeyState
    {
        private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
        private readonly PlayRoot _root;
        private readonly (FVector Spot, FVector Face)? _wall;
        private int _lastClip = -1;
        private int _lastShotsFired = -1;
        private int _explosionsBefore = -1;
        private readonly Dictionary<string, double> _holds = new(StringComparer.OrdinalIgnoreCase);
        private float _throwPressedAt = -1f;

        public KeyState(PlaySession session, PlayRoot root, (FVector Spot, FVector Face)? wall)
        {
            Session = session;
            _root = root;
            _wall = wall;
        }

        public PlaySession Session { get; }

        public int Frame { get; set; }

        public int Ticks { get; set; }

        public float StepStart { get; set; }

        public object? Remember { get; set; }

        public int PressedAt { get; set; } = -1;

        public Action<PlaySession>? ServerHook { get; set; }

        public SBasePawn? Target { get; set; }

        public FVector? PinSpot { get; set; }

        public bool Pinning { get; set; }

        public List<float> ShotTimes { get; } = new();

        public List<(float Disp, int Row, float Speed)> Spread { get; } = new();

        public FragTrack? Frag { get; private set; }

        /// <summary>The throw's charge value from the hold time (grenade-throw.md rule 2).</summary>
        public float ThrowValue { get; private set; }

        /// <summary>A timed hold (<see cref="HoldFor"/>) is still down.</summary>
        public bool Held => _holds.Values.Any(v => v >= 0);

        public float ClientTime => Session.Client.World.Level.Info.TimeSeconds;

        public SPlayerController? PC => Session.LocalController;

        public SBasePawn? Pawn => Session.LocalPawn;

        public SWeaponDefense? Rifle => Pawn?.MyWeapon as SWeaponDefense;

        public int Clip => Rifle?.ClipAmmo ?? -1;

        public SGadTorcheLaser? Laser => (Pawn as SPAWNDEFENSE)?.TorcheLaser;

        public SGadTorche? Torch => (Pawn as SPAWNDEFENSE)?.Torche;

        /// <summary>The play scene's laser view, read by name so the smoke also runs (and fails) on a build without it.</summary>
        public ViewProbe Lasers => new(_root.FindChild("MercLasers", false, false));

        /// <summary>The play scene's torch view, read the same way.</summary>
        public ViewProbe FlashLight => new(_root.FindChild("FlashLight", false, false));

        public OpenScctGame.Hud.HudLayer? Hud => _root.Hud;

        /// <summary>The material the drawn rifle's rate-of-fire screen (view model slot 3) shows.</summary>
        public string? RofScreen => _root.FirstPerson is { Model: { } m } fp
            ? fp.SlotSkin(3) ?? (m.Slots.Count > 3 ? m.Slots[3] : null)
            : null;

        public bool FirstPersonShown => _root.FirstPerson?.Shown == true;

        public bool Walking => PC?.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true;

        public void NextStep()
        {
            ReleaseAll();
            Frame = 0;
            Remember = null;
            PressedAt = -1;
            ShotTimes.Clear();
            _lastClip = -1;
            _holds.Clear();
            StepStart = ClientTime;
        }

        /// <summary>Remembers <paramref name="value"/> for the next step and returns <paramref name="detail"/>.</summary>
        public string Remember2(object value, string detail)
        {
            _carry = value;
            return detail;
        }

        private object? _carry;

        public void Tap(string ik, int at, int frames)
        {
            if (Frame == 0 && _carry is not null)
            {
                Remember ??= _carry;
                _carry = null;
            }

            if (Frame == at)
            {
                Send(ik, true);
            }
            else if (Frame == at + frames)
            {
                Send(ik, false);
            }
        }

        /// <summary>Presses <paramref name="ik"/> once in the step and releases it after <paramref name="seconds"/> of client time.</summary>
        public void HoldFor(string ik, double seconds)
        {
            if (!_holds.TryGetValue(ik, out double until))
            {
                _holds[ik] = ClientTime + seconds;
                Send(ik, true);
                if (ik == "RightMouse")
                {
                    _throwPressedAt = ClientTime;
                }

                return;
            }

            if (until >= 0 && ClientTime >= until)
            {
                if (ik == "RightMouse" && _throwPressedAt >= 0f)
                {
                    float e = ClientTime - _throwPressedAt;
                    ThrowValue = e < 0.25f ? 1f : MathF.Min((e - 0.25f) / 1f, 1f);
                    if (Hud?.Snapshot.Ballistic is > 0f and var b)
                    {
                        ThrowValue = b;
                    }
                }

                Send(ik, false);
                _holds[ik] = -1;
            }
        }

        /// <summary>The largest in-flight recoil offset of the view seen (the Reloaded animation's applied pitch).</summary>
        public int PeakKick { get; set; }

        public void WatchRecoil()
        {
            if (Rifle?.GetType().GetField("RecoilAppliedPitch")?.GetValue(Rifle) is int applied && applied > PeakKick)
            {
                PeakKick = applied;
            }
        }

        /// <summary>Notes each client clip decrement (one per shot the merc's client fired) with its time.</summary>
        public void WatchShots()
        {
            int clip = Clip;
            if (_lastClip >= 0 && clip >= 0 && clip < _lastClip)
            {
                for (int i = clip; i < _lastClip; i++)
                {
                    ShotTimes.Add(ClientTime);
                }
            }

            _lastClip = clip;
        }

        public string Gaps()
        {
            var gaps = new List<string>();
            for (int i = 1; i < ShotTimes.Count; i++)
            {
                gaps.Add((ShotTimes[i] - ShotTimes[i - 1]).ToString("F2", CultureInfo.InvariantCulture));
            }

            return gaps.Count == 0 ? "none" : string.Join(",", gaps);
        }

        /// <summary>Inline host only: records each server shot's spread, row and the merc's speed.</summary>
        public void ServerShots()
        {
            (int fired, float disp, int row, float speed) = Session.ServerSnapshot(h =>
            {
                var merc = Session.ServerController?.Pawn as SPAWNDEFENSE;
                var rifle = merc?.MyWeapon as SWeaponDefense;
                return (merc?.WeaponDummy?.MuzzleCount ?? -1, rifle?.DispersionActuelle ?? 0f, rifle?.BulletInARow ?? 0, merc?.Velocity.Size() ?? 0f);
            });
            if (_lastShotsFired >= 0 && fired > _lastShotsFired)
            {
                Spread.Add((disp, row, speed));
            }

            _lastShotsFired = fired;
        }

        /// <summary>Follows the first frag the client sees: launch speed, bounces, travel, life, the explosion actor.</summary>
        public void WatchFrag()
        {
            Level level = Session.Client.World.Level;
            if (_explosionsBefore < 0)
            {
                _explosionsBefore = Live<SGrenadeExplosion>(level);
            }

            if (Frag is null)
            {
                SProjFrag? f = null;
                foreach (SProjFrag candidate in level.AllActors<SProjFrag>())
                {
                    if (!candidate.bDeleteMe)
                    {
                        f = candidate;
                        break;
                    }
                }

                if (f is null)
                {
                    return;
                }

                Frag = new FragTrack { Actor = f, Born = ClientTime, LaunchSpeed = f.Velocity.Size(), Start = f.Location, LastAt = f.Location, LastVz = f.Velocity.Z, LastVelocity = f.Velocity };
                return;
            }

            FragTrack t = Frag;
            if (t.Actor is { bDeleteMe: false } a)
            {
                if (t.LastVz < -1f && a.Velocity.Z > 1f)
                {
                    t.Bounces++;
                }

                // A turn: the horizontal direction changed by more than 20 degrees in one frame.
                FVector before = new(t.LastVelocity.X, t.LastVelocity.Y, 0f);
                FVector after = new(a.Velocity.X, a.Velocity.Y, 0f);
                if (before.Size() > 20f && after.Size() > 1f && (before.SafeNormal() | after.SafeNormal()) < 0.94f && t.Turns.Count < 8)
                {
                    t.Turns.Add(string.Create(CultureInfo.InvariantCulture,
                        $"at {a.Location} {t.LastVelocity.Size():F0}->{a.Velocity.Size():F0} uu/s, nearest pawn {NearestPawn(level, a.Location)}"));
                }

                t.LastVelocity = a.Velocity;

                t.LastVz = a.Velocity.Z;
                t.LastSpeed = a.Velocity.Size();
                t.LastAt = a.Location;
                t.Travel = MathF.Max(t.Travel, FVector.Dist(a.Location, t.Start));
                t.Life = ClientTime - t.Born;
            }
            else if (!t.Gone)
            {
                t.Gone = true;
                t.Life = ClientTime - t.Born;
            }

            if (!t.Exploded && Live<SGrenadeExplosion>(level) > _explosionsBefore)
            {
                t.Exploded = true;
            }
        }

        private string NearestPawn(Level level, FVector at)
        {
            string best = "none";
            float bestDist = float.MaxValue;
            foreach (Pawn p in level.AllActors<Pawn>())
            {
                float d = FVector.Dist(p.Location, at);
                if (!p.bDeleteMe && d < bestDist)
                {
                    bestDist = d;
                    best = string.Create(CultureInfo.InvariantCulture, $"{(ReferenceEquals(p, Pawn) ? "the merc" : p.GetType().Name)} {d:F0} uu");
                }
            }

            return best;
        }

        private static int Live<T>(Level level)
            where T : Actor
        {
            int n = 0;
            foreach (T a in level.AllActors<T>())
            {
                if (!a.bDeleteMe)
                {
                    n++;
                }
            }

            return n;
        }

        public (int Yaw, int Pitch) AimAt(FVector point)
        {
            if (PC is not { } pc || Pawn is not { } pawn)
            {
                return (int.MaxValue, int.MaxValue);
            }

            FVector eye = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
            FRotator want = (point - eye).Rotation();
            FRotator have = pc.MyDesiredRotation;
            int yaw = (short)(want.Yaw - have.Yaw);
            int pitch = (short)(want.Pitch - have.Pitch);
            // At most one move a simulation tick: the error is measured on the last tick's rotation, and the step's
            // check calls this too.
            if ((Math.Abs(yaw) > 60 || Math.Abs(pitch) > 60) && Session.TicksRun != _aimTick)
            {
                _aimTick = Session.TicksRun;
                MouseMove(Math.Clamp(yaw / KeyPilot.UnitsPerCount, -30f, 30f), Math.Clamp(-pitch / KeyPilot.UnitsPerCount, -20f, 20f));
            }

            return (yaw, pitch);
        }

        private long _aimTick = -1;

        public void FaceAway()
        {
            if (PC is not { } pc || Pawn is not { } pawn || _wall is not { } w)
            {
                return;
            }

            FVector d = pawn.Location - w.Face;
            int want = new FVector(d.X, d.Y, 0f).Rotation().Yaw;
            int err = (short)(want - pc.MyDesiredRotation.Yaw);
            int pitchErr = (short)(0 - pc.MyDesiredRotation.Pitch);
            if (Math.Abs(err) > 150 || Math.Abs(pitchErr) > 600)
            {
                MouseMove(Math.Clamp(err / 60f, -40f, 40f), Math.Clamp(-pitchErr / 60f, -20f, 20f));
            }
        }

        /// <summary>The yaw <see cref="FaceOpen"/> turns to, and how far its lines run clear.</summary>
        public int OpenYaw { get; private set; } = -1;

        public float OpenClear { get; private set; }

        /// <summary>
        /// Turns (mouse motion) to the level yaw with the most open space ahead: chosen once, of 32 directions, by the
        /// shorter of two world lines from the eye, level and rising 200 uu over 1500 uu. True once facing it.
        /// </summary>
        public bool FaceOpen()
        {
            if (PC is not { } pc || Pawn is not { } pawn)
            {
                return false;
            }

            if (OpenYaw < 0)
            {
                FVector eye = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
                for (int k = 0; k < 32; k++)
                {
                    int yaw = k * 2048;
                    FVector dir = new FRotator(0, yaw, 0).Vector();
                    float clear = MathF.Min(Clear(pawn, eye, eye + (dir * 1500f)), Clear(pawn, eye, eye + (dir * 1500f) + new FVector(0f, 0f, 200f)));
                    if (clear > OpenClear)
                    {
                        OpenClear = clear;
                        OpenYaw = yaw;
                    }
                }
            }

            int err = (short)(OpenYaw - pc.MyDesiredRotation.Yaw);
            int pitchErr = (short)(0 - pc.MyDesiredRotation.Pitch);
            if (Math.Abs(err) > 150 || Math.Abs(pitchErr) > 600)
            {
                MouseMove(Math.Clamp(err / 60f, -40f, 40f), Math.Clamp(-pitchErr / 60f, -20f, 20f));
                return false;
            }

            return true;
        }

        private static float Clear(SBasePawn pawn, FVector from, FVector to) =>
            GadgetWorld.Line(pawn, to, from, ETraceFlags.TRACE_World, out FCheckResult hit) ? FVector.Dist(hit.Location, from) : FVector.Dist(to, from);

        /// <summary>Turns away from the target bot (half a turn) with mouse motion.</summary>
        public void FaceEmpty()
        {
            if (PC is not { } pc || Pawn is not { } pawn || Target is not { } t)
            {
                return;
            }

            FVector d = pawn.Location - t.Location;
            int want = new FVector(d.X, d.Y, 0f).Rotation().Yaw;
            int err = (short)(want - pc.MyDesiredRotation.Yaw);
            if (Math.Abs(err) > 150)
            {
                MouseMove(Math.Clamp(err / 60f, -40f, 40f), 0f);
            }
        }

        public void MouseMove(float dx, float dy)
        {
            float width = _root.GetViewport().GetVisibleRect().Size.X;
            if (width > 1f && width < 640f)
            {
                dx *= 1280f / width;
                dy *= 1280f / width;
            }

            var ev = new InputEventMouseMotion { Relative = new Vector2(dx, dy), ScreenRelative = new Vector2(dx, dy), Position = Center };
            Input.ParseInputEvent(ev);
            Input.FlushBufferedEvents();
        }

        private Vector2 Center => _root.GetViewport().GetVisibleRect().Size * 0.5f;

        public void Send(string ik, bool pressed)
        {
            InputEvent? ev = GodotKeys.ToEvent(ik);
            switch (ev)
            {
                case InputEventKey key:
                    key.Keycode = key.PhysicalKeycode;
                    key.Pressed = pressed;
                    break;
                case InputEventMouseButton button:
                    button.Pressed = pressed;
                    button.Position = Center;
                    button.GlobalPosition = Center;
                    break;
                default:
                    throw new InvalidOperationException("no Godot event for " + ik);
            }

            if (pressed)
            {
                _held.Add(ik);
            }
            else
            {
                _held.Remove(ik);
            }

            Input.ParseInputEvent(ev);
        }

        public void ReleaseAll()
        {
            foreach (string ik in new List<string>(_held))
            {
                Send(ik, false);
            }
        }

        public string Detail(string text) => string.Create(CultureInfo.InvariantCulture, $"{text} (tick {Ticks})");

        public string Describe() => string.Create(CultureInfo.InvariantCulture,
            $"client={PC?.GetStateName()} weapon={Pawn?.WeaponLoaded} wstate={Rifle?.GetStateName()} rof={Rifle?.ROFMode} screen={RofScreen} clip={Clip}/{Rifle?.Ammo} gadget={Pawn?.SelectedGadget?.GetType().Name} " +
            $"laser={Laser?.bInUse}/{(Pawn as SPAWNDEFENSE)?.bLaserOn} torch={Torch?.bInUse}/{Torch?.LightType} beam={Lasers.LocalVisible} light={FlashLight.On} fov={PC?.FovAngle:F0} binoc={PC?.fBinocularVisionFOV:F0} " +
            $"used={(Pawn?.UsedObject is { } uo ? Id(uo) : "none")} mask={Pawn?.bGasMask} rend={PC?.RendMap} shots=[{Gaps()}] frag={(Frag is { } f ? $"{f.LaunchSpeed:F0}/{f.Bounces}/{f.Life:F2}/{f.Gone}/{f.Exploded}" : "none")} " +
            $"input=[{Session.Input.Get("bFire")}{Session.Input.Get("bAltFire")}] log={string.Join("|", Session.Log)}");
    }
}
