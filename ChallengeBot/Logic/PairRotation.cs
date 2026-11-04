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

    /// <summary>
    /// Новичок встаёт в уже существующую цепочку: после первого участника.
    /// Пара (a, b) превращается в тройку a → новичок → b → a, остальные пары не трогаем.
    /// </summary>
    public static void AddNewcomer(Dictionary<long, long> partnerMap, long newcomerId)
    {
        if (partnerMap.Count == 0)
            return;

        var anchor = partnerMap.Keys.First();
        partnerMap[newcomerId] = partnerMap[anchor];
        partnerMap[anchor] = newcomerId;
    }

    /// <summary>Восстанавливает группы из карты «кто кого отмечает» (идём по цепочкам).</summary>
    public static List<PairGroup> GroupsFromPartnerMap(IReadOnlyDictionary<long, long> partnerMap)
    {
        var groups = new List<PairGroup>();
        var seen = new HashSet<long>();

        foreach (var start in partnerMap.Keys)
        {
            if (seen.Contains(start))
                continue;

            var members = new List<long>();
            var current = start;
            while (seen.Add(current))
            {
                members.Add(current);
                if (!partnerMap.TryGetValue(current, out current))
                    break;
            }
            groups.Add(new PairGroup(members));
        }
        return groups;
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
