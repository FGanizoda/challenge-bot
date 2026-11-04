namespace ChallengeBot.Logic;

/// <summary>
/// Местное время челленджа. Всё время берётся через TimeProvider,
/// чтобы его можно было подменить в тестах и в режиме симуляции.
/// </summary>
public class ChallengeClock
{
    private readonly TimeProvider _time;
    public TimeZoneInfo Zone { get; }

    public ChallengeClock(TimeProvider time, string timeZoneId)
    {
        _time = time;
        Zone = ResolveZone(timeZoneId);
    }

    /// <summary>
    /// Ищет часовой пояс. На Windows IANA-имя может не находиться,
    /// тогда для Ташкента пробуем "West Asia Standard Time" (тоже UTC+5).
    /// </summary>
    public static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            if (id == "Asia/Tashkent")
                return TimeZoneInfo.FindSystemTimeZoneById("West Asia Standard Time");
            throw;
        }
    }

    /// <summary>Текущее местное время.</summary>
    public DateTime Now => ToLocal(_time.GetUtcNow());

    /// <summary>Сегодняшняя местная дата.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Перевод момента времени в местное время.</summary>
    public DateTime ToLocal(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, Zone).DateTime;

    /// <summary>Местная дата для момента времени (например, для message.date).</summary>
    public DateOnly ToLocalDate(DateTimeOffset moment) =>
        DateOnly.FromDateTime(ToLocal(moment));
}
