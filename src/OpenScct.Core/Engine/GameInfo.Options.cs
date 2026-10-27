using System;
using System.Globalization;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// The URL option grammar (<c>?Key=Value?Key2=Value2</c>) and the UnrealScript string conversions it relies on.
/// GameInfo and SMatchInfo carry identical copies of the four option helpers; both use this one implementation.
/// </summary>
public static class UcOptions
{
    /// <summary>
    /// Takes the first <c>?Pair</c> off <paramref name="Options"/>: false unless the text starts with '?'; otherwise
    /// <paramref name="Result"/> is the text up to the next '?' (or the end) and the pair is removed.
    /// </summary>
    public static bool GrabOption(ref string Options, ref string Result)
    {
        Options ??= string.Empty;
        if (Options.Length == 0 || Options[0] != '?')
        {
            return false;
        }

        string rest = Options[1..];
        int next = rest.IndexOf('?', StringComparison.Ordinal);
        Result = next >= 0 ? rest[..next] : rest;
        Options = next >= 0 ? rest[next..] : string.Empty;
        return true;
    }

    /// <summary>Splits a pair at its first '=': key before, value after. Without '=' the value is empty.</summary>
    public static void GetKeyValue(string Pair, ref string Key, ref string Value)
    {
        Pair ??= string.Empty;
        int eq = Pair.IndexOf('=', StringComparison.Ordinal);
        if (eq >= 0)
        {
            Key = Pair[..eq];
            Value = Pair[(eq + 1)..];
        }
        else
        {
            Key = Pair;
            Value = string.Empty;
        }
    }

    /// <summary>The value of the first pair whose key equals <paramref name="InKey"/> ignoring case, or "".</summary>
    public static string ParseOption(string Options, string InKey)
    {
        string rest = Options ?? string.Empty;
        string pair = string.Empty;
        string key = string.Empty;
        string value = string.Empty;
        while (GrabOption(ref rest, ref pair))
        {
            GetKeyValue(pair, ref key, ref value);
            if (UcStrings.EqualsIgnoreCase(key, InKey))
            {
                return value;
            }
        }

        return string.Empty;
    }

    /// <summary><c>int(value)</c> when the option has a non-empty value, else <paramref name="CurrentValue"/>.</summary>
    public static int GetIntOption(string Options, string ParseString, int CurrentValue)
    {
        string inOpt = ParseOption(Options, ParseString);
        return inOpt.Length != 0 ? UcStrings.Atoi(inOpt) : CurrentValue;
    }
}

/// <summary>UnrealScript string operations used by the game rules, with the engine's conversion rules.</summary>
public static class UcStrings
{
    /// <summary>The <c>~=</c> operator: equal ignoring case.</summary>
    public static bool EqualsIgnoreCase(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>int(string)</c>: the C runtime atoi. Leading white space, an optional sign, then digits up to the first
    /// other character; 0 when there are none. Values beyond the int range clamp.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 string to int conversion (appAtoi); overflow clamping follows strtol")]
    public static int Atoi(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return 0;
        }

        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r')))
        {
            i++;
        }

        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            negative = s[i] == '-';
            i++;
        }

        long value = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            value = (value * 10) + (s[i] - '0');
            if (value > (long)int.MaxValue + 1)
            {
                value = (long)int.MaxValue + 1;
            }

            i++;
        }

        if (negative)
        {
            value = -value;
        }

        return value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;
    }

    /// <summary>
    /// <c>float(string)</c>: the C runtime atof. Leading white space, then the longest decimal number (sign,
    /// digits, a point, an exponent); 0 when there is none.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 string to float conversion (appAtof)")]
    public static float Atof(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return 0f;
        }

        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r')))
        {
            i++;
        }

        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            i++;
        }

        int digits = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            i++;
            digits++;
        }

        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return 0f;
        }

        int end = i;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-'))
            {
                j++;
            }

            int expDigits = 0;
            while (j < s.Length && s[j] >= '0' && s[j] <= '9')
            {
                j++;
                expDigits++;
            }

            if (expDigits > 0)
            {
                end = j;
            }
        }

        return float.Parse(s.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>bool(string)</c>: "True" is true and "False" false (any case); any other text is true when its integer
    /// value is not zero.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 string to bool conversion (execStringToBool)")]
    public static bool ToBool(string? s)
    {
        if (EqualsIgnoreCase(s, "True"))
        {
            return true;
        }

        if (EqualsIgnoreCase(s, "False"))
        {
            return false;
        }

        return Atoi(s) != 0;
    }

    /// <summary><c>Left(S, N)</c>: the first N characters (all of them when shorter, none when N is 0 or less).</summary>
    public static string Left(string? s, int n)
    {
        s ??= string.Empty;
        return n <= 0 ? string.Empty : n >= s.Length ? s : s[..n];
    }

    /// <summary>A bool as UnrealScript prints it: "True" or "False".</summary>
    public static string BoolText(bool b) => b ? "True" : "False";
}

public partial class GameInfo
{
    [Uc("Engine.GameInfo.GrabOption", Sha1 = "e189f18613e4702a6ca36fc845fce0aade4adef9")]
    [PortOf("uc:Engine.GameInfo:GrabOption", Spec = "spec/game/match-options.md#rules")]
    public bool GrabOption(ref string Options, ref string Result) => UcOptions.GrabOption(ref Options, ref Result);

    [Uc("Engine.GameInfo.GetKeyValue", Sha1 = "f91f186778cd1714d7498976b191b8c360c38ba7")]
    [PortOf("uc:Engine.GameInfo:GetKeyValue", Spec = "spec/game/match-options.md#rules")]
    public void GetKeyValue(string Pair, ref string Key, ref string Value) => UcOptions.GetKeyValue(Pair, ref Key, ref Value);

    [Uc("Engine.GameInfo.ParseOption", Sha1 = "f931ae0caaa7ec4b27f6f2257aacb50f723de46c")]
    [PortOf("uc:Engine.GameInfo:ParseOption", Spec = "spec/game/match-options.md#rules")]
    public string ParseOption(string Options, string InKey) => UcOptions.ParseOption(Options, InKey);

    [Uc("Engine.GameInfo.GetIntOption", Sha1 = "b7280ff123b04d676fb31b101e9d0d76a6c78b26")]
    [PortOf("uc:Engine.GameInfo:GetIntOption", Spec = "spec/game/match-options.md#rules")]
    public int GetIntOption(string Options, string ParseString, int CurrentValue) =>
        UcOptions.GetIntOption(Options, ParseString, CurrentValue);
}
