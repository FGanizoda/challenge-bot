namespace ChallengeBot.Logic;

/// <summary>Выбор даты старта для нового участника.</summary>
public static class StartDatePlanner
{
    /// <summary>
    /// Новичок идёт со своим днём 1 в понедельник. Если сегодня понедельник, старт сегодня,
    /// иначе ближайший следующий понедельник. До общего старта челленджа старт не раньше его даты.
    /// </summary>
    public static DateOnly PlanStart(DateOnly today, DateOnly challengeStart)
    {
        var from = today > challengeStart ? today : challengeStart;
        return NextMondayOnOrAfter(from);
    }

    public static DateOnly NextMondayOnOrAfter(DateOnly date)
    {
        // DayOfWeek: Sunday = 0, Monday = 1 ... Saturday = 6
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilMonday);
    }
}
