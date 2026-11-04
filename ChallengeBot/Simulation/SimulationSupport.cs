using System.Net;
using System.Text;
using System.Text.Json;
using ChallengeBot.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace ChallengeBot.Simulation;

/// <summary>Часы, которыми управляет сценарий: время стоит на месте, пока его не передвинут.</summary>
public class SimulatedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public SimulatedTimeProvider(DateTimeOffset start) => _utcNow = start.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Set(DateTimeOffset moment) => _utcNow = moment.ToUniversalTime();
}

/// <summary>База SQLite в памяти для симуляции. Живёт, пока открыто соединение, поэтому «перезапуск» данные не теряет.</summary>
public sealed class SimulationDbFactory : IDbContextFactory<ChallengeDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SimulationDbFactory()
    {
        _connection.Open();
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public ChallengeDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ChallengeDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>Запрос, который бот отправил в «Telegram».</summary>
public record FakeCall(string Method, JsonElement? Body, int ContentLength);

/// <summary>
/// Подменяет сервер Telegram: запоминает всё, что бот пытается отправить, и отвечает как настоящий сервер.
/// Благодаря этому в симуляции и тестах работают настоящие обработчики и настоящая библиотека Telegram.Bot.
/// </summary>
public class FakeTelegramHandler : HttpMessageHandler
{
    public const string FakeToken = "123456:SIMULATION";

    private int _nextMessageId = 10_000;

    /// <summary>Все вызовы API по порядку.</summary>
    public List<FakeCall> Calls { get; } = [];

    /// <summary>Эмодзи, которые «сервер» отвергает как реакцию (чтобы проверить запасной вариант).</summary>
    public HashSet<string> RejectedReactions { get; } = [];

    /// <summary>Telegram id пользователей, которых «сервер» считает админами чата.</summary>
    public HashSet<long> ChatAdmins { get; } = [];

    /// <summary>Вызывается при каждом sendMessage: (текст, html ли).</summary>
    public Action<string>? OnMessageSent { get; set; }

    public IEnumerable<string> SentTexts =>
        Calls.Where(c => c.Method == "sendMessage").Select(c => c.Body!.Value.GetProperty("text").GetString() ?? "");

    public int Count(string method) => Calls.Count(c => c.Method == method);

    public TelegramBotClient CreateClient() => new(FakeToken, new HttpClient(this));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        JsonElement? body = null;
        int length = 0;

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            length = bytes.Length;
            if (request.Content.Headers.ContentType?.MediaType == "application/json" && length > 0)
                body = JsonDocument.Parse(bytes).RootElement.Clone();
        }

        Calls.Add(new FakeCall(method, body, length));
        return Respond(method, body);
    }

    private HttpResponseMessage Respond(string method, JsonElement? body)
    {
        switch (method)
        {
            case "getMe":
                return Ok("""{"id":777,"is_bot":true,"first_name":"Симуляция","username":"sim_bot"}""");

            case "sendMessage":
                OnMessageSent?.Invoke(body!.Value.GetProperty("text").GetString() ?? "");
                return Ok(FakeMessage());

            case "sendDocument":
                return Ok(FakeMessage());

            case "setMessageReaction":
                var emoji = ReactionEmoji(body);
                if (emoji is not null && RejectedReactions.Contains(emoji))
                    return Error("Bad Request: REACTION_INVALID");
                return Ok("true");

            case "getChatMember":
                var userId = body!.Value.GetProperty("user_id").GetInt64();
                var user = "{\"id\":" + userId + ",\"is_bot\":false,\"first_name\":\"Участник\"}";
                return Ok(ChatAdmins.Contains(userId)
                    ? "{\"status\":\"administrator\",\"user\":" + user +
                      ",\"can_be_edited\":false,\"is_anonymous\":false,\"can_manage_chat\":true," +
                      "\"can_delete_messages\":true,\"can_manage_video_chats\":true,\"can_restrict_members\":true," +
                      "\"can_promote_members\":false,\"can_change_info\":true,\"can_invite_users\":true}"
                    : "{\"status\":\"member\",\"user\":" + user + "}");

            default:
                return Ok("true");
        }
    }

    private static string? ReactionEmoji(JsonElement? body)
    {
        if (body is null || !body.Value.TryGetProperty("reaction", out var reactions) || reactions.GetArrayLength() == 0)
            return null;
        return reactions[0].GetProperty("emoji").GetString();
    }

    private string FakeMessage() =>
        "{\"message_id\":" + _nextMessageId++ + ",\"date\":0,\"chat\":{\"id\":-1001,\"type\":\"supergroup\"}}";

    private static HttpResponseMessage Ok(string result) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Error(string description) => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent("{\"ok\":false,\"error_code\":400,\"description\":\"" + description + "\"}", Encoding.UTF8, "application/json")
    };
}
