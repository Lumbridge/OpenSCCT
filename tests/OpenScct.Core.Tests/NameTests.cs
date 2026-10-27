using System.Linq;
using System.Threading.Tasks;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class NameTests
{
    [Fact]
    public void NoneIsIndexZeroAndDefault()
    {
        Assert.Equal(0, Name.None.Index);
        Assert.True(default(Name).IsNone);
        Assert.Equal("None", Name.None.ToString());
        Assert.Equal(Name.None, new Name("None"));
        Assert.Equal(Name.None, new Name("none"));
        Assert.Equal(Name.None, new Name(""));
        Assert.Equal(Name.None, new Name(null));
    }

    [Fact]
    public void CaseInsensitiveAndKeepsFirstSpelling()
    {
        Name a = "NameTests_MixedCase";
        Name b = "nametests_mixedcase";
        Name c = "NAMETESTS_MIXEDCASE";
        Assert.Equal(a, b);
        Assert.True(a == c);
        Assert.Equal(a.Index, c.Index);
        Assert.Equal("NameTests_MixedCase", c.ToString());
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentTextGivesDifferentNames()
    {
        Name a = "NameTests_Alpha";
        Name b = "NameTests_Beta";
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void FindDoesNotIntern()
    {
        // Other tests intern names in parallel, so check through Find itself rather than the table size.
        Assert.True(Name.Find("NameTests_NeverInterned_7f3a").IsNone);
        Assert.True(Name.Find("NameTests_NeverInterned_7f3a").IsNone);

        Name made = "NameTests_Interned_7f3a";
        Assert.Equal(made, Name.Find("nametests_interned_7F3A"));
        Assert.True(Name.TableCount > made.Index);
    }

    [Fact]
    public void ConcurrentInterningAgrees()
    {
        string[] texts = Enumerable.Range(0, 2000).Select(i => "NameTests_Concurrent_" + i).ToArray();
        var results = new int[8][];
        int[] strides = [1, 3, 7, 9, 11, 13, 17, 19]; // coprime with 2000, so each visits every entry
        Parallel.For(0, 8, worker =>
        {
            var indexes = new int[texts.Length];
            for (int k = 0; k < texts.Length; k++)
            {
                // Workers walk the list in different orders so they race on new entries.
                int i = (k * strides[worker]) % texts.Length;
                indexes[i] = new Name(worker % 2 == 0 ? texts[i] : texts[i].ToUpperInvariant()).Index;
            }

            results[worker] = indexes;
        });

        for (int w = 1; w < 8; w++)
        {
            Assert.Equal(results[0], results[w]);
        }

        Assert.Equal(texts.Length, results[0].Distinct().Count());
        for (int i = 0; i < texts.Length; i++)
        {
            Assert.Equal(texts[i], new Name(texts[i]).ToString(), ignoreCase: true);
        }
    }
}
