using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>One notify of a pawn clip: its time, normalised over the frame count N, and the notify object.</summary>
/// <param name="Time">Normalised time in [0, 1).</param>
/// <param name="Notify">The notify object (sound, stop sound, script).</param>
public readonly record struct PawnClipNotify(float Time, UAnimNotify Notify);

/// <summary>A pawn animation sequence: <see cref="Frames"/> frames played at <see cref="Fps"/> at play rate 1.</summary>
/// <param name="Name">Sequence name.</param>
/// <param name="Frames">Frame count N.</param>
/// <param name="Fps">Frames per second at play rate 1.</param>
/// <param name="Root">
/// The root bone's position per frame in pawn space (X forward, Y right, Z up, uu, absolute: the key as the package
/// stores it), or null when the source has no root data.
/// </param>
public sealed record PawnClip(Name Name, int Frames, float Fps, FVector[]? Root = null)
{
    /// <summary>The clip's notifies in package order (firing follows time; ties keep this order).</summary>
    public IReadOnlyList<PawnClipNotify> Notifies { get; init; } = [];

    /// <summary>
    /// The clip's keyed root track (the motion chunk's RootTrack: key frames and positions in pawn space, absolute),
    /// or null when the clip has none. A clip with one moves the pawn by it instead of by the root bone.
    /// </summary>
    public PawnRootTrack? RootTrack { get; init; }

    /// <summary>
    /// The root position at a normalised frame: the keyed root track when the clip has one, else the root bone (both
    /// linear between keys and absolute, so a clip change while locked measures from the last clip's last position;
    /// zero without root data).
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "goldens moveset.spy-ladder-up, spy-ladder-down, merc-ladder-down: LaddStAlEu0 moves the pawn by its RootTrack, linear between keys, not by the root bone (ANSWERS-moveset.md finding 1)")]
    [Provenance(ProvenanceKind.Measured, "golden moveset.spy-wall-jump frame 36: WljpStAlBgR (track end 50 forward) to WljpStAlUpR (bone start 40.69 forward, 23.31 up) moves the pawn 9.3 back and 23.3 up (ANSWERS-moveset.md finding 2)")]
    public FVector RootAt(float frame)
    {
        if (RootTrack is { } track)
        {
            return track.At(SvmMath.Clamp(frame, 0f, 1f) * Frames);
        }

        if (Root is not { Length: > 0 } root)
        {
            return default;
        }

        float f = SvmMath.Clamp(frame, 0f, 1f) * Frames;
        int i = SvmMath.Min((int)f, root.Length - 1);
        int j = SvmMath.Min(i + 1, root.Length - 1);
        return root[i] + (root[j] - root[i]) * (f - i);
    }

    /// <summary>
    /// The root bone's yaw per frame in rotator units, relative to the first frame (the turn a root-motion move gives
    /// the pawn with lock bit 2), or null when the source has no root rotation.
    /// </summary>
    public float[]? RootYaw { get; init; }

    /// <summary>
    /// The root yaw at a normalised frame (linear between key frames; zero without root data). A clip with a root
    /// track turns by the track's rotation keys, not by the root bone.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "root update game 0x109b9c40: the root sample (position and rotation) comes from the root track sampler when the sequence has track keys (+0x48 above 0), else from the root bone; GetRootRotationDelta game 0x109b7f30 takes the yaw the other way round for a track")]
    public float RootYawAt(float frame)
    {
        if (RootTrack is { } track)
        {
            return track.YawAt(SvmMath.Clamp(frame, 0f, 1f) * Frames);
        }

        if (RootYaw is not { Length: > 0 } yaw)
        {
            return 0f;
        }

        float f = SvmMath.Clamp(frame, 0f, 1f) * Frames;
        int i = SvmMath.Min((int)f, yaw.Length - 1);
        int j = SvmMath.Min(i + 1, yaw.Length - 1);
        return yaw[i] + ((yaw[j] - yaw[i]) * (f - i));
    }

    /// <summary>Length of a one-shot play at rate 1: (N - 1) / fps.</summary>
    public float OneShotSeconds => (Frames - 1) / Fps;
}

/// <summary>
/// A clip's keyed root track: positions in pawn space (X forward, Y right, Z up, uu, absolute) at key frames (frame
/// numbers of the clip, rising).
/// </summary>
/// <param name="KeyFrames">The frame of each key.</param>
/// <param name="Positions">The root position at each key.</param>
public sealed record PawnRootTrack(float[] KeyFrames, FVector[] Positions)
{
    /// <summary>
    /// The root yaw at each key in rotator units, relative to the first key, signed as the pawn turns (the track's
    /// keys turn the other way from the root bone's), or null when the track has one rotation (no turn).
    /// </summary>
    public float[]? Yaw { get; init; }

    /// <summary>The yaw at a frame number: linear between the keys around it, held outside them; 0 without yaw keys.</summary>
    public float YawAt(float frame)
    {
        if (Yaw is not { Length: > 1 } yaw || yaw.Length != KeyFrames.Length)
        {
            return 0f;
        }

        if (frame <= KeyFrames[0])
        {
            return yaw[0];
        }

        for (int i = 1; i < KeyFrames.Length; i++)
        {
            if (frame < KeyFrames[i])
            {
                float span = KeyFrames[i] - KeyFrames[i - 1];
                float a = span > 0f ? (frame - KeyFrames[i - 1]) / span : 1f;
                return yaw[i - 1] + ((yaw[i] - yaw[i - 1]) * a);
            }
        }

        return yaw[^1];
    }

    /// <summary>The position at a frame number: linear between the keys around it, held before the first and after the last.</summary>
    public FVector At(float frame)
    {
        if (frame <= KeyFrames[0])
        {
            return Positions[0];
        }

        for (int i = 1; i < KeyFrames.Length; i++)
        {
            if (frame < KeyFrames[i])
            {
                float span = KeyFrames[i] - KeyFrames[i - 1];
                float a = span > 0f ? (frame - KeyFrames[i - 1]) / span : 1f;
                return Positions[i - 1] + ((Positions[i] - Positions[i - 1]) * a);
            }
        }

        return Positions[^1];
    }
}

/// <summary>Where a pawn's animation sequences come from (the converted packages, or the built-in table).</summary>
public interface IPawnAnimationSource
{
    /// <summary>The sequence <paramref name="Sequence"/> of the animation set <paramref name="Pawn"/> uses, or false.</summary>
    bool TryGetClip(SBasePawn Pawn, Name Sequence, out PawnClip Clip);
}

/// <summary>
/// The animation sets of the two pawn meshes: the spy's SPerso.PRO (SPawnAttaque) and the merc's SPerso.Def
/// (SPAWNDEFENSE), with the frame counts and rates of the movement clips. <see cref="Default"/> is the built-in
/// table (decoded from the packages: the clips the movement actions play); <see cref="FromContent"/> reads every
/// sequence of the converted <c>svm.anim/1</c> files, with their root tracks and notifies.
/// </summary>
public sealed class PawnAnimationSource : IPawnAnimationSource
{
    private readonly Dictionary<Name, PawnClip> _spy;
    private readonly Dictionary<Name, PawnClip> _merc;

    private PawnAnimationSource(Dictionary<Name, PawnClip> spy, Dictionary<Name, PawnClip> merc)
    {
        _spy = spy;
        _merc = merc;
    }

    /// <summary>The built-in table of the movement clips.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPerso.PRO and SPerso.Def animation packages: frame counts and rates of the movement clips")]
    public static PawnAnimationSource Default => s_default;

    /// <summary>
    /// Reads <c>SPerso/PRO.anim.json</c> (spy) and <c>SPerso/Def.anim.json</c> (merc) under the converted
    /// characters folder (<c>.content/shared/chars</c>). Sequences the files lack fall back to the built-in table.
    /// </summary>
    public static PawnAnimationSource FromContent(string charsDir)
    {
        ArgumentNullException.ThrowIfNull(charsDir);
        var spy = new Dictionary<Name, PawnClip>(Default._spy);
        var merc = new Dictionary<Name, PawnClip>(Default._merc);
        Load(Path.Combine(charsDir, "SPerso", "PRO.anim.json"), spy);
        Load(Path.Combine(charsDir, "SPerso", "Def.anim.json"), merc);
        return new PawnAnimationSource(spy, merc);
    }

    public bool TryGetClip(SBasePawn Pawn, Name Sequence, out PawnClip Clip)
    {
        Dictionary<Name, PawnClip> set = Pawn is SPAWNDEFENSE ? _merc : _spy;
        if (set.TryGetValue(Sequence, out PawnClip? found))
        {
            Clip = found;
            return true;
        }

        Clip = null!;
        return false;
    }

    private static void Load(string path, Dictionary<Name, PawnClip> into)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!doc.RootElement.TryGetProperty("sequences", out JsonElement seqs) || seqs.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty p in seqs.EnumerateObject())
        {
            JsonElement s = p.Value;
            if (!s.TryGetProperty("frames", out JsonElement f) || !s.TryGetProperty("rate", out JsonElement r))
            {
                continue;
            }

            int frames = f.GetInt32();
            float fps = r.GetSingle();
            if (frames < 1 || !(fps > 0f))
            {
                continue;
            }

            FVector[]? root = null;
            if (s.TryGetProperty("rootBone", out JsonElement rb) && rb.TryGetProperty("pos", out JsonElement pos) &&
                pos.ValueKind == JsonValueKind.Array && pos.GetArrayLength() == frames)
            {
                // Mesh space has Y forward and X to the left of the pawn: pawn space is (y, -x, z).
                root = new FVector[frames];
                int i = 0;
                foreach (JsonElement v in pos.EnumerateArray())
                {
                    root[i++] = MeshToPawn(v);
                }
            }

            PawnRootTrack? track = s.TryGetProperty("rootTrack", out JsonElement rt) ? ReadRootTrack(rt) : null;

            float[]? rootYaw = null;
            if (rb.ValueKind == JsonValueKind.Object && rb.TryGetProperty("rot", out JsonElement rot) &&
                rot.ValueKind == JsonValueKind.Array && rot.GetArrayLength() == frames)
            {
                rootYaw = ReadRootYaw(rot, frames);
            }

            var name = new Name(p.Name);
            into[name] = new PawnClip(name, frames, fps, root)
            {
                Notifies = PawnAnimNotifies.Read(s), RootYaw = rootYaw, RootTrack = track,
            };
        }
    }

    private static FVector MeshToPawn(JsonElement v) => new(v[1].GetSingle(), -v[0].GetSingle(), v[2].GetSingle());

    /// <summary>
    /// The keyed root track of a converted sequence, or null when empty. Its positions and rotations share the time
    /// keys; a track with one position (a turn-only track such as LedgStAlTiL) holds that position at every key, and
    /// one with one rotation does not turn.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "root update game 0x109b9c40: any sequence with track keys samples the track (a single position key is held), never the root bone")]
    private static PawnRootTrack? ReadRootTrack(JsonElement rt)
    {
        if (rt.ValueKind != JsonValueKind.Object || !rt.TryGetProperty("pos", out JsonElement pos) ||
            !rt.TryGetProperty("time", out JsonElement time) || pos.ValueKind != JsonValueKind.Array ||
            time.ValueKind != JsonValueKind.Array || pos.GetArrayLength() == 0 || time.GetArrayLength() == 0 ||
            (pos.GetArrayLength() != time.GetArrayLength() && pos.GetArrayLength() != 1))
        {
            return null;
        }

        int n = time.GetArrayLength();
        var keys = new float[n];
        var at = new FVector[n];
        int i = 0;
        foreach (JsonElement t in time.EnumerateArray())
        {
            keys[i++] = t.GetSingle();
        }

        i = 0;
        foreach (JsonElement v in pos.EnumerateArray())
        {
            at[i++] = MeshToPawn(v);
        }

        for (; i < n; i++)
        {
            at[i] = at[0];
        }

        float[]? yaw = null;
        if (rt.TryGetProperty("rot", out JsonElement rot) && rot.ValueKind == JsonValueKind.Array && rot.GetArrayLength() == n &&
            n > 1)
        {
            yaw = ReadRootYaw(rot, n, relativeWhenUpright: true);
            for (int k = 0; k < n; k++)
            {
                yaw[k] = -yaw[k];
            }
        }

        return new PawnRootTrack(keys, at) { Yaw = yaw };
    }

    /// <summary>
    /// The root keys' turn about the mesh's up axis, in rotator units relative to the first key (unwrapped), from the
    /// converted quaternions (x, y, z, w). With <paramref name="relativeWhenUpright"/>, keys whose first key has its X
    /// axis upright (a track keyed in a turned basis, such as the merc's AtakStAlTu) are read relative to that first
    /// key (inverse of the first times the key), whose turn is then about the vertical.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "golden traversal.spy-ledge-pull-up: the pawn's yaw during LedgStAlUp0 follows the root keys' turn about Z (+1375 at the last key)")]
    [Provenance(ProvenanceKind.Estimated, "Def AtakStAlTu root track: every key is (-0.5, 0.5, -0.5, -0.5) times a turn about local Z; read raw the X axis is upright and the yaw came out 0 or a half turn (the berserk left the merc a half turn round, the camera snapped at the end); read relative to the first key it turns one full turn, as the root bone does and melee rule 27 states")]
    private static float[] ReadRootYaw(JsonElement rot, int frames, bool relativeWhenUpright = false)
    {
        var yaw = new float[frames];
        float first = 0f;
        float previous = 0f;
        float unwrap = 0f;
        bool relative = false;
        float ix = 0f, iy = 0f, iz = 0f, iw = 1f;
        int i = 0;
        foreach (JsonElement q in rot.EnumerateArray())
        {
            float x = q[0].GetSingle();
            float y = q[1].GetSingle();
            float z = q[2].GetSingle();
            float w = q[3].GetSingle();
            if (i == 0 && relativeWhenUpright)
            {
                float fx = 1f - (2f * ((y * y) + (z * z)));
                float fy = 2f * ((w * z) + (x * y));
                relative = (fx * fx) + (fy * fy) < UprightAxisSquared;
                (ix, iy, iz, iw) = (-x, -y, -z, w);
            }

            if (relative)
            {
                // The inverse of the first key times this key (Hamilton product).
                (x, y, z, w) = ((iw * x) + (ix * w) + (iy * z) - (iz * y), (iw * y) - (ix * z) + (iy * w) + (iz * x),
                    (iw * z) + (ix * y) - (iy * x) + (iz * w), (iw * w) - (ix * x) - (iy * y) - (iz * z));
            }

            // The yaw of the key's X axis; where that axis stands nearly upright (a pawn lying down) the yaw is not
            // defined and the previous key's is kept.
            float ax = 1f - (2f * ((y * y) + (z * z)));
            float ay = 2f * ((w * z) + (x * y));
            if (i > 0 && (ax * ax) + (ay * ay) < UprightAxisSquared)
            {
                yaw[i++] = previous - first;
                continue;
            }

            float turn = SvmMath.Atan2(ay, ax) * RotatorUnitsPerRadian;
            if (i > 0)
            {
                float step = (turn + unwrap) - previous;
                if (step > HalfTurn)
                {
                    unwrap -= FullTurn;
                }
                else if (step < -HalfTurn)
                {
                    unwrap += FullTurn;
                }
            }

            float value = turn + unwrap;
            if (i == 0)
            {
                first = value;
            }

            previous = value;
            yaw[i++] = value - first;
        }

        return yaw;
    }

    [Provenance(ProvenanceKind.Estimated, "Keys whose X axis is within 60 degrees of upright keep the previous yaw (the engine's own value there is not decoded)")]
    private const float UprightAxisSquared = 0.25f;

    private const float FullTurn = 65536f;
    private const float HalfTurn = 32768f;
    private const float RotatorUnitsPerRadian = 10430.378f;

    private static PawnAnimationSource BuildDefault()
    {
        var spy = new Dictionary<Name, PawnClip>();
        var merc = new Dictionary<Name, PawnClip>();
        foreach ((string n, int f, float fps) in SpyClips)
        {
            spy[new Name(n)] = new PawnClip(new Name(n), f, fps);
        }

        foreach ((string n, int f, float fps) in MercClips)
        {
            merc[new Name(n)] = new PawnClip(new Name(n), f, fps);
        }

        // The merc's set lacks the spy-only traversal clips; the merc never plays them.
        foreach (KeyValuePair<Name, PawnClip> kv in spy)
        {
            merc.TryAdd(kv.Key, kv.Value);
        }

        // SPerso.Def mine clips: retain the gameplay interaction notify even on
        // dedicated hosts without converted animation content.
        foreach (string stance in new[] { "St", "Cr" })
        {
            Name grab = new("Mine" + stance + "AlBg0");
            Name place = new("Mine" + stance + "AlEd0");
            merc[grab] = new PawnClip(grab, 7, 15f);
            merc[place] = new PawnClip(place, 15, 15f)
            {
                Notifies = [new PawnClipNotify(stance == "St" ? .47333333f : .45333335f,
                    new UAnimNotify_Script { NotifyName = new Name("AnimNotify_Interact") })],
            };
        }

        return new PawnAnimationSource(spy, merc);
    }

    private static readonly (string Name, int Frames, float Fps)[] SpyClips =
    [
        ("WaitStAlFd0", 8, 8f), ("WaitStSpFd2", 8, 8f), ("waitCrAlFd0", 9, 8f), ("waitCrSpFd2", 9, 8f),
        ("WaitCrAlBg0", 9, 15f), ("WaitCrAlEd0", 9, 15f), ("WaitCrSpBg2", 9, 15f), ("WaitCrSpEd2", 9, 15f),
        ("jumpstalfdl", 12, 15f), ("jumpstalfdr", 12, 15f), ("fallstaldn0", 11, 8f), ("fallstaldn2", 11, 8f),
        ("FallStAlFr0", 16, 15f), ("fallStAlQt0", 9, 8f), ("landStAlLo0", 10, 15f), ("landStAlHi0", 20, 15f),
        ("landCrAlQt0", 13, 15f), ("landStAlLo2", 7, 15f), ("landStAlHi2", 18, 15f), ("landCrAlQt2", 13, 15f),
        ("RollStAlFdL", 16, 15f), ("RollStAlFdR", 16, 15f), ("RollStJpFdL", 19, 15f), ("RollStJpFdR", 19, 15f),
        ("LedgStAlBg0", 8, 15f), ("LedgStAlNt0", 8, 8f), ("LedgStAlUp0", 19, 15f), ("LedgCrAlUp0", 19, 15f),
        ("LedgStAlTiL", 16, 15f), ("LedgStAlToL", 26, 15f), ("LedgStAlRb0", 16, 15f), ("LedgCrNwUp0", 46, 15f),
        ("LedgStAlLt0", 10, 15f), ("LedgStAlRt0", 10, 15f), ("PipVStAlIOR", 13, 15f), ("PipVStAlBg0", 12, 15f),
        ("PipVStAlUpRL", 17, 15f), ("PipVStAlNtL", 15, 15f), ("PipVStAlEuL", 36, 15f), ("PipVStAlEuR", 36, 15f),
        ("PipVStAlDn0", 5, 8f), ("PipVCrAlEuL", 36, 15f), ("PipVCrAlEuR", 36, 15f), ("ZippStAlBg0", 16, 15f),
        ("ZippStAlDn0", 16, 15f), ("ZippCrAlBg0", 16, 15f), ("ZippCrAlDn0", 16, 15f), ("ZippCrAlEd0", 16, 15f),
        ("ZippStAlSN0", 16, 15f), ("ZippStAlSw0", 16, 15f), ("ZippCrAlBl0", 12, 15f), ("PipVSZilBhL", 18, 15f),
        ("PipVSZilBhR", 18, 15f), ("BackCrAlBg0", 11, 15f), ("BackStAlBg0", 11, 15f), ("BackCrAlEd0", 11, 15f),
        ("BackStAlEd0", 11, 15f), ("BackStAlNt0", 9, 8f), ("BackCrAlNt0", 9, 8f), ("BackStAlTr0", 2, 8f),
        ("BackCrAlTr0", 2, 8f), ("BackStAlFdL", 17, 15f), ("BackStAlFdR", 17, 15f), ("BackCrAlFdL", 24, 15f),
        ("BackCrAlFdR", 24, 15f), ("JumpStAlBr0", 9, 15f), ("JumpStAlBf0", 2, 15f), ("LedgStAlFtL", 7, 15f),
        ("LedgStAlFtR", 7, 15f), ("LedgStAlQk0", 19, 15f), ("LedgCrAlQk0", 19, 15f), ("LedgStAlBQ0", 2, 15f),
        ("DrawCrSpBg2", 10, 15f), ("DrawCrSpEd2", 10, 15f), ("DrawStSpBg2", 9, 15f), ("DrawStSpEd2", 10, 15f),
        ("BackCrPkNtL", 9, 8f), ("BackCrPkNtR", 9, 8f), ("BackCrSpNtL", 9, 8f), ("BackCrSpNtR", 9, 8f),
        ("BackStPkNtL", 9, 8f), ("BackStPkNtR", 9, 8f), ("BackStSpNtL", 9, 8f), ("BackStSpNtR", 8, 8f),
        ("PipVCrAlBuL", 36, 15f), ("PipVCrAlBuR", 36, 15f), ("PipVStAlBuL", 36, 15f), ("PipVStAlBuR", 36, 15f),
        ("PipVStAlTrLL", 19, 15f), ("PipVStAlTrRL", 19, 15f), ("PipVStLgBuR", 21, 15f), ("PipVStLgEuR", 21, 15f),
    ];

    private static readonly (string Name, int Frames, float Fps)[] MercClips =
    [
        ("WaitStSpFd2", 8, 8f), ("waitCrSpFd2", 8, 8f), ("WaitCrSpBg2", 9, 15f), ("WaitCrSpEd2", 9, 15f),
        ("JumpStAlFL2", 12, 15f), ("JumpStAlFR2", 12, 15f), ("fallstaldn0", 9, 8f), ("fallstaldn2", 9, 8f),
        ("FallStAlFr0", 16, 15f), ("landStAlLo0", 10, 15f), ("landStAlHi0", 20, 15f), ("landCrAlQt0", 12, 15f),
        ("landStAlLo2", 10, 15f), ("landStAlHi2", 20, 15f), ("landCrAlQt2", 12, 15f), ("LedgStAlFtL", 9, 15f),
        ("LedgStAlFtR", 9, 15f),
    ];

    // After the clip tables: static fields initialise in order.
    private static readonly PawnAnimationSource s_default = BuildDefault();
}

/// <summary>
/// One animation channel of a pawn: the UE2 mesh instance channel record (spec/native/anim.md, channel record), as
/// the channel natives and the walk blender read and write it.
/// </summary>
public sealed class PawnAnimChannel
{
    /// <summary>The sequence (None when the channel is empty).</summary>
    public Name Sequence { get; internal set; }

    /// <summary>The clip of <see cref="Sequence"/>, for its frame count, rate, root track and notifies.</summary>
    public PawnClip? Clip { get; internal set; }

    /// <summary>Frame count N of the clip.</summary>
    public int Frames { get; internal set; }

    /// <summary>The frame, normalised over N (0 to <see cref="AnimLast"/>; negative while tweening in).</summary>
    public float Frame { get; internal set; }

    /// <summary>AnimRate: normalised frames per second (clip rate x play rate / N); negative = scaled by speed.</summary>
    public float Rate { get; internal set; }

    /// <summary>AnimLast: the last frame's start, 1 - 1/N (0 for a pose).</summary>
    public float AnimLast { get; internal set; }

    /// <summary>Normalised tween frames per second while <see cref="Frame"/> is negative.</summary>
    public float TweenRate { get; internal set; }

    /// <summary>OldAnimRate: the rate before the last play (the tween -1 rule).</summary>
    public float OldAnimRate { get; internal set; }

    /// <summary>Clip rate / N: AnimRate = rate scale x this.</summary>
    public float BaseRate { get; internal set; }

    /// <summary>True for a looping play.</summary>
    public bool bLoop { get; internal set; }

    /// <summary>The clip plays from its end back to its start (only its sampled key and notify times are mirrored).</summary>
    public bool bBackward { get; internal set; }

    /// <summary>The pose is mirrored left to right (ExecuteAction's x; root motion sideways reversed).</summary>
    public bool bMirror { get; internal set; }

    /// <summary>The clip has notifies.</summary>
    public bool bAnimNotify { get; internal set; }

    /// <summary>EnableChannelNotify(false): no notifies and no AnimEnd.</summary>
    public bool bNotifyIgnore { get; internal set; }

    /// <summary>Blend weight over the channels below (channel 0 is drawn at 1 and keeps 0 here).</summary>
    public float BlendAlpha { get; internal set; }

    /// <summary>AnimBlendParams' in and out times (stored, at most 1).</summary>
    public float BlendInTime { get; internal set; }

    public float BlendOutTime { get; internal set; }

    /// <summary>The bone the channel starts at (None: the whole skeleton).</summary>
    public Name StartBone { get; internal set; }

    /// <summary>AnimBlendParams' bGlobalPose.</summary>
    public bool bGlobalPose { get; internal set; }

    /// <summary>An AnimBlendToAlpha in progress: alpha moves to <see cref="BlendTarget"/> in <see cref="BlendTimer"/> s.</summary>
    public bool bTimedBlend { get; internal set; }

    public float BlendTarget { get; internal set; }

    public float BlendTimer { get; internal set; }

    /// <summary>
    /// The tween weight the pose build keeps (1 + frame x N while the frame is negative, its last value afterwards).
    /// </summary>
    public float TweenWeight { get; internal set; }

    /// <summary>True while a channel plays: it has a sequence and its tween rate (frame below 0) or rate is not 0.</summary>
    public bool bPlaying => !Sequence.IsNone && (Frame < 0f ? TweenRate : Rate) != 0f;

    /// <summary>The last frame of a one-shot play (AnimLast).</summary>
    public float EndFrame => AnimLast;

    /// <summary>
    /// The position in the clip the pose samples, normalised (anim.md ANSWERS A5): the frame clamped to [0, 1],
    /// mirrored in time for a backward play (1 - frame for a loop, AnimLast - frame for a one-shot).
    /// </summary>
    public float ClipFrame
    {
        get
        {
            float f = SvmMath.Clamp(Frame, 0f, 1f);
            if (!bBackward)
            {
                return f;
            }

            return bLoop ? 1f - f : SvmMath.Max(0f, AnimLast - f);
        }
    }

    /// <summary>Bumped when the channel's clip changes, so a reader (root motion, views) sees a new play.</summary>
    internal int Serial;

    internal float TweenCacheFrame;
    internal Name TweenCacheSequence;

    internal bool bQueued;
    internal PawnClip? QueuedClip;
    internal float QueuedRate;
    internal float QueuedTween;
    internal bool QueuedLoop;
    internal bool QueuedBackward;
    internal int NetworkChangeDepth;

    internal void Clear()
    {
        Sequence = Name.None;
        Clip = null;
        Frames = 0;
        Frame = 0f;
        Rate = 0f;
        AnimLast = 0f;
        TweenRate = 0f;
        OldAnimRate = 0f;
        BaseRate = 0f;
        bLoop = false;
        bBackward = false;
        bMirror = false;
        bAnimNotify = false;
        bQueued = false;
        QueuedClip = null;
        bTimedBlend = false;
        Serial++;
    }
}

/// <summary>
/// The animation channels of one pawn: UE2 skeletal mesh instance channels 0 to 24 with the engine's channel
/// natives (PlayAnim with its tween rules, blend params and timed blends, rate scales, sequence swaps and copies)
/// and its per-tick update (frame advance in up to 4 passes, the 1e-4 start nudge, notifies, AnimEnd for one-shots
/// and loops, the root-motion hold), written from spec/native/anim.md and spec/native/ANSWERS-anim.md. The pawn's
/// own tick advances them first with the tick's time (ANSWERS-fidelity.md F3): a clip the controller starts gets
/// one update in the same tick, a clip started by physics or by an AnimEnd gets its first update on the next tick.
/// The walk blender (<see cref="PawnMovementAnimation"/>) drives channels 1 to 13 after physics.
/// </summary>
public sealed partial class PawnAnimation
{
    /// <summary>Channels 0 to LASTCHANNEL (24).</summary>
    public const int ChannelCount = SBasePawn.LASTCHANNEL + 1;

    [Provenance(ProvenanceKind.Decoded, "USkeletalMeshInstance::UpdateAnimation editor 0x111056c0: a frame of exactly 0 becomes 1e-4 (0x38d1b717); PlayAnim 0x110ff8f0: a looping play without tween starts there")]
    [Provenance(ProvenanceKind.Measured, "goldens: a clip started without tween, a finished tween and a loop wrap all show frame 0.0001")]
    public const float StartFrame = 0.0001f;

    [Provenance(ProvenanceKind.Decoded, "UpdateAnimation 0x111056c0: at most 4 passes per channel per update")]
    private const int MaxPasses = 4;

    [Provenance(ProvenanceKind.Decoded, "UpdateAnimation 0x111056c0: a negative rate advances by |Velocity| x -rate, at least 0.3 per second")]
    private const float SpeedRateFloor = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "PlayAnim 0x110ff8f0: a one-frame clip without tween time tweens at 10 per second")]
    private const float PoseTweenRate = 10f;

    [Provenance(ProvenanceKind.Decoded, "PlayAnim 0x110ff8f0: tween -1 without an old rate tweens over 0.025 N")]
    private const float DefaultTweenSecondsPerFrame = 0.025f;

    [Provenance(ProvenanceKind.Decoded, "UpdateAnimation 0x111056c0: backward notify window opens 1e-6 after the old frame")]
    private const float BackwardNotifyMargin = 1e-6f;

    private readonly PawnAnimChannel[] _channels = new PawnAnimChannel[ChannelCount];
    private bool[] _fired = new bool[8];
    private bool _poseCached;

    public PawnAnimation()
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new PawnAnimChannel();
        }
    }

    /// <summary>The channel <paramref name="channel"/>.</summary>
    public PawnAnimChannel this[int channel] => _channels[channel];

    /// <summary>The mesh instance's aim and look-at bone control (AimAt requests and the angles the pose turns bones by).</summary>
    public OpenScct.Game.Animation.MeshAimIK Aim { get; } = new();

    /// <summary>The mesh instance's bone direction controllers (SetBoneDirection: TurnHead's arm aim).</summary>
    public OpenScct.Game.Animation.MeshBoneDirections Directions { get; } = new();

    /// <summary>Tick of the last <see cref="Advance"/>, so a pawn is advanced once per tick.</summary>
    public long LastAdvanceTick { get; internal set; } = -1;

    /// <summary>
    /// The animated actor: notifies run with it as owner and a speed-scaled rate reads its velocity. Without one
    /// (bare channel tests) notifies are skipped and the speed is 0.
    /// </summary>
    public Actor? Owner { get; set; }

    /// <summary>The channel of the last notify or AnimEnd (the engine's notify channel).</summary>
    public int NotifyChannel { get; private set; }

    /// <summary>True for a channel index the pawn has (0 to 24).</summary>
    public bool IsValidChannel(int channel) => channel >= 0 && channel < ChannelCount;

    // ---- PlayAnim and kin ---------------------------------------------------------------------------------------

    /// <summary>
    /// Starts <paramref name="clip"/> on a channel (Play with the UE2 argument set without continue or mirror). With a
    /// tween the frame starts at -1/N and rises to 0 in <paramref name="tweenTime"/> seconds before the clip plays.
    /// Without a tween a loop starts at 1e-4 and a one-shot exactly at its start, which its first update only nudges
    /// to 1e-4.
    /// </summary>
    public void Play(int channel, PawnClip clip, float rate, float tweenTime, bool loop, bool mirror = false, bool backward = false) =>
        PlayAnim(channel, clip, rate, tweenTime, loop, backward, continueAtFrame: false, mirror);

    /// <summary>
    /// USkeletalMeshInstance::PlayAnim (anim.md rule 1, ANSWERS-anim.md A1): the native argument set (Channel,
    /// Sequence, Rate, TweenTime, bLoop, bBackward, bContinueAtFrame, bMirror). False when refused (a negative rate
    /// without loop, a clip without frames).
    /// </summary>
    [UcNative("AActor::execPlayAnim", Exe = 0x10a23610, Editor = 0x11185cd0)]
    [UcNative("USkeletalMeshInstance::PlayAnim", Exe = 0x10a23590, Editor = 0x110ff8f0)]
    [PortOf("nat:Engine.Actor.PlayAnim", Spec = "spec/native/ANSWERS-anim.md#a1-playanim-uskeletalmeshinstanceplayanim-0x110ff8f0-complete")]
    [Provenance(ProvenanceKind.Decoded, "PlayAnim 0x110ff8f0: tween start -1/N, tween rate 1 / (TweenTime N), tween -1 rules, one-frame poses, the loop continue, no tween on an empty channel")]
    [Provenance(ProvenanceKind.Measured, "goldens: tween start -1/N and tween rate 1 / (TweenTime N); rate fps x play rate / N")]
    public bool PlayAnim(int channel, PawnClip clip, float rate, float tweenTime, bool loop, bool backward = false,
        bool continueAtFrame = false, bool mirror = false)
    {
        using var change = TrackAuthoredChange(channel);
        ArgumentNullException.ThrowIfNull(clip);
        if (!IsValidChannel(channel) || clip.Frames < 1)
        {
            return false;
        }

        PawnAnimChannel c = _channels[channel];
        c.bMirror = mirror;
        FireInterruptedNotifies(channel, c, clip);
        float n = clip.Frames;
        float inv = 1f / n;
        if (loop)
        {
            if (c.Sequence == clip.Name && c.bLoop && c.bPlaying)
            {
                c.BaseRate = clip.Fps / n;
                c.bBackward = backward;
                c.Rate = c.BaseRate * rate;
                c.OldAnimRate = c.Rate;
                return true;
            }

            SetClip(c, clip);
            c.Serial++;
            c.BaseRate = inv * clip.Fps;
            c.Rate = c.BaseRate * rate;
            c.AnimLast = 1f - inv;
            c.bLoop = true;
            c.bBackward = backward;
            c.bAnimNotify = clip.Notifies.Count > 0;
            if (c.AnimLast == 0f)
            {
                PlayPose(c, tweenTime, inv);
                return true;
            }

            if (tweenTime > 0f)
            {
                c.TweenRate = 1f / (n * tweenTime);
                if (!continueAtFrame)
                {
                    c.OldAnimRate = c.Rate;
                    c.Frame = inv * -1f;
                    return true;
                }
            }
            else if (tweenTime == -1f)
            {
                if (!continueAtFrame)
                {
                    c.Frame = inv * -1f;
                }

                TweenFromOldRate(c, n);
                return true;
            }
            else
            {
                c.TweenRate = 0f;
                if (!continueAtFrame)
                {
                    c.Frame = StartFrame;
                }
            }

            c.OldAnimRate = c.Rate;
            return true;
        }

        if (rate <= 0f)
        {
            if (rate != 0f)
            {
                return false;
            }

            // TweenAnim: a pose of the clip's first frame.
            SetClip(c, clip);
            c.Serial++;
            c.AnimLast = 0f;
            c.bAnimNotify = false;
            c.bLoop = false;
            c.Rate = 0f;
            c.OldAnimRate = 0f;
            c.BaseRate = clip.Fps * inv;
            if (tweenTime > 0f)
            {
                c.TweenRate = 1f / (n * tweenTime);
                c.Frame = inv * -1f;
            }
            else
            {
                c.TweenRate = 0f;
                c.Frame = 0f;
            }

            return true;
        }

        if (c.Sequence.IsNone)
        {
            tweenTime = 0f;
        }

        SetClip(c, clip);
        c.Serial++;
        c.BaseRate = inv * clip.Fps;
        c.Rate = c.BaseRate * rate;
        c.AnimLast = 1f - inv;
        bool backwardChanged = c.bBackward != backward;
        c.bBackward = backward;
        c.bLoop = false;
        c.bAnimNotify = clip.Notifies.Count > 0;
        if (c.AnimLast == 0f)
        {
            PlayPose(c, tweenTime, inv);
            return true;
        }

        if (tweenTime > 0f)
        {
            c.TweenRate = 1f / (n * tweenTime);
            if (!continueAtFrame)
            {
                c.OldAnimRate = c.Rate;
                c.Frame = inv * -1f;
                return true;
            }

            ContinueAtFrame(c, backwardChanged);
            return true;
        }

        if (tweenTime != -1f)
        {
            c.TweenRate = 0f;
            if (!continueAtFrame)
            {
                c.Frame = 0f;
                c.OldAnimRate = c.Rate;
                return true;
            }

            ContinueAtFrame(c, backwardChanged);
            return true;
        }

        if (!continueAtFrame)
        {
            c.Frame = inv * -1f;
        }
        else if (backwardChanged)
        {
            c.Frame = c.AnimLast - c.Frame;
        }

        TweenFromOldRate(c, n);
        return true;
    }

    /// <summary>TweenAnim(Sequence, Time, Channel): PlayAnim at rate 0 (a pose of the clip's first frame).</summary>
    [UcNative("AActor::execTweenAnim", Editor = 0x11185f30)]
    [PortOf("nat:Engine.Actor.TweenAnim", Spec = "spec/native/anim.md#1-playanim-aactorplayanim-0x11189250-uskeletalmeshinstanceplayanim-0x110ff8f0")]
    public bool TweenAnim(int channel, PawnClip clip, float time) => PlayAnim(channel, clip, 0f, time, loop: false);

    /// <summary>PlayAnimAfter: queues a clip that starts when the channel's one-shot ends (ANSWERS-anim.md A2.8).</summary>
    [UcNative("USkeletalMeshInstance::PlayAnimAfter", Editor = 0x11100210)]
    [PortOf("nat:Engine.Actor.PlayAnimAfter", Spec = "spec/native/anim.md#4-other-channel-natives")]
    public void PlayAnimAfter(int channel, PawnClip? clip, float rate, float tweenTime, bool loop = false, bool backward = false)
    {
        using var change = TrackAuthoredChange(channel);
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        c.QueuedClip = clip;
        c.QueuedRate = rate;
        c.QueuedTween = tweenTime;
        c.QueuedLoop = loop;
        c.QueuedBackward = backward;
        c.bQueued = true;
    }

    /// <summary>Stops and clears a channel.</summary>
    public void Stop(int channel)
    {
        using var change = TrackAuthoredChange(channel);
        PawnAnimChannel c = _channels[channel];
        c.Clear();
    }

    /// <summary>
    /// Puts <paramref name="clip"/> on a channel as a loop without restarting the channel's frame (a channel that held
    /// nothing starts at 0), at play rate 0 until <see cref="SetPlayRate"/> moves it. The movement's own helper; the
    /// engine native is <see cref="SetAnimSequence"/>.
    /// </summary>
    public void SetSequence(int channel, PawnClip clip)
    {
        using var change = TrackAuthoredChange(channel);
        ArgumentNullException.ThrowIfNull(clip);
        PawnAnimChannel c = _channels[channel];
        if (c.Sequence == clip.Name && c.Clip is not null)
        {
            return;
        }

        bool wasPlaying = c.Clip is not null;
        SetAnimSequence(channel, clip);
        c.bLoop = true;
        c.bBackward = false;
        c.bMirror = false;
        c.TweenRate = 0f;
        c.Rate = 0f;
        c.AnimLast = clip.Frames <= 1 ? 0f : 1f - (1f / clip.Frames);
        if (!wasPlaying)
        {
            c.Frame = 0f;
        }
    }

    /// <summary>
    /// SetAnimSequence: the channel's sequence, base rate and notify flag change; its frame, rate, loop and backward
    /// flags are kept (ANSWERS-anim.md A3).
    /// </summary>
    [UcNative("USkeletalMeshInstance::SetAnimSequence", Editor = 0x11102a10)]
    [PortOf("eng:ASBasePawn::UpdateMovementAnimation", Spec = "spec/native/ANSWERS-anim.md#a3-channel-helpers-used-by-the-walk-blender-and-executeaction")]
    public void SetAnimSequence(int channel, PawnClip clip)
    {
        using var change = TrackAuthoredChange(channel);
        ArgumentNullException.ThrowIfNull(clip);
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        if (c.Sequence != clip.Name)
        {
            c.Serial++;
        }

        SetClip(c, clip);
        c.bAnimNotify = clip.Notifies.Count > 0;
        c.BaseRate = clip.Fps / clip.Frames;
    }

    /// <summary>SetAnimSequence with None: the channel keeps its timing but holds no sequence (base rate 0).</summary>
    public void ClearSequence(int channel)
    {
        using var change = TrackAuthoredChange(channel);
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        if (!c.Sequence.IsNone)
        {
            c.Serial++;
        }

        c.Sequence = Name.None;
        c.Clip = null;
        c.Frames = 0;
        c.bAnimNotify = false;
        c.BaseRate = 0f;
    }

    /// <summary>A channel's play rate (1 plays its clip at the clip's own rate, 0 holds the frame).</summary>
    public void SetPlayRate(int channel, float playRate) => SetChannelRateScale(channel, playRate);

    /// <summary>SetChannelRateScale: AnimRate = scale x base rate (0 freezes the channel).</summary>
    [UcNative("USkeletalMeshInstance::SetChannelRateScale", Editor = 0x11102730)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/anim.md#4-other-channel-natives")]
    public void SetChannelRateScale(int channel, float scale)
    {
        using var change = TrackAuthoredChange(channel, phase: true, procedural: true);
        if (IsValidChannel(channel))
        {
            PawnAnimChannel c = _channels[channel];
            c.Rate = scale * c.BaseRate;
        }
    }

    /// <summary>SetAnimFrame without the unit flag: the channel's normalised frame.</summary>
    public void SetFrame(int channel, float frame) => SetAnimFrame(channel, frame, unitFlag: false);

    /// <summary>SetAnimFrame: frame = Time, or Time / (N - 1) with the unit flag (frames in frame units).</summary>
    [UcNative("USkeletalMeshInstance::SetAnimFrame", Editor = 0x11102870)]
    [PortOf("nat:Engine.Actor.SetAnimFrame", Spec = "spec/native/anim.md#4-other-channel-natives")]
    public void SetAnimFrame(int channel, float time, bool unitFlag)
    {
        using var change = TrackAuthoredChange(channel, phase: true, procedural: true);
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        c.Frame = unitFlag && c.Frames >= 1 && c.Frames - 1f >= 1f ? time / (c.Frames - 1f) : time;
    }

    /// <summary>True while a channel plays a sequence that has not ended.</summary>
    [UcNative("USkeletalMeshInstance::IsAnimating", Editor = 0x110fed00)]
    public bool IsAnimating(int channel) => IsValidChannel(channel) && _channels[channel].bPlaying;

    /// <summary>True when the channel plays its clip backward.</summary>
    public bool IsAnimBackward(int channel) => IsValidChannel(channel) && _channels[channel].bBackward;

    /// <summary>The channel's frame (0 for a missing channel).</summary>
    public float GetAnimFrame(int channel) => IsValidChannel(channel) ? _channels[channel].Frame : 0f;

    /// <summary>The channel's blend alpha (0 for a missing channel).</summary>
    public float GetBlendAlpha(int channel) => IsValidChannel(channel) ? _channels[channel].BlendAlpha : 0f;

    /// <summary>The channel's sequence (None for a missing channel).</summary>
    public Name GetSequence(int channel) => IsValidChannel(channel) ? _channels[channel].Sequence : Name.None;

    /// <summary>The channel's clip rate in frames per second, 30 when the clip is missing or has no rate.</summary>
    [UcNative("USkeletalMeshInstance::ClipRate", Editor = 0x11105660)]
    [Provenance(ProvenanceKind.Decoded, "0x11105660: the channel's sequence rate, 30 when missing or 0")]
    public float ClipRate(int channel)
    {
        if (IsValidChannel(channel) && _channels[channel].Clip is { Fps: not 0f } clip)
        {
            return clip.Fps;
        }

        return AnimationK.MissingClipRate;
    }

    /// <summary>AnimBlendParams: the channel's alpha, blend times, start bone and global pose (not on channel 0).</summary>
    [UcNative("USkeletalMeshInstance::SetBlendParams", Editor = 0x111005a0)]
    [UcNative("AActor::execAnimBlendParams", Editor = 0x111866c0)]
    [PortOf("nat:Engine.Actor.AnimBlendParams", Spec = "spec/native/ANSWERS-anim.md#a3-channel-helpers-used-by-the-walk-blender-and-executeaction")]
    [Provenance(ProvenanceKind.Decoded, "SetBlendParams 0x111005a0: channel 0 only logs; times above 1 become 1; the timed blend is cancelled")]
    public bool SetBlendParams(int channel, float alpha, float inTime = 0f, float outTime = 0f, Name bone = default, bool globalPose = false)
    {
        using var change = TrackAuthoredChange(channel);
        if (channel <= 0 || !IsValidChannel(channel))
        {
            return false;
        }

        PawnAnimChannel c = _channels[channel];
        c.BlendAlpha = alpha;
        c.BlendInTime = inTime >= 1f || float.IsNaN(inTime) ? 1f : inTime;
        c.BlendOutTime = outTime >= 1f || float.IsNaN(outTime) ? 1f : outTime;
        c.StartBone = bone;
        c.bTimedBlend = false;
        c.bGlobalPose = globalPose;
        return true;
    }

    /// <summary>AnimBlendToAlpha: the channel's alpha moves to <paramref name="target"/> over <paramref name="time"/> s.</summary>
    [UcNative("USkeletalMeshInstance::BlendToAlpha", Editor = 0x11102b50)]
    [UcNative("AActor::execAnimBlendToAlpha", Editor = 0x111867c0)]
    [PortOf("nat:Engine.Actor.AnimBlendToAlpha", Spec = "spec/native/anim.md#3-blending-animblendparams-animblendtoalpha-pose-build")]
    public void BlendToAlpha(int channel, float target, float time)
    {
        using var change = TrackAuthoredChange(channel);
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        c.BlendTarget = target;
        c.BlendTimer = time;
        c.bTimedBlend = true;
    }

    /// <summary>StepBlendAlpha: alpha moves toward <paramref name="target"/> by at most <paramref name="step"/>.</summary>
    [UcNative("USkeletalMeshInstance::StepBlendAlpha", Editor = 0x111029a0)]
    [PortOf("eng:ASBasePawn::UpdateMovementAnimation", Spec = "spec/native/ANSWERS-anim.md#a3-channel-helpers-used-by-the-walk-blender-and-executeaction")]
    public void StepBlendAlpha(int channel, float target, float step)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        float d = c.BlendAlpha - target;
        if (d < 0f)
        {
            d = -d;
        }

        if (d > step)
        {
            c.BlendAlpha = c.BlendAlpha < target ? step + c.BlendAlpha : c.BlendAlpha - step;
            return;
        }

        c.BlendAlpha = target;
    }

    /// <summary>The channel's alpha set at once, clamped to [0, 1].</summary>
    [UcNative("USkeletalMeshInstance::SetBlendAlpha", Editor = 0x111027d0)]
    public void SetBlendAlpha(int channel, float alpha)
    {
        if (IsValidChannel(channel))
        {
            _channels[channel].BlendAlpha = alpha < 0f ? 0f : alpha < 1f ? alpha : 1f;
        }
    }

    /// <summary>CopyAnimation: the clip, timing, alpha and timed blend of one channel onto another (ANSWERS-anim.md A3).</summary>
    [UcNative("USkeletalMeshInstance::CopyAnimation", Editor = 0x11102620)]
    [PortOf("eng:ASBasePawn::UpdateMovementAnimation", Spec = "spec/native/ANSWERS-anim.md#a3-channel-helpers-used-by-the-walk-blender-and-executeaction")]
    public void CopyAnimation(int from, int to)
    {
        using var change = TrackAuthoredChange(to, procedural: true);
        if (!IsValidChannel(from) || !IsValidChannel(to))
        {
            return;
        }

        PawnAnimChannel s = _channels[from];
        PawnAnimChannel d = _channels[to];
        d.Sequence = s.Sequence;
        d.Clip = s.Clip;
        d.Frames = s.Frames;
        d.Rate = s.Rate;
        d.BaseRate = s.BaseRate;
        d.Frame = s.Frame;
        d.BlendAlpha = s.BlendAlpha;
        d.AnimLast = s.AnimLast;
        d.bAnimNotify = s.bAnimNotify;
        d.bLoop = s.bLoop;
        d.TweenRate = s.TweenRate;
        d.OldAnimRate = s.OldAnimRate;
        d.bTimedBlend = s.bTimedBlend;
        d.BlendTarget = s.BlendTarget;
        d.BlendTimer = s.BlendTimer;
        d.Serial++;
    }

    /// <summary>EnableChannelNotify: with false the channel fires no notifies and no AnimEnd.</summary>
    [UcNative("USkeletalMeshInstance::EnableChannelNotify", Editor = 0x11101270)]
    [PortOf("nat:Engine.Actor.EnableChannelNotify", Spec = "spec/native/anim.md#4-other-channel-natives")]
    public void EnableChannelNotify(int channel, bool enable)
    {
        using var change = TrackAuthoredChange(channel);
        if (IsValidChannel(channel))
        {
            _channels[channel].bNotifyIgnore = !enable;
        }
    }

    /// <summary>The mirror flag of a channel (the climb step flips channel 0's).</summary>
    public void SetMirror(int channel, bool mirror)
    {
        using var change = TrackAuthoredChange(channel);
        if (IsValidChannel(channel))
        {
            _channels[channel].bMirror = mirror;
        }
    }

    /// <summary>
    /// APawn::ZeroMovementAlpha: channels <paramref name="from"/> to <paramref name="to"/> - 1 step toward alpha 0;
    /// when none had an alpha above 0 each gets rate scale 0 and frame 0.
    /// </summary>
    [UcNative("APawn::ZeroMovementAlpha", Editor = 0x110dd700)]
    [PortOf("eng:ASBasePawn::UpdateMovementAnimation", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    public void ZeroMovementAlpha(int from, int to, float step)
    {
        bool none = true;
        for (int ch = from; ch < to; ch++)
        {
            if (GetBlendAlpha(ch) > 0f)
            {
                none = false;
                StepBlendAlpha(ch, 0f, step);
            }
        }

        if (none)
        {
            for (int ch = from; ch < to; ch++)
            {
                SetChannelRateScale(ch, 0f);
                SetAnimFrame(ch, 0f, unitFlag: false);
            }
        }
    }

    // ---- the per-tick update -----------------------------------------------------------------------------------

    /// <summary>
    /// UpdateAnimation (ANSWERS-anim.md A2): every channel in index order, by this tick's time. Timed blends move,
    /// tweens run into their clip, clips advance in up to 4 passes (a frame of exactly 0 is only nudged to 1e-4, a
    /// negative rate scales with the owner's speed), notifies fire in time order and split the advance, a one-shot
    /// holds at AnimLast and raises AnimEnd (a queued clip starts first), a loop raises AnimEnd when it crosses
    /// AnimLast and wraps to 0 at 1. While <paramref name="inRootMotion"/> answers true a clip that reaches its last
    /// frame stops there for this tick, so the move takes its last root delta, and ends on the next tick.
    /// </summary>
    [UcNative("USkeletalMeshInstance::UpdateAnimation", Editor = 0x111056c0)]
    [PortOf("eng:USkeletalMeshInstance::UpdateAnimation", Spec = "spec/native/ANSWERS-anim.md#a2-updateanimation-0x111056c0-complete")]
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::Tick 0x11205fb0 and AActor::Tick 0x11183b50 update the mesh with the tick's DeltaSeconds before TickAuthoritative")]
    [Provenance(ProvenanceKind.Decoded, "UpdateAnimation 0x111056c0: passes, nudge, notify window, AnimLast end, loop AnimEnd at the AnimLast crossing, wrap to 0")]
    [Provenance(ProvenanceKind.Measured, "goldens traversal.spy-crawl-vent, spy-roll: each frame's advance is that frame's m_dT x rate (1e-8)")]
    public void Advance(float tickDeltaTime, Action<int> animEnd, Func<bool>? inRootMotion = null)
    {
        ArgumentNullException.ThrowIfNull(animEnd);
        if (tickDeltaTime <= 0f)
        {
            return;
        }

        // The IK step runs first, with the update's time (UpdateAnimation 0x111056c0 calls 0x110fbb90 before the channels).
        Aim.Update(tickDeltaTime);
        for (int ch = 0; ch < _channels.Length; ch++)
        {
            UpdateChannel(ch, tickDeltaTime, animEnd, inRootMotion);
        }

        if (TweenCacheAfterAdvance)
        {
            UpdateTweenCache();
        }
    }

    /// <summary>
    /// When the pose is built for a pawn that is not drawn (the local merc in first person): right after the advance,
    /// as the bone lookups of its attached weapon do in the original, instead of after the tick (A4).
    /// </summary>
    public bool TweenCacheAfterAdvance { get; set; }

    /// <summary>
    /// The pose build's tween cache (ANSWERS-anim.md A4), once per frame: after the tick for a drawn pawn, right after
    /// the advance for one that is not (<see cref="TweenCacheAfterAdvance"/>). For each drawn channel that tweens in (mirrored ones in the
    /// mirror pass) the tween weight follows 1 + frame x N, restarting at 0 on a new tween.
    /// </summary>
    [UcNative("USkeletalMeshInstance::BuildPose", Editor = 0x11109a50)]
    [PortOf("eng:USkeletalMeshInstance::UpdateAnimation", Spec = "spec/native/ANSWERS-anim.md#a4-tween-cache-pose-build-0x11109a50")]
    [Provenance(ProvenanceKind.Decoded, "pose build 0x11109a50: t = 1 - frame / cached frame, weight += (1 - weight) t, reset to -1/N on a new sequence or t outside [0, 1]")]
    public void UpdateTweenCache()
    {
        bool cached = _poseCached;
        for (int ch = 0; ch < _channels.Length; ch++)
        {
            PawnAnimChannel c = _channels[ch];
            float weight = ch == 0 ? 1f : c.BlendAlpha;
            if (c.Sequence.IsNone || c.Clip is null || !(weight > 0f))
            {
                continue;
            }

            if (c.Frame >= 0f || !cached)
            {
                continue;
            }

            float t = 1f - (c.Frame / c.TweenCacheFrame);
            if (c.TweenCacheSequence != c.Sequence || t < 0f || t > 1f || float.IsNaN(t))
            {
                c.TweenCacheFrame = -1f / c.Frames;
                c.TweenCacheSequence = c.Sequence;
                c.TweenWeight = 0f;
            }
            else
            {
                c.TweenCacheFrame = c.Frame;
                c.TweenWeight = ((1f - c.TweenWeight) * t) + c.TweenWeight;
            }
        }

        _poseCached = true;
    }

    /// <summary>Restores a channel record (difftests start from a recorded channel state).</summary>
    public void Restore(int channel, PawnClip? clip, float frame, float rate, float alpha, float tweenWeight, float tweenRate,
        bool loop, bool notifyIgnore)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        PawnAnimChannel c = _channels[channel];
        if (clip is null)
        {
            c.Clear();
        }
        else
        {
            SetClip(c, clip);
            c.Serial++;
            c.BaseRate = clip.Fps / clip.Frames;
            c.AnimLast = clip.Frames <= 1 ? 0f : 1f - (1f / clip.Frames);
            c.bAnimNotify = clip.Notifies.Count > 0;
        }

        c.Frame = frame;
        c.Rate = rate;
        c.OldAnimRate = rate;
        c.BlendAlpha = alpha;
        c.TweenWeight = tweenWeight;
        c.TweenRate = tweenRate;
        c.bLoop = loop;
        c.bNotifyIgnore = notifyIgnore;
        c.TweenCacheFrame = frame;
        c.TweenCacheSequence = c.Sequence;
        _poseCached = true;
    }

    private void UpdateChannel(int ch, float dt, Action<int> animEnd, Func<bool>? inRootMotion)
    {
        PawnAnimChannel c = _channels[ch];
        if (c.bTimedBlend)
        {
            float f = dt / c.BlendTimer;
            if (!float.IsNaN(f) && f >= 1f)
            {
                f = 1f;
            }

            c.BlendAlpha = ((c.BlendTarget - c.BlendAlpha) * f) + c.BlendAlpha;
            float left = c.BlendTimer - dt;
            if (left < 0f)
            {
                left = 0f;
            }

            c.BlendTimer = left;
            if (left == 0f)
            {
                c.bTimedBlend = false;
                if (c.BlendTarget == 0f)
                {
                    c.Rate = 0f;
                }
            }
        }

        ClearFired(c);
        int passes = 0;
        while (c.bPlaying && dt > 0f && ++passes <= MaxPasses)
        {
            float old = c.Frame;
            if (old < 0f)
            {
                float tweened = (dt * c.TweenRate) + c.Frame;
                c.Frame = tweened;
                if (tweened < 0f)
                {
                    return;
                }

                c.Frame = 0f;
                dt = dt * tweened / (tweened - old);
                if (c.Rate == 0f)
                {
                    NotifyChannel = ch;
                    if (!c.bNotifyIgnore)
                    {
                        animEnd(ch);
                    }
                }

                continue;
            }

            if (old == 0f)
            {
                c.Frame = StartFrame;
            }
            else
            {
                float step = c.Rate < 0f ? SvmMath.Max(SpeedRateFloor, -(OwnerSpeed() * c.Rate)) * dt : dt * c.Rate;
                c.Frame = step + c.Frame;
            }

            if (FireNotify(ch, c, old, ref dt))
            {
                continue;
            }

            if (c.Frame < c.AnimLast)
            {
                return;
            }

            if (inRootMotion is not null && old < c.AnimLast && inRootMotion())
            {
                c.Frame = c.AnimLast;
                dt = 0f;
                continue;
            }

            if (!c.bLoop)
            {
                float reached = c.Frame;
                c.Frame = c.AnimLast;
                NotifyChannel = ch;
                dt = (reached - c.AnimLast) * dt / (reached - old);
                if (c.bQueued)
                {
                    StartQueued(ch, c);
                }

                if (c.bNotifyIgnore || c.Rate <= 0f)
                {
                    c.Rate = 0f;
                    continue;
                }

                c.Rate = 0f;
                animEnd(ch);
                continue;
            }

            if (c.Frame >= 1f)
            {
                float reached = c.Frame;
                c.Frame = 0f;
                dt = (reached - 1f) * dt / (reached - old);
            }
            else
            {
                dt = 0f;
            }

            if (c.AnimLast <= old)
            {
                continue;
            }

            NotifyChannel = ch;
            if (!c.bNotifyIgnore)
            {
                animEnd(ch);
            }
        }
    }

    /// <summary>The earliest notify in this pass's window fires: the frame stops at its time, the rest of the time carries.</summary>
    private bool FireNotify(int ch, PawnAnimChannel c, float old, ref float dt)
    {
        if (!c.bAnimNotify || c.bNotifyIgnore || c.Clip is not { Notifies.Count: > 0 } clip || Owner is null)
        {
            return false;
        }

        IReadOnlyList<PawnClipNotify> notifies = clip.Notifies;
        int best = -1;
        float bestDistance = 100000f;
        for (int i = 0; i < notifies.Count && i < _fired.Length; i++)
        {
            float t = notifies[i].Time;
            bool inside;
            float distance;
            if (!c.bBackward)
            {
                inside = old < t && t <= c.Frame;
                distance = t - old;
            }
            else
            {
                float m = c.AnimLast - t;
                inside = old + BackwardNotifyMargin < m && m <= c.Frame;
                distance = m - old;
            }

            if (inside && !_fired[i] && (best == -1 || distance < bestDistance))
            {
                best = i;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            return false;
        }

        float at = c.bBackward ? c.AnimLast - notifies[best].Time : notifies[best].Time;
        float into = c.bBackward ? (c.Frame - c.AnimLast) + notifies[best].Time : c.Frame - notifies[best].Time;
        dt = into * dt / (c.Frame - old);
        c.Frame = at;
        _fired[best] = true;
        NotifyChannel = ch;
        PawnAnimNotifies.Fire(Owner, ch, notifies[best].Notify);
        return true;
    }

    private void StartQueued(int ch, PawnAnimChannel c)
    {
        if (c.QueuedClip is { } next)
        {
            PlayAnim(ch, next, c.QueuedRate, c.QueuedTween, c.QueuedLoop, c.QueuedBackward);
            c.bQueued = false;
            c.QueuedClip = null;
            return;
        }

        if (c.QueuedTween > 0f)
        {
            BlendToAlpha(ch, 0f, c.QueuedTween);
        }
        else
        {
            SetBlendParams(ch, 0f);
        }
    }

    /// <summary>A one-shot interrupted by another clip fires its remaining script notifies now (anim.md rule 1.4).</summary>
    private void FireInterruptedNotifies(int ch, PawnAnimChannel c, PawnClip next)
    {
        if (c.bLoop || c.Sequence == next.Name || !c.bAnimNotify || c.bNotifyIgnore || !c.bPlaying ||
            c.Clip is not { Notifies.Count: > 0 } clip || Owner is null)
        {
            return;
        }

        float frame = c.Frame;
        foreach (PawnClipNotify n in clip.Notifies)
        {
            bool ahead = c.bBackward ? n.Time < frame : frame < n.Time;
            if (ahead && n.Notify is UAnimNotify_Script)
            {
                NotifyChannel = ch;
                c.Frame = n.Time;
                PawnAnimNotifies.Fire(Owner, ch, n.Notify);
            }
        }
    }

    private void ClearFired(PawnAnimChannel c)
    {
        int count = c.Clip?.Notifies.Count ?? 0;
        if (count > _fired.Length)
        {
            _fired = new bool[count];
        }

        Array.Clear(_fired);
    }

    private float OwnerSpeed() => Owner?.Velocity.Size() ?? 0f;

    private static void SetClip(PawnAnimChannel c, PawnClip clip)
    {
        c.Sequence = clip.Name;
        c.Clip = clip;
        c.Frames = clip.Frames;
    }

    /// <summary>A one-frame clip: a tween to its pose, over TweenTime or at 10 per second.</summary>
    private static void PlayPose(PawnAnimChannel c, float tweenTime, float inv)
    {
        c.bAnimNotify = false;
        c.OldAnimRate = 0f;
        c.Rate = 0f;
        c.TweenRate = tweenTime > 0f ? 1f / tweenTime : PoseTweenRate;
        c.Frame = inv * -1f;
    }

    private static void ContinueAtFrame(PawnAnimChannel c, bool backwardChanged)
    {
        if (backwardChanged)
        {
            c.Frame = c.AnimLast - c.Frame;
        }

        c.OldAnimRate = c.Rate;
    }

    /// <summary>Tween -1: the old rate, or for a speed-scaled clip a rate from the owner's speed, else over 0.025 N.</summary>
    private void TweenFromOldRate(PawnAnimChannel c, float n)
    {
        if (c.OldAnimRate > 0f)
        {
            c.TweenRate = c.OldAnimRate;
            c.OldAnimRate = c.Rate;
            return;
        }

        if (c.OldAnimRate < 0f)
        {
            c.TweenRate = SvmMath.Max(c.Rate * 0.5f, OwnerSpeed() * c.OldAnimRate * -1f);
            c.OldAnimRate = c.Rate;
            return;
        }

        c.OldAnimRate = c.Rate;
        c.TweenRate = 1f / (n * DefaultTweenSecondsPerFrame);
    }
}
