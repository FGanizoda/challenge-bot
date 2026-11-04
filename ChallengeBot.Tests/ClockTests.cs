using ChallengeBot.Logic;
using Microsoft.Extensions.Time.Testing;

namespace ChallengeBot.Tests;

public class ClockTests
{
    private static ChallengeClock MakeClock(DateTimeOffset utcNow)
    {
        var time = new FakeTimeProvider(utcNow);
        return new ChallengeClock(time, "Asia/Tashkent");
    }

    [Fact]
    public void Zone_Tashkent_IsUtcPlus5()
    {
        var zone = ChallengeClock.ResolveZone("Asia/Tashkent");
        Assert.Equal(TimeSpan.FromHours(5), zone.GetUtcOffset(new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc)));
        // Летнего времени нет: зимой и летом одинаково.
        Assert.Equal(TimeSpan.FromHours(5), zone.GetUtcOffset(new DateTime(2027, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Zone_WindowsId_IsUtcPlus5()
    {
        // Запасной идентификатор существует только на Windows.
        if (!OperatingSystem.IsWindows())
            return;

        var zone = TimeZoneInfo.FindSystemTimeZoneById("West Asia Standard Time");
        Assert.Equal(TimeSpan.FromHours(5), zone.BaseUtcOffset);
    }

    [Fact]
    public void Zone_UnknownId_Throws() =>
        Assert.Throws<TimeZoneNotFoundException>(() => ChallengeClock.ResolveZone("Нет/Такой"));

    [Fact]
    public void MessageDate_JustBeforeLocalMidnight_IsSameDay()
    {
        // 18:59:59 UTC = 23:59:59 в Ташкенте, ещё 9 ноября.
        var clock = MakeClock(DateTimeOffset.UtcNow);
        var date = clock.ToLocalDate(new DateTimeOffset(2026, 11, 9, 18, 59, 59, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 9), date);
    }

    [Fact]
    public void MessageDate_AtLocalMidnight_IsNextDay()
    {
        // 19:00:00 UTC = 00:00:00 10 ноября в Ташкенте.
        var clock = MakeClock(DateTimeOffset.UtcNow);
        var date = clock.ToLocalDate(new DateTimeOffset(2026, 11, 9, 19, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 10), date);
    }

    [Fact]
    public void Today_UsesInjectedTimeProvider()
    {
        // 20:00 UTC 9 ноября = 01:00 10 ноября по местному времени.
        var clock = MakeClock(new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 10), clock.Today);
        Assert.Equal(1, clock.Now.Hour);
    }
}
