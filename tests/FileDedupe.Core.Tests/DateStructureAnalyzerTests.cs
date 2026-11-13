using FileDedupe.Core.Archiving;

namespace FileDedupe.Core.Tests;

public class DateStructureAnalyzerTests
{
    [Theory]
    [InlineData("2024-05-15", 3)]
    [InlineData("2024_05_15", 3)]
    [InlineData("2024.05.15", 3)]
    [InlineData("20240515", 3)]
    [InlineData("2024-05", 2)]
    [InlineData("2024", 1)]
    [InlineData("nikon", 0)]
    [InlineData("mypictures", 0)]
    public void ScoresSegmentsByDateStrength(string segment, int expected) =>
        Assert.Equal(expected, DateStructureAnalyzer.ScoreSegment(segment));

    [Theory]
    [InlineData("2024-13-01")]  // month out of range
    [InlineData("2024-02-30")]  // day out of range for February
    [InlineData("1800")]        // year below the supported range
    [InlineData("202405")]      // bare six digits: too easily a serial number
    [InlineData("12345678")]    // eight digits that are not a valid date
    public void RejectsValuesThatOnlyLookLikeDates(string segment) =>
        Assert.Equal(0, DateStructureAnalyzer.ScoreSegment(segment));

    [Fact]
    public void DatedPathOutscoresDescriptivePath()
    {
        var dated = DateStructureAnalyzer.Score(@"D:\pictures\2024\2024-05-15", @"D:\pictures");
        var descriptive = DateStructureAnalyzer.Score(@"D:\pictures\mypictures\nikon", @"D:\pictures");

        Assert.True(dated > descriptive, $"expected dated ({dated}) > descriptive ({descriptive})");
    }

    [Fact]
    public void SegmentsAboveTheRootAreIgnored()
    {
        // Both files live under D:\2024; that shared segment must not contribute, or
        // every candidate would score the same and the rule would quietly do nothing.
        var score = DateStructureAnalyzer.Score(@"D:\2024\nikon", @"D:\2024");

        Assert.Equal(0, score);
    }

    [Fact]
    public void UnrelatedRootFallsBackToScoringTheWholePath()
    {
        var score = DateStructureAnalyzer.Score(@"D:\pictures\2024", @"X:\other");

        Assert.True(score >= 1);
    }

    [Fact]
    public void EmptyPathScoresZero() => Assert.Equal(0, DateStructureAnalyzer.Score(string.Empty));
}
