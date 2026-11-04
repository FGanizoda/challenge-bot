using ChallengeBot.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Handlers;

/// <summary>Фоновая служба: подключается к Telegram и слушает сообщения (long polling).</summary>
public class BotService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly BotUpdateHandler _handler;
    private readonly BotClientHolder _holder;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BotService> _log;

    public BotService(IConfiguration config, BotUpdateHandler handler, BotClientHolder holder,
        IHostApplicationLifetime lifetime, ILogger<BotService> log)
    {
        _config = config;
        _handler = handler;
        _holder = holder;
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

        // После перезагрузки компьютера интернет может появиться не сразу, поэтому пробуем снова и снова.
        // Останавливаемся только если Telegram прямо ответил, что токен неверный.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var me = await bot.GetMe(stoppingToken);
                _handler.BotUsername = me.Username ?? "";
                _holder.Set(bot); // теперь планировщик может писать в группу
                _log.LogInformation("Бот запущен: @{Username}", me.Username);
                break;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 401)
            {
                _log.LogError("Telegram не принял токен (401). Проверь BOT_TOKEN или appsettings.Local.json.");
                _lifetime.StopApplication();
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Пока не удалось подключиться к Telegram, повторю через 30 секунд: {Reason}", ex.Message);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // Слушаем только сообщения. Накопившиеся за время простоя обновления придут при старте.
        // При обрывах связи библиотека сама переподключается.
        var receiverOptions = new ReceiverOptions { AllowedUpdates = [UpdateType.Message] };
        await bot.ReceiveAsync(_handler, receiverOptions, stoppingToken);
    }
}
