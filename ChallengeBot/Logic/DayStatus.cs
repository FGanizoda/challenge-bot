namespace ChallengeBot.Logic;

/// <summary>Статус дня участника.</summary>
public enum DayStatus
{
    /// <summary>д — выполнено.</summary>
    Done,
    /// <summary>щ — щадящий режим (засчитывается как выполненный).</summary>
    Gentle,
    /// <summary>б — больничный (без штрафа).</summary>
    Sick,
    /// <summary>п — пропуск без причины (штраф).</summary>
    Miss
}

public static class DayStatusExtensions
{
    /// <summary>Буква из таблицы учёта группы.</summary>
    public static string Code(this DayStatus status) => status switch
    {
        DayStatus.Done => "д",
        DayStatus.Gentle => "щ",
        DayStatus.Sick => "б",
        DayStatus.Miss => "п",
        _ => "?"
    };

    /// <summary>Разбор буквы (для /mark). Возвращает null, если буква неизвестна.</summary>
    public static DayStatus? FromCode(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "д" => DayStatus.Done,
        "щ" => DayStatus.Gentle,
        "б" => DayStatus.Sick,
        "п" => DayStatus.Miss,
        _ => null
    };

    /// <summary>Считается ли день выполненным (д или щ).</summary>
    public static bool IsCompleted(this DayStatus status) =>
        status is DayStatus.Done or DayStatus.Gentle;
}
