using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Logic;

namespace ChallengeBot.Texts;

/// <summary>
/// Все тексты бота в одном месте, чтобы их было легко читать и менять.
/// Тон нейтральный и поддерживающий: бот не стыдит за пропуски и не даёт советов по упражнениям.
/// </summary>
public static class Messages
{
    public const string Help =
        "Правила челленджа:\n" +
        "• Каждый день видео 30–60 секунд в теме «Отчёты» до 22:00.\n" +
        "• Подпись: «День N/60 - готово. Самочувствие X/10». Для щадящего режима добавь слово «щадящий».\n" +
        "• При острой боли видео не нужно: возьми /sick (больничных 3 на весь челлендж, без штрафа).\n\n" +
        "Команды:\n" +
        "/join — вступить в челлендж\n" +
        "/status — мой прогресс\n" +
        "/table — таблица группы за 7 дней\n" +
        "/bank — штрафы и банк\n" +
        "/sick — больничный на сегодня\n" +
        "/help — эта справка\n\n" +
        "Для админов: /setup, /pair, /mark, /setstart, /export";

    public const string NotSetUp =
        "Бот ещё не настроен: админ должен выполнить /setup в теме «Отчёты».";

    public const string SetupNeedsTopic =
        "Выполни /setup внутри темы «Отчёты» (не в общем чате).";

    public const string AdminOnly = "Эта команда только для админов.";

    public const string PrivateChatHint =
        "Привет! Я работаю в группе челленджа. Список команд: /help";

    public static string SetupDone => "Готово: отчёты принимаю в этой теме.";

    public static string AlreadyJoined(DateOnly start) =>
        $"Ты уже в челлендже. Твой день 1: {Date(start)}.";

    public static string Joined(DateOnly start, bool startsToday) =>
        startsToday
            ? $"Добро пожаловать! Твой день 1 — сегодня, {Date(start)}. Видео присылай в тему «Отчёты» до 22:00."
            : $"Добро пожаловать! Новички стартуют в понедельник, поэтому твой день 1 — {Date(start)}. До этого дня отчёты не нужны.";

    public static string NotRegistered =>
        "Сначала вступи в челлендж командой /join.";

    public static string ChallengeNotStarted(DateOnly start) =>
        $"Твой день 1 начнётся {Date(start)}. Отчёт пока не нужен.";

    public static string ChallengeFinished(int days) =>
        $"Твои {days} дней уже закончились, этот отчёт не засчитан.";

    public static string VideoTooShort(int minSeconds) =>
        $"Видео короче {minSeconds} секунд, не засчитал. Запиши чуть подольше.";

    public static string VideoNoteRequired =>
        "Сейчас принимаются только кружки (видеосообщения), обычное видео не засчитал.";

    public static string Forwarded =>
        "Пересланное видео не засчитывается. Запиши видео сегодня.";

    public static string AlreadyCounted(int day, int totalDays) =>
        $"День {day}/{totalDays} уже засчитан.";

    public static string DayClosed =>
        "Время отчёта за этот день прошло. Если это ошибка, напиши админу: он может поправить через /mark.";

    public static string Accepted(int day, int totalDays) =>
        $"День {day}/{totalDays} принят";

    public static string SuggestGentle =>
        "Вижу, что сегодня самочувствие не лучшее. Можно сделать день щадящим: просто добавь слово «щадящий» в подпись следующего видео. " +
        "А если совсем плохо, есть /sick.";

    public static string SuggestDoctor =>
        "Уже три дня самочувствие 4/10 или ниже. Если это не проходит, стоит показаться врачу. Береги себя.";

    public static string Status(Participant p, int dayNumber, int totalDays, ParticipantStats stats, ChallengeOptions o)
    {
        var header = dayNumber switch
        {
            < 1 => $"Твой день 1 начнётся {Date(p.StartDate)}.",
            _ when dayNumber > totalDays => $"Челлендж пройден: {totalDays} дней позади.",
            _ => $"День {dayNumber}/{totalDays} (старт {Date(p.StartDate)})."
        };

        return $"{p.DisplayName}\n{header}\n" +
               $"Выполнено: {stats.Completed} (д: {stats.Done}, щ: {stats.Gentle})\n" +
               $"Больничные: использовано {stats.Sick}, осталось {stats.SickRemaining} из {o.SickLimit}\n" +
               $"Пропуски: {stats.Miss}, штраф: {Money(stats.Fines, o)}";
    }

    public static string Money(decimal amount, ChallengeOptions o) => $"{amount:0.##} {o.Currency}";

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy");
}
