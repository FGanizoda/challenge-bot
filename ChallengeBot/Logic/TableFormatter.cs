using System.Net;
using System.Text;

namespace ChallengeBot.Logic;

/// <summary>Один участник для таблицы группы.</summary>
public record TableRow(string Name, DateOnly StartDate, IReadOnlyDictionary<DateOnly, DayStatus> Days);

/// <summary>Компактная таблица группы за последние дни. Выводится моноширинным текстом (HTML &lt;pre&gt;).</summary>
public static class TableFormatter
{
    private const int MaxNameLength = 10;

    /// <summary>Одна клетка: буква статуса, «.» если записи ещё нет, «-» если день вне челленджа участника.</summary>
    public static string Cell(TableRow row, DateOnly date, int totalDays)
    {
        if (row.Days.TryGetValue(date, out var status))
            return status.Code();

        var dayNumber = DayCalculator.GetDayNumber(row.StartDate, date);
        return DayCalculator.IsInRange(dayNumber, totalDays) ? "." : "-";
    }

    public static string Build(IReadOnlyList<TableRow> rows, DateOnly today, int totalDays, int windowDays = 7)
    {
        var dates = Enumerable.Range(0, windowDays).Select(i => today.AddDays(i - windowDays + 1)).ToList();
        int nameWidth = Math.Min(MaxNameLength, Math.Max(4, rows.Select(r => r.Name.Length).DefaultIfEmpty(4).Max()));

        var sb = new StringBuilder();
        sb.AppendLine("<pre>");

        // Заголовок: числа месяца
        sb.Append(new string(' ', nameWidth + 1));
        sb.AppendLine(string.Join(" ", dates.Select(d => d.Day.ToString("00"))) + "  итого");

        foreach (var row in rows)
        {
            var name = row.Name.Length > nameWidth ? row.Name[..nameWidth] : row.Name.PadRight(nameWidth);
            var cells = dates.Select(d => Cell(row, d, totalDays).PadLeft(2));

            // Общий итог за весь челлендж: выполнено / больничных / пропусков
            var all = row.Days.Values;
            int completed = all.Count(s => s.IsCompleted());
            int sick = all.Count(s => s == DayStatus.Sick);
            int miss = all.Count(s => s == DayStatus.Miss);

            sb.AppendLine($"{WebUtility.HtmlEncode(name)} {string.Join(" ", cells)}  {completed}/{sick}/{miss}");
        }

        sb.AppendLine("</pre>");
        sb.Append("д выполнено, щ щадящий, б больничный, п пропуск, . ещё нет записи, - вне челленджа. ");
        sb.Append("Итого: выполнено/больничных/пропусков за весь челлендж.");
        return sb.ToString();
    }
}
