using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using OpenScct.Content.Json;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Content.Tests;

/// <summary>
/// Runs the pipeline's own Python code (content/svmbuild/canonical.py) on the same inputs as the C# reader and
/// compares: map hashes, float32 text, decimal parsing and canonical JSON. Skipped when Python with numpy is not
/// installed, unless SVM_REQUIRE_PYTHON is set.
/// </summary>
public sealed class PythonCrossCheckTests
{
    private const string Script = """
        import json, struct, sys
        from pathlib import Path
        sys.path.insert(0, sys.argv[1])
        from svmbuild import canonical

        req = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
        out = {"maps": {}, "format": [], "parse": [], "canonical": []}
        for root in req["maps"]:
            files, hashes = canonical.hash_map(Path(root))
            out["maps"][root] = {"files": files, "hashes": hashes}
        for b in req["formatBits"]:
            out["format"].append(canonical.format_f32(struct.unpack("<f", struct.pack("<I", b))[0]))
        for t in req["parseTokens"]:
            try:
                out["parse"].append("%08x" % canonical.f32_bits(canonical.parse_f32(t)))
            except ValueError:
                out["parse"].append("overflow")
        for doc in req["canonicalInputs"]:
            try:
                out["canonical"].append(canonical.canonical_bytes(doc.encode("utf-8")).hex())
            except ValueError as e:
                out["canonical"].append("error")
        Path(sys.argv[3]).write_text(json.dumps(out), encoding="utf-8")
        """;

    [Fact]
    public void PipelineAndReaderAgree()
    {
        string? python = FindPython();
        if (python is null)
        {
            if (Environment.GetEnvironmentVariable("SVM_REQUIRE_PYTHON") is { Length: > 0 })
            {
                Assert.Fail("SVM_REQUIRE_PYTHON is set but no Python with numpy was found.");
            }

            Assert.Skip("Python with numpy is not installed.");
        }

        using var scratch = new ScratchDir();
        var random = new SimRandom(20261023, stream: 1, purpose: 1);

        // A map folder with awkward names and content next to the TestBox.
        string map = scratch.File("map");
        TestPaths.CopyTree(TestPaths.TestBox, map);
        Directory.CreateDirectory(Path.Combine(map, "sim", "nested"));
        Directory.CreateDirectory(Path.Combine(map, "ui"));
        File.WriteAllText(Path.Combine(map, "sim", "nested", "Ωmega.json"), RandomDocument(random, 4));
        File.WriteAllText(Path.Combine(map, "sim", "～.json"), RandomDocument(random, 3));
        File.WriteAllText(Path.Combine(map, "sim", "B.json"), "  [1.0, -0, 1e3]\r\n");
        File.WriteAllBytes(Path.Combine(map, "sim", "raw.dat"), RandomBytes(random, 333));
        File.WriteAllText(Path.Combine(map, "ui", "menu.json"), "{\"b\": 1, \"a\": 2}");

        var formatBits = new List<uint>();
        while (formatBits.Count < 20_000)
        {
            uint bits = random.NextUInt();
            if (float.IsFinite(BitConverter.UInt32BitsToSingle(bits)))
            {
                formatBits.Add(bits);
            }
        }

        formatBits.AddRange([0u, 0x80000000u, 1u, 0x80000001u, 0x007fffffu, 0x00800000u, 0x7f7fffffu, 0xff7fffffu, 0x3f800000u, 0x4b800001u]);
        List<string> parseTokens = ParseTokens(random);
        var documents = new List<string>();
        for (int i = 0; i < 300; i++)
        {
            documents.Add(RandomDocument(random, 1 + (i % 5)));
        }

        var request = new SvJsonObject();
        request.Add("maps", new SvJsonArray([SvJsonNode.String(TestPaths.TestBox), SvJsonNode.String(map)]));
        request.Add("formatBits", new SvJsonArray(formatBits.ConvertAll(b => SvJsonNode.Integer(b))));
        request.Add("parseTokens", new SvJsonArray(parseTokens.ConvertAll(SvJsonNode.String)));
        request.Add("canonicalInputs", new SvJsonArray(documents.ConvertAll(SvJsonNode.String)));
        string requestPath = scratch.File("request.json");
        string responsePath = scratch.File("response.json");
        string scriptDir = Path.Combine(scratch.Path, "script");
        Directory.CreateDirectory(scriptDir);
        string scriptPath = Path.Combine(scriptDir, "crosscheck.py");
        File.WriteAllBytes(requestPath, CanonicalJson.ToCanonicalBytes(request));
        File.WriteAllText(scriptPath, Script.Replace("\r\n", "\n", StringComparison.Ordinal));
        Run(python, [scriptPath, TestPaths.ContentPipeline, requestPath, responsePath], scratch.Path);
        var response = (SvJsonObject)SvJson.Parse(File.ReadAllBytes(responsePath));

        // Map hashes: every file digest and size, and every group hash.
        var maps = (SvJsonObject)response["maps"]!;
        foreach (string root in new[] { TestPaths.TestBox, map })
        {
            var expected = (SvJsonObject)maps[root]!;
            SvMapHashes actual = SvMapHash.HashMap(root);
            var files = (SvJsonObject)expected["files"]!;
            Assert.Equal(files.Count, actual.Files.Count);
            foreach (SvMapFileDigest f in actual.Files)
            {
                var entry = (SvJsonObject)files[f.Path]!;
                Assert.Equal(((SvJsonString)entry["sha256"]!).Value, f.Sha256);
                Assert.Equal(((SvJsonNumber)entry["size"]!).IntegerText, f.Size.ToString(CultureInfo.InvariantCulture));
            }

            var hashes = (SvJsonObject)expected["hashes"]!;
            Assert.Equal(hashes.Count, actual.Groups.Count);
            foreach (KeyValuePair<string, SvJsonNode> g in hashes)
            {
                Assert.Equal(((SvJsonString)g.Value).Value, actual.Groups[g.Key]);
            }
        }

        var formats = (SvJsonArray)response["format"]!;
        for (int i = 0; i < formatBits.Count; i++)
        {
            Assert.Equal(((SvJsonString)formats[i]).Value, Float32Text.Format(BitConverter.UInt32BitsToSingle(formatBits[i])));
        }

        var parses = (SvJsonArray)response["parse"]!;
        for (int i = 0; i < parseTokens.Count; i++)
        {
            string actual;
            try
            {
                actual = BitConverter.SingleToUInt32Bits(Float32Text.Parse(parseTokens[i])).ToString("x8", CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                actual = "overflow";
            }

            Assert.True(((SvJsonString)parses[i]).Value == actual, $"token {parseTokens[i]}: python {((SvJsonString)parses[i]).Value}, C# {actual}");
        }

        var canonical = (SvJsonArray)response["canonical"]!;
        for (int i = 0; i < documents.Count; i++)
        {
            string actual;
            try
            {
                actual = Convert.ToHexStringLower(CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes(documents[i])));
            }
            catch (SvMapFormatException)
            {
                actual = "error";
            }

            Assert.True(((SvJsonString)canonical[i]).Value == actual, $"document {i}: {documents[i]}");
        }
    }

    // ---- inputs --------------------------------------------------------------------------------------------

    private static List<string> ParseTokens(SimRandom r)
    {
        var tokens = new List<string>
        {
            "340282356779733661637539395458142568448.0",
            "340282356779733661637539395458142568447.9",
            "-340282356779733661637539395458142568448.0",
            "1e39", "1e-46", "7e-46", "0.7e-45", "1.4e-45", "2.1e-45",
        };
        while (tokens.Count < 6_000)
        {
            uint bits = r.NextUInt() & 0x7FFFFFFFu;
            if (bits >= 0x7F7FFFFFu)
            {
                continue;
            }

            // Exact halfway points (ties go to even) and points just beyond them.
            double low = BitConverter.UInt32BitsToSingle(bits);
            double high = BitConverter.UInt32BitsToSingle(bits + 1);
            string mid = ExactDecimal((low + high) / 2);
            string sign = r.Rand(2) == 0 ? "" : "-";
            tokens.Add(sign + mid);
            tokens.Add(sign + mid + "0000000000000000000001");
            tokens.Add(sign + RandomDecimal(r));
        }

        return tokens;
    }

    /// <summary>The exact decimal expansion of a finite double.</summary>
    private static string ExactDecimal(double d)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        int exp = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0)
        {
            exp = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exp -= 1075;
        string sign = bits < 0 ? "-" : "";
        if (exp >= 0)
        {
            return sign + (new BigInteger(mantissa) << exp).ToString(CultureInfo.InvariantCulture) + ".0";
        }

        string digits = (mantissa * BigInteger.Pow(5, -exp)).ToString(CultureInfo.InvariantCulture).PadLeft(-exp + 1, '0');
        return sign + digits[..^(-exp)] + "." + digits[^(-exp)..];
    }

    private static string RandomDecimal(SimRandom r)
    {
        var sb = new StringBuilder();
        int intDigits = 1 + r.Rand(3);
        sb.Append((char)('1' + r.Rand(9)));
        for (int i = 1; i < intDigits; i++)
        {
            sb.Append((char)('0' + r.Rand(10)));
        }

        sb.Append('.');
        int fracDigits = 1 + r.Rand(30);
        for (int i = 0; i < fracDigits; i++)
        {
            sb.Append((char)('0' + r.Rand(10)));
        }

        int exp = r.Rand(90) - 52;
        if (r.Rand(4) != 0)
        {
            sb.Append(r.Rand(2) == 0 ? 'e' : 'E').Append(exp.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string RandomDocument(SimRandom r, int depth)
    {
        var sb = new StringBuilder();
        Space(sb, r);
        WriteValue(sb, r, depth);
        Space(sb, r);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, SimRandom r, int depth)
    {
        int kind = depth > 0 ? r.Rand(10) : 2 + r.Rand(8);
        switch (kind)
        {
            case 0:
            {
                sb.Append('{');
                int n = r.Rand(5);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < n; i++)
                {
                    string key = RandomKey(r);
                    if (!keys.Add(key))
                    {
                        continue;
                    }

                    if (keys.Count > 1)
                    {
                        sb.Append(',');
                    }

                    Space(sb, r);
                    WriteString(sb, r, key);
                    Space(sb, r);
                    sb.Append(':');
                    Space(sb, r);
                    WriteValue(sb, r, depth - 1);
                    Space(sb, r);
                }

                sb.Append('}');
                break;
            }

            case 1:
            {
                sb.Append('[');
                int n = r.Rand(5);
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    Space(sb, r);
                    WriteValue(sb, r, depth - 1);
                    Space(sb, r);
                }

                sb.Append(']');
                break;
            }

            case 2:
            case 3:
                WriteString(sb, r, RandomText(r));
                break;
            case 4:
            case 5:
            case 6:
                sb.Append(r.Rand(2) == 0 ? "-" : "").Append(RandomFloatToken(r));
                break;
            case 7:
                sb.Append(RandomIntegerToken(r));
                break;
            default:
                sb.Append(r.Rand(3) switch { 0 => "true", 1 => "false", _ => "null" });
                break;
        }
    }

    private static string RandomFloatToken(SimRandom r)
    {
        switch (r.Rand(5))
        {
            case 0:
                return "0.0";
            case 1:
                return Float32Text.Format(BitConverter.UInt32BitsToSingle(r.NextUInt() & 0x7F7FFFFFu));
            case 2:
                return (1 + r.Rand(9)).ToString(CultureInfo.InvariantCulture) + "e" + (r.Rand(70) - 40).ToString(CultureInfo.InvariantCulture);
            default:
            {
                string d = RandomDecimal(r);
                // Keep the magnitude inside binary32 so both sides accept it.
                int e = d.IndexOfAny(['e', 'E']);
                return e >= 0 && int.Parse(d[(e + 1)..], CultureInfo.InvariantCulture) > 34 ? d[..e] : d;
            }
        }
    }

    private static string RandomIntegerToken(SimRandom r) => r.Rand(4) switch
    {
        0 => "0",
        1 => "-0",
        2 => ((int)r.NextUInt()).ToString(CultureInfo.InvariantCulture),
        _ => "-" + (1 + r.Rand(9)).ToString(CultureInfo.InvariantCulture) + new string('7', r.Rand(30)),
    };

    private static string RandomKey(SimRandom r)
    {
        var sb = new StringBuilder();
        int n = r.Rand(6);
        for (int i = 0; i < n; i++)
        {
            sb.Append((char)r.Rand(0x80));
        }

        return sb.ToString();
    }

    private static string RandomText(SimRandom r)
    {
        var sb = new StringBuilder();
        int n = r.Rand(12);
        for (int i = 0; i < n; i++)
        {
            int cp = r.Rand(7) switch
            {
                0 => r.Rand(0x20),
                1 => "\"\\/\u007f"[r.Rand(4)],
                2 => 0x80 + r.Rand(0x780),
                3 => 0x800 + r.Rand(0xD000),
                4 => 0xE000 + r.Rand(0x2000),
                5 => 0x10000 + r.Rand(0x100000),
                _ => 0x20 + r.Rand(0x5F),
            };
            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }

    /// <summary>Writes a JSON string, choosing raw characters or escapes at random (never a bare control character).</summary>
    private static void WriteString(StringBuilder sb, SimRandom r, string value)
    {
        sb.Append('"');
        Span<char> units = stackalloc char[2];
        foreach (Rune rune in value.EnumerateRunes())
        {
            int cp = rune.Value;
            bool mustEscape = cp < 0x20 || cp == '"' || cp == '\\';
            if (!mustEscape && r.Rand(3) != 0)
            {
                sb.Append(rune.ToString());
                continue;
            }

            string? shortForm = cp switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\t' => "\\t",
                '/' => "\\/",
                _ => null,
            };
            if (shortForm is not null && r.Rand(2) == 0)
            {
                sb.Append(shortForm);
                continue;
            }

            int count = rune.EncodeToUtf16(units);
            for (int i = 0; i < count; i++)
            {
                sb.Append("\\u").Append(((int)units[i]).ToString(r.Rand(2) == 0 ? "x4" : "X4", CultureInfo.InvariantCulture));
            }
        }

        sb.Append('"');
    }

    private static void Space(StringBuilder sb, SimRandom r)
    {
        int n = r.Rand(4) == 0 ? r.Rand(3) : 0;
        for (int i = 0; i < n; i++)
        {
            sb.Append(" \t\r\n"[r.Rand(4)]);
        }
    }

    private static byte[] RandomBytes(SimRandom r, int n)
    {
        var bytes = new byte[n];
        for (int i = 0; i < n; i++)
        {
            bytes[i] = (byte)r.NextUInt();
        }

        return bytes;
    }

    // ---- python ---------------------------------------------------------------------------------------------

    internal static string? FindPython()
    {
        foreach (string candidate in new[] { "python", "python3" })
        {
            try
            {
                using Process? p = Process.Start(new ProcessStartInfo(candidate, ["-c", "import numpy"])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                if (p is not null && p.WaitForExit(60_000) && p.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return null;
    }

    internal static void Run(string python, string[] args, string workingDirectory)
    {
        var info = new ProcessStartInfo(python, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        using Process p = Process.Start(info)!;
        System.Threading.Tasks.Task<string> stderr = p.StandardError.ReadToEndAsync();
        System.Threading.Tasks.Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Assert.True(p.WaitForExit(300_000), "the Python cross-check timed out");
        Assert.True(p.ExitCode == 0, $"python failed ({p.ExitCode}): {stderr.Result}{stdout.Result}");
    }
}
