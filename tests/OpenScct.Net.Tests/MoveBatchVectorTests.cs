using OpenScct.Core;
using OpenScct.Net.Moves;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class MoveBatchVectorTests
{
    [Theory]
    [InlineData(5371f, 3862f, 633.005f)]
    [InlineData(-5371f, -3862f, -633.005f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(33554432f, -33554432f, 0f)]
    public void AWholeBatchPreservesNormalPackedVectorValues(float x, float y, float z)
    {
        FVector[] values = [new(x, y, z), new(x + 0.005f, y, z), new(x + 5, y - 3, z + 7),
            new(-x, -y, -z), new(17.7f, 0.03125f, -0.03125f), default];
        var writer = new FBitWriter(2048);
        FVector previous = default;
        foreach (FVector value in values) { MoveBatchVector.Write(writer, value, previous); previous = value; }
        Assert.False(writer.IsError);
        var reader = new FBitReader(writer.Buffer, writer.NumBits);
        previous = default;
        foreach (FVector value in values)
        {
            previous = MoveBatchVector.Read(reader, previous);
            Assert.Equal(NetQuantize.RoundTripVector(value), previous);
        }
        Assert.False(reader.IsError);
    }

    [Fact]
    public void SmallMovementAtMapCoordinatesUsesLessThanHalfTheAbsoluteVectorPayload()
    {
        var delta = new FBitWriter(2048);
        var absolute = new FBitWriter(2048);
        FVector previous = default;
        for (int i = 0; i < 6; i++)
        {
            var value = new FVector(5371 + i * 3f, 3862, 633);
            MoveBatchVector.Write(delta, value, previous);
            absolute.WritePackedVector(value);
            previous = value;
        }
        Assert.True(delta.NumBits < absolute.NumBits / 2, $"delta {delta.NumBits}, absolute {absolute.NumBits}");
    }

    [Fact]
    public void OutOfBoundsRelativeCoordinateIsRejected()
    {
        var writer = new FBitWriter(256);
        writer.WriteBit(true); writer.WriteBit(true);
        writer.WriteBit(true); writer.WriteSignedPacked(100); writer.WriteBit(false); writer.WriteBit(false);
        var reader = new FBitReader(writer.Buffer, writer.NumBits);
        MoveBatchVector.Read(reader, new FVector(33554432f, 0, 0));
        Assert.True(reader.IsError);
    }
}
