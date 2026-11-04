using System.Net;
using System.Text;
using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Logic;

namespace ChallengeBot.Texts;

/// <summary>Тексты сообщений по расписанию (формат HTML, имена экранируются).</summary>
public static class ScheduledMessages
{
    /// <summary>Упоминание человека: ссылка, по которой Telegram пришлёт ему уведомление.</summary>
    public static string Mention(Participant p) =>
        $"<a href=\"tg://user?id={p.UserId}\">{WebUtility.HtmlEncode(p.DisplayName)}</a>";

    public static string StageRange(int stage) => stage switch
    {
        1 => "дни 1–14",
        2 => "дни 15–40",
        _ => "дни 41–60"
    };

    public static string Morning(int groupDay, ChallengeOptions o,
        List<(Participant P, int Day)> active, List<Participant> notStarted, List<(Participant P, int Day)> measuring)
    {
        var sb = new StringBuilder();
        sb.AppendLine(groupDay >= 1
            ? $"Доброе утро! День {groupDay}/{o.Days}."
            : "Доброе утро!");

        foreach (var (p, day) in active)
            sb.AppendLine($"• {WebUtility.HtmlEncode(p.DisplayName)}: день {day}/{o.Days}, этап {DayCalculator.GetStage(day)} ({StageRange(DayCalculator.GetStage(day))})");

        foreach (var p in notStarted)
            sb.AppendLine($"• {WebUtility.HtmlEncode(p.DisplayName)}: старт {Messages.Date(p.StartDate)}");

        if (measuring.Count > 0)
        {
            var names = string.Join(", ", measuring.Select(m => $"{Mention(m.P)} (день {m.Day})"));
            sb.AppendLine();
            sb.AppendLine($"Сегодня контрольные замеры: {names}.");
            sb.AppendLine("Нужно записать: боль 0–10, наклон вперёд в см, фото осанки.");
        }

        sb.Append($"Отчёт до {o.Deadline:HH\\:mm}.");
        return sb.ToString();
    }

    public static string Reminder(List<Participant> notReported, TimeSpan left, ChallengeOptions o)
    {
        var names = string.Join(", ", notReported.Select(Mention));
        return $"Напоминание: до {o.Deadline:HH\\:mm} осталось {TimeLeft(left)}. Ещё нет отчёта: {names}.\n" +
               "Если сегодня болит, можно взять /sick.";
    }

    public static string TimeLeft(TimeSpan left)
    {
        var hours = (int)left.TotalHours;
        var minutes = left.Minutes;
        if (hours > 0 && minutes > 0) return $"{hours} ч {minutes} мин";
        if (hours > 0) return $"{hours} ч";
        return $"{Math.Max(minutes, 0)} мин";
    }

    /// <summary>Итог дедлайна: кто без отчёта и кого из напарников упомянуть.</summary>
    public static string Deadline(DateOnly date, bool isToday, List<(Participant Missed, Participant? Partner)> misses, ChallengeOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine(isToday
            ? $"Время отчёта вышло ({o.Deadline:HH\\:mm}). Без отчёта сегодня:"
            : $"Закрыт день {Messages.Date(date)}. Без отчёта:");

        foreach (var (missed, partner) in misses)
            sb.AppendLine(partner is null
                ? $"• {Mention(missed)}"
                : $"• {Mention(missed)} (напарник: {Mention(partner)})");

        sb.Append("Отмечено как пропуск. Напарники, напишите друзьям пару тёплых слов. Если было плохо со здоровьем, обсудите с группой.");
        return sb.ToString();
    }

    public static string Weekly(List<WeeklyLine> lines, decimal bankBalance, ChallengeOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Итоги недели:");
        foreach (var l in lines)
        {
            sb.AppendLine($"• {WebUtility.HtmlEncode(l.Name)}: {l.Completed}/{l.DaysInWeek} " +
                          $"(пропусков {l.Miss}, больничных {l.Sick}), больничных осталось {l.SickRemaining}");
        }
        sb.AppendLine();
        sb.AppendLine($"В банке: {Messages.Money(bankBalance, o)}.");
        sb.AppendLine();
        sb.Append("Как у вас с самочувствием? Что мешало заниматься на этой неделе? Расскажите в чате, если хочется поделиться.");
        return sb.ToString();
    }

    public static string NewPairs(List<PairGroup> groups, IReadOnlyDictionary<long, Participant> byId)
    {
        var sb = new StringBuilder("Напарники:\n");
        foreach (var g in groups)
            sb.AppendLine("• " + string.Join(" — ", g.Members.Select(id => byId.TryGetValue(id, out var p) ? Mention(p) : id.ToString())));
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Строка итогов недели по одному участнику.</summary>
public record WeeklyLine(string Name, int Completed, int DaysInWeek, int Miss, int Sick, int SickRemaining);
