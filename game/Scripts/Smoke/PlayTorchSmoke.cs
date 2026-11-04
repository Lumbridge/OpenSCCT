using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>The first-person Merc torch follows the drawn camera near a wall and while changing stance.</summary>
public sealed class PlayTorchSmoke : SmokeTest
{
    public const string TestName = "play-torch";

    private sealed record Case(float Distance, bool Crouched);
    private static readonly Case[] Cases = [new(80f, false), new(80f, true), new(600f, true), new(600f, false)];
    private readonly List<string> _checks = [];
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private int _case;
    private int _phase;
    private double _phaseAt;
    private int _samples;
    private float _largestGap;
    private SmokeResult _final;
    private int _teardown = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown >= 0) return ++_teardown < 6 ? SmokeResult.Running : _final;
        if (_root is null)
        {
            _root = TestRange.Start(context, PlaySide.Merc, bots: 0);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || _root.Session is not { } session || !TestRange.Walking(session.LocalController))
            return context.ElapsedSeconds > 90 ? Finish(SmokeResult.Fail("Merc did not spawn")) : SmokeResult.Running;
        if (session.ServerFailure is { } failure) return Finish(SmokeResult.Fail(failure.Message));
        _keys ??= KeyPilot.Attach(_root);
        _keys.Update();
        if (!_keys.Ready || session.LocalController is not { } pc || session.LocalPawn is not SPAWNDEFENSE pawn)
            return SmokeResult.Running;

        double elapsed = context.ElapsedSeconds - _phaseAt;
        switch (_phase)
        {
            case 0:
                Case current = Cases[_case];
                TestRange.Station lane = TestRange.Stations()["Lane"];
                TestRange.Place(session, _ => session.ServerController,
                    new FVector(3584f - current.Distance, lane.Spot.Y, 0f), 0);
                Next(context, 1);
                break;
            case 1:
                if (elapsed < 1.0) break;
                TestRange.Face(pc, 0);
                if (pawn.bIsCrouched != Cases[_case].Crouched) _keys.Tap("bCrouch");
                if (pawn.Torche is not { bInUse: true }) _keys.Tap("GadTorcheSwitch");
                Next(context, 2);
                break;
            case 2:
                if (elapsed < 1.0) break;
                if (pawn.bIsCrouched != Cases[_case].Crouched)
                    return Finish(SmokeResult.Fail("Crouch key did not reach the requested stance"));
                if (pawn.Torche is not { bInUse: true } || _root.FlashLight is not { On: true })
                    return Finish(SmokeResult.Fail("Torch key did not illuminate the scene"));
                _samples = 0;
                _largestGap = 0;
                Next(context, 3);
                break;
            case 3:
                if (_root.FlashLight is not { On: true } light || _root.Camera is not { } camera)
                    return Finish(SmokeResult.Fail("Torch or camera disappeared"));
                var pose = camera.Current;
                float gap = FVector.Dist(light.Placement.Location, pose.Location);
                _largestGap = MathF.Max(_largestGap, gap);
                int pitch = Math.Abs(FRotator.NormalizeAxis(light.Placement.Rotation.Pitch - pose.Rotation.Pitch));
                int yaw = Math.Abs(FRotator.NormalizeAxis(light.Placement.Rotation.Yaw - pose.Rotation.Yaw));
                if (light.FromBone || gap > 0.01f || pitch > 1 || yaw > 1)
                    return Finish(SmokeResult.Fail($"Case {_case}: light-camera gap={gap:F3}, pitch={pitch}, yaw={yaw}, bone={light.FromBone}"));
                _samples++;
                _keys.Look(0.5f, -0.1f);
                if (elapsed < 0.5 || _samples < 10) break;
                _checks.Add($"{Cases[_case].Distance:F0}uu {(pawn.bIsCrouched ? "crouched" : "standing")}: {_samples} frames, gap {_largestGap:F3}uu");
                TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"case-{_case}");
                if (++_case < Cases.Length) Next(context, 0);
                else
                {
                    pc.GameReplicationInfo!.bAvlb_CinematicCamera = true;
                    pc.GameReplicationInfo.bAlternateMode = true;
                    pc.ToggleFreeCamera();
                    pc.FreeCamLocation += new FVector(0f, 200f, 0f);
                    Next(context, 4);
                }
                break;
            case 4:
                if (elapsed < 0.5) break;
                if (pc.FreeCameraFlying == 0 || _root.FlashLight is not { On: true, FromBone: true } worldLight ||
                    _root.Camera is not { } freeCamera || FVector.Dist(worldLight.Placement.Location, freeCamera.Current.Location) < 100f)
                    return Finish(SmokeResult.Fail($"Free camera did not retain world placement: flying={pc.FreeCameraFlying}, " +
                        $"bone={_root.FlashLight?.FromBone}, on={_root.FlashLight?.On}"));
                _checks.Add("free camera retains pawn bone placement");
                return Finish(SmokeResult.Pass(string.Join("; ", _checks)));
        }

        return SmokeResult.Running;
    }

    private void Next(SmokeContext context, int phase)
    {
        _phase = phase;
        _phaseAt = context.ElapsedSeconds;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _keys?.ReleaseAll();
        _root?.QueueFree();
        _final = result;
        _teardown = 0;
        return SmokeResult.Running;
    }
}
