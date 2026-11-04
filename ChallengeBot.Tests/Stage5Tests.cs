using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Jobs;
using ChallengeBot.Logic;
using ChallengeBot.Texts;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ChallengeBot.Tests;

public class TableFormatterTests
{
    private static readonly DateOnly Start = new(2026, 11, 9);

    private static TableRow Row(string name, DateOnly start, params (int Offset, DayStatus Status)[] days) =>
        new(name, start, days.ToDictionary(d => start.AddDays(d.Offset), d => d.Status));

    [Fact]
    public void Cells_ShowStatusDotAndDash()
    {
        var row = Row("Анна", Start, (0, DayStatus.Done), (1, DayStatus.Miss));

        Assert.Equal("д", TableFormatter.Cell(row, Start, 60));
        Assert.Equal("п", TableFormatter.Cell(row, Start.AddDays(1), 60));
        Assert.Equal(".", TableFormatter.Cell(row, Start.AddDays(2), 60));   // в челлендже, записи ещё нет
        Assert.Equal("-", TableFormatter.Cell(row, Start.AddDays(-1), 60));  // до старта
        Assert.Equal("-", TableFormatter.Cell(row, Start.AddDays(60), 60));  // после дня 60
    }

    [Fact]
    public void Table_HasLastSevenDays_AndTotals()
    {
        var anna = Row("Анна", Start, (0, DayStatus.Done), (1, DayStatus.Gentle), (2, DayStatus.Sick), (3, DayStatus.Miss));
        var today = Start.AddDays(3);

        var text = TableFormatter.Build([anna], today, 60);

        Assert.Contains("<pre>", text);
        Assert.Contains("Анна", text);
        Assert.Contains("2/1/1", text); // выполнено 2 (д+щ), больничных 1, пропусков 1
        Assert.Contains("12", text);    // 12 ноября — последняя дата в заголовке
        Assert.Contains("06", text);    // 6 ноября — первая из семи
    }

    [Fact]
    public void Table_EscapesHtmlInNames()
    {
        var row = Row("<b>Хак", Start);
        var text = TableFormatter.Build([row], Start, 60);
        Assert.DoesNotContain("<b>", text);
        Assert.Contains("&lt;b&gt;", text);
    }

    [Fact]
    public void Table_LongNameIsTrimmed()
    {
        var row = Row("Александрович-Длинный", Start);
        var text = TableFormatter.Build([row], Start, 60);
        Assert.Contains("Александро ", text);           // 10 символов и пробел-разделитель
        Assert.DoesNotContain("Александрович", text);
    }
}

public class ExcelExporterTests
{
    [Fact]
    public void Export_HasThreeSheets_WithData()
    {
        var options = new ChallengeOptions();
        var start = new DateOnly(2026, 11, 9);
        var anna = new Participant { UserId = 1, DisplayName = "Анна", Username = "anna", StartDate = start, PartnerUserId = 2, JoinedAt = DateTime.UtcNow };
        var bobur = new Participant { UserId = 2, DisplayName = "Бобур", StartDate = start, PartnerUserId = 1, JoinedAt = DateTime.UtcNow };
        var days = new List<DayRecord>
        {
            new() { UserId = 1, Date = start, Status = DayStatus.Done },
            new() { UserId = 2, Date = start, Status = DayStatus.Miss },
            new() { UserId = 2, Date = start.AddDays(1), Status = DayStatus.Miss },
        };

        var bytes = ExcelExporter.Build([anna, bobur], days, options, start.AddDays(1));

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Учёт по дням", "Итоги", "Участники"], wb.Worksheets.Select(w => w.Name).ToArray());

        var byDays = wb.Worksheet("Учёт по дням");
        Assert.Equal("д", byDays.Cell(2, 2).GetString());  // Анна, 09.11
        Assert.Equal("п", byDays.Cell(3, 2).GetString());  // Бобур, 09.11

        var totals = wb.Worksheet("Итоги");
        Assert.Equal("Бобур", totals.Cell(3, 1).GetString());
        Assert.Equal(2, totals.Cell(3, 6).GetValue<int>());       // 2 пропуска
        Assert.Equal(100, totals.Cell(3, 8).GetValue<decimal>()); // штраф 2 × 50
        Assert.Equal(3, totals.Cell(3, 7).GetValue<int>());       // больничных осталось

        var list = wb.Worksheet("Участники");
        Assert.Equal("@anna", list.Cell(2, 2).GetString());
        Assert.Equal("Бобур", list.Cell(2, 5).GetString());       // напарник Анны
    }

    [Fact]
    public void Export_WithoutParticipants_DoesNotCrash()
    {
        var bytes = ExcelExporter.Build([], [], new ChallengeOptions(), new DateOnly(2026, 11, 9));
        Assert.NotEmpty(bytes);
    }
}

public class BankMessageTests
{
    [Fact]
    public void Bank_WithoutDeposit_SaysNoDeposits()
    {
        var o = new ChallengeOptions();
        var text = Messages.Bank(2, [("Анна", 0), ("Бобур", 3)], o);

        Assert.Contains("Взносов нет", text);
        Assert.Contains("3 × 50 сомони = 150 сомони", text);
        Assert.Contains("Бобур: 3", text);
        Assert.DoesNotContain("Анна:", text); // без пропусков в список штрафов не попадает
        Assert.Contains("Всего в банке: 150 сомони", text);
    }

    [Fact]
    public void Bank_WithDeposit_ShowsDeposits()
    {
        var o = new ChallengeOptions { Deposit = 200, Fine = 20 };
        var text = Messages.Bank(4, [("А", 1)], o);
        Assert.Contains("4 × 200 сомони = 800 сомони", text);
        Assert.Contains("Всего в банке: 820 сомони", text);
    }
}

public class SickOverLimitTests : IDisposable
{
    private readonly MemoryDbFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSender _sender = new();
    private readonly ChallengeStore _store;
    private readonly ChallengeJobs _jobs;

    public SickOverLimitTests()
    {
        var options = new ChallengeOptions();
        _store = new ChallengeStore(_factory);
        _jobs = new ChallengeJobs(_store, new ChallengeClock(_time, options.TimeZone), Options.Create(options),
            _sender, NullLogger<ChallengeJobs>.Instance);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task OverLimitSickRequest_DoesNotGetAutomaticFine()
    {
        var day1 = new DateOnly(2026, 11, 9);
        await _store.SaveParticipantAsync(new Participant
        {
            UserId = 1, DisplayName = "Анна", StartDate = day1, Active = true,
            JoinedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await _store.SetSettingAsync(ChallengeStore.SickOverLimitKey(1, day1), "1");

        _time.SetUtcNow(new DateTimeOffset(2026, 11, 9, 22, 30, 0, TimeSpan.FromHours(5)).ToUniversalTime());
        await _jobs.CloseDaysAsync(default);

        Assert.Null(await _store.GetDayAsync(1, day1));
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task AdminMark_CanOverrideExistingDay()
    {
        var day1 = new DateOnly(2026, 11, 9);
        await _store.UpsertDayAsync(new DayRecord { UserId = 1, Date = day1, Status = DayStatus.Miss });
        await _store.UpsertDayAsync(new DayRecord { UserId = 1, Date = day1, Status = DayStatus.Sick });

        var day = await _store.GetDayAsync(1, day1);
        Assert.Equal(DayStatus.Sick, day!.Status);
        Assert.Single(await _store.GetDaysAsync(1));
    }

    [Fact]
    public async Task FindParticipant_ByUsernameAndId()
    {
        await _store.SaveParticipantAsync(new Participant
        {
            UserId = 42, Username = "Anna_T", DisplayName = "Анна", StartDate = new DateOnly(2026, 11, 9), Active = true
        });

        Assert.Equal(42, (await _store.FindParticipantAsync("@anna_t"))!.UserId);
        Assert.Equal(42, (await _store.FindParticipantAsync("42"))!.UserId);
        Assert.Null(await _store.FindParticipantAsync("@nobody"));
        Assert.Null(await _store.FindParticipantAsync("@"));
    }
}
