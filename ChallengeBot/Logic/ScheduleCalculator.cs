using ChallengeBot.Config;

namespace ChallengeBot.Logic;

public enum JobKind
{
    Morning,
    Reminder,
    WeeklySummary
}

/// <summary>Задание по расписанию. Key уникален для конкретной даты и времени: по нему ставится отметка «уже отправлено».</summary>
public record DueJob(JobKind Kind, string Key, TimeOnly SlotTime);

/// <summary>
/// Определяет, какие задания пора выполнить прямо сейчас.
/// Если бот был выключен ненадолго, задание ещё выполнится, пока не вышло окно (устаревшие напоминания не шлём).
/// Закрытие дня в дедлайн сюда не входит: оно выполняется при каждой проверке и всегда дозакрывает пропущенное.
/// </summary>
public static class ScheduleCalculator
{
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MorningWindow = TimeSpan.FromHours(3);
    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromHours(3);

    public static List<DueJob> GetDueJobs(DateTime nowLocal, ChallengeOptions options)
    {
        var jobs = new List<DueJob>();
        var date = DateOnly.FromDateTime(nowLocal);
        var time = TimeOnly.FromDateTime(nowLocal);

        if (IsInWindow(time, options.MorningTime, MorningWindow))
            jobs.Add(MakeJob(JobKind.Morning, date, options.MorningTime));

        foreach (var reminder in options.ReminderTimes)
        {
            // Напоминание после дедлайна бессмысленно.
            if (reminder < options.Deadline && IsInWindow(time, reminder, ReminderWindow))
                jobs.Add(MakeJob(JobKind.Reminder, date, reminder));
        }

        if (date.DayOfWeek == options.WeeklySummaryDay &&
            IsInWindow(time, options.WeeklySummaryTime, WeeklyWindow))
            jobs.Add(MakeJob(JobKind.WeeklySummary, date, options.WeeklySummaryTime));

        return jobs;
    }

    /// <summary>Время уже наступило, но прошло меньше, чем длится окно.</summary>
    private static bool IsInWindow(TimeOnly now, TimeOnly slot, TimeSpan window)
    {
        var since = now - slot; // TimeOnly - TimeOnly даёт TimeSpan (может быть «через полночь»)
        if (now < slot) return false;
        return since < window;
    }

    private static DueJob MakeJob(JobKind kind, DateOnly date, TimeOnly slot) =>
        new(kind, $"{kind}:{date:yyyy-MM-dd}:{slot:HH\\:mm}", slot);
}
