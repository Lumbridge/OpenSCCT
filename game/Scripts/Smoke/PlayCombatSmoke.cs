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
/// <c>--svm-smoke=play-combat</c>: weapons and gadgets through the player's real keys, as Ryan plays them: every press
/// is a Godot input event (<see cref="Input.ParseInputEvent"/>) for the key the side's layout binds, read by
/// <see cref="PlayInput"/>, applied to the client's controller and sent over loopback to the listen server. Four legs
/// (SVM_COMBAT_LEGS picks some, by name):
/// <list type="bullet">
/// <item>spy (the launchers' settings: Play Level rules, server thread, 3 bots): night vision (F1), draw the Shocker (E),
/// fire it (left mouse: the shot, the flash, the scorch and sparks), holster, select and fire a sticky cam (3, right
/// mouse), look and zoom through it (mouse, Page Up), gas (Shift), leave (right mouse), then quick-throw a smoke
/// grenade (1, left mouse while holstered).</item>
/// <item>merc (same settings): fire the rifle (clip, HUD, flash, mark), hold fire until a tracer flies, rate of fire (F),
/// reload (R), scope and zoom (B, Page Up), throw a frag (1, hold and release the right mouse).</item>
/// <item>melee (merc, same settings): the berserk spin (Shift standing still) draws the merc's own body from the head
/// camera, then the elbow whiff (Shift again within 3 s) plays the rifle butt strike on the view model.</item>
/// <item>bot (the server inline, so a spy bot can be held in front): aim with the mouse and fire until the bot falls;
/// the client sees its health drop, its pain clips and its death, the server counts the kill.</item>
/// <item>shock (inline too): the spy shocks a merc bot held in front; the server must freeze it.</item>
/// </list>
/// With a window and SVM_PLAY_SHOTS naming a folder, each step and each step's first muzzle flash is saved there.
/// </summary>
public sealed class PlayCombatSmoke : SmokeTest
{
    public const string TestName = "play-combat";

    /// <summary>SVM_COMBAT_TRACE=1 prints the input, weapon and state of every tick and each impact mark.</summary>
    private static readonly bool Trace = System.Environment.GetEnvironmentVariable("SVM_COMBAT_TRACE") == "1";

    private const int TeardownFrames = 4;

    private readonly List<string> _report = new();
    private readonly string[] _legs;
    private int _leg;
    private CombatLeg? _current;
    private int _teardown = -1;
    private SmokeResult? _final;

    public PlayCombatSmoke()
    {
        string? only = System.Environment.GetEnvironmentVariable("SVM_COMBAT_LEGS");
        _legs = string.IsNullOrWhiteSpace(only) ? ["spy", "merc", "bot", "shock"] : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            _current = _legs[_leg] switch
            {
                "merc" => MercLeg(context),
                "melee" => MeleeLeg(context),
                "bot" => BotLeg(context),
                "shock" => ShockBotLeg(context),
                _ => SpyLeg(context),
            };
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

    // ---- the spy ----------------------------------------------------------------------------------------------

    private static CombatLeg SpyLeg(SmokeContext context)
    {
        var leg = new CombatLeg(context, PlaySide.Spy, standoff: 260f);

        // The spy starts in the dark: night vision first, so the screenshots show what the shots do.
        leg.Add("night vision (F1)", 240, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = s.PC?.RendMap ?? 0;
            }

            s.Tap("F1", 2, 3);
        }, s => s.PC is { } pc && s.Remember is int before && pc.RendMap != before ? s.Detail($"vision {before} -> {pc.RendMap}") : null);
        leg.Add("draw the Shocker (tap E)", 240, s => s.Tap("E", 0, 4), s =>
            s.Pawn is { WeaponLoaded: 2 } && s.Aiming ? s.Detail($"drawn, state {s.PC!.GetStateName()}") : null);
        leg.Add("fire the Shocker (left mouse)", 180, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = (s.Energy, s.MuzzleCount, s.LastShot, s.Flashes("SMuzzleSticky"), s.Fx?.Marks ?? 0, s.Fx?.Sparks ?? 0);
            }

            if (s.Frame < 20)
            {
                s.FaceWall();
            }

            s.Tap("LeftMouse", 20, 3);
        }, s =>
        {
            // The energy refills 7 a whole second, so the shot shows in the pistol's shot time and the server's count;
            // the player sees the pistol's flash, then the shock's scorch and sparks on the wall.
            (float energy, int muzzle, float last, int flashes, int marks, int sparks) = ((float, int, float, int, int, int))s.Remember!;
            if (!(s.LastShot > last && s.MuzzleCount > muzzle))
            {
                return null;
            }

            if (s.Fx is { } fx && (s.Flashes("SMuzzleSticky") <= flashes || fx.Marks <= marks || fx.Sparks <= sparks))
            {
                return null;
            }

            return s.Detail($"shot at {s.LastShot:F2} s: energy {energy:F0} -> {s.Energy:F0}, muzzle count {muzzle} -> {s.MuzzleCount}, " +
                $"flash {s.Flashes("SMuzzleSticky")}, marks {s.Fx?.Marks}, sparks {s.Fx?.Sparks}, mark at {s.Fx?.LastMarkAt}");
        });
        leg.Add("holster (tap E)", 240, s => s.Tap("E", 10, 4), s =>
            s.Pawn is { WeaponLoaded: 0 } && s.Walking ? s.Detail("holstered") : null);
        leg.Add("select the sticky cam (3)", 120, s => s.Tap("3", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadStickyCam ? s.Detail("selected") : null);
        leg.Add("draw for the sticky cam (tap E)", 240, s =>
        {
            s.FaceWall();
            s.Tap("E", 0, 4);
        }, s => s.Pawn is { WeaponLoaded: 2 } && s.Aiming ? s.Detail("drawn") : null);
        leg.Add("fire the sticky cam (right mouse)", 600, s =>
        {
            s.FaceWall();
            s.Tap("RightMouse", 30, 4);
        }, s => s.PC is { } pc && GadgetStates.InStickyCamViewState(pc) ? s.Detail($"looking through the cam, {pc.GetStateName()}") : null);
        leg.Add("look through the cam (mouse)", 150, s =>
        {
            // The view settles on the cam first; its rotation then is the start.
            if (s.Frame == 0)
            {
                s.Remember = null;
            }
            else if (s.Ticks >= 6 && s.Remember is null)
            {
                s.Remember = s.PC?.Rotation ?? default;
            }

            // Moved until the view turns (right, then left in case the cam's yaw window stops it on that side): the
            // counts scale with the window size, and a headless run's is small. The step acts once a tick, and the
            // cam ignores a tick's turn under its dead zone (K.CamInputDeadZone), so a tick's move is a brisk one.
            if (s.Remember is not null)
            {
                s.MouseMove(s.Ticks < 50 ? 40f : -40f, 0f);
            }
        }, s => s.PC is { } pc && s.Remember is FRotator before && Math.Abs((short)(pc.Rotation.Yaw - before.Yaw)) > 200
            ? s.Detail($"cam yaw {before.Yaw} -> {pc.Rotation.Yaw}") : null);
        leg.Add("zoom the cam (Page Up)", 240, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = s.PC?.FovAngle ?? 0f;
            }

            s.Hold("PageUp", 2, 40);
        }, s => s.PC is { } pc && s.Remember is float fov && pc.FovAngle < fov - 2f ? s.Detail($"FOV {fov:F0} -> {pc.FovAngle:F0}") : null);
        leg.Add("gas (Shift)", 300, s => s.Tap("Shift", 10, 6), s =>
            s.ClientCount<SStickyCamEmitter>() > 0 ? s.Detail("gas cloud on the client") : null);
        leg.Add("leave the cam (right mouse)", 300, s => s.Tap("RightMouse", 20, 4), s =>
            s.PC is { } pc && !GadgetStates.InStickyCamView(pc) ? s.Detail($"back in {pc.GetStateName()}") : null);
        // Leaving the cam puts the Shocker away by itself; E is pressed only if it is still out (a press while
        // holstered would draw it again).
        leg.Add("holstered after the cam (E if still drawn)", 400, s =>
        {
            if (s.Frame == 40)
            {
                s.Remember = s.Pawn is { WeaponLoaded: 2 } && s.Aiming;
            }

            if (s.Remember is true)
            {
                s.Tap("E", 41, 4);
            }
        }, s => s.Pawn is { WeaponLoaded: 0 } && s.PC is { } pc && (pc.IsInState("s_PlayerWalkingCr0H") || pc.IsInState("s_PlayerWalkingSt0H")) && s.Ticks > 30
            ? s.Detail($"holstered in {s.PC!.GetStateName()}{(s.Remember is true ? " (pressed E)" : " (by the cam exit)")}") : null);
        leg.Add("select the smoke grenade (1)", 120, s => s.Tap("1", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadSmokeGrenade ? s.Detail("selected") : null);
        leg.Add("quick throw (left mouse, holstered)", 600, s =>
        {
            s.FaceAway();
            s.Tap("LeftMouse", 40, 3);
        }, s => s.ClientCount<SProjSmoke>() > 0 || s.ClientCount<SSmokeEmitter>() > 0
            ? s.Detail($"grenade {s.ClientCount<SProjSmoke>()}, cloud {s.ClientCount<SSmokeEmitter>()}, charges {s.Pawn?.SelectedGadget?.Charge}") : null);
        return leg;
    }

    // ---- the merc ---------------------------------------------------------------------------------------------

    private static CombatLeg MercLeg(SmokeContext context)
    {
        var leg = new CombatLeg(context, PlaySide.Merc, standoff: 300f);
        leg.Add("fire the rifle (left mouse)", 240, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = (s.Clip, s.MuzzleCount, s.Flashes("SpawnSFX"), s.Fx?.Marks ?? 0);
            }

            s.Tap("LeftMouse", 10, 3);
        }, s =>
        {
            // The server's count, the client's clip and the HUD's, the first-person flash and the bullet's mark.
            (int clip, int muzzle, int flashes, int marks) = ((int, int, int, int))s.Remember!;
            if (!(s.Clip < clip && s.MuzzleCount > muzzle) || (s.Hud is { } hud && hud.Snapshot.ClipAmmo != s.Clip))
            {
                return null;
            }

            if (s.Fx is { } fx && (s.Flashes("SpawnSFX") <= flashes || fx.Marks <= marks))
            {
                return null;
            }

            return s.Detail($"shot: clip {clip} -> {s.Clip} (HUD {s.Hud?.Snapshot.ClipAmmo.ToString(CultureInfo.InvariantCulture) ?? "none"}), muzzle count {muzzle} -> {s.MuzzleCount}, " +
                $"flash {s.Flashes("SpawnSFX")}, marks {s.Fx?.Marks}");
        });
        leg.Add("hold fire until a tracer flies (left mouse held)", 900, s => s.Hold("LeftMouse", 2, 10_000), s =>
            s.Fx is null || s.Fx.Tracers > 0 ? s.Detail($"tracers {s.Fx?.Tracers}, clip {s.Clip}, marks {s.Fx?.Marks}") : null);
        leg.Add("rate of fire (F)", 120, s =>
        {
            // The rifle takes the switch only when idle (not in the 0.2 s after the last shot of a burst).
            if (s.Remember is null && s.Rifle is { } idle && idle.GetStateName().ToString() == "s_Selected" && s.Frame >= 5)
            {
                s.Remember = idle.ROFMode;
                s.Send("F", true);
                s.PressedAt = s.Frame;
            }
            else if (s.PressedAt >= 0 && s.Frame == s.PressedAt + 3)
            {
                s.Send("F", false);
            }
        }, s => s.Rifle is { } r && s.Remember is SRateOfFireMode before && r.ROFMode != before ? s.Detail($"{before} -> {r.ROFMode}") : null);
        leg.Add("reload (R)", 300, s => s.Tap("R", 5, 3), s => s.Rifle is { } r && r.ClipAmmo == r.ClipMaxAmmo ? s.Detail($"clip {r.ClipAmmo}, ammo {r.Ammo}") : null);
        leg.Add("scope (B)", 240, s => s.Tap("B", 5, 3), s =>
            s.PC is { } pc && pc.GetStateName().ToString() == "s_Binoculars_Def" ? s.Detail($"scoped, FOV {pc.FovAngle:F0}") : null);
        leg.Add("scope zoom (Page Up)", 240, s => s.Tap("PageUp", 20, 3), s =>
            s.PC is { } pc && pc.fBinocularVisionFOV < 29f ? s.Detail($"zoom FOV {pc.fBinocularVisionFOV:F0}") : null);
        leg.Add("leave the scope (B)", 240, s => s.Tap("B", 10, 3), s =>
            s.PC is { } pc && pc.GetStateName().ToString() != "s_Binoculars_Def" && s.Walking ? s.Detail($"back in {pc.GetStateName()}") : null);
        leg.Add("select the frag grenade (1)", 120, s => s.Tap("1", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadFragGrenade ? s.Detail("selected") : null);

        // Last: the throw away from the wall, held for a longer throw.
        leg.Add("throw a frag (hold and release right mouse)", 900, s =>
        {
            s.FaceAway();
            s.Tap("RightMouse", 60, 50);
        }, s => s.ClientCount<SProjFrag>() > 0 ? s.Detail($"frag in the air, charges {s.Pawn?.SelectedGadget?.Charge}") : null);
        return leg;
    }

    // ---- the merc's melee -----------------------------------------------------------------------------------

    /// <summary>
    /// The merc's melee through the Use key (spec/combat/melee-elbow-charge-berserk.md rules 30 to 32): standing still,
    /// Shift spins him (s_Berserk360) with the camera on his head and his own body drawn; Shift again within the 3 s
    /// cooldown whiffs an elbow, the rifle butt clip FirstAtakCrFd2 on the view model's channel 0.
    /// </summary>
    private static CombatLeg MeleeLeg(SmokeContext context)
    {
        var leg = new CombatLeg(context, PlaySide.Merc, standoff: 300f);
        leg.Add("berserk spin (Shift)", 480, s =>
        {
            if (s.Frame == 0)
            {
                s.PressedAt = -1;
                s.Remember = -1;
            }

            // The settle walk can end in a spin of its own: press once the 3 s berserk and charge spacing is over.
            if (s.Remember is -1 && s.PC is { } ready && s.Walking &&
                ready.Level.TimeSeconds - ready.fLastBerserkTime > 3.2f && ready.Level.TimeSeconds - ready.fLastChargingTime > 3.2f)
            {
                s.Remember = s.Frame;
            }

            if (s.Remember is int at && at >= 0)
            {
                s.Tap("Shift", at, 3);
            }
        }, s =>
        {
            if (s.PressedAt < 0 && s.PC?.IsInState("s_Berserk360") == true)
            {
                s.PressedAt = s.Ticks;
            }

            if (Trace && s.Ticks % 10 == 0)
            {
                GD.Print($"SVM_SMOKE {TestName} melee tick {s.Ticks}: state {s.PC?.GetStateName()} spinAt {s.PressedAt}");
            }

            // Half a second into the spin: the head camera, the merc's own body drawn, no view model.
            if (s.PressedAt < 0 || s.Ticks - s.PressedAt < 30 || s.PC is not { } pc || s.Pawn is not { } pawn || !pc.IsInState("s_Berserk360"))
            {
                return null;
            }

            OpenScctGame.Views.SimPawnView? body = s.Root.Pawns?.ViewOf(pawn);
            bool drawn = body is { Visible: true };
            bool rifleHidden = s.Root.FirstPerson is not { Shown: true };
            float headGap = s.Root.Pawns is { } views && views.TryGetBone(pawn, new Name("B Head"), out Transform3D head)
                ? FVector.Dist(s.Session.View.Location, OpenScctGame.UeSpace.ToUe(head.Origin)) : float.NaN;
            Name clip = pawn.PawnAnim[0].Sequence;
            if (!(pc.bBehindView && pc.bFirstPersonWithPawn && drawn && rifleHidden && clip == new Name("AtakStAlTu") && headGap < 30f))
            {
                GD.Print($"SVM_SMOKE {TestName} melee spin not drawn yet: behind={pc.bBehindView} withPawn={pc.bFirstPersonWithPawn} bodyDrawn={drawn} rifleHidden={rifleHidden} clip={clip} headGap={headGap:F1}");
                return null;
            }

            return s.Detail($"spin: body drawn, rifle hidden, channel 0 {clip} frame {pawn.PawnAnim[0].Frame:F2}, camera {headGap:F1} uu from B Head");
        });
        leg.Add("elbow whiff (Shift within 3 s)", 300, s =>
        {
            // Once the spin is over, one press (still within the 3 s spacing, so no second spin).
            if (s.Frame == 0)
            {
                s.Remember = -1;
            }

            if (s.Remember is -1 && s.Walking && s.Rifle is { } r && !r.IsInState("s_ElbowShocking"))
            {
                s.Remember = s.Frame + 2;
            }

            if (s.Remember is int at && at >= 0)
            {
                s.Tap("Shift", at, 3);
            }
        }, s =>
        {
            if (s.Rifle is not { } rifle || rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL) != ViewModelClips.FirstAtakCrFd2)
            {
                return null;
            }

            if (s.Root.View is not { } view || view.DoubleVision.Factor <= 0f || view.DoubleVision.Draws == 0)
                return null;

            float frame = s.Root.FirstPerson?.ChannelFrame(SWeaponDefense.MVTCHANNEL) ?? -1f;
            return frame >= 0.35f ? s.Detail($"rifle butt FirstAtakCrFd2 drawn at frame {frame:F2}, channel 19 {s.Pawn?.PawnAnim[19].Sequence}") : null;
        });
        return leg;
    }

    // ---- a merc against a spy bot -----------------------------------------------------------------------------

    /// <summary>
    /// The merc shoots a spy bot through the keys: the listen server runs inline here so the test can hold the bot
    /// still in front of the merc (setup only; the bot keeps its controller and reacts as usual). The aim is mouse
    /// motion, the shots the held left button; the client must see the bot's health fall, its damage reactions and its
    /// death, and the server must count the kill.
    /// </summary>
    private static CombatLeg BotLeg(SmokeContext context)
    {
        var leg = new CombatLeg(context, PlaySide.Merc, standoff: 300f, PlayHostMode.Inline, botCount: 1, walkToWall: false);
        leg.Add("hold a spy bot in front (setup)", 900, s =>
        {
            s.ServerHook ??= PinBot(s, 450f);
            s.Pinning = true;
        }, s =>
        {
            if (s.Target is null && s.PinSpot is { } spot)
            {
                s.Target = NearestEnemy(s, spot);
                s.TargetStartHealth = s.Target?.Health ?? 0;
            }

            return s.Target is { } t && s.PinSpot is { } at && FVector.Dist(t.Location, at) < 80f && s.Ticks > 60
                ? s.Detail($"bot {t.PlayerReplicationInfo?.PlayerName} at {t.Location}, health {t.Health}") : null;
        });
        leg.Add("aim at the bot (mouse)", 600, s =>
        {
            if (s.Target is { } t)
            {
                s.AimAt(t.Location + new FVector(0f, 0f, 20f));
            }
        }, s => s.Target is { } t && s.AimAt(t.Location + new FVector(0f, 0f, 20f)) is var e && Math.Abs(e.Yaw) < 250 && Math.Abs(e.Pitch) < 250
            ? s.Detail($"aim error yaw {e.Yaw} pitch {e.Pitch}") : null);
        leg.Add("shoot the bot until it falls (left mouse held)", 1800, s =>
        {
            if (s.Target is { } t)
            {
                s.AimAt(t.Location + new FVector(0f, 0f, 20f));
                Watch(s, t);
            }

            s.Hold("LeftMouse", 2, 100_000);
        }, s =>
        {
            if (s.Target is not { } t)
            {
                return null;
            }

            Watch(s, t);
            int kills = s.Session.ServerSnapshot(h => MercKills(h.World.Level));
            bool dead = s.TargetLowestHealth <= 0 || t.bDeleteMe || t.bTearOff;
            if (!dead || kills < 1)
            {
                return null;
            }

            s.Pinning = false;
            return s.Detail($"health {s.TargetStartHealth} -> {s.TargetLowestHealth}, reactions [{string.Join(",", s.TargetAnims)}], kills {kills}, clip {s.Clip}, marks {s.Fx?.Marks}, tracers {s.Fx?.Tracers}");
        });
        return leg;
    }

    /// <summary>
    /// The spy shocks a merc bot through the keys (inline host, the bot held in front as in <see cref="BotLeg"/>): the
    /// Shocker is drawn with E, aimed with the mouse and fired with the left button; the server must shock the bot.
    /// </summary>
    private static CombatLeg ShockBotLeg(SmokeContext context)
    {
        var leg = new CombatLeg(context, PlaySide.Spy, standoff: 300f, PlayHostMode.Inline, botCount: 1, walkToWall: false);
        leg.Add("hold a merc bot in front (setup)", 900, s =>
        {
            s.ServerHook ??= PinBot(s, 400f, spy: false);
            s.Pinning = true;
        }, s =>
        {
            if (s.Target is null && s.PinSpot is { } spot)
            {
                s.Target = NearestEnemy(s, spot);
            }

            return s.Target is { } t && s.PinSpot is { } at && FVector.Dist(t.Location, at) < 80f && s.Ticks > 60
                ? s.Detail($"bot {t.PlayerReplicationInfo?.PlayerName} at {t.Location}") : null;
        });
        leg.Add("draw the Shocker (tap E)", 300, s => s.Tap("E", 0, 4), s => s.Pawn is { WeaponLoaded: 2 } && s.Aiming ? s.Detail("drawn") : null);
        leg.Add("aim at the bot (mouse)", 600, s =>
        {
            if (s.Target is { } t)
            {
                s.AimAt(t.Location + new FVector(0f, 0f, 30f));
            }
        }, s => s.Target is { } t && s.AimAt(t.Location + new FVector(0f, 0f, 30f)) is var e && Math.Abs(e.Yaw) < 200 && Math.Abs(e.Pitch) < 200
            ? s.Detail($"aim error yaw {e.Yaw} pitch {e.Pitch}") : null);
        leg.Add("shock the bot (left mouse)", 600, s =>
        {
            if (s.Target is { } t)
            {
                s.AimAt(t.Location + new FVector(0f, 0f, 30f));
                Watch(s, t);
            }

            if (s.Frame % 40 == 5)
            {
                s.Send("LeftMouse", true);
            }
            else if (s.Frame % 40 == 8)
            {
                s.Send("LeftMouse", false);
            }
        }, s =>
        {
            if (s.Target is not { } t)
            {
                return null;
            }

            Watch(s, t);
            bool shocked = s.Session.ServerSnapshot(h =>
            {
                foreach (SBasePawn p in h.World.Level.AllActors<SBasePawn>())
                {
                    if (p is SPAWNDEFENSE { bStickyShocking: true })
                    {
                        return true;
                    }
                }

                return false;
            });
            if (!shocked)
            {
                return null;
            }

            s.Pinning = false;
            return s.Detail($"the server shocked the bot, client anims [{string.Join(",", s.TargetAnims)}], energy {s.Energy:F0}, marks {s.Fx?.Marks}, sparks {s.Fx?.Sparks}");
        });
        return leg;
    }

    /// <summary>The kills of the mercs' players on a level.</summary>
    private static int MercKills(Level level)
    {
        int kills = 0;
        foreach (SPlayerController pc in level.AllActors<SPlayerController>())
        {
            if (pc.Pawn is SPAWNDEFENSE || pc.PlayerReplicationInfo?.Team is STeamDefenseur)
            {
                kills += pc.PlayerReplicationInfo?.Kills ?? 0;
            }
        }

        return kills;
    }

    /// <summary>Notes the target's lowest health and the clips it played on its action channels.</summary>
    private static void Watch(KeyState s, SBasePawn t)
    {
        if (Trace && s.Fx is { } fx && fx.Marks != s.MarksSeen)
        {
            s.MarksSeen = fx.Marks;
            GD.Print($"SVM_SMOKE {TestName} mark {fx.Marks} at {fx.LastMarkAt}, target {t.Location} r={t.CollisionRadius} h={t.CollisionHeight}, view {s.Session.View.Location} {s.Session.View.Rotation}");
        }

        s.TargetLowestHealth = Math.Min(s.TargetLowestHealth, t.Health);
        for (int c = 0; c < OpenScct.Game.Movement.PawnAnimation.ChannelCount; c++)
        {
            if (t.PawnAnim[c].Sequence is { IsNone: false } seq)
            {
                s.TargetAnims.Add(c.ToString(CultureInfo.InvariantCulture) + ":" + seq.ToString());
            }
        }
    }

    /// <summary>The client pawn of the other side nearest to <paramref name="spot"/>.</summary>
    private static SBasePawn? NearestEnemy(KeyState s, FVector spot)
    {
        SBasePawn? best = null;
        float bestD = float.MaxValue;
        foreach (SBasePawn p in s.Session.Client.World.Level.AllActors<SBasePawn>())
        {
            if (p.bDeleteMe || ReferenceEquals(p, s.Pawn) || (p is SPawnAttaque) == (s.Pawn is SPawnAttaque))
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

    /// <summary>
    /// The server hook that holds the first spy bot <paramref name="distance"/> uu from the merc, in the first of eight
    /// directions with a clear line from the merc's eyes, facing away; it lets go when <see cref="KeyState.Pinning"/>
    /// is off or the bot dies.
    /// </summary>
    private static Action<PlaySession> PinBot(KeyState state, float distance, bool spy = true) => session =>
    {
        if (!state.Pinning || session.Bots is not { } bots || session.ServerController?.Pawn is not SBasePawn merc)
        {
            // Let go of a held merc bot's brain with the pin.
            foreach (OpenScct.Game.AI.AiController b in session.Bots?.Bots ?? [])
            {
                b.Held = false;
            }

            return;
        }

        SBasePawn? bot = bots.Bots.Select(b => b.Pawn).FirstOrDefault(p => p is { bDeleteMe: false, Health: > 0 } && (p is SPawnAttaque) == spy);
        if (bot is null)
        {
            return;
        }

        // A held merc bot does not shoot the player before the shock can land: the Shocker's immunity covers the first
        // 5 s of the level (RealTimeInvulSticky), and whether the bot killed the spy first depended on the match's timing.
        if (!spy)
        {
            foreach (OpenScct.Game.AI.AiController b in bots.Bots)
            {
                b.Held = ReferenceEquals(b.Pawn, bot);
            }
        }

        if (state.PinSpot is not { } spot)
        {
            FVector eye = merc.Location + new FVector(0f, 0f, merc.EyeHeight);
            // The first clear direction (16 around, nearest the view first) at the distance, else at shorter ones.
            for (float d = distance; d >= 200f && state.PinSpot is null; d -= 100f)
            {
                for (int k = 0; k < 16 && state.PinSpot is null; k++)
                {
                    int step = (k + 1) / 2 * ((k & 1) == 0 ? 1 : -1);
                    FVector dir = new FRotator(0, merc.Rotation.Yaw + (step * 4096), 0).Vector();
                    FVector at = merc.Location + (dir * d);
                    FVector far = merc.Location + (dir * (d + 120f));
                    if (!GadgetWorld.Line(merc, far + new FVector(0f, 0f, merc.EyeHeight), eye, ETraceFlags.TRACE_World, out _) &&
                        !GadgetWorld.Line(merc, far, merc.Location, ETraceFlags.TRACE_World, out _))
                    {
                        state.PinSpot = at;
                    }
                }
            }

            if (state.PinSpot is not { } found)
            {
                return;
            }

            spot = found;
        }

        // Feet on the floor under the spot (the two pawns' collision heights differ: a crouched spy, a standing merc).
        float floor = merc.Location.Z - merc.CollisionHeight;
        if (GadgetWorld.Line(merc, new FVector(spot.X, spot.Y, floor - 300f), new FVector(spot.X, spot.Y, floor + 100f), ETraceFlags.TRACE_World, out FCheckResult ground))
        {
            floor = ground.Location.Z;
        }

        spot.Z = floor + bot.CollisionHeight + 1f;

        // Moved back only when it has wandered, so in between it stands on the floor (walking, not falling).
        float dx = bot.Location.X - spot.X;
        float dy = bot.Location.Y - spot.Y;
        if ((dx * dx) + (dy * dy) > 30f * 30f)
        {
            bot.SetLocation(spot);
            bot.Velocity = default;
        }

        bot.Velocity = new FVector(0f, 0f, bot.Velocity.Z);
        bot.Acceleration = default;
        FVector away = spot - merc.Location;
        bot.Rotation = new FRotator(0, away.Rotation().Yaw, 0);
    };

    /// <summary>
    /// One play of SteeE on one side: the scripted pilot walks the pawn to a wall (setup only), then the steps press
    /// the bound keys as Godot input events.
    /// </summary>
    private sealed class CombatLeg : IDisposable
    {
        private readonly List<(string Name, int TimeoutTicks, Action<KeyState> Act, Func<KeyState, string?> Done)> _steps = new();
        private readonly List<string> _results = new();
        private readonly float _standoff;
        private readonly PlaySide _side;
        private PlayRoot? _root;
        private ScriptedPilot? _pilot;
        private KeyState? _state;
        private (FVector Spot, FVector Face)? _wall;
        private int _index;
        private bool _hooked;
        private long _stepStartTick;
        private long _lastActTick = -1;
        private int _shots;
        private int _logAfter;
        private int _flashShotStep = -1;
        private int _flashSeenFrames;
        private int _lastLoaded = -1;
        private Name _lastState;

        public CombatLeg(SmokeContext context, PlaySide side, float standoff, PlayHostMode host = PlayHostMode.Thread, int? botCount = null,
            bool walkToWall = true)
        {
            _side = side;
            _standoff = standoff;
            _pilot = walkToWall ? new ScriptedPilot(Wall) { UseHoldTicks = 1 } : null;
            int bots = botCount ?? (int.TryParse(System.Environment.GetEnvironmentVariable("SVM_COMBAT_BOTS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ? b : 3);
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = "SteeE",
                    Side = side,
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
                // The pilot's last turn toward the wall can start the merc's fast turn (s_Berserk360), which takes no
                // weapon keys: every leg starts once the pawn walks again.
                _steps.Add(("settle", 600, _ => { }, s => s.Walking && s.Ticks > 30 ? s.Detail($"in {s.PC!.GetStateName()}") : null));
            }

            _steps.Add((name, timeoutTicks, act, done));
        }

        private string Current => _index < _steps.Count ? _steps[_index].Name : "end";

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

            if (session.LoginError is { } login)
            {
                return SmokeResult.Fail("login refused: " + login);
            }

            if (!_hooked)
            {
                // The pilot only walks the pawn to its wall; after that every input comes from the key events.
                Action<PlaySession, long>? before = session.BeforeClientTick;
                Action<PlaySession, long>? after = session.AfterClientTick;
                session.AfterClientTick = (s, t) =>
                {
                    after?.Invoke(s, t);
                    if (Trace && _logAfter > 0)
                    {
                        _logAfter--;
                        GD.Print($"SVM_SMOKE {TestName} after tick {t}: {Weapon(s)}");
                    }
                };
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

                    bool piloting = _pilot is { Phase: not PilotPhase.Done and not PilotPhase.Failed };
                    if (Trace && s.LocalPawn is { } lp && (lp.WeaponLoaded != _lastLoaded || s.LocalController?.GetStateName() != _lastState))
                    {
                        _lastLoaded = lp.WeaponLoaded;
                        _lastState = s.LocalController?.GetStateName() ?? default;
                        GD.Print($"SVM_SMOKE {TestName} tick {t}: weapon {lp.WeaponLoaded} state {_lastState} fireMode={s.LocalController?.bFireMode} force={s.LocalController?.bForceFireMode} date={s.LocalController?.fDateFireMode:F2} lache={s.LocalController?.bTestBoutonLache}");
                    }

                    if (Trace && !piloting && (s.Input.Commands.Count > 0 || s.Input.Get("bFire") != 0 || s.Input.Get("bAltFire") != 0 || s.Input.Get("bROF") != 0 || s.Input.Get("bReload") != 0))
                    {
                        _logAfter = 2;
                        GD.Print($"SVM_SMOKE {TestName} input tick {t}: commands=[{string.Join(",", s.Input.Commands)}] fire={s.Input.Get("bFire")} alt={s.Input.Get("bAltFire")} rof={s.Input.Get("bROF")} reload={s.Input.Get("bReload")} change={s.LocalController?.bChangeROF} mode={(s.LocalPawn?.MyWeapon as SWeaponDefense)?.ROFMode} state={s.LocalController?.GetStateName()} {Weapon(s)}");
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
                // The walk is only setup (a bot in the way can block it): after a minute the steps start from where
                // the pawn is, still facing the wall it walked toward.
                if (session.TicksRun <= 60 * 60)
                {
                    return null;
                }

                GD.Print($"SVM_SMOKE {TestName} {_side}: the walk to the wall stopped at {session.LocalPawn?.Location} ({_pilot.Phase}); the steps start here");
                _pilot = null;
                session.Input.Clear();
            }

            _state ??= new KeyState(session, controls, _root, _wall);
            if (_state.Frame == 0 && _index == 0 && _stepStartTick == 0)
            {
                _stepStartTick = session.TicksRun;
            }

            (string name, int timeout, Action<KeyState> act, Func<KeyState, string?> done) = _steps[_index];
            _state.Ticks = (int)(session.TicksRun - _stepStartTick);
            // The first frame of a step only acts (a step may note the state it starts from).
            string? detail = _state.Frame == 0 ? null : done(_state);
            if (detail is not null)
            {
                _results.Add($"{name}: {detail}");
                GD.Print($"SVM_SMOKE {TestName} {_side} ok {name}: {detail}");
                Shot(context, name);
                _state.ReleaseAll();
                _index++;
                _state.Frame = 0;
                _state.Remember = null;
                _state.PressedAt = -1;
                _stepStartTick = session.TicksRun;
                if (_index >= _steps.Count)
                {
                    return SmokeResult.Pass($"{_side}: " + string.Join("; ", _results));
                }

                return null;
            }

            if (_state.Ticks > timeout)
            {
                Shot(context, name + " FAILED");
                return SmokeResult.Fail($"step '{name}' had no result after {timeout} ticks ({_state.Describe()}) | done: {string.Join("; ", _results)}");
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

            // One look at the first muzzle flash of each step, while it is lit.
            // A look at the first muzzle flash of each step: the frames just after it is lit.
            if (_root.Fx is { LiveFlashes: > 0 } && _flashShotStep != _index)
            {
                _flashSeenFrames++;
                if (_flashSeenFrames is 2 or 4)
                {
                    Shot(context, name + " flash " + _flashSeenFrames.ToString(CultureInfo.InvariantCulture));
                }

                if (_flashSeenFrames >= 4)
                {
                    _flashShotStep = _index;
                    _flashSeenFrames = 0;
                }
            }

            return null;
        }

        private static string Weapon(PlaySession s)
        {
            SBasePawn? p = s.LocalPawn;
            SWeapon? w = p?.MyWeapon;
            return string.Create(CultureInfo.InvariantCulture,
                $"weapon={w?.GetType().Name}#{w?.ActorId} inst={w?.Instigator?.GetType().Name} owner={w?.Owner?.GetType().Name} ctrl={w?.Controller?.GetType().Name} wl={p?.WeaponLoaded} transit={p?.bWeaponTransit} shock={p?.bStickyShocking} inv={s.LocalController?.bIsInInventoryMode} pad={s.LocalController?.iDontUpdatePadInput} energy={(p as SPawnAttaque)?.EnergyLevel:F1} muzzle={(w as SWeaponAttaque)?.MuzzleCount} last={(w as SWeaponAttaque)?.LastShot:F2} now={p?.Level.TimeSeconds:F2} wstate={w?.GetStateName()}");
        }

        /// <summary>With a window and SVM_PLAY_SHOTS naming a folder (keep it under local/), saves the window after a step.</summary>
        private void Shot(SmokeContext context, string step)
        {
            string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
            if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
            {
                return;
            }

            System.IO.Directory.CreateDirectory(folder);
            Image image = _root.GetViewport().GetTexture().GetImage();
            string safe = string.Concat(step.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
            image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_side}-{_shots++:D2}-{safe}.png")));
        }

        /// <summary>The nearest wall in 16 directions, the spot in front of it and the point to face (found once).</summary>
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

            float best = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                FVector dir = new FRotator(0, k * 4096, 0).Vector();
                FVector start = pawn.Location;
                if (!GadgetWorld.Line(pawn, start + dir * 1500f, start, ETraceFlags.TRACE_World, out FCheckResult hit) || MathF.Abs(hit.Normal.Z) > 0.3f)
                {
                    continue;
                }

                FVector eye = start + new FVector(0f, 0f, pawn.EyeHeight);
                if (!GadgetWorld.Line(pawn, eye + dir * 1500f, eye, SGadMultiMinesLauncher.PlaceTraceFlags, out FCheckResult high) ||
                    MathF.Abs(high.Normal.Z) > 0.3f || MathF.Abs(FVector.Dist(high.Location, eye) - FVector.Dist(hit.Location, start)) > 8f)
                {
                    continue;
                }

                float d = FVector.Dist(hit.Location, start);
                if (d > _standoff + 60f && d < best)
                {
                    best = d;
                    FVector spot = hit.Location - dir * _standoff;
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

    /// <summary>What a step sees, and the keys it presses as Godot input events.</summary>
    private sealed class KeyState
    {
        private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
        private readonly PlayRoot _root;
        private readonly (FVector Spot, FVector Face)? _wall;

        public KeyState(PlaySession session, PlayInput controls, PlayRoot root, (FVector Spot, FVector Face)? wall)
        {
            Session = session;
            Controls = controls;
            _root = root;
            _wall = wall;
        }

        public PlaySession Session { get; }

        public PlayInput Controls { get; }

        /// <summary>The play scene (its pawn views, view model and camera).</summary>
        public PlayRoot Root => _root;

        /// <summary>Frames since the step started.</summary>
        public int Frame { get; set; }

        /// <summary>Client ticks since the step started.</summary>
        public int Ticks { get; set; }

        public object? Remember { get; set; }

        /// <summary>Run on the client's thread before each client tick when the host is inline (test setup on the server).</summary>
        public Action<PlaySession>? ServerHook { get; set; }

        /// <summary>The bot pawn the bot leg shoots, on the client.</summary>
        public SBasePawn? Target { get; set; }

        /// <summary>The target's health when it was found.</summary>
        public int TargetStartHealth { get; set; }

        /// <summary>The lowest health the client saw on the target, and the damage reactions it played.</summary>
        public int TargetLowestHealth { get; set; } = int.MaxValue;

        public HashSet<string> TargetAnims { get; } = new(StringComparer.Ordinal);

        /// <summary>Where the server holds the bot (set once), and the bot's server pawn.</summary>
        public FVector? PinSpot { get; set; }

        public bool Pinning { get; set; }

        public int MarksSeen { get; set; }

        /// <summary>The frame a step pressed its key, or -1.</summary>
        public int PressedAt { get; set; } = -1;

        /// <summary>Turns the view toward <paramref name="point"/> with mouse motion; returns the yaw and pitch error left.</summary>
        public (int Yaw, int Pitch) AimAt(FVector point)
        {
            if (PC is not { } pc || Pawn is not { } pawn)
            {
                return (int.MaxValue, int.MaxValue);
            }

            // The merc aims from his eyes; the spy puts his behind camera's centre on the point.
            FVector eye = pawn is SPawnAttaque && Session.View.Valid ? Session.View.Location : pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
            FRotator want = (point - eye).Rotation();
            // A merc aims with his desired rotation, a spy with his camera.
            FRotator have = pawn is SPawnAttaque ? pc.Rotation : pc.MyDesiredRotation;
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

        public SPlayerController? PC => Session.LocalController;

        public OpenScctGame.Fx.CombatFxView? Fx => _root.Fx;

        public OpenScctGame.Hud.HudLayer? Hud => _root.Hud;

        /// <summary>Muzzle flashes of one kind the view drew.</summary>
        public int Flashes(string kind) => _root.Fx?.FlashesByKind.GetValueOrDefault(kind) ?? 0;

        public SBasePawn? Pawn => Session.LocalPawn;

        public float Energy => Pawn is SPawnAttaque spy ? spy.EnergyLevel : -1f;

        /// <summary>The spy pistol's last shot time on the client, or -1.</summary>
        public float LastShot => Pawn?.MyWeapon is SWeaponAttaque pistol ? pistol.LastShot : -1f;

        public SWeaponDefense? Rifle => Pawn?.MyWeapon as SWeaponDefense;

        public int Clip => Rifle?.ClipAmmo ?? -1;

        /// <summary>The replicated shot count: the spy pistol's, or the merc's third-person gun's.</summary>
        public int MuzzleCount => Pawn?.MyWeapon switch
        {
            SWeaponAttaque pistol => pistol.MuzzleCount,
            SWeaponDefense when Pawn is SPAWNDEFENSE { WeaponDummy: { } dummy } => dummy.MuzzleCount,
            _ => -1,
        };

        /// <summary>The spy is in the armed walk (the draw transition is over).</summary>
        public bool Aiming => PC is { } pc && (pc.IsInState("s_PlayerWalkingSt2H") || pc.IsInState("s_PlayerWalkingCr2H"));

        public bool Walking => PC?.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true;

        public int ClientCount<T>()
            where T : Actor
        {
            int n = 0;
            foreach (T a in Session.Client.World.Level.AllActors<T>())
            {
                if (!a.bDeleteMe)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Presses <paramref name="ik"/> on frame <paramref name="at"/> of the step and releases it <paramref name="frames"/> frames later.</summary>
        public void Tap(string ik, int at, int frames)
        {
            if (Frame == at)
            {
                Send(ik, true);
            }
            else if (Frame == at + frames)
            {
                Send(ik, false);
            }
        }

        /// <summary>Holds <paramref name="ik"/> from frame <paramref name="from"/> for <paramref name="frames"/> frames.</summary>
        public void Hold(string ik, int from, int frames) => Tap(ik, from, frames);

        /// <summary>Mouse motion in pixels (right and down positive, as the screen).</summary>
        public void MouseMove(float dx, float dy)
        {
            // The look scales the counts by the window size (MouseLook); a headless window is tiny, so the motion is
            // scaled up to what it is in a 1280 pixel window.
            float width = _root.GetViewport().GetVisibleRect().Size.X;
            if (width > 1f && width < 640f)
            {
                dx *= 1280f / width;
                dy *= 1280f / width;
            }

            var ev = new InputEventMouseMotion { Relative = new Vector2(dx, dy), ScreenRelative = new Vector2(dx, dy), Position = Center };
            Input.ParseInputEvent(ev);

            // Motion is accumulated until the display flushes it, which a headless display never does.
            Input.FlushBufferedEvents();
        }

        /// <summary>Turns the view toward the wall the pilot walked to, with mouse motion (a few pixels a frame).</summary>
        public void FaceWall()
        {
            if (PC is not { } pc || Pawn is not { } pawn || _wall is not { } w)
            {
                return;
            }

            FVector d = w.Face - pawn.Location;
            int want = new FVector(d.X, d.Y, 0f).Rotation().Yaw;
            int err = (short)(want - pc.Rotation.Yaw);
            int pitchErr = (short)(0 - pc.Rotation.Pitch);
            if (Math.Abs(err) > 150 || Math.Abs(pitchErr) > 600)
            {
                MouseMove(Math.Clamp(err / 60f, -40f, 40f), Math.Clamp(-pitchErr / 60f, -20f, 20f));
            }
        }

        /// <summary>Turns the view away from the wall the pilot walked to, with mouse motion.</summary>
        public void FaceAway()
        {
            if (PC is not { } pc || Pawn is not { } pawn || _wall is not { } w)
            {
                return;
            }

            FVector d = pawn.Location - w.Face;
            int want = new FVector(d.X, d.Y, 0f).Rotation().Yaw;
            int err = (short)(want - pc.Rotation.Yaw);
            int pitchErr = (short)(0 - pc.Rotation.Pitch);
            if (Math.Abs(err) > 150 || Math.Abs(pitchErr) > 600)
            {
                MouseMove(Math.Clamp(err / 60f, -40f, 40f), Math.Clamp(-pitchErr / 60f, -20f, 20f));
            }
        }

        private Vector2 Center => _root.GetViewport().GetVisibleRect().Size * 0.5f;

        /// <summary>One key or mouse button event for the IK name, through Godot's input as the window would send it.</summary>
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
            $"client={PC?.GetStateName()} pawn={Pawn?.GetType().Name} weapon={Pawn?.WeaponLoaded} transit={Pawn?.bWeaponTransit} gadget={Pawn?.SelectedGadget?.GetType().Name} energy={Energy:F0} clip={Clip} muzzle={MuzzleCount} " +
            $"bFire={PC?.bFire} bAltFire={PC?.bAltFire} bFireMode={PC?.bFireMode} bUsing={PC?.bUsing} canFire={PC?.CheckIfCanFire()} fov={PC?.FovAngle:F0} rot={PC?.Rotation} desired={PC?.MyDesiredRotation} target={Target?.Location} pawnAt={Pawn?.Location} view={Session.View.Location}/{Session.View.Rotation} lastMark={_root.Fx?.LastMarkAt} muzzleEst={(Pawn?.MyWeapon is { } mw ? OpenScct.Game.Combat.CombatNatives.EstimatedBoneCoords(mw).Origin + "->" + OpenScct.Game.Combat.CombatNatives.EstimatedBoneCoords(mw).XAxis : "")} cam={(PC is { } cpc && OpenScct.Game.Camera.PlayerCamera.Of(cpc) is { } pcam ? pcam.m_PrevPosition.ToString() : "none")} behind={PC?.bBehindView} conv={(PC is { } q && Pawn is { } qp ? OpenScct.Game.Combat.CombatNatives.AimedAtCameraTarget(q, qp, qp.Location) + "|" + (qp.MyWeapon is { } qw ? OpenScct.Game.Combat.CombatNatives.AimedAtCameraTarget(q, qp, qp.Location + new FVector(0f, 0f, qp.EyeHeight)) + "|eye=" + qp.EyeHeight + "|inst=" + qw.Instigator?.GetType().Name : "") : "-")} ctl={Pawn?.Controller?.GetType().Name} " +
            $"input=[{Session.Input.Get("bFire")}{Session.Input.Get("bAltFire")}{Session.Input.Get("bFireMode")}{Session.Input.Get("bUsing")}] log={string.Join("|", Session.Log)} hovered={_root.GetViewport().GuiGetHoveredControl()?.GetPath()} vp={Session.ViewportSize} aMouseX={PC?.aMouseX} aTurn={PC?.aTurn} turn={PC?.fTurnValue} captured={Controls.IsCaptured}");
    }
}
