using ChallengeBot.Config;
using ChallengeBot.Data;
using ClosedXML.Excel;

namespace ChallengeBot.Logic;

/// <summary>Выгрузка в Excel: «Учёт по дням», «Итоги», «Участники». Не зависит от Telegram.</summary>
public static class ExcelExporter
{
    public static byte[] Build(IReadOnlyList<Participant> participants, IReadOnlyList<DayRecord> days,
        ChallengeOptions options, DateOnly today)
    {
        using var workbook = new XLWorkbook();
        var byUser = days.GroupBy(d => d.UserId).ToDictionary(g => g.Key, g => g.ToDictionary(d => d.Date, d => d.Status));
        var names = participants.ToDictionary(p => p.UserId, p => p.DisplayName);

        AddDaysSheet(workbook, participants, byUser, options);
        AddTotalsSheet(workbook, participants, byUser, options);
        AddParticipantsSheet(workbook, participants, names);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Dictionary<DateOnly, DayStatus> Mine(Dictionary<long, Dictionary<DateOnly, DayStatus>> byUser, long id) =>
        byUser.TryGetValue(id, out var d) ? d : [];

    // Лист 1: строки — участники, столбцы — даты, в клетках буквы статуса.
    private static void AddDaysSheet(XLWorkbook wb, IReadOnlyList<Participant> participants,
        Dictionary<long, Dictionary<DateOnly, DayStatus>> byUser, ChallengeOptions o)
    {
        var sheet = wb.Worksheets.Add("Учёт по дням");
        sheet.Cell(1, 1).Value = "Участник";

        if (participants.Count > 0)
        {
            var first = participants.Min(p => p.StartDate);
            var last = participants.Max(p => DayCalculator.DateOfDay(p.StartDate, o.Days));
            int column = 2;
            for (var date = first; date <= last; date = date.AddDays(1), column++)
            {
                sheet.Cell(1, column).Value = date.ToString("dd.MM.yy");
                int row = 2;
                foreach (var p in participants)
                {
                    var mine = Mine(byUser, p.UserId);
                    if (mine.TryGetValue(date, out var status))
                        sheet.Cell(row, column).Value = status.Code();
                    row++;
                }
            }
        }

        int r = 2;
        foreach (var p in participants)
            sheet.Cell(r++, 1).Value = p.DisplayName;

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.SheetView.FreezeColumns(1);
        sheet.Column(1).AdjustToContents();
    }

    // Лист 2: итоги по участникам.
    private static void AddTotalsSheet(XLWorkbook wb, IReadOnlyList<Participant> participants,
        Dictionary<long, Dictionary<DateOnly, DayStatus>> byUser, ChallengeOptions o)
    {
        var sheet = wb.Worksheets.Add("Итоги");
        string[] headers = ["Участник", "Выполнено", "д", "щ", "б", "п", "Больничных осталось", $"Штраф, {o.Currency}"];
        for (int i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        int row = 2;
        int totalMisses = 0;
        foreach (var p in participants)
        {
            var stats = BankCalculator.CalculateStats(Mine(byUser, p.UserId).Values, o.SickLimit, o.Fine);
            totalMisses += stats.Miss;
            sheet.Cell(row, 1).Value = p.DisplayName;
            sheet.Cell(row, 2).Value = stats.Completed;
            sheet.Cell(row, 3).Value = stats.Done;
            sheet.Cell(row, 4).Value = stats.Gentle;
            sheet.Cell(row, 5).Value = stats.Sick;
            sheet.Cell(row, 6).Value = stats.Miss;
            sheet.Cell(row, 7).Value = stats.SickRemaining;
            sheet.Cell(row, 8).Value = stats.Fines;
            row++;
        }

        row++;
        sheet.Cell(row, 1).Value = "Банк (взносы + штрафы)";
        sheet.Cell(row, 8).Value = BankCalculator.Balance(participants.Count, totalMisses, o.Deposit, o.Fine);

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }

    // Лист 3: список участников.
    private static void AddParticipantsSheet(XLWorkbook wb, IReadOnlyList<Participant> participants,
        Dictionary<long, string> names)
    {
        var sheet = wb.Worksheets.Add("Участники");
        string[] headers = ["Telegram id", "Username", "Имя", "Старт", "Напарник", "Активен", "Вступил (UTC)"];
        for (int i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        int row = 2;
        foreach (var p in participants)
        {
            sheet.Cell(row, 1).Value = p.UserId;
            sheet.Cell(row, 2).Value = p.Username is null ? "" : "@" + p.Username;
            sheet.Cell(row, 3).Value = p.DisplayName;
            sheet.Cell(row, 4).Value = p.StartDate.ToString("dd.MM.yyyy");
            sheet.Cell(row, 5).Value = p.PartnerUserId is long id && names.TryGetValue(id, out var partner) ? partner : "";
            sheet.Cell(row, 6).Value = p.Active ? "да" : "нет";
            sheet.Cell(row, 7).Value = p.JoinedAt.ToString("yyyy-MM-dd HH:mm");
            row++;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }
}
