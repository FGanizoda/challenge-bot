namespace ChallengeBot.Logic;

/// <summary>
/// Забота о здоровье. Бот не даёт советов по упражнениям и диагнозам,
/// только мягко предлагает щадящий режим или визит к врачу.
/// </summary>
public static class HealthAdvisor
{
    public const int LowWellbeing = 4;
    public const int DaysInRowForDoctor = 3;

    /// <summary>Самочувствие 4/10 и ниже: предложить щадящий режим.</summary>
    public static bool ShouldSuggestGentle(int? wellbeing) =>
        wellbeing is not null && wellbeing <= LowWellbeing;

    /// <summary>
    /// Три дня подряд (по календарю) самочувствие 4/10 и ниже: предложить показаться врачу.
    /// В списке даты и самочувствие последних отчётов, включая сегодняшний.
    /// </summary>
    public static bool ShouldSuggestDoctor(IEnumerable<(DateOnly Date, int? Wellbeing)> recent, DateOnly today)
    {
        var byDate = recent
            .Where(r => r.Wellbeing is not null)
            .GroupBy(r => r.Date)
            .ToDictionary(g => g.Key, g => g.First().Wellbeing!.Value);

        for (int i = 0; i < DaysInRowForDoctor; i++)
        {
            if (!byDate.TryGetValue(today.AddDays(-i), out var value) || value > LowWellbeing)
                return false;
        }
        return true;
    }
}
