using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class WindowLightTransferTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OverlappingPanesRestoreOnlyTheBrokenPanesTransmission(bool faithful)
    {
        var data = new WindowLightTransfer { Panes = [new() { Actor = "Red", Tint = [1,.1f,.1f] }, new() { Actor = "Blue", Tint = [.1f,.1f,1] }] };
        float[] initial = faithful ? [.02f+12/255f,.02f+1/255f,.02f+12/255f] : [.07f,.025f,.07f];
        var batch = new WindowLightTransfer.Batch { Faithful = faithful,
            Samples = [new() { Base = [.02f,.02f,.02f], Direct = initial, Initial = initial, Terms = [new() { Panes = [0,1], Colour = faithful ? [127.5f,127.5f,127.5f] : [.5f,.5f,.5f] }] }] };
        float[] intact = data.Evaluate(batch, new HashSet<string>())[0];
        float[] redBroken = data.Evaluate(batch, new HashSet<string> { "Red" })[0];
        float[] blueBroken = data.Evaluate(batch, new HashSet<string> { "Blue" })[0];
        float[] clear = data.Evaluate(batch, new HashSet<string> { "Red", "Blue" })[0];
        Assert.True(intact[1] < intact[0] && intact[1] < intact[2]);
        Assert.True(redBroken[2] > 5 * redBroken[0]); Assert.True(blueBroken[0] > 5 * blueBroken[2]);
        Assert.InRange(MathF.Abs(clear[0]-clear[1]),0,.00001f); Assert.InRange(MathF.Abs(clear[0]-clear[2]),0,.00001f);
        Assert.Equal(intact, data.Evaluate(batch, new HashSet<string>())[0]);
    }
    [Fact]
    public void PaddingAndBounceRespondWithoutTintingUnrelatedAmbient()
    {
        var data = new WindowLightTransfer { Panes = [new() { Actor = "Pane", Tint = [.2f, .2f, .2f] }] };
        var batch = new WindowLightTransfer.Batch { Samples = [
            new() { Base = [.1f,.1f,.1f], Direct = [.2f,.2f,.2f], Initial = [.2f,.2f,.2f], Terms = [new() { Panes = [0], Colour = [.5f,.5f,.5f] }] },
            new() { Direct = [.2f,.2f,.2f], Initial = [.2f,.2f,.2f] }, new() { Initial = [.12f,.12f,.12f] }],
            DirectFill = [new() { Target = 1, Sources = [0] }], Bounces = [new() { Source = 1, Target = 2, Gain = [.25f,.25f,.25f], Ambient = [.1f,.1f,.1f] }] };
        var values = data.Evaluate(batch, new HashSet<string> { "Pane" });
        Assert.InRange(values[0][0], .59999f, .60001f); Assert.InRange(values[1][0], .59999f, .60001f);
        Assert.InRange(values[2][0], .21999f,.22001f);
    }
    [Fact]
    public void OptionalResponseRoundTripsAndRejectsInvalidReferences()
    {
        var data = new WindowLightTransfer { Panes = [new() { Actor = "Pane" }], Batches = [new() { Samples = [new()] }] };
        using var stream = new MemoryStream(data.Save());
        Assert.Equal("Pane", WindowLightTransfer.Load(stream).Panes[0].Actor);
        data.Batches[0].Samples[0].Terms.Add(new() { Panes = [4] });
        Assert.Throws<InvalidDataException>(data.Validate);
    }
    [Theory]
    [InlineData("{\"panes\":null}")]
    [InlineData("{\"panes\":[null]}")]
    [InlineData("{\"batches\":[{\"samples\":null}]}")]
    [InlineData("{\"batches\":[{\"samples\":[{\"terms\":[null]}]}]}")]
    public void InvalidOptionalDataFailsWithAnActionableLoadError(string json)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, true)) gzip.Write(Encoding.UTF8.GetBytes(json));
        bytes.Position = 0;
        Assert.Throws<InvalidDataException>(() => WindowLightTransfer.Load(bytes));
    }
}
