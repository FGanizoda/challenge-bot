using ChallengeBot.Logic;

namespace ChallengeBot.Data;

/// <summary>Участник челленджа (таблица participants).</summary>
public class Participant
{
    public long UserId { get; set; }
    public string? Username { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>Дата начала (день 1) именно у этого участника.</summary>
    public DateOnly StartDate { get; set; }

    public long? PartnerUserId { get; set; }
    public bool Active { get; set; } = true;
    public DateTime JoinedAt { get; set; }
}

/// <summary>Запись за день (таблица days). Ключ: участник + дата.</summary>
public class DayRecord
{
    public long UserId { get; set; }
    public DateOnly Date { get; set; }
    public DayStatus Status { get; set; }

    /// <summary>Самочувствие 0–10 из подписи, если было.</summary>
    public int? Wellbeing { get; set; }

    /// <summary>Id сообщения с видео. Само видео бот не хранит.</summary>
    public int? MessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Настройка вида ключ-значение (таблица settings): chat_id, reports_thread_id.</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Журнал событий для отладки и разбора споров (таблица events).</summary>
public class EventLog
{
    public long Id { get; set; }
    public DateTime Ts { get; set; }
    public string Kind { get; set; } = "";
    public string? Payload { get; set; }
}
