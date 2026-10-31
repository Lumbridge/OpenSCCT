using System;
using System.IO;
using OpenScct.Core.Uc;
using Xunit;

namespace OpenScct.Core.Tests;

[CollectionDefinition("Localized text", DisableParallelization = true)]
public sealed class UcTextCollection;

[Collection("Localized text")]
public sealed class UcTextTests
{
    [Theory]
    [InlineData("[\"First\",\"Second\"]")]
    [InlineData("{\"0\":\"First\",\"1\":\"Second\"}")]
    [InlineData("{\"$elements\":{\"0\":\"First\",\"1\":\"Second\"}}")]
    public void LocalizedStaticArraysReadDenseSparseAndWrappedDefaults(string values)
    {
        string file = Path.Combine(Path.GetTempPath(), "openscct-text-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """
                {"classes":{"Test.Menu":{"properties":[{"name":"Labels","flags":"0x00408000"}],"defaults":{"Labels":__VALUES__}}}}
                """.Replace("__VALUES__", values, StringComparison.Ordinal));
            UcText.Use(file);
            Assert.Equal("First", UcText.Get("Test.Menu", "Labels", 0, "inherited"));
            Assert.Equal("Second", UcText.Get("Test.Menu", "Labels", 1, "inherited"));
            Assert.Equal("inherited", UcText.Get("Test.Menu", "Labels", 2, "inherited"));
            Assert.Equal("inherited", UcText.Get("Test.Menu", "Labels", -1, "inherited"));
        }
        finally
        {
            UcText.Use(null);
            File.Delete(file);
        }
    }
}
