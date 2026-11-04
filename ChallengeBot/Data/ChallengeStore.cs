using Microsoft.EntityFrameworkCore;

namespace ChallengeBot.Data;

/// <summary>
/// Все обращения к базе в одном месте. Каждый метод открывает свой короткий контекст,
/// поэтому им можно пользоваться из разных потоков (бот и планировщик).
/// </summary>
public class ChallengeStore
{
    public const string ChatIdKey = "chat_id";
    public const string ReportsThreadIdKey = "reports_thread_id";

    private readonly IDbContextFactory<ChallengeDbContext> _factory;

    public ChallengeStore(IDbContextFactory<ChallengeDbContext> factory) => _factory = factory;

    // ---------- Настройки ----------

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return (await db.Settings.FindAsync(key))?.Value;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var setting = await db.Settings.FindAsync(key);
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;
        await db.SaveChangesAsync();
    }

    /// <summary>Сохранённый чат и тема «Отчёты». null, если /setup ещё не выполняли.</summary>
    public async Task<(long ChatId, int ThreadId)?> GetReportsTargetAsync()
    {
        var chat = await GetSettingAsync(ChatIdKey);
        var thread = await GetSettingAsync(ReportsThreadIdKey);
        if (long.TryParse(chat, out var chatId) && int.TryParse(thread, out var threadId))
            return (chatId, threadId);
        return null;
    }

    // ---------- Участники ----------

    public async Task<Participant?> GetParticipantAsync(long userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Participants.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
    }

    public async Task<List<Participant>> GetActiveParticipantsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Participants.AsNoTracking().Where(p => p.Active).OrderBy(p => p.JoinedAt).ToListAsync();
    }

    /// <summary>Добавляет участника или обновляет имя и дату старта у существующего.</summary>
    public async Task SaveParticipantAsync(Participant participant)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Participants.FindAsync(participant.UserId);
        if (existing is null)
        {
            db.Participants.Add(participant);
        }
        else
        {
            existing.Username = participant.Username;
            existing.DisplayName = participant.DisplayName;
            existing.StartDate = participant.StartDate;
            existing.Active = participant.Active;
        }
        await db.SaveChangesAsync();
    }

    // ---------- Дни ----------

    public async Task<DayRecord?> GetDayAsync(long userId, DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Days.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.Date == date);
    }

    public async Task<List<DayRecord>> GetDaysAsync(long userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Days.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.Date).ToListAsync();
    }

    /// <summary>Добавляет запись за день. Возвращает false, если запись за этот день уже есть.</summary>
    public async Task<bool> TryAddDayAsync(DayRecord record)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (await db.Days.AnyAsync(d => d.UserId == record.UserId && d.Date == record.Date))
            return false;

        db.Days.Add(record);
        await db.SaveChangesAsync();
        return true;
    }

    // ---------- Журнал ----------

    public async Task LogAsync(string kind, string? payload = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Events.Add(new EventLog { Ts = DateTime.UtcNow, Kind = kind, Payload = payload });
        await db.SaveChangesAsync();
    }
}
