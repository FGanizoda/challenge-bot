namespace ChallengeBot.Logic;

/// <summary>Группа напарников: обычно двое, при нечётном числе участников одна группа из троих.</summary>
public record PairGroup(IReadOnlyList<long> Members);

/// <summary>Автоматическая ротация пар раз в 2 недели.</summary>
public static class PairRotation
{
    /// <summary>Номер ротации: меняется каждые 2 недели от старта челленджа.</summary>
    public static int RotationIndex(DateOnly challengeStart, DateOnly date) =>
        Math.Max(0, (date.DayNumber - challengeStart.DayNumber) / 14);

    /// <summary>
    /// Строит пары методом «круга»: первый участник стоит на месте, остальные сдвигаются
    /// на rotationIndex позиций. Так в соседних ротациях напарники разные.
    /// При нечётном числе последний человек добавляется к последней паре (получается тройка).
    /// </summary>
    public static List<PairGroup> BuildGroups(IReadOnlyList<long> userIds, int rotationIndex)
    {
        var groups = new List<PairGroup>();
        if (userIds.Count < 2)
            return groups;

        var order = Shift(userIds, rotationIndex);

        for (int i = 0; i + 1 < order.Count; i += 2)
            groups.Add(new PairGroup([order[i], order[i + 1]]));

        // Остался один человек: добавляем его в последнюю пару.
        if (order.Count % 2 == 1)
        {
            var last = groups[^1].Members.Append(order[^1]).ToList();
            groups[^1] = new PairGroup(last);
        }

        return groups;
    }

    /// <summary>
    /// Кого отмечать напарнику каждого участника: в паре это второй человек,
    /// в тройке цепочка a → b → c → a.
    /// </summary>
    public static Dictionary<long, long> PartnerMap(IEnumerable<PairGroup> groups)
    {
        var map = new Dictionary<long, long>();
        foreach (var group in groups)
        {
            var m = group.Members;
            for (int i = 0; i < m.Count; i++)
                map[m[i]] = m[(i + 1) % m.Count];
        }
        return map;
    }

    // Первый остаётся, остальные сдвигаются по кругу.
    private static List<long> Shift(IReadOnlyList<long> ids, int rotationIndex)
    {
        var rest = ids.Skip(1).ToList();
        if (rest.Count > 0)
        {
            int shift = rotationIndex % rest.Count;
            rest = rest.Skip(shift).Concat(rest.Take(shift)).ToList();
        }
        return new List<long> { ids[0] }.Concat(rest).ToList();
    }
}
