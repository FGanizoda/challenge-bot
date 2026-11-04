namespace ChallengeBot.Logic;

/// <summary>Правила «больничных».</summary>
public static class SickPolicy
{
    /// <summary>Сколько «больничных» осталось (не меньше нуля).</summary>
    public static int Remaining(int limit, int used) => Math.Max(0, limit - used);

    /// <summary>Можно ли взять ещё один «больничный». Решает только лимит, штрафов нет.</summary>
    public static bool CanTake(int limit, int used) => Remaining(limit, used) > 0;
}
