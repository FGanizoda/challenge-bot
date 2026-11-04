namespace ChallengeBot.Config;

/// <summary>
/// Настройки челленджа. Читаются из секции "Challenge" в appsettings.json.
/// Токен бота здесь НЕ хранится: он берётся из переменной окружения BOT_TOKEN
/// или из файла appsettings.Local.json (ключ "BotToken").
/// </summary>
public class ChallengeOptions
{
    public const string SectionName = "Challenge";

    /// <summary>Часовой пояс. На Windows, если такого id нет, используется "West Asia Standard Time".</summary>
    public string TimeZone { get; set; } = "Asia/Tashkent";

    /// <summary>Дата первого дня челленджа (понедельник).</summary>
    public DateOnly StartDate { get; set; } = new(2026, 11, 9);

    /// <summary>Сколько дней длится челлендж.</summary>
    public int Days { get; set; } = 60;

    /// <summary>Время дедлайна отчёта (местное).</summary>
    public TimeOnly Deadline { get; set; } = new(22, 0);

    /// <summary>Время вечерних напоминаний.</summary>
    public List<TimeOnly> ReminderTimes { get; set; } = [new(18, 0), new(21, 0)];

    /// <summary>Время утреннего сообщения «День N/60».</summary>
    public TimeOnly MorningTime { get; set; } = new(8, 0);

    /// <summary>День недели для итогов недели.</summary>
    public DayOfWeek WeeklySummaryDay { get; set; } = DayOfWeek.Sunday;

    /// <summary>Время итогов недели.</summary>
    public TimeOnly WeeklySummaryTime { get; set; } = new(20, 0);

    /// <summary>Сколько «больничных» у человека на весь челлендж.</summary>
    public int SickLimit { get; set; } = 3;

    /// <summary>Взнос с человека. В нашей группе взноса нет, поэтому 0.</summary>
    public decimal Deposit { get; set; } = 0;

    /// <summary>Штраф за каждый пропуск без причины.</summary>
    public decimal Fine { get; set; } = 50;

    /// <summary>Название валюты для сообщений.</summary>
    public string Currency { get; set; } = "сомони";

    /// <summary>Минимальная длительность видео в секундах.</summary>
    public int MinVideoSeconds { get; set; } = 10;

    /// <summary>true = принимать только кружки (видеосообщения).</summary>
    public bool RequireVideoNote { get; set; } = false;

    /// <summary>Telegram id админов. Если пусто, админы — админы группы.</summary>
    public List<long> AdminIds { get; set; } = [];
}
