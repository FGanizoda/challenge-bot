using System.Globalization;
using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Logic;
using ChallengeBot.Texts;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Handlers;

/// <summary>Команды только для админов: /pair, /mark, /setstart, /export.</summary>
public class AdminCommandHandler
{
    private readonly ChallengeStore _store;
    private readonly ChallengeClock _clock;
    private readonly ChallengeOptions _options;

    public AdminCommandHandler(ChallengeStore store, ChallengeClock clock, IOptions<ChallengeOptions> options)
    {
        _store = store;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>
    /// Админ: если в настройках задан AdminIds, то только они;
    /// если список пуст, то админы (и создатель) группы.
    /// </summary>
    public async Task<bool> IsAdminAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        if (_options.AdminIds.Count > 0)
            return _options.AdminIds.Contains(userId);

        var member = await bot.GetChatMember(chatId, userId, ct);
        return member.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
    }

    /// <summary>Обрабатывает админскую команду. Возвращает false, если команда не админская.</summary>
    public async Task<bool> TryHandleAsync(ITelegramBotClient bot, Message message, string command, string args, CancellationToken ct)
    {
        if (command is not ("pair" or "mark" or "setstart" or "export"))
            return false;

        if (message.From is null || !await IsAdminAsync(bot, message.Chat.Id, message.From.Id, ct))
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.AdminOnly, ct);
            return true;
        }

        switch (command)
        {
            case "pair": await PairAsync(bot, message, args, ct); break;
            case "mark": await MarkAsync(bot, message, args, ct); break;
            case "setstart": await SetStartAsync(bot, message, args, ct); break;
            case "export": await ExportAsync(bot, message, ct); break;
        }
        return true;
    }

    private static string[] Tokens(string args) =>
        args.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);

    // ---------- /pair @a @b ----------

    private async Task PairAsync(ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var t = Tokens(args);
        if (t.Length != 2)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.PairUsage, ct);
            return;
        }

        var a = await _store.FindParticipantAsync(t[0]);
        var b = await _store.FindParticipantAsync(t[1]);
        if (a is null || b is null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.UserNotFound(a is null ? t[0] : t[1]), ct);
            return;
        }
        if (a.UserId == b.UserId)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.PairSame, ct);
            return;
        }

        var active = await _store.GetActiveParticipantsAsync();
        var map = active.Where(p => p.PartnerUserId is not null)
            .ToDictionary(p => p.UserId, p => p.PartnerUserId!.Value);

        // Прежние напарники двух людей: если оба остались без пары, ставим их друг к другу.
        long? oldA = map.TryGetValue(a.UserId, out var partnerOfA) ? partnerOfA : null;
        long? oldB = map.TryGetValue(b.UserId, out var partnerOfB) ? partnerOfB : null;

        // Убираем ссылки на a и b у остальных, чтобы не осталось «висящих» связей.
        foreach (var key in map.Where(kv => kv.Value == a.UserId || kv.Value == b.UserId).Select(kv => kv.Key).ToList())
            map.Remove(key);
        map.Remove(a.UserId);
        map.Remove(b.UserId);

        map[a.UserId] = b.UserId;
        map[b.UserId] = a.UserId;

        if (oldA is long ca && oldB is long cb && ca != cb &&
            ca != a.UserId && ca != b.UserId && cb != a.UserId && cb != b.UserId)
        {
            map[ca] = cb;
            map[cb] = ca;
        }
        // Остальные, кто остался без напарника, получат его автоматически при очередной проверке расписания.

        await _store.SetPartnersAsync(map);
        await _store.LogAsync("pair", $"admin={message.From!.Id} {a.UserId}<->{b.UserId}");
        await BotHelpers.ReplyAsync(bot, message, Messages.PairSet(a.DisplayName, b.DisplayName), ct);
    }

    // ---------- /mark @user YYYY-MM-DD д|щ|б|п ----------

    private async Task MarkAsync(ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var t = Tokens(args);
        if (t.Length != 3)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.MarkUsage, ct);
            return;
        }

        var participant = await _store.FindParticipantAsync(t[0]);
        if (participant is null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.UserNotFound(t[0]), ct);
            return;
        }

        if (!TryParseDate(t[1], out var date))
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.BadDate, ct);
            return;
        }

        var status = DayStatusExtensions.FromCode(t[2]);
        if (status is null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.MarkUsage, ct);
            return;
        }

        var dayNumber = DayCalculator.GetDayNumber(participant.StartDate, date);
        if (!DayCalculator.IsInRange(dayNumber, _options.Days) || date > _clock.Today)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.MarkOutOfRange(participant.StartDate, _options.Days), ct);
            return;
        }

        await _store.UpsertDayAsync(new DayRecord
        {
            UserId = participant.UserId,
            Date = date,
            Status = status.Value,
            CreatedAt = DateTime.UtcNow
        });
        await _store.LogAsync("mark", $"admin={message.From!.Id} user={participant.UserId} date={date:yyyy-MM-dd} status={status}");

        await BotHelpers.ReplyAsync(bot, message, Messages.Marked(participant.DisplayName, date, dayNumber, status.Value), ct);
    }

    // ---------- /setstart @user YYYY-MM-DD ----------

    private async Task SetStartAsync(ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var t = Tokens(args);
        if (t.Length != 2)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.SetStartUsage, ct);
            return;
        }

        var participant = await _store.FindParticipantAsync(t[0]);
        if (participant is null)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.UserNotFound(t[0]), ct);
            return;
        }

        if (!TryParseDate(t[1], out var start))
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.BadDate, ct);
            return;
        }

        await _store.SetStartDateAsync(participant.UserId, start);
        await _store.LogAsync("setstart", $"admin={message.From!.Id} user={participant.UserId} start={start:yyyy-MM-dd}");

        var text = Messages.StartChanged(participant.DisplayName, start);
        if (start.DayOfWeek != DayOfWeek.Monday)
            text += Messages.StartNotMonday(start);
        await BotHelpers.ReplyAsync(bot, message, text, ct);
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // ---------- /export ----------

    private async Task ExportAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var participants = await _store.GetAllParticipantsAsync();
        var days = await _store.GetAllDaysAsync();
        var today = _clock.Today;

        var bytes = ExcelExporter.Build(participants, days, _options, today);
        await using var stream = new MemoryStream(bytes);

        await bot.SendDocument(message.Chat.Id,
            InputFile.FromStream(stream, $"challenge_{today:yyyy-MM-dd}.xlsx"),
            caption: Messages.ExportCaption(today),
            messageThreadId: BotHelpers.ThreadIdOf(message),
            cancellationToken: ct);
    }
}
