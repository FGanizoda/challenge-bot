using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Handlers;
using ChallengeBot.Logic;
using ChallengeBot.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Tests;

/// <summary>Весь сценарий недели (режим --simulate) как один автоматический тест.</summary>
public class SimulationScenarioTests
{
    [Fact]
    public async Task WholeWeekScenario_Passes() =>
        Assert.Equal(0, await SimulationRunner.RunAsync(quiet: true));
}

/// <summary>Подтверждение отчёта: реакция ✅, запасная 👍, потом короткая фраза.</summary>
public class ReactionFallbackTests : IDisposable
{
    private readonly SimulationDbFactory _db = new();
    private readonly FakeTelegramHandler _telegram = new();
    private readonly ChallengeStore _store;
    private readonly ReportHandler _handler;
    private readonly SimulatedTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(5)));

    public ReactionFallbackTests()
    {
        var options = new ChallengeOptions();
        _store = new ChallengeStore(_db);
        _handler = new ReportHandler(_store, new ChallengeClock(_time, options.TimeZone),
            Options.Create(options), NullLogger<ReportHandler>.Instance);

        _store.SetSettingAsync(ChallengeStore.ChatIdKey, "-1001").Wait();
        _store.SetSettingAsync(ChallengeStore.ReportsThreadIdKey, "7").Wait();
        _store.SaveParticipantAsync(new Participant
        {
            UserId = 1, DisplayName = "Анна", StartDate = new DateOnly(2026, 11, 9), Active = true, JoinedAt = DateTime.UtcNow
        }).Wait();
    }

    public void Dispose() => _db.Dispose();

    private Task SendVideo(int messageId) => _handler.HandleAsync(_telegram.CreateClient(), new Message
    {
        Id = messageId,
        Date = _time.GetUtcNow().UtcDateTime,
        From = new User { Id = 1, FirstName = "Анна" },
        Chat = new Chat { Id = -1001, Type = ChatType.Supergroup },
        MessageThreadId = 7,
        IsTopicMessage = true,
        Caption = "День 1/60 - готово. Самочувствие 8/10",
        Video = new Video { FileId = "f", FileUniqueId = "u", Width = 1, Height = 1, Duration = 30 }
    }, default);

    [Fact]
    public async Task CheckMarkAccepted_NoTextReply()
    {
        await SendVideo(1);
        Assert.Equal(1, _telegram.Count("setMessageReaction"));
        Assert.Empty(_telegram.SentTexts);
    }

    [Fact]
    public async Task CheckMarkRejected_ThumbsUpIsUsed()
    {
        _telegram.RejectedReactions.Add("✅");
        await SendVideo(1);
        Assert.Equal(2, _telegram.Count("setMessageReaction"));
        Assert.Empty(_telegram.SentTexts);
    }

    [Fact]
    public async Task NoReactionsAtAll_ShortTextReply()
    {
        _telegram.RejectedReactions.Add("✅");
        _telegram.RejectedReactions.Add("👍");
        await SendVideo(1);
        Assert.Equal(["День 1/60 принят"], _telegram.SentTexts.ToArray());
        // День при этом засчитан.
        Assert.Equal(DayStatus.Done, (await _store.GetDayAsync(1, new DateOnly(2026, 11, 9)))!.Status);
    }

    [Fact]
    public async Task RequireVideoNote_RejectsRegularVideo()
    {
        var options = new ChallengeOptions { RequireVideoNote = true };
        var handler = new ReportHandler(_store, new ChallengeClock(_time, options.TimeZone),
            Options.Create(options), NullLogger<ReportHandler>.Instance);

        await handler.HandleAsync(_telegram.CreateClient(), new Message
        {
            Id = 5, Date = _time.GetUtcNow().UtcDateTime,
            From = new User { Id = 1, FirstName = "Анна" },
            Chat = new Chat { Id = -1001, Type = ChatType.Supergroup },
            MessageThreadId = 7, IsTopicMessage = true,
            Video = new Video { FileId = "f", FileUniqueId = "u", Width = 1, Height = 1, Duration = 30 }
        }, default);

        Assert.Contains("кружки", _telegram.SentTexts.Single());
        Assert.Null(await _store.GetDayAsync(1, new DateOnly(2026, 11, 9)));
    }
}
