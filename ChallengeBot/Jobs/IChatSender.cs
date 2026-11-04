using ChallengeBot.Data;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Jobs;

/// <summary>Куда планировщик отправляет сообщения. Текст в формате HTML (так работают упоминания).</summary>
public interface IChatSender
{
    Task SendAsync(string htmlText, CancellationToken ct);
}

/// <summary>Хранит клиент Telegram, когда бот подключился. Планировщик ждёт этого момента.</summary>
public class BotClientHolder
{
    private readonly TaskCompletionSource<ITelegramBotClient> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ITelegramBotClient> WhenReady => _ready.Task;

    public void Set(ITelegramBotClient client) => _ready.TrySetResult(client);
}

/// <summary>Отправка в тему «Отчёты» (или в общий чат, если тем нет).</summary>
public class TelegramChatSender : IChatSender
{
    private readonly ChallengeStore _store;
    private readonly BotClientHolder _holder;

    public TelegramChatSender(ChallengeStore store, BotClientHolder holder)
    {
        _store = store;
        _holder = holder;
    }

    public async Task SendAsync(string htmlText, CancellationToken ct)
    {
        var target = await _store.GetReportsTargetAsync();
        if (target is null)
            return; // /setup ещё не выполняли, писать некуда

        var bot = await _holder.WhenReady.WaitAsync(ct);
        await bot.SendMessage(target.Value.ChatId, htmlText,
            parseMode: ParseMode.Html, messageThreadId: target.Value.ThreadId, cancellationToken: ct);
    }
}
