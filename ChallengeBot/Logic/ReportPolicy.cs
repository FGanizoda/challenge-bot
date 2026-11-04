namespace ChallengeBot.Logic;

/// <summary>Что делать с пришедшим отчётом.</summary>
public enum ReportDecision
{
    /// <summary>Засчитать.</summary>
    Accept,
    /// <summary>За этот день уже есть запись (повторный отчёт не задваивает день).</summary>
    AlreadyCounted,
    /// <summary>День уже закрыт: дедлайн прошёл или стоит пропуск. Решает админ через /mark.</summary>
    DayClosed
}

public static class ReportPolicy
{
    /// <param name="existing">Статус дня, если запись уже есть.</param>
    /// <param name="localTime">Местное время отправки сообщения.</param>
    /// <param name="deadline">Дедлайн (например, 22:00).</param>
    public static ReportDecision Decide(DayStatus? existing, TimeOnly localTime, TimeOnly deadline)
    {
        if (existing is DayStatus.Done or DayStatus.Gentle or DayStatus.Sick)
            return ReportDecision.AlreadyCounted;

        // Пропуск уже поставлен, либо время вышло.
        if (existing == DayStatus.Miss || localTime >= deadline)
            return ReportDecision.DayClosed;

        return ReportDecision.Accept;
    }

    /// <summary>Какой статус ставить принятому отчёту.</summary>
    public static DayStatus StatusFor(CaptionInfo caption) =>
        caption.IsGentle ? DayStatus.Gentle : DayStatus.Done;
}
