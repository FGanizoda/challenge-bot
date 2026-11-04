using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Jobs;
using ChallengeBot.Logic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ChallengeBot.Tests;

public class ScheduleCalculatorTests
{
    private static readonly ChallengeOptions Options = new();

    private static List<DueJob> At(int y, int mo, int d, int h, int mi) =>
        ScheduleCalculator.GetDueJobs(new DateTime(y, mo, d, h, mi, 0), Options);

    [Fact]
    public void Morning_At0800() =>
        Assert.Contains(At(2026, 11, 10, 8, 0), j => j.Kind == JobKind.Morning);

    [Fact]
    public void Morning_NotBefore0800() =>
        Assert.DoesNotContain(At(2026, 11, 10, 7, 59), j => j.Kind == JobKind.Morning);

    [Fact]
    public void Morning_CatchesUpAfterShortDowntime() =>
        Assert.Contains(At(2026, 11, 10, 9, 30), j => j.Kind == JobKind.Morning);

    [Fact]
    public void Morning_NotSentInTheEvening() =>
        Assert.DoesNotContain(At(2026, 11, 10, 19, 0), j => j.Kind == JobKind.Morning);

    [Theory]
    [InlineData(18, 0)]
    [InlineData(21, 0)]
    [InlineData(21, 20)]
    public void Reminders_InWindow(int h, int m) =>
        Assert.Contains(At(2026, 11, 10, h, m), j => j.Kind == JobKind.Reminder);

    [Theory]
    [InlineData(17, 59)]
    [InlineData(19, 0)]   // 18:00 уже устарело
    [InlineData(22, 0)]   // после дедлайна напоминаний нет
    public void Reminders_OutsideWindow(int h, int m) =>
        Assert.DoesNotContain(At(2026, 11, 10, h, m), j => j.Kind == JobKind.Reminder);

    [Fact]
    public void WeeklySummary_OnlyOnSunday()
    {
        // 15 ноября 2026 — воскресенье
        Assert.Contains(At(2026, 11, 15, 20, 0), j => j.Kind == JobKind.WeeklySummary);
        Assert.DoesNotContain(At(2026, 11, 14, 20, 0), j => j.Kind == JobKind.WeeklySummary);
    }

    [Fact]
    public void JobKeys_AreDifferentPerDayAndSlot()
    {
        var a = At(2026, 11, 10, 18, 0).Single(j => j.Kind == JobKind.Reminder).Key;
        var b = At(2026, 11, 11, 18, 0).Single(j => j.Kind == JobKind.Reminder).Key;
        var c = At(2026, 11, 10, 21, 0).Single(j => j.Kind == JobKind.Reminder).Key;
        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
    }
}

public class NewcomerAndStartTests
{
    [Fact]
    public void Newcomer_JoinsPair_MakesTriple()
    {
        var map = PairRotation.PartnerMap(PairRotation.BuildGroups([1, 2], 0)); // 1<->2
        PairRotation.AddNewcomer(map, 3);

        var groups = PairRotation.GroupsFromPartnerMap(map);
        Assert.Single(groups);
        Assert.Equal(3, groups[0].Members.Count);
        Assert.All(new long[] { 1, 2, 3 }, id => Assert.True(map.ContainsKey(id)));
    }

    [Fact]
    public void Newcomer_DoesNotTouchOtherPairs()
    {
        var map = PairRotation.PartnerMap(PairRotation.BuildGroups([1, 2, 3, 4], 0));
        var before34 = (map[3], map[4]);
        PairRotation.AddNewcomer(map, 5);

        // Якорь — первый участник (1), пара 3-4 осталась как была.
        Assert.Equal(before34, (map[3], map[4]));
        Assert.Equal(2, PairRotation.GroupsFromPartnerMap(map).Count);
    }

    [Fact]
    public void GroupsFromPartnerMap_RestoresPairs()
    {
        var map = new Dictionary<long, long> { [1] = 2, [2] = 1, [3] = 4, [4] = 3 };
        var groups = PairRotation.GroupsFromPartnerMap(map);
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void MondayAfterDeadline_StartsNextMonday()
    {
        // Понедельник 16 ноября 22:30: день 1 сразу стал бы пропуском.
        var start = StartDatePlanner.PlanStart(new DateTime(2026, 11, 16, 22, 30, 0), new DateOnly(2026, 11, 9), new TimeOnly(22, 0));
        Assert.Equal(new DateOnly(2026, 11, 23), start);
    }

    [Fact]
    public void MondayBeforeDeadline_StartsToday()
    {
        var start = StartDatePlanner.PlanStart(new DateTime(2026, 11, 16, 21, 0, 0), new DateOnly(2026, 11, 9), new TimeOnly(22, 0));
        Assert.Equal(new DateOnly(2026, 11, 16), start);
    }
}

/// <summary>Складывает отправленные сообщения вместо отправки в Telegram.</summary>
public class FakeSender : IChatSender
{
    public List<string> Messages { get; } = [];

    public Task SendAsync(string htmlText, CancellationToken ct)
    {
        Messages.Add(htmlText);
        return Task.CompletedTask;
    }
}

/// <summary>Одна общая SQLite-база в памяти для всех контекстов теста.</summary>
public class MemoryDbFactory : IDbContextFactory<ChallengeDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public MemoryDbFactory()
    {
        _connection.Open();
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public ChallengeDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ChallengeDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>Проверка заданий планировщика на подменённом времени.</summary>
public class ChallengeJobsTests : IDisposable
{
    private const long Anna = 1, Bobur = 2, Carim = 3;

    private readonly MemoryDbFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSender _sender = new();
    private readonly ChallengeStore _store;
    private readonly ChallengeJobs _jobs;

    public ChallengeJobsTests()
    {
        var options = new ChallengeOptions();
        _store = new ChallengeStore(_factory);
        var clock = new ChallengeClock(_time, options.TimeZone);
        _jobs = new ChallengeJobs(_store, clock, Options.Create(options), _sender, NullLogger<ChallengeJobs>.Instance);
        SetLocal(2026, 11, 9, 7, 0); // понедельник, день 1
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Ставит текущее время по Ташкенту (UTC+5).</summary>
    private void SetLocal(int y, int mo, int d, int h, int mi) =>
        _time.SetUtcNow(new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(5)).ToUniversalTime());

    private async Task AddParticipant(long id, string name, long? partner = null, DateOnly? start = null) =>
        await _store.SaveParticipantAsync(new Participant
        {
            UserId = id,
            DisplayName = name,
            StartDate = start ?? new DateOnly(2026, 11, 9),
            PartnerUserId = partner,
            Active = true,
            // Вступили до старта, чтобы день 1 считался
            JoinedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        });

    private Task Report(long id, DateOnly date, DayStatus status = DayStatus.Done) =>
        _store.TryAddDayAsync(new DayRecord { UserId = id, Date = date, Status = status, CreatedAt = DateTime.UtcNow });

    private static readonly DateOnly Day1 = new(2026, 11, 9);

    [Fact]
    public async Task Deadline_MarksMiss_AndMentionsPartner()
    {
        await AddParticipant(Anna, "Анна", partner: Bobur);
        await AddParticipant(Bobur, "Бобур", partner: Anna);
        await Report(Anna, Day1);

        SetLocal(2026, 11, 9, 22, 0);
        await _jobs.CloseDaysAsync(default);

        var bobur = await _store.GetDayAsync(Bobur, Day1);
        Assert.Equal(DayStatus.Miss, bobur!.Status);
        Assert.Equal(DayStatus.Done, (await _store.GetDayAsync(Anna, Day1))!.Status);

        var message = Assert.Single(_sender.Messages);
        Assert.Contains("Бобур", message);
        Assert.Contains($"tg://user?id={Anna}", message); // напарник упомянут
    }

    [Fact]
    public async Task BeforeDeadline_NothingHappens()
    {
        await AddParticipant(Anna, "Анна");
        SetLocal(2026, 11, 9, 21, 59);
        await _jobs.CloseDaysAsync(default);

        Assert.Null(await _store.GetDayAsync(Anna, Day1));
        Assert.Empty(_sender.Messages);
    }

    [Theory]
    [InlineData(DayStatus.Sick)]
    [InlineData(DayStatus.Gentle)]
    [InlineData(DayStatus.Done)]
    public async Task ExistingRecord_IsNotOverwritten_AndNoMessage(DayStatus status)
    {
        await AddParticipant(Anna, "Анна");
        await Report(Anna, Day1, status);

        SetLocal(2026, 11, 9, 22, 5);
        await _jobs.CloseDaysAsync(default);

        Assert.Equal(status, (await _store.GetDayAsync(Anna, Day1))!.Status);
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task RunningTwice_DoesNotDuplicateMissOrMessage()
    {
        await AddParticipant(Anna, "Анна");
        SetLocal(2026, 11, 9, 22, 0);
        await _jobs.CloseDaysAsync(default);
        await _jobs.CloseDaysAsync(default);

        Assert.Single(_sender.Messages);
        Assert.Single(await _store.GetDaysAsync(Anna));
    }

    [Fact]
    public async Task AfterDowntime_MissedDeadlinesAreClosedOnStart()
    {
        await AddParticipant(Anna, "Анна");
        await Report(Anna, Day1); // день 1 выполнен, потом бот выключили

        // Включили в четверг утром: вторник и среда остались без записи.
        SetLocal(2026, 11, 12, 9, 0);
        await _jobs.CloseDaysAsync(default);

        var days = await _store.GetDaysAsync(Anna);
        Assert.Equal(3, days.Count);
        Assert.Equal(DayStatus.Miss, days.Single(d => d.Date == new DateOnly(2026, 11, 10)).Status);
        Assert.Equal(DayStatus.Miss, days.Single(d => d.Date == new DateOnly(2026, 11, 11)).Status);
        Assert.DoesNotContain(days, d => d.Date == new DateOnly(2026, 11, 12)); // сегодня ещё не закрыт
    }

    [Fact]
    public async Task NoMissBeforeParticipantJoined()
    {
        // Вступил в среду, старт в понедельник: до старта пропусков нет.
        await _store.SaveParticipantAsync(new Participant
        {
            UserId = Carim, DisplayName = "Карим", StartDate = new DateOnly(2026, 11, 23), Active = true,
            JoinedAt = new DateTime(2026, 11, 18, 5, 0, 0, DateTimeKind.Utc)
        });

        SetLocal(2026, 11, 22, 23, 0);
        await _jobs.CloseDaysAsync(default);
        Assert.Empty(await _store.GetDaysAsync(Carim));
    }

    [Fact]
    public async Task NoMissAfterDay60()
    {
        await AddParticipant(Anna, "Анна");
        SetLocal(2027, 1, 20, 23, 0); // давно после 7 января

        await _jobs.CloseDaysAsync(default);

        var days = await _store.GetDaysAsync(Anna);
        Assert.Equal(60, days.Count);
        Assert.Equal(new DateOnly(2027, 1, 7), days.Max(d => d.Date));
    }

    [Fact]
    public async Task Reminder_MentionsOnlyThoseWithoutReport()
    {
        await AddParticipant(Anna, "Анна");
        await AddParticipant(Bobur, "Бобур");
        await Report(Anna, Day1);

        SetLocal(2026, 11, 9, 18, 0);
        await _jobs.TickAsync(default);

        var reminder = _sender.Messages.Single(m => m.StartsWith("Напоминание"));
        Assert.Contains($"tg://user?id={Bobur}", reminder);
        Assert.DoesNotContain($"tg://user?id={Anna}\"", reminder);
        Assert.Contains("4 ч", reminder); // до 22:00 осталось 4 часа
    }

    [Fact]
    public async Task Reminder_NotSentTwice_AfterTickRepeats()
    {
        await AddParticipant(Anna, "Анна");
        SetLocal(2026, 11, 9, 18, 0);
        await _jobs.TickAsync(default);
        SetLocal(2026, 11, 9, 18, 1);
        await _jobs.TickAsync(default);

        Assert.Single(_sender.Messages, m => m.StartsWith("Напоминание"));
    }

    [Fact]
    public async Task Reminder_Silent_WhenEveryoneReported()
    {
        await AddParticipant(Anna, "Анна");
        await Report(Anna, Day1);
        SetLocal(2026, 11, 9, 21, 0);
        await _jobs.TickAsync(default);

        Assert.DoesNotContain(_sender.Messages, m => m.StartsWith("Напоминание"));
    }

    [Fact]
    public async Task Morning_ShowsDayAndStage()
    {
        await AddParticipant(Anna, "Анна");
        SetLocal(2026, 11, 9, 8, 0);
        await _jobs.TickAsync(default);

        var morning = _sender.Messages.Single(m => m.StartsWith("Доброе утро"));
        Assert.Contains("День 1/60", morning);
        Assert.Contains("этап 1", morning);
        Assert.Contains("замеры", morning); // день 1 — контрольные замеры
    }

    [Fact]
    public async Task Weekly_SummaryOnSunday()
    {
        await AddParticipant(Anna, "Анна");
        for (int i = 0; i < 6; i++)
            await Report(Anna, Day1.AddDays(i));
        // воскресенье 15 ноября: отчёта нет, итоги в 20:00 раньше дедлайна

        SetLocal(2026, 11, 15, 20, 0);
        await _jobs.TickAsync(default);

        var summary = _sender.Messages.Single(m => m.StartsWith("Итоги недели"));
        Assert.Contains("6/7", summary);
        Assert.Contains("больничных осталось 3", summary);
        Assert.Contains("самочувствием", summary);
    }

    [Fact]
    public async Task Pairs_AssignedForEveryone_AndRotateEveryTwoWeeks()
    {
        foreach (var (id, name) in new[] { (1L, "А"), (2L, "Б"), (3L, "В"), (4L, "Г") })
            await AddParticipant(id, name);

        await _jobs.EnsurePairsAsync(default);
        var first = (await _store.GetActiveParticipantsAsync()).ToDictionary(p => p.UserId, p => p.PartnerUserId);
        Assert.All(first.Values, v => Assert.NotNull(v));

        // Через 2 недели пары меняются.
        SetLocal(2026, 11, 23, 9, 0);
        await _jobs.EnsurePairsAsync(default);
        var second = (await _store.GetActiveParticipantsAsync()).ToDictionary(p => p.UserId, p => p.PartnerUserId);
        Assert.NotEqual(first[1], second[1]);

        // Внутри одной ротации пары не меняются.
        SetLocal(2026, 11, 30, 9, 0);
        await _jobs.EnsurePairsAsync(default);
        var third = (await _store.GetActiveParticipantsAsync()).ToDictionary(p => p.UserId, p => p.PartnerUserId);
        Assert.Equal(second, third);
    }
}
