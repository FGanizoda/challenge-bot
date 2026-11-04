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

    public CommandHandler(ChallengeStore store, ChallengeClock clock, IOptions<ChallengeOptions> options)
    {
        _store = store;
        _clock = clock;
        _options = options.Value;
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
        }
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

        if (message.From is null || !await IsAdminAsync(bot, message.Chat.Id, message.From.Id, ct))
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
        var start = StartDatePlanner.PlanStart(today, _options.StartDate);

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

    // ---------- Админы ----------

    /// <summary>
    /// Админ: если в настройках задан ADMIN_IDS, то только они;
    /// если список пуст, то админы (и создатель) группы.
    /// </summary>
    public async Task<bool> IsAdminAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        if (_options.AdminIds.Count > 0)
            return _options.AdminIds.Contains(userId);

        var member = await bot.GetChatMember(chatId, userId, ct);
        return member.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
    }
}
