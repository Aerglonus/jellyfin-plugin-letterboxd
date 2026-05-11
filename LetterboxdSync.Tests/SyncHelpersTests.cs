using System;
using Xunit;

namespace LetterboxdSync.Tests;

public class SyncHelpersTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 0.5)]
    [InlineData(2.0, 1.0)]
    [InlineData(3.0, 1.5)]
    [InlineData(4.0, 2.0)]
    [InlineData(5.0, 2.5)]
    [InlineData(6.0, 3.0)]
    [InlineData(7.0, 3.5)]
    [InlineData(8.0, 4.0)]
    [InlineData(9.0, 4.5)]
    [InlineData(10.0, 5.0)]
    [InlineData(15.0, 5.0)] // Clamped to 5.0 max
    public void GetLetterboxdRating_ConvertsAndClampsProperly(double? input, double? expected)
    {
        // Act
        var result = SyncHelpers.GetLetterboxdRating(input);

        // Assert
        Assert.Equal(expected, result);
    }
}
