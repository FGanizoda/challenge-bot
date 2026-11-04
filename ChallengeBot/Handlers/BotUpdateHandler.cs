using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Handlers;

/// <summary>Принимает все обновления от Telegram и раздаёт их обработчикам.</summary>
public class BotUpdateHandler : IUpdateHandler
{
    private readonly CommandHandler _commands;
    private readonly ReportHandler _reports;
    private readonly ILogger<BotUpdateHandler> _log;

    /// <summary>Имя бота (без @), заполняется при старте. Нужно, чтобы понимать «/join@имя_бота».</summary>
    public string BotUsername { get; set; } = "";

    public BotUpdateHandler(CommandHandler commands, ReportHandler reports, ILogger<BotUpdateHandler> log)
    {
        _commands = commands;
        _reports = reports;
        _log = log;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var message = update.Message;
        if (message is null)
            return;

        try
        {
            var command = CommandHandler.Parse(message.Text, BotUsername);
            if (command is not null)
                await _commands.HandleAsync(bot, message, command.Value.Command, command.Value.Args, ct);
            else if (ReportHandler.IsVideo(message))
                await _reports.HandleAsync(bot, message, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Одна ошибка не должна останавливать бота.
            _log.LogError(ex, "Ошибка при обработке сообщения {MessageId}", message.MessageId);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        _log.LogWarning("Ошибка Telegram ({Source}): {Message}", source, exception.Message);
        return Task.CompletedTask;
    }
}
