using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenScct.Content.Json;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class CanonicalJsonTests
{
    private static SvJsonObject Vectors => (SvJsonObject)SvJson.Parse(File.ReadAllBytes(Path.Combine(TestPaths.Spec, "vectors", "canonical.json")));

    private static string Str(SvJsonObject o, string key) => ((SvJsonString)o[key]!).Value;

    [Fact]
    public void JsonCasesMatchSpecVectors()
    {
        var cases = (SvJsonArray)Vectors["json"]!;
        Assert.NotEmpty(cases);
        foreach (SvJsonNode node in cases)
        {
            var c = (SvJsonObject)node;
            byte[] canonical = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes(Str(c, "input")));
            Assert.Equal(Str(c, "canonicalHex"), Convert.ToHexStringLower(canonical));
            Assert.Equal(Str(c, "canonical"), Encoding.UTF8.GetString(canonical));
            Assert.Equal(Str(c, "sha256"), SvMapHash.Sha256Hex(canonical));
        }
    }

    [Fact]
    public void GroupCaseMatchesSpecVector()
    {
        var group = (SvJsonObject)Vectors["group"]!;
        var rows = new List<(string, string)>();
        foreach (SvJsonNode node in (SvJsonArray)group["files"]!)
        {
            var f = (SvJsonObject)node;
            string path = Str(f, "path");
            rows.Add((path, SvMapHash.FileDigest(path, Encoding.UTF8.GetBytes(Str(f, "content")))));
        }

        Assert.Equal(Str(group, "listing"), SvMapHash.GroupListing(rows));
        Assert.Equal(Str(group, "sha256"), SvMapHash.GroupHash(rows));
    }

    [Theory]
    [InlineData("{\"a\": 1, \"a\": 2}", "duplicate key")]
    [InlineData("{\"a\": NaN}", "invalid JSON")]
    [InlineData("{\"a\": Infinity}", "invalid JSON")]
    [InlineData("[1, 2,]", "invalid JSON")]
    [InlineData("{\"a\": 1} // c", "invalid JSON")]
    [InlineData("{\"a\": 1} {}", "invalid JSON")]
    [InlineData("{\"a\": 01}", "invalid JSON")]
    [InlineData("{\"a\": 1e39}", "overflows binary32")]
    [InlineData("", "invalid JSON")]
    [InlineData("\"tab\there\"", "invalid JSON")]
    [InlineData("\"\\ud800\"", "invalid JSON")]
    public void StrictReaderRejects(string text, string reason)
    {
        var e = Assert.Throws<SvMapFormatException>(() => SvJson.Parse(text, "sim/x.json"));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
        Assert.Equal("sim/x.json", e.File);
    }

    [Fact]
    public void ByteOrderMarkAndBadUtf8AreRejected()
    {
        Assert.Throws<SvMapFormatException>(() => SvJson.Parse([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']));
        Assert.Throws<SvMapFormatException>(() => SvJson.Parse([(byte)'"', 0xC3, (byte)'"']));
    }

    [Fact]
    public void NonAsciiKeysCannotBeCanonicalised()
    {
        SvJsonNode node = SvJson.Parse("{\"cl\u00e9\": 1}");
        Assert.Throws<SvMapFormatException>(() => CanonicalJson.ToCanonicalBytes(node));
    }

    [Fact]
    public void NumbersFollowTheFloatAndIntegerRules()
    {
        string canonical = CanonicalJson.ToCanonicalString(SvJson.Parse("[1, -0, 1.0, 1e0, 1E2, -0.0, 0.30000001192092896, 16777217.0, 123456789012345678901234567890]"));
        Assert.Equal("[1,0,1.0,1.0,100.0,-0.0,0.3,16777216.0,123456789012345678901234567890]", canonical);
    }

    [Fact]
    public void StringsEscapeOnlyQuotesBackslashesAndControls()
    {
        string canonical = CanonicalJson.ToCanonicalString(SvJson.Parse("\"a/b\\u0000\\u001f\\b\\f\\n\\r\\t\\\"\\\\\\u00e9\\ud83d\\ude00\\u2028\\u007f\""));
        Assert.Equal("\"a/b\\u0000\\u001f\\b\\f\\n\\r\\t\\\"\\\\\u00e9\ud83d\ude00\u2028\u007f\"", canonical);
    }

    [Fact]
    public void KeysSortByAsciiBytes()
    {
        Assert.Equal("{\"A\":1,\"B\":2,\"_\":4,\"a\":3}", CanonicalJson.ToCanonicalString(SvJson.Parse("{\"a\":3,\"_\":4,\"B\":2,\"A\":1}")));
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("sim/gameplay.json")]
    [InlineData("sim/traversal.json")]
    public void PrettyFormIsByteIdenticalToThePipelineWriter(string relPath)
    {
        byte[] original = TestPaths.TestBoxFile(relPath);
        byte[] rewritten = CanonicalJson.ToFileBytes(SvJson.Parse(original));
        Assert.Equal(Encoding.UTF8.GetString(original), Encoding.UTF8.GetString(rewritten));
    }

    [Fact]
    public void PrettyFormInlinesShortScalarContainersOnly()
    {
        SvJsonNode node = SvJson.Parse("{\"b\":[1,2],\"a\":{\"x\":[1.5],\"y\":{}}}");
        Assert.Equal("{\n \"a\": {\n  \"x\": [1.5],\n  \"y\": {}\n },\n \"b\": [1, 2]\n}", CanonicalJson.ToPrettyString(node));
        Assert.Equal(CanonicalJson.ToCanonicalString(node), CanonicalJson.ToCanonicalString(SvJson.Parse(CanonicalJson.ToPrettyString(node))));
    }

    [Fact]
    public void PrettyWidthCountsCodePoints()
    {
        // 60 emoji in a one-element array: 64 code points inline (124 UTF-16 units, which would not fit).
        string emoji = string.Concat(System.Linq.Enumerable.Repeat("\ud83d\ude00", 60));
        SvJsonNode node = new SvJsonArray([SvJsonNode.String(emoji)]);
        Assert.Equal($"[\"{emoji}\"]", CanonicalJson.ToPrettyString(node));

        // 97 code points of text: 101 inline, so it breaks.
        string wide = new('x', 97);
        Assert.Equal($"[\n \"{wide}\"\n]", CanonicalJson.ToPrettyString(new SvJsonArray([SvJsonNode.String(wide)])));
    }
}
