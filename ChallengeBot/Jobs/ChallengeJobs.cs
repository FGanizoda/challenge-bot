using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Logic;
using ChallengeBot.Texts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChallengeBot.Jobs;

/// <summary>
/// Всё, что бот делает сам по расписанию: утро, напоминания, закрытие дня, итоги недели, пары.
/// Метод TickAsync вызывается раз в минуту (и в режиме симуляции, с подменённым временем).
/// </summary>
public class ChallengeJobs
{
    private const string PairsRotationKey = "pairs_rotation";

    private readonly ChallengeStore _store;
    private readonly ChallengeClock _clock;
    private readonly ChallengeOptions _options;
    private readonly IChatSender _sender;
    private readonly ILogger<ChallengeJobs> _log;

    public ChallengeJobs(ChallengeStore store, ChallengeClock clock, IOptions<ChallengeOptions> options,
        IChatSender sender, ILogger<ChallengeJobs> log)
    {
        _store = store;
        _clock = clock;
        _options = options.Value;
        _sender = sender;
        _log = log;
    }

    /// <summary>Одна проверка расписания. Сбой одного задания не мешает остальным.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        await RunSafe("пары", () => EnsurePairsAsync(ct));
        await RunSafe("закрытие дней", () => CloseDaysAsync(ct));

        var now = _clock.Now;
        foreach (var job in ScheduleCalculator.GetDueJobs(now, _options))
        {
            // Отметка «уже отправлено» переживает перезапуск, поэтому после старта ничего не дублируется.
            var marker = "fired:" + job.Key;
            if (await _store.GetSettingAsync(marker) is not null)
                continue;

            var done = await RunSafe(job.Key, () => RunJobAsync(job, ct));
            if (done)
                await _store.SetSettingAsync(marker, now.ToString("s"));
        }
    }

    private async Task<bool> RunSafe(string name, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка в задании «{Name}», повторю при следующей проверке", name);
            return false;
        }
    }

    private Task RunJobAsync(DueJob job, CancellationToken ct) => job.Kind switch
    {
        JobKind.Morning => SendMorningAsync(ct),
        JobKind.Reminder => SendReminderAsync(ct),
        JobKind.WeeklySummary => SendWeeklySummaryAsync(ct),
        _ => Task.CompletedTask
    };

    // ---------- Утро ----------

    public async Task SendMorningAsync(CancellationToken ct)
    {
        var today = _clock.Today;
        var participants = await _store.GetActiveParticipantsAsync();

        var active = new List<(Participant, int)>();
        var notStarted = new List<Participant>();
        var measuring = new List<(Participant, int)>();

        foreach (var p in participants)
        {
            var day = DayCalculator.GetDayNumber(p.StartDate, today);
            if (DayCalculator.IsInRange(day, _options.Days))
            {
                active.Add((p, day));
                if (DayCalculator.IsMeasurementDay(day))
                    measuring.Add((p, day));
            }
            else if (day < 1)
            {
                notStarted.Add(p);
            }
        }

        if (active.Count == 0)
            return; // сегодня никому отчитываться не нужно

        var groupDay = DayCalculator.GetDayNumber(_options.StartDate, today);
        await _sender.SendAsync(ScheduledMessages.Morning(groupDay, _options, active, notStarted, measuring), ct);
    }

    // ---------- Напоминания 18:00 и 21:00 ----------

    public async Task SendReminderAsync(CancellationToken ct)
    {
        var now = _clock.Now;
        var notReported = await FindNotReportedAsync(DateOnly.FromDateTime(now));
        if (notReported.Count == 0)
            return; // все молодцы, молчим

        var left = _options.Deadline - TimeOnly.FromDateTime(now);
        await _sender.SendAsync(ScheduledMessages.Reminder(notReported, left, _options), ct);
    }

    /// <summary>Участники, у которых сегодня рабочий день челленджа и ещё нет записи.</summary>
    private async Task<List<Participant>> FindNotReportedAsync(DateOnly today)
    {
        var result = new List<Participant>();
        foreach (var p in await _store.GetActiveParticipantsAsync())
        {
            var day = DayCalculator.GetDayNumber(p.StartDate, today);
            if (!DayCalculator.IsInRange(day, _options.Days))
                continue;
            if (await _store.GetDayAsync(p.UserId, today) is null)
                result.Add(p);
        }
        return result;
    }

    // ---------- Дедлайн: закрытие дней ----------

    /// <summary>
    /// Ставит «п» всем, у кого нет записи за уже закрытый день. Закрытый день: вчера и раньше,
    /// а сегодня только после дедлайна. Проверка идёт каждую минуту и при старте, поэтому
    /// дедлайн, пропущенный из-за простоя, дозакрывается сам. Задним числом (до даты вступления
    /// и вне дней 1–N) пропуски не ставятся. Если записи уже есть, ничего не меняется.
    /// </summary>
    public async Task CloseDaysAsync(CancellationToken ct)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now);
        var pastDeadline = TimeOnly.FromDateTime(now) >= _options.Deadline;
        var lastClosable = pastDeadline ? today : today.AddDays(-1);

        var participants = await _store.GetActiveParticipantsAsync();
        var byId = participants.ToDictionary(p => p.UserId);
        var newMisses = new SortedDictionary<DateOnly, List<(Participant, Participant?)>>();

        foreach (var p in participants)
        {
            var joinedLocal = _clock.ToLocalDate(new DateTimeOffset(DateTime.SpecifyKind(p.JoinedAt, DateTimeKind.Utc)));
            var first = p.StartDate > joinedLocal ? p.StartDate : joinedLocal;
            var lastOfChallenge = DayCalculator.DateOfDay(p.StartDate, _options.Days);
            var last = lastClosable < lastOfChallenge ? lastClosable : lastOfChallenge;

            if (last < first)
                continue;

            var existing = (await _store.GetDaysAsync(p.UserId)).Select(d => d.Date).ToHashSet();
            for (var date = first; date <= last; date = date.AddDays(1))
            {
                if (existing.Contains(date))
                    continue;

                var added = await _store.TryAddDayAsync(new DayRecord
                {
                    UserId = p.UserId,
                    Date = date,
                    Status = DayStatus.Miss,
                    CreatedAt = DateTime.UtcNow
                });
                if (!added)
                    continue; // запись появилась только что (например, пришёл отчёт)

                await _store.LogAsync("miss", $"user={p.UserId} date={date:yyyy-MM-dd}");

                Participant? partner = null;
                if (p.PartnerUserId is long partnerId)
                    byId.TryGetValue(partnerId, out partner);

                if (!newMisses.TryGetValue(date, out var list))
                    newMisses[date] = list = [];
                list.Add((p, partner));
            }
        }

        foreach (var (date, misses) in newMisses)
            await _sender.SendAsync(ScheduledMessages.Deadline(date, date == today, misses, _options), ct);
    }

    // ---------- Итоги недели ----------

    public async Task SendWeeklySummaryAsync(CancellationToken ct)
    {
        var today = _clock.Today;
        var weekStart = today.AddDays(-6);
        var participants = await _store.GetActiveParticipantsAsync();
        var allDays = await _store.GetAllDaysAsync();

        var lines = new List<WeeklyLine>();
        foreach (var p in participants)
        {
            var mine = allDays.Where(d => d.UserId == p.UserId).ToList();
            var week = mine.Where(d => d.Date >= weekStart && d.Date <= today).ToList();

            // Сколько дней из этой недели входят в челлендж участника.
            int daysInWeek = 0;
            for (var d = weekStart; d <= today; d = d.AddDays(1))
                if (DayCalculator.IsInRange(DayCalculator.GetDayNumber(p.StartDate, d), _options.Days))
                    daysInWeek++;

            if (daysInWeek == 0)
                continue;

            var all = BankCalculator.CalculateStats(mine.Select(d => d.Status), _options.SickLimit, _options.Fine);
            lines.Add(new WeeklyLine(
                p.DisplayName,
                week.Count(d => d.Status.IsCompleted()),
                daysInWeek,
                week.Count(d => d.Status == DayStatus.Miss),
                week.Count(d => d.Status == DayStatus.Sick),
                all.SickRemaining));
        }

        if (lines.Count == 0)
            return;

        var totalMisses = allDays.Count(d => d.Status == DayStatus.Miss && participants.Any(p => p.UserId == d.UserId));
        var bank = BankCalculator.Balance(participants.Count, totalMisses, _options.Deposit, _options.Fine);
        await _sender.SendAsync(ScheduledMessages.Weekly(lines, bank, _options), ct);
    }

    // ---------- Пары ----------

    /// <summary>
    /// Раз в 2 недели пары меняются сами. Новичок, у которого нет напарника, встаёт в уже готовую цепочку,
    /// остальные пары не трогаем. Админ может назначить пары вручную через /pair.
    /// </summary>
    public async Task EnsurePairsAsync(CancellationToken ct)
    {
        var participants = await _store.GetActiveParticipantsAsync();
        if (participants.Count < 2)
            return;

        var rotation = PairRotation.RotationIndex(_options.StartDate, _clock.Today);
        var storedText = await _store.GetSettingAsync(PairsRotationKey);
        var rotationChanged = !int.TryParse(storedText, out var stored) || stored != rotation;
        var unpaired = participants.Where(p => p.PartnerUserId is null).ToList();

        if (!rotationChanged && unpaired.Count == 0)
            return;

        Dictionary<long, long> map;
        if (rotationChanged)
        {
            var groups = PairRotation.BuildGroups(participants.Select(p => p.UserId).ToList(), rotation);
            map = PairRotation.PartnerMap(groups);
        }
        else
        {
            map = participants.Where(p => p.PartnerUserId is not null)
                .ToDictionary(p => p.UserId, p => p.PartnerUserId!.Value);

            if (map.Count == 0)
            {
                map = PairRotation.PartnerMap(PairRotation.BuildGroups(unpaired.Select(p => p.UserId).ToList(), rotation));
            }
            else
            {
                foreach (var newcomer in unpaired)
                    PairRotation.AddNewcomer(map, newcomer.UserId);
            }
        }

        await _store.SetPartnersAsync(map);
        await _store.SetSettingAsync(PairsRotationKey, rotation.ToString());
        await _store.LogAsync("pairs", string.Join(", ", map.Select(kv => $"{kv.Key}->{kv.Value}")));

        var byId = participants.ToDictionary(p => p.UserId);
        await _sender.SendAsync(ScheduledMessages.NewPairs(PairRotation.GroupsFromPartnerMap(map), byId), ct);
    }
}
