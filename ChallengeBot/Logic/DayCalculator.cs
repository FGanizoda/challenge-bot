namespace ChallengeBot.Logic;

/// <summary>Подсчёт номера дня участника.</summary>
public static class DayCalculator
{
    /// <summary>Номер дня = (местная дата − дата старта) + 1. Может быть меньше 1 или больше 60.</summary>
    public static int GetDayNumber(DateOnly startDate, DateOnly localDate) =>
        localDate.DayNumber - startDate.DayNumber + 1;

    /// <summary>Допустим ли номер дня (1..totalDays).</summary>
    public static bool IsInRange(int dayNumber, int totalDays) =>
        dayNumber >= 1 && dayNumber <= totalDays;

    /// <summary>Дата, на которую приходится заданный день участника.</summary>
    public static DateOnly DateOfDay(DateOnly startDate, int dayNumber) =>
        startDate.AddDays(dayNumber - 1);

    /// <summary>Этап челленджа по номеру дня: 1 (дни 1–14), 2 (15–40), 3 (41–60).</summary>
    public static int GetStage(int dayNumber) => dayNumber switch
    {
        <= 14 => 1,
        <= 40 => 2,
        _ => 3
    };

    /// <summary>Контрольные замеры делаются на днях 1, 30 и 60.</summary>
    public static bool IsMeasurementDay(int dayNumber) =>
        dayNumber is 1 or 30 or 60;
}
