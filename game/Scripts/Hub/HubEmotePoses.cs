using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Server.Hub;
using OpenScctGame.Views;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub's emotes drawn as poses (an OpenSCCT addition, spec/additions/hub.md): the character packages hold no emote
/// clips, so each emote is a small program of bone turns laid over the pawn's own animation (<see cref="IPawnPoseOverlay"/>),
/// blended in and out. Turns are about the pawn's frame: F forward, R right, U up (right-handed in Godot space, so
/// a negative turn about F raises the right arm out to the side, a positive one the left; a positive turn about R
/// raises an arm forward; a positive turn about U swings the right arm in toward the chest). The bone names are the
/// same in the spy's and the merc's skeletons. These are our own numbers, tuned by eye; they carry no provenance.
/// </summary>
public sealed class HubEmotePoses : IPawnPoseOverlay
{
    private const float BlendSeconds = 0.25f;

    private static readonly Vector3 F = new(1f, 0f, 0f);
    private static readonly Vector3 R = new(0f, 1f, 0f);
    private static readonly Vector3 U = new(0f, 0f, 1f);

    private readonly Func<HubPlayState?> _play;

    public HubEmotePoses(Func<HubPlayState?> play) => _play = play;

    public float Collect(SBasePawn pawn, List<PoseTurn> turns)
    {
        if (_play() is not { } play || pawn.PlayerReplicationInfo is not { } pri ||
            !play.EmoteState(pri.PlayerID, out string emote, out double since, out double left))
        {
            return 0f;
        }

        // Blend in over the first quarter second, out over the quarter second after it ends.
        float w = (float)Math.Clamp(Math.Min(since / BlendSeconds, 1.0 + (left / BlendSeconds)), 0.0, 1.0);
        if (w <= 0f)
        {
            return 0f;
        }

        float t = (float)since;
        var pose = new Pose(turns, w);
        switch (emote)
        {
            case "wave":
                pose.Turn("B R UpperArm", F, -150f);
                pose.Turn("B R ForeArm", F, 25f * MathF.Sin(t * 9f));
                pose.Turn("B Head", U, 8f);
                break;
            case "salute":
                pose.Turn("B R UpperArm", F, -95f);
                pose.Turn("B R UpperArm", R, 25f);
                pose.Turn("B R ForeArm", F, 125f);
                pose.Turn("B Spine1", R, -4f);
                break;
            case "cheer":
            {
                float pump = 12f * MathF.Max(0f, MathF.Sin(t * 7f));
                pose.Turn("B L UpperArm", F, 160f - pump);
                pose.Turn("B R UpperArm", F, -160f + pump);
                pose.Turn("B Head", R, -12f);
                pose.Turn("B Spine1", R, -6f);
                break;
            }

            case "dance":
            {
                float s = MathF.Sin(t * 6f);
                float c = MathF.Cos(t * 6f);
                pose.Turn("B Pelvis", U, 14f * s);
                pose.Turn("B Spine", F, 8f * c);
                pose.Turn("B Spine1", U, -10f * s);
                pose.Turn("B L UpperArm", F, 70f + (25f * c));
                pose.Turn("B R UpperArm", F, -70f + (25f * c));
                pose.Turn("B L ForeArm", F, 50f);
                pose.Turn("B R ForeArm", F, -50f);
                pose.Turn("B Head", F, 10f * s);
                break;
            }

            case "sit":
                // On the floor, legs out in front, leaning back on the hands.
                pose.Turn("B Pelvis", R, -10f);
                pose.Turn("B L Thigh", R, 80f);
                pose.Turn("B R Thigh", R, 80f);
                pose.Turn("B L Calf", R, -15f);
                pose.Turn("B R Calf", R, -15f);
                pose.Turn("B Spine1", R, -10f);
                pose.Turn("B L UpperArm", R, -35f);
                pose.Turn("B R UpperArm", R, -35f);
                pose.Turn("B L UpperArm", F, 15f);
                pose.Turn("B R UpperArm", F, -15f);
                return pose.Drop(72f);
            case "point":
                pose.Turn("B R UpperArm", R, 88f);
                pose.Turn("B R UpperArm", U, -8f);
                pose.Turn("B Head", U, -6f);
                break;
            case "laugh":
            {
                float shake = 5f * MathF.Sin(t * 16f);
                pose.Turn("B Spine1", R, 14f + shake);
                pose.Turn("B Head", R, -18f - shake);
                pose.Turn("B L UpperArm", R, 22f);
                pose.Turn("B R UpperArm", R, 22f);
                pose.Turn("B L UpperArm", U, -20f);
                pose.Turn("B R UpperArm", U, 20f);
                pose.Turn("B L ForeArm", U, -50f);
                pose.Turn("B R ForeArm", U, 50f);
                break;
            }

            case "clap":
            {
                float clap = 18f * (0.5f + (0.5f * MathF.Sin(t * 13f)));
                pose.Turn("B L UpperArm", R, 55f);
                pose.Turn("B R UpperArm", R, 55f);
                pose.Turn("B L UpperArm", U, -22f);
                pose.Turn("B R UpperArm", U, 22f);
                pose.Turn("B L ForeArm", U, -(35f + clap));
                pose.Turn("B R ForeArm", U, 35f + clap);
                break;
            }
        }

        return 0f;
    }

    /// <summary>Adds weighted turns (degrees) and the weighted hips' drop.</summary>
    private readonly struct Pose
    {
        private readonly List<PoseTurn> _turns;
        private readonly float _w;

        public Pose(List<PoseTurn> turns, float w)
        {
            _turns = turns;
            _w = w;
        }

        public void Turn(string bone, Vector3 axis, float degrees) =>
            _turns.Add(new PoseTurn(bone, axis, Mathf.DegToRad(degrees) * _w));

        public float Drop(float uu) => uu * _w;
    }
}
