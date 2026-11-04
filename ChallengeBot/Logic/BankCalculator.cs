namespace ChallengeBot.Logic;

/// <summary>Сводка по одному участнику.</summary>
public record ParticipantStats(int Done, int Gentle, int Sick, int Miss, int SickRemaining, decimal Fines)
{
    /// <summary>Выполненные дни (д + щ).</summary>
    public int Completed => Done + Gentle;
}

/// <summary>Подсчёт статистики, штрафов и банка.</summary>
public static class BankCalculator
{
    public static ParticipantStats CalculateStats(IEnumerable<DayStatus> statuses, int sickLimit, decimal fine)
    {
        int done = 0, gentle = 0, sick = 0, miss = 0;
        foreach (var status in statuses)
        {
            switch (status)
            {
                case DayStatus.Done: done++; break;
                case DayStatus.Gentle: gentle++; break;
                case DayStatus.Sick: sick++; break;
                case DayStatus.Miss: miss++; break;
            }
        }

        return new ParticipantStats(done, gentle, sick, miss,
            SickPolicy.Remaining(sickLimit, sick), miss * fine);
    }

    /// <summary>
    /// Банк = взносы всех участников + все штрафы.
    /// В нашей группе взноса нет (deposit = 0), тогда банк состоит только из штрафов.
    /// </summary>
    public static decimal Balance(int participantCount, int totalMisses, decimal deposit, decimal fine) =>
        participantCount * deposit + totalMisses * fine;
}
