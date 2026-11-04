using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Handlers;

/// <summary>Фоновая служба: подключается к Telegram и слушает сообщения (long polling).</summary>
public class BotService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly BotUpdateHandler _handler;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BotService> _log;

    public BotService(IConfiguration config, BotUpdateHandler handler,
        IHostApplicationLifetime lifetime, ILogger<BotService> log)
    {
        _config = config;
        _handler = handler;
        _lifetime = lifetime;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Токен: переменная окружения BOT_TOKEN или BotToken из appsettings.Local.json.
        // В лог токен не пишем никогда.
        var token = _config["BOT_TOKEN"] ?? _config["BotToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogError("Токен бота не задан. Положи его в переменную окружения BOT_TOKEN или в appsettings.Local.json (см. README).");
            _lifetime.StopApplication();
            return;
        }

        var bot = new TelegramBotClient(token);

        try
        {
            var me = await bot.GetMe(stoppingToken);
            _handler.BotUsername = me.Username ?? "";
            _log.LogInformation("Бот запущен: @{Username}", me.Username);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError("Не удалось подключиться к Telegram (проверь токен и интернет): {Reason}", ex.Message);
            _lifetime.StopApplication();
            return;
        }

        // Слушаем только сообщения. Накопившиеся за время простоя обновления придут при старте.
        var receiverOptions = new ReceiverOptions { AllowedUpdates = [UpdateType.Message] };
        await bot.ReceiveAsync(_handler, receiverOptions, stoppingToken);
    }
}
