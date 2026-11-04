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

    /// <summary>
    /// То же, но если сегодня понедельник и дедлайн уже прошёл, день 1 сразу стал бы пропуском.
    /// Поэтому в этом случае старт переносится на следующий понедельник.
    /// </summary>
    public static DateOnly PlanStart(DateTime nowLocal, DateOnly challengeStart, TimeOnly deadline)
    {
        var today = DateOnly.FromDateTime(nowLocal);
        var start = PlanStart(today, challengeStart);
        if (start == today && TimeOnly.FromDateTime(nowLocal) >= deadline)
            start = start.AddDays(7);
        return start;
    }

    public static DateOnly NextMondayOnOrAfter(DateOnly date)
    {
        // DayOfWeek: Sunday = 0, Monday = 1 ... Saturday = 6
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilMonday);
    }
}
