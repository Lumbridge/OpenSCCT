using System;
using System.Globalization;
using System.Text;

namespace OpenScct.Content.Json;

/// <summary>
/// The SVMap float32 text form (conventions.md): the shortest digits that read back as the same binary32, laid out
/// positionally with at least one digit on each side of the point (<c>0.1</c>, <c>100.0</c>, <c>-0.0</c>,
/// <c>0.0000001</c>). Parsing rounds the decimal directly to binary32 (round to nearest, ties to even).
/// </summary>
public static class Float32Text
{
    /// <summary>Formats a finite binary32 value.</summary>
    public static string Format(float value)
    {
        var sb = new StringBuilder(16);
        Append(sb, value);
        return sb.ToString();
    }

    /// <summary>Appends the text form of a finite binary32 value.</summary>
    public static void Append(StringBuilder sb, float value)
    {
        ArgumentNullException.ThrowIfNull(sb);
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "SVMap floats must be finite.");
        }

        if (value == 0f)
        {
            sb.Append(float.IsNegative(value) ? "-0.0" : "0.0");
            return;
        }

        // "R" gives the shortest round-trip digits (nearest when two qualify); only the layout changes below.
        Span<char> text = stackalloc char[48];
        if (!value.TryFormat(text, out int written, "R", CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("Float formatting overflowed its buffer.");
        }

        ReadOnlySpan<char> s = text[..written];
        bool negative = s[0] == '-';
        if (negative)
        {
            s = s[1..];
        }

        int exponent = 0;
        int e = s.IndexOfAny('E', 'e');
        if (e >= 0)
        {
            exponent = int.Parse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            s = s[..e];
        }

        Span<char> digits = stackalloc char[48];
        int count = 0;
        int dot = s.IndexOf('.');
        int point = (dot >= 0 ? dot : s.Length) + exponent;
        foreach (char c in s)
        {
            if (c != '.')
            {
                digits[count++] = c;
            }
        }

        int lead = 0;
        while (lead < count - 1 && digits[lead] == '0')
        {
            lead++;
            point--;
        }

        ReadOnlySpan<char> d = digits[lead..count];
        while (d.Length > 1 && d[^1] == '0')
        {
            d = d[..^1];
        }

        int start = sb.Length;
        if (negative)
        {
            sb.Append('-');
        }

        if (point <= 0)
        {
            sb.Append("0.").Append('0', -point).Append(d);
        }
        else if (point >= d.Length)
        {
            sb.Append(d).Append('0', point - d.Length).Append(".0");
        }
        else
        {
            sb.Append(d[..point]).Append('.').Append(d[point..]);
        }

        // The layout must not change the value; this check is cheap next to the formatting itself.
        float back = ParseToken(sb.ToString(start, sb.Length - start));
        if (BitConverter.SingleToUInt32Bits(back) != BitConverter.SingleToUInt32Bits(value))
        {
            throw new InvalidOperationException($"Float text for bits {BitConverter.SingleToUInt32Bits(value):x8} does not round trip.");
        }
    }

    /// <summary>
    /// Parses a JSON number token to the nearest binary32 (ties to even). Throws <see cref="FormatException"/> for
    /// text that is not a JSON number and <see cref="OverflowException"/> when it rounds beyond the binary32 range.
    /// </summary>
    public static float Parse(ReadOnlySpan<char> token)
    {
        if (!IsNumberToken(token))
        {
            throw new FormatException($"'{token}' is not a JSON number.");
        }

        return ParseToken(token);
    }

    /// <summary>Like <see cref="Parse(ReadOnlySpan{char})"/>, false instead of throwing.</summary>
    public static bool TryParse(ReadOnlySpan<char> token, out float value)
    {
        value = 0f;
        if (!IsNumberToken(token))
        {
            return false;
        }

        float f = float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!float.IsFinite(f))
        {
            return false;
        }

        value = f;
        return true;
    }

    /// <summary>True for text matching the JSON number grammar.</summary>
    public static bool IsNumberToken(ReadOnlySpan<char> s)
    {
        int i = 0;
        int n = s.Length;
        if (i < n && s[i] == '-')
        {
            i++;
        }

        if (i >= n)
        {
            return false;
        }

        if (s[i] == '0')
        {
            i++;
        }
        else if (s[i] is >= '1' and <= '9')
        {
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
            }
        }
        else
        {
            return false;
        }

        if (i < n && s[i] == '.')
        {
            i++;
            int fracStart = i;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            if (i == fracStart)
            {
                return false;
            }
        }

        if (i < n && s[i] is 'e' or 'E')
        {
            i++;
            if (i < n && s[i] is '+' or '-')
            {
                i++;
            }

            int expStart = i;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            if (i == expStart)
            {
                return false;
            }
        }

        return i == n;
    }

    /// <summary>The .NET parser is correctly rounded straight to binary32 (no double step); out of range gives infinity.</summary>
    private static float ParseToken(ReadOnlySpan<char> token)
    {
        float f = float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!float.IsFinite(f))
        {
            throw new OverflowException($"'{token}' is beyond the binary32 range.");
        }

        return f;
    }
}
