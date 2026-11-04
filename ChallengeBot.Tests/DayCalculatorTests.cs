using ChallengeBot.Logic;

namespace ChallengeBot.Tests;

public class DayCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 11, 9);

    [Fact]
    public void FirstDay_IsOne() =>
        Assert.Equal(1, DayCalculator.GetDayNumber(Start, Start));

    [Fact]
    public void LastDay_IsSixty() =>
        Assert.Equal(60, DayCalculator.GetDayNumber(Start, new DateOnly(2027, 1, 7)));

    [Fact]
    public void DayBeforeStart_IsZero_AndOutOfRange()
    {
        var n = DayCalculator.GetDayNumber(Start, new DateOnly(2026, 11, 8));
        Assert.Equal(0, n);
        Assert.False(DayCalculator.IsInRange(n, 60));
    }

    [Fact]
    public void DayAfterFinish_IsSixtyOne_AndOutOfRange()
    {
        var n = DayCalculator.GetDayNumber(Start, new DateOnly(2027, 1, 8));
        Assert.Equal(61, n);
        Assert.False(DayCalculator.IsInRange(n, 60));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    [InlineData(61, false)]
    public void IsInRange_Boundaries(int day, bool expected) =>
        Assert.Equal(expected, DayCalculator.IsInRange(day, 60));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(14, 1)]
    [InlineData(15, 2)]
    [InlineData(40, 2)]
    [InlineData(41, 3)]
    [InlineData(60, 3)]
    public void Stage_ByDay(int day, int stage) =>
        Assert.Equal(stage, DayCalculator.GetStage(day));

    [Theory]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(2, false)]
    public void MeasurementDays(int day, bool expected) =>
        Assert.Equal(expected, DayCalculator.IsMeasurementDay(day));
}
