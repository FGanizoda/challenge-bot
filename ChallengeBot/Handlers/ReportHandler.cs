using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Logic;
using ChallengeBot.Texts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace ChallengeBot.Handlers;

/// <summary>Приём видео-отчётов в теме «Отчёты».</summary>
public class ReportHandler
{
    private readonly ChallengeStore _store;
    private readonly ChallengeClock _clock;
    private readonly ChallengeOptions _options;
    private readonly ILogger<ReportHandler> _log;

    public ReportHandler(ChallengeStore store, ChallengeClock clock,
        IOptions<ChallengeOptions> options, ILogger<ReportHandler> log)
    {
        _store = store;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    /// <summary>Сообщение с видео или кружком? Тогда это кандидат на отчёт.</summary>
    public static bool IsVideo(Message message) => message.Video is not null || message.VideoNote is not null;

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        // 1. Сообщение должно быть в теме «Отчёты» сохранённого чата.
        var target = await _store.GetReportsTargetAsync();
        if (target is null)
            return;
        if (message.Chat.Id != target.Value.ChatId || BotHelpers.ThreadIdOf(message) != target.Value.ThreadId)
            return;

        var user = message.From;
        if (user is null || user.IsBot)
            return;

        // 2. Отправитель зарегистрирован.
        var participant = await _store.GetParticipantAsync(user.Id);
        if (participant is null || !participant.Active)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.NotRegistered, ct);
            return;
        }

        // 3. Само видео: тип, длительность, не пересланное.
        var problem = CheckVideo(message);
        if (problem is not null)
        {
            await BotHelpers.ReplyAsync(bot, message, problem, ct);
            return;
        }

        // 4. День участника: дата по message.date в нашем часовом поясе.
        var sentAt = BotHelpers.SentAt(message);
        var date = _clock.ToLocalDate(sentAt);
        var localTime = TimeOnly.FromDateTime(_clock.ToLocal(sentAt));
        var dayNumber = DayCalculator.GetDayNumber(participant.StartDate, date);

        if (dayNumber < 1)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.ChallengeNotStarted(participant.StartDate), ct);
            return;
        }
        if (dayNumber > _options.Days)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.ChallengeFinished(_options.Days), ct);
            return;
        }

        // 5. Один день засчитывается один раз; после дедлайна день закрыт.
        var existing = await _store.GetDayAsync(user.Id, date);
        var decision = ReportPolicy.Decide(existing?.Status, localTime, _options.Deadline);
        if (decision == ReportDecision.AlreadyCounted)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.AlreadyCounted(dayNumber, _options.Days), ct);
            return;
        }
        if (decision == ReportDecision.DayClosed)
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.DayClosed, ct);
            return;
        }

        // 6. Засчитываем.
        var caption = CaptionParser.Parse(message.Caption);
        var record = new DayRecord
        {
            UserId = user.Id,
            Date = date,
            Status = ReportPolicy.StatusFor(caption),
            Wellbeing = caption.Wellbeing,
            MessageId = message.MessageId,
            CreatedAt = DateTime.UtcNow
        };

        // Если запись успели создать параллельно, второй раз не засчитываем.
        if (!await _store.TryAddDayAsync(record))
        {
            await BotHelpers.ReplyAsync(bot, message, Messages.AlreadyCounted(dayNumber, _options.Days), ct);
            return;
        }

        await _store.LogAsync("report", $"user={user.Id} date={date:yyyy-MM-dd} day={dayNumber} status={record.Status}");

        await ConfirmAsync(bot, message, dayNumber, ct);
        await CareAsync(bot, message, user.Id, date, caption, ct);
    }

    /// <summary>Возвращает текст проблемы с видео или null, если всё в порядке.</summary>
    private string? CheckVideo(Message message)
    {
        if (message.ForwardOrigin is not null)
            return Messages.Forwarded;

        if (_options.RequireVideoNote && message.VideoNote is null)
            return Messages.VideoNoteRequired;

        int duration = message.VideoNote?.Duration ?? message.Video?.Duration ?? 0;
        if (duration < _options.MinVideoSeconds)
            return Messages.VideoTooShort(_options.MinVideoSeconds);

        return null;
    }

    /// <summary>
    /// Подтверждение: реакция ✅. Если Telegram её не разрешает в этом чате,
    /// пробуем 👍, а если и это не вышло, отвечаем короткой фразой.
    /// </summary>
    private async Task ConfirmAsync(ITelegramBotClient bot, Message message, int dayNumber, CancellationToken ct)
    {
        foreach (var emoji in new[] { "✅", "👍" })
        {
            try
            {
                await bot.SetMessageReaction(message.Chat.Id, message.MessageId,
                    [new ReactionTypeEmoji { Emoji = emoji }], cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex)
            {
                _log.LogInformation("Реакция {Emoji} не поставилась: {Reason}", emoji, ex.Message);
            }
        }

        await BotHelpers.ReplyAsync(bot, message, Messages.Accepted(dayNumber, _options.Days), ct);
    }

    /// <summary>Забота о здоровье: мягкие предложения при низком самочувствии.</summary>
    private async Task CareAsync(ITelegramBotClient bot, Message message, long userId,
        DateOnly date, CaptionInfo caption, CancellationToken ct)
    {
        if (!HealthAdvisor.ShouldSuggestGentle(caption.Wellbeing))
            return;

        // Три дня подряд 4/10 и ниже: предлагаем врача, иначе только щадящий режим.
        var days = await _store.GetDaysAsync(userId);
        var recent = days.Select(d => (d.Date, d.Wellbeing));
        var text = HealthAdvisor.ShouldSuggestDoctor(recent, date)
            ? Messages.SuggestDoctor
            : (caption.IsGentle ? null : Messages.SuggestGentle);

        if (text is not null)
            await BotHelpers.ReplyAsync(bot, message, text, ct);
    }
}
