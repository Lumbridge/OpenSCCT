using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>
/// The loudest output of the audio buses since the last <see cref="Reset"/>: Master and every bus of the sound system
/// (SvmWorld, SvmFlat, SvmMusic, SvmReverbN), sampled from the audio server's peak meters whenever
/// <see cref="Sample"/> runs (once a frame). A bus that never played reads <see cref="Silence"/>.
/// </summary>
public sealed class AudioPeaks
{
    /// <summary>The audio server's floor for a silent bus, in dB.</summary>
    public const float Silence = -200f;

    /// <summary>A peak above this counts as sound (the voices start far louder; the floor is -200 dB).</summary>
    public const float AudibleDb = -90f;

    /// <summary>
    /// The Master volume of a quiet run (tests, unattended runs): the mix still reaches the output and its meter, about
    /// 80 dB below what a player would hear.
    /// </summary>
    public const float QuietMasterDb = -80f;

    /// <summary>
    /// Turns Master down to <see cref="QuietMasterDb"/>; returns the volume it had. The settings set Master again when
    /// they are applied, so a quiet run calls it every frame.
    /// </summary>
    public static float Quiet()
    {
        float before = AudioServer.GetBusVolumeDb(0);
        if (before != QuietMasterDb)
        {
            AudioServer.SetBusVolumeDb(0, QuietMasterDb);
        }

        return before;
    }

    private readonly Dictionary<string, float> _max = new(StringComparer.Ordinal);

    private const int NamesEvery = 600;

    private string[] _names = [];
    private int _sinceNames;

    /// <summary>Times <see cref="Sample"/> ran.</summary>
    public int Samples { get; private set; }

    /// <summary>The loudest peak of each bus seen so far (dB, the louder channel).</summary>
    public IReadOnlyDictionary<string, float> Max => _max;

    /// <summary>Reads every bus's peak meter now.</summary>
    public void Sample()
    {
        Samples++;
        int count = AudioServer.BusCount;
        if (_names.Length != count || ++_sinceNames >= NamesEvery)
        {
            // Bus names are read again only when the count changes or every 600 samples (a name read makes a string).
            Array.Resize(ref _names, count);
            for (int i = 0; i < count; i++)
            {
                _names[i] = AudioServer.GetBusName(i);
            }

            _sinceNames = 0;
        }

        for (int i = 0; i < count; i++)
        {
            string name = _names[i];
            if (i != 0 && !name.StartsWith("Svm", StringComparison.Ordinal))
            {
                continue;
            }

            float peak = Silence;
            for (int c = 0; c < AudioServer.GetBusChannels(i); c++)
            {
                peak = Math.Max(peak, Math.Max(AudioServer.GetBusPeakVolumeLeftDb(i, c), AudioServer.GetBusPeakVolumeRightDb(i, c)));
            }

            _max[name] = _max.TryGetValue(name, out float best) ? Math.Max(best, peak) : peak;
        }
    }

    /// <summary>The loudest peak of <paramref name="bus"/>, or <see cref="Silence"/>.</summary>
    public float Of(string bus) => _max.TryGetValue(bus, out float v) ? v : Silence;

    /// <summary>The loudest peak of the sound system's buses (all but Master), or <see cref="Silence"/>.</summary>
    public float LoudestSvm()
    {
        float best = Silence;
        foreach (KeyValuePair<string, float> kv in _max)
        {
            if (kv.Key.StartsWith("Svm", StringComparison.Ordinal))
            {
                best = Math.Max(best, kv.Value);
            }
        }

        return best;
    }

    public void Reset()
    {
        _max.Clear();
        Samples = 0;
    }

    /// <summary>One line: bus=peak for every bus that was sampled.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, float> kv in _max)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(kv.Key).Append('=').Append(kv.Value <= Silence ? "silent" : kv.Value.ToString("F1", CultureInfo.InvariantCulture) + "dB");
        }

        return sb.Length == 0 ? "no buses" : sb.ToString();
    }
}
