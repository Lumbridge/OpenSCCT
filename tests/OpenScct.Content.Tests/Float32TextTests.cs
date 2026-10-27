using System;
using System.Globalization;
using System.IO;
using CsCheck;
using OpenScct.Content.Json;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class Float32TextTests
{
    private static SvJsonObject Vectors => (SvJsonObject)SvJson.Parse(File.ReadAllBytes(Path.Combine(TestPaths.Spec, "vectors", "canonical.json")));

    private static float FromBits(string hex) => BitConverter.UInt32BitsToSingle(uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    [Fact]
    public void FormatMatchesSpecVectors()
    {
        var cases = (SvJsonArray)Vectors["float32Format"]!;
        Assert.NotEmpty(cases);
        foreach (SvJsonNode node in cases)
        {
            var c = (SvJsonObject)node;
            string bits = ((SvJsonString)c["bits"]!).Value;
            string text = ((SvJsonString)c["text"]!).Value;
            Assert.Equal(text, Float32Text.Format(FromBits(bits)));
        }
    }

    [Fact]
    public void ParseMatchesSpecVectors()
    {
        var cases = (SvJsonArray)Vectors["float32Parse"]!;
        Assert.NotEmpty(cases);
        foreach (SvJsonNode node in cases)
        {
            var c = (SvJsonObject)node;
            string bits = ((SvJsonString)c["bits"]!).Value;
            string token = ((SvJsonString)c["token"]!).Value;
            Assert.Equal(bits, BitConverter.SingleToUInt32Bits(Float32Text.Parse(token)).ToString("x8", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void ConventionsTableExamples()
    {
        Assert.Equal("0.1", Float32Text.Format(0.1f));
        Assert.Equal("100.0", Float32Text.Format(100f));
        Assert.Equal("-0.0", Float32Text.Format(-0f));
        Assert.Equal("0.0", Float32Text.Format(0f));
        Assert.Equal("123456790.0", Float32Text.Format(123456790f));
        Assert.Equal("-1024.5", Float32Text.Format(-1024.5f));
    }

    [Fact]
    public void FormatRejectsNonFinite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Float32Text.Format(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Float32Text.Format(float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Float32Text.Format(float.NegativeInfinity));
    }

    [Fact]
    public void ParseRejectsOverflowAndNonJsonText()
    {
        Assert.Throws<OverflowException>(() => Float32Text.Parse("3.4028236e38"));
        Assert.Throws<OverflowException>(() => Float32Text.Parse("1e400"));
        Assert.Equal(float.MaxValue, Float32Text.Parse("3.40282356e38"));
        foreach (string bad in new[] { "", "-", "+1.0", ".5", "5.", "01.0", "1e", "1e+", "NaN", "Infinity", " 1.0", "1.0 ", "0x10" })
        {
            Assert.Throws<FormatException>(() => Float32Text.Parse(bad));
            Assert.False(Float32Text.TryParse(bad, out _));
        }
    }

    [Fact]
    public void EveryFiniteFloatRoundTripsAndHasALegalLayout()
    {
        Gen.UInt.Where(bits => float.IsFinite(BitConverter.UInt32BitsToSingle(bits))).Sample(bits =>
        {
            float f = BitConverter.UInt32BitsToSingle(bits);
            string text = Float32Text.Format(f);
            Assert.True(Float32Text.IsNumberToken(text), text);
            Assert.Contains('.', text);
            Assert.DoesNotContain('e', text);
            Assert.DoesNotContain('E', text);
            int dot = text.IndexOf('.', StringComparison.Ordinal);
            Assert.True(dot > (text[0] == '-' ? 1 : 0) && dot < text.Length - 1, text);
            Assert.Equal(bits, BitConverter.SingleToUInt32Bits(Float32Text.Parse(text)));
        }, iter: 200_000);
    }

    [Fact]
    public void DigitsAreTheShortestThatRoundTrip()
    {
        Gen.UInt.Where(bits => float.IsFinite(BitConverter.UInt32BitsToSingle(bits)) && BitConverter.UInt32BitsToSingle(bits) != 0f).Sample(bits =>
        {
            float f = BitConverter.UInt32BitsToSingle(bits);
            int digits = SignificantDigits(Float32Text.Format(f));
            for (int precision = 1; precision < digits; precision++)
            {
                string shorter = f.ToString("E" + (precision - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                Assert.NotEqual(bits, BitConverter.SingleToUInt32Bits(float.Parse(shorter, CultureInfo.InvariantCulture)));
            }
        }, iter: 20_000);
    }

    [Fact]
    public void IntegerTokensStayIntegers()
    {
        Assert.Equal("0", SvJsonNumber.FromIntegerToken("-0").IntegerText);
        Assert.Equal("123456789012345678901234567890", SvJsonNumber.FromIntegerToken("123456789012345678901234567890").IntegerText);
        Assert.Throws<FormatException>(() => SvJsonNumber.FromIntegerToken("01"));
        Assert.Throws<FormatException>(() => SvJsonNumber.FromIntegerToken("1.0"));
    }

    private static int SignificantDigits(string text)
    {
        string digits = text.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        return Math.Max(1, digits.TrimEnd('0').Length);
    }
}
