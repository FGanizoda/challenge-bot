using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Logic;
using ChallengeBot.Texts;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Handlers;

/// <summary>Команды бота: /setup, /join, /status, /help (остальные добавятся в следующих этапах).</summary>
public class CommandHandler
{
    private readonly ChallengeStore _store;
    private readonly ChallengeClock _clock;
    private readonly ChallengeOptions _options;
    private readonly AdminCommandHandler _admin;

    public CommandHandler(ChallengeStore store, ChallengeClock clock, IOptions<ChallengeOptions> options,
        AdminCommandHandler admin)
    {
        _store = store;
        _clock = clock;
        _options = options.Value;
        _admin = admin;
    }

    /// <summary>
    /// Разбирает команду из текста: «/join@my_bot arg» → ("join", "arg").
    /// Возвращает null, если это не команда или она адресована другому боту.
    /// </summary>
    public static (string Command, string Args)? Parse(string? text, string botUsername)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '/')
            return null;

        var parts = text.Split([' ', '\n'], 2, StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0][1..];
        var args = parts.Length > 1 ? parts[1].Trim() : "";

        var at = head.IndexOf('@');
        if (at >= 0)
        {
            var addressedTo = head[(at + 1)..];
            if (!addressedTo.Equals(botUsername, StringComparison.OrdinalIgnoreCase))
                return null;
            head = head[..at];
        }

        return (head.ToLowerInvariant(), args);
    }

    public async Task HandleAsync(ITelegramBotClient bot, Message message, string command, string args, CancellationToken ct)
    {
        // В личке работает только справка.
        if (message.Chat.Type == ChatType.Private)
        {
            var text = command is "start" or "help" ? Messages.Help : Messages.PrivateChatHint;
            await BotHelpers.ReplyAsync(bot, message, text, ct);
            return;
        }

        switch (command)
        {
            case "help":
            case "start":
                await BotHelpers.ReplyAsync(bot, message, Messages.Help, ct);
                break;
            case "setup":
                await SetupAsync(bot, message, ct);
                break;
            case "join":
                await JoinAsync(bot, message, ct);
                break;
            case "status":
                await StatusAsync(bot, message, ct);
                break;
            case "sick":
                await SickAsync(bot, message, ct);
                break;
            case "table":
                await TableAsync(bot, message, ct);
                break;
            case "bank":
                await BankAsync(bot, message, ct);
                break;
            default:
                // /pair, /mark, /setstart, /export
                await _admin.TryHandleAsync(bot, message, command, args, ct);
                break;
        }
    }

    // ---------- /sick ----------

    private async Task SickAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var user = message.From;
        if (user is null)
            return;

        var participant = await _store.GetParticipantAsync(user.Id);
        if (participant is null || !participant.Active)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.NotRegistered, ct);
            return;
        }

        var today = _clock.Today;
        var dayNumber = DayCalculator.GetDayNumber(participant.StartDate, today);
        if (!DayCalculator.IsInRange(dayNumber, _options.Days))
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.SickNotToday(participant.StartDate, dayNumber, _options.Days), ct);
            return;
        }

        var existing = await _store.GetDayAsync(user.Id, today);
        if (existing is not null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.SickAlreadyHasRecord(existing.Status), ct);
            return;
        }

        var days = await _store.GetDaysAsync(user.Id);
        var used = days.Count(d => d.Status == DayStatus.Sick);

        if (!SickPolicy.CanTake(_options.SickLimit, used))
        {
            // Лимит исчерпан: ничего не штрафуем автоматически. Ставим отметку, чтобы дедлайн не записал пропуск,
            // а решение осталось за группой и админом (/mark).
            await _store.SetSettingAsync(ChallengeStore.SickOverLimitKey(user.Id, today), "1");
            await _store.LogAsync("sick_over_limit", $"user={user.Id} date={today:yyyy-MM-dd}");
            await BotHelpers.ReplyAsync(bot, message, Messages.SickLimitReached(_options.SickLimit), ct);
            return;
        }

        var added = await _store.TryAddDayAsync(new DayRecord
        {
            UserId = user.Id,
            Date = today,
            Status = DayStatus.Sick,
            CreatedAt = DateTime.UtcNow
        });
        if (!added)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.SickAlreadyHasRecord(DayStatus.Sick), ct);
            return;
        }

        await _store.LogAsync("sick", $"user={user.Id} date={today:yyyy-MM-dd}");
        await BotHelpers.ReplyAsync(bot, message,
            Messages.SickTaken(SickPolicy.Remaining(_options.SickLimit, used + 1), _options.SickLimit), ct);
    }

    // ---------- /table ----------

    private async Task TableAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var participants = await _store.GetActiveParticipantsAsync();
        if (participants.Count == 0)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.NoParticipants, ct);
            return;
        }

        var allDays = await _store.GetAllDaysAsync();
        var rows = participants.Select(p => new TableRow(
            p.DisplayName,
            p.StartDate,
            allDays.Where(d => d.UserId == p.UserId).ToDictionary(d => d.Date, d => d.Status))).ToList();

        await BotHelpers.ReplyHtmlAsync(bot, message, TableFormatter.Build(rows, _clock.Today, _options.Days), ct);
    }

    // ---------- /bank ----------

    private async Task BankAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var participants = await _store.GetActiveParticipantsAsync();
        if (participants.Count == 0)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.NoParticipants, ct);
            return;
        }

        var allDays = await _store.GetAllDaysAsync();
        var fines = participants
            .Select(p => (p.DisplayName, allDays.Count(d => d.UserId == p.UserId && d.Status == DayStatus.Miss)))
            .ToList();

        await BotHelpers.ReplyAsync(bot, message, Messages.Bank(participants.Count, fines, _options), ct);
    }

    // ---------- /setup ----------

    private async Task SetupAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var threadId = BotHelpers.ThreadIdOf(message);
        if (threadId is null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.SetupNeedsTopic, ct);
            return;
        }

        if (message.From is null || !await _admin.IsAdminAsync(bot, message.Chat.Id, message.From.Id, ct))
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.AdminOnly, ct);
            return;
        }

        await _store.SetSettingAsync(ChallengeStore.ChatIdKey, message.Chat.Id.ToString());
        await _store.SetSettingAsync(ChallengeStore.ReportsThreadIdKey, threadId.Value.ToString());
        await _store.LogAsync("setup", $"chat={message.Chat.Id} thread={threadId}");

        await BotHelpers.ReplyAsync(bot, message, Messages.SetupDone, ct);
    }

    // ---------- /join ----------

    private async Task JoinAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var user = message.From;
        if (user is null)
            return;

        if (await _store.GetReportsTargetAsync() is null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.NotSetUp, ct);
            return;
        }

        var existing = await _store.GetParticipantAsync(user.Id);
        if (existing is { Active: true })
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.AlreadyJoined(existing.StartDate), ct);
            return;
        }

        // Новички стартуют в понедельник: если сегодня другой день, ждём ближайший понедельник.
        var today = _clock.Today;
        var start = StartDatePlanner.PlanStart(_clock.Now, _options.StartDate, _options.Deadline);

        await _store.SaveParticipantAsync(new Participant
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = BotHelpers.DisplayNameOf(user),
            StartDate = start,
            Active = true,
            JoinedAt = DateTime.UtcNow
        });
        await _store.LogAsync("join", $"user={user.Id} start={start:yyyy-MM-dd}");

        await BotHelpers.ReplyAsync(bot, message, Messages.Joined(start, start == today), ct);
    }

    // ---------- /status ----------

    private async Task StatusAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var user = message.From;
        if (user is null)
            return;

        var participant = await _store.GetParticipantAsync(user.Id);
        if (participant is null || !participant.Active)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.NotRegistered, ct);
            return;
        }

        var days = await _store.GetDaysAsync(user.Id);
        var stats = BankCalculator.CalculateStats(days.Select(d => d.Status), _options.SickLimit, _options.Fine);
        var dayNumber = DayCalculator.GetDayNumber(participant.StartDate, _clock.Today);

        await BotHelpers.ReplyAsync(bot, message, Messages.Status(participant, dayNumber, _options.Days, stats, _options), ct);
    }
}
