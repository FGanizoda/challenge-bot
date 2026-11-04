using ChallengeBot.Logic;

namespace ChallengeBot.Tests;

public class StartDateTests
{
    private static readonly DateOnly ChallengeStart = new(2026, 11, 9); // понедельник

    [Fact]
    public void JoinOnMonday_StartsToday()
    {
        var start = StartDatePlanner.PlanStart(new DateOnly(2026, 11, 16), ChallengeStart);
        Assert.Equal(new DateOnly(2026, 11, 16), start);
    }

    [Fact]
    public void JoinOnWednesday_StartsNextMonday()
    {
        // Среда 18 ноября, ближайший понедельник 23 ноября.
        var start = StartDatePlanner.PlanStart(new DateOnly(2026, 11, 18), ChallengeStart);
        Assert.Equal(new DateOnly(2026, 11, 23), start);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
    }

    [Fact]
    public void JoinOnSunday_StartsNextDay()
    {
        var start = StartDatePlanner.PlanStart(new DateOnly(2026, 11, 22), ChallengeStart);
        Assert.Equal(new DateOnly(2026, 11, 23), start);
    }

    [Fact]
    public void JoinBeforeChallenge_StartsOnChallengeStart()
    {
        var start = StartDatePlanner.PlanStart(new DateOnly(2026, 11, 4), ChallengeStart);
        Assert.Equal(ChallengeStart, start);
    }

    [Fact]
    public void NewcomerHasOwnDayOne()
    {
        // Вошёл в среду 18 ноября, старт 23 ноября: 23-го у него день 1, а у остальных уже день 15.
        var start = StartDatePlanner.PlanStart(new DateOnly(2026, 11, 18), ChallengeStart);
        var date = new DateOnly(2026, 11, 23);
        Assert.Equal(1, DayCalculator.GetDayNumber(start, date));
        Assert.Equal(15, DayCalculator.GetDayNumber(ChallengeStart, date));
    }
}

public class ReportPolicyTests
{
    private static readonly TimeOnly Deadline = new(22, 0);

    [Fact]
    public void FirstReport_Accepted() =>
        Assert.Equal(ReportDecision.Accept, ReportPolicy.Decide(null, new TimeOnly(10, 0), Deadline));

    [Theory]
    [InlineData(DayStatus.Done)]
    [InlineData(DayStatus.Gentle)]
    [InlineData(DayStatus.Sick)]
    public void RepeatReportSameDay_NotCountedTwice(DayStatus existing) =>
        Assert.Equal(ReportDecision.AlreadyCounted, ReportPolicy.Decide(existing, new TimeOnly(10, 0), Deadline));

    [Fact]
    public void ReportAfterMiss_DayClosed() =>
        Assert.Equal(ReportDecision.DayClosed, ReportPolicy.Decide(DayStatus.Miss, new TimeOnly(10, 0), Deadline));

    [Fact]
    public void ReportAtDeadline_DayClosed() =>
        Assert.Equal(ReportDecision.DayClosed, ReportPolicy.Decide(null, new TimeOnly(22, 0), Deadline));

    [Fact]
    public void ReportOneMinuteBeforeDeadline_Accepted() =>
        Assert.Equal(ReportDecision.Accept, ReportPolicy.Decide(null, new TimeOnly(21, 59), Deadline));

    [Fact]
    public void GentleCaption_GivesGentleStatus() =>
        Assert.Equal(DayStatus.Gentle, ReportPolicy.StatusFor(CaptionParser.Parse("День 3/60 - готово, щадящий. Самочувствие 5/10")));

    [Fact]
    public void NormalCaption_GivesDoneStatus() =>
        Assert.Equal(DayStatus.Done, ReportPolicy.StatusFor(CaptionParser.Parse("День 3/60 - готово. Самочувствие 8/10")));
}

public class CaptionParserTests
{
    [Fact]
    public void ParsesWellbeing()
    {
        var info = CaptionParser.Parse("День 5/60 - готово. Самочувствие 7/10");
        Assert.Equal(7, info.Wellbeing);
        Assert.False(info.IsGentle);
    }

    [Fact]
    public void ParsesTen()
    {
        Assert.Equal(10, CaptionParser.Parse("самочувствие 10/10").Wellbeing);
    }

    [Fact]
    public void ParsesWithColonAndSpaces()
    {
        Assert.Equal(4, CaptionParser.Parse("Самочувствие: 4 / 10").Wellbeing);
    }

    [Theory]
    [InlineData("Самочувствие 11/10")]
    [InlineData("без цифр")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidOrMissing_GivesNull(string? caption) =>
        Assert.Null(CaptionParser.Parse(caption).Wellbeing);

    [Theory]
    [InlineData("День 2/60 - щадящий режим")]
    [InlineData("ЩАДЯЩИЙ день")]
    public void DetectsGentle(string caption) =>
        Assert.True(CaptionParser.Parse(caption).IsGentle);
}

public class SickPolicyTests
{
    [Theory]
    [InlineData(3, 0, 3, true)]
    [InlineData(3, 2, 1, true)]
    [InlineData(3, 3, 0, false)]
    [InlineData(3, 5, 0, false)]
    public void SickLimit(int limit, int used, int remaining, bool canTake)
    {
        Assert.Equal(remaining, SickPolicy.Remaining(limit, used));
        Assert.Equal(canTake, SickPolicy.CanTake(limit, used));
    }
}

public class BankTests
{
    [Fact]
    public void Stats_CountsEachStatus()
    {
        var statuses = new[]
        {
            DayStatus.Done, DayStatus.Done, DayStatus.Gentle,
            DayStatus.Sick, DayStatus.Miss, DayStatus.Miss
        };

        var stats = BankCalculator.CalculateStats(statuses, sickLimit: 3, fine: 50);

        Assert.Equal(2, stats.Done);
        Assert.Equal(1, stats.Gentle);
        Assert.Equal(3, stats.Completed);
        Assert.Equal(1, stats.Sick);
        Assert.Equal(2, stats.Miss);
        Assert.Equal(2, stats.SickRemaining);
        Assert.Equal(100m, stats.Fines);
    }

    [Fact]
    public void Stats_SickDoesNotCostMoney()
    {
        var stats = BankCalculator.CalculateStats([DayStatus.Sick, DayStatus.Sick, DayStatus.Sick], 3, 50);
        Assert.Equal(0m, stats.Fines);
        Assert.Equal(0, stats.SickRemaining);
    }

    [Fact]
    public void Bank_WithoutDeposit_IsOnlyFines() =>
        Assert.Equal(350m, BankCalculator.Balance(participantCount: 4, totalMisses: 7, deposit: 0, fine: 50));

    [Fact]
    public void Bank_WithDeposit_AddsDeposits() =>
        Assert.Equal(4 * 200m + 3 * 20m, BankCalculator.Balance(4, 3, 200, 20));

    [Fact]
    public void StatusCodes_MatchGroupTable()
    {
        Assert.Equal("д", DayStatus.Done.Code());
        Assert.Equal("щ", DayStatus.Gentle.Code());
        Assert.Equal("б", DayStatus.Sick.Code());
        Assert.Equal("п", DayStatus.Miss.Code());
        Assert.Equal(DayStatus.Miss, DayStatusExtensions.FromCode("П"));
        Assert.Null(DayStatusExtensions.FromCode("x"));
    }
}

public class PairRotationTests
{
    private static readonly long[] Four = [1, 2, 3, 4];

    [Fact]
    public void RotationIndex_ChangesEveryTwoWeeks()
    {
        var start = new DateOnly(2026, 11, 9);
        Assert.Equal(0, PairRotation.RotationIndex(start, start));
        Assert.Equal(0, PairRotation.RotationIndex(start, start.AddDays(13)));
        Assert.Equal(1, PairRotation.RotationIndex(start, start.AddDays(14)));
        Assert.Equal(2, PairRotation.RotationIndex(start, start.AddDays(28)));
    }

    [Fact]
    public void EvenCount_MakesPairs()
    {
        var groups = PairRotation.BuildGroups(Four, 0);
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(2, g.Members.Count));
        Assert.Equal(4, groups.SelectMany(g => g.Members).Distinct().Count());
    }

    [Fact]
    public void OddCount_MakesOneTriple()
    {
        var groups = PairRotation.BuildGroups([1, 2, 3, 4, 5], 0);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 2, 3 }, groups.Select(g => g.Members.Count).OrderBy(c => c));
        Assert.Equal(5, groups.SelectMany(g => g.Members).Distinct().Count());
    }

    [Fact]
    public void NextRotation_ChangesPartners()
    {
        var first = PairRotation.PartnerMap(PairRotation.BuildGroups(Four, 0));
        var second = PairRotation.PartnerMap(PairRotation.BuildGroups(Four, 1));
        Assert.NotEqual(first[1], second[1]);
    }

    [Fact]
    public void ThreeRotations_FourPeople_NobodyRepeatsPartner()
    {
        // Для 4 человек есть 3 разных разбиения на пары, они не повторяются.
        var partners = Enumerable.Range(0, 3)
            .Select(i => PairRotation.PartnerMap(PairRotation.BuildGroups(Four, i))[1])
            .ToList();
        Assert.Equal(3, partners.Distinct().Count());
    }

    [Fact]
    public void PartnerMap_PairIsMutual()
    {
        var map = PairRotation.PartnerMap(PairRotation.BuildGroups(Four, 0));
        foreach (var (user, partner) in map)
            Assert.Equal(user, map[partner]);
    }

    [Fact]
    public void PartnerMap_TripleIsCycle()
    {
        var map = PairRotation.PartnerMap([new PairGroup([1, 2, 3])]);
        Assert.Equal(2, map[1]);
        Assert.Equal(3, map[2]);
        Assert.Equal(1, map[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LessThanTwo_NoGroups(int count) =>
        Assert.Empty(PairRotation.BuildGroups(Enumerable.Range(1, count).Select(i => (long)i).ToList(), 0));
}

public class HealthAdvisorTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData(4, true)]
    [InlineData(1, true)]
    [InlineData(5, false)]
    [InlineData(null, false)]
    public void SuggestGentle(int? wellbeing, bool expected) =>
        Assert.Equal(expected, HealthAdvisor.ShouldSuggestGentle(wellbeing));

    [Fact]
    public void ThreeLowDaysInRow_SuggestDoctor()
    {
        var recent = new (DateOnly, int?)[] { (Today, 4), (Today.AddDays(-1), 3), (Today.AddDays(-2), 2) };
        Assert.True(HealthAdvisor.ShouldSuggestDoctor(recent, Today));
    }

    [Fact]
    public void OneGoodDayBreaksStreak()
    {
        var recent = new (DateOnly, int?)[] { (Today, 4), (Today.AddDays(-1), 7), (Today.AddDays(-2), 2) };
        Assert.False(HealthAdvisor.ShouldSuggestDoctor(recent, Today));
    }

    [Fact]
    public void GapInDates_BreaksStreak()
    {
        var recent = new (DateOnly, int?)[] { (Today, 4), (Today.AddDays(-1), 3), (Today.AddDays(-3), 2) };
        Assert.False(HealthAdvisor.ShouldSuggestDoctor(recent, Today));
    }
}
