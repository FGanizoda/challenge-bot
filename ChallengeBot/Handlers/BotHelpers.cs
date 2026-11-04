using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Handlers;

/// <summary>Мелкие помощники для работы с Telegram.</summary>
public static class BotHelpers
{
    /// <summary>
    /// Id темы, в которой написано сообщение. Если чат без тем (или это общий чат), вернёт null.
    /// В обычных группах MessageThreadId заполняется для ответов, поэтому смотрим на IsTopicMessage.
    /// </summary>
    public static int? ThreadIdOf(Message message) =>
        message.IsTopicMessage == true ? message.MessageThreadId : null;

    /// <summary>Время сообщения как DateTimeOffset (Telegram отдаёт UTC).</summary>
    public static DateTimeOffset SentAt(Message message) =>
        new(DateTime.SpecifyKind(message.Date, DateTimeKind.Utc));

    /// <summary>Ответ в тот же чат и ту же тему.</summary>
    public static Task<Message> ReplyAsync(ITelegramBotClient bot, Message message, string text, CancellationToken ct) =>
        bot.SendMessage(message.Chat.Id, text, messageThreadId: ThreadIdOf(message), cancellationToken: ct);

    /// <summary>Ответ в формате HTML (текст должен быть уже экранирован).</summary>
    public static Task<Message> ReplyHtmlAsync(ITelegramBotClient bot, Message message, string html, CancellationToken ct) =>
        bot.SendMessage(message.Chat.Id, html, parseMode: ParseMode.Html,
            messageThreadId: ThreadIdOf(message), cancellationToken: ct);

    /// <summary>Имя для показа: «Имя Фамилия», если нет, то @username, если нет, то id.</summary>
    public static string DisplayNameOf(User user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (name.Length > 0) return name;
        return user.Username is not null ? "@" + user.Username : user.Id.ToString();
    }
}
