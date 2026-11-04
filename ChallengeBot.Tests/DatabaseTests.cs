using ChallengeBot.Data;
using ChallengeBot.Logic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBot.Tests;

/// <summary>Проверка, что схема создаётся и данные читаются обратно (SQLite в памяти).</summary>
public class DatabaseTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public DatabaseTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private ChallengeDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ChallengeDbContext>().UseSqlite(_connection).Options;
        return new ChallengeDbContext(options);
    }

    [Fact]
    public void SaveAndLoad_DayRecord_KeepsStatusAndDate()
    {
        using (var db = NewContext())
        {
            db.Database.EnsureCreated();
            db.Participants.Add(new Participant { UserId = 1, DisplayName = "Тест", StartDate = new DateOnly(2026, 11, 9) });
            db.Days.Add(new DayRecord { UserId = 1, Date = new DateOnly(2026, 11, 9), Status = DayStatus.Gentle, Wellbeing = 5 });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var day = db.Days.Single();
            Assert.Equal(DayStatus.Gentle, day.Status);
            Assert.Equal(new DateOnly(2026, 11, 9), day.Date);
            Assert.Equal(5, day.Wellbeing);
            Assert.Equal(new DateOnly(2026, 11, 9), db.Participants.Single().StartDate);
        }
    }

    [Fact]
    public void StatusIsStoredAsWord()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Days.Add(new DayRecord { UserId = 1, Date = new DateOnly(2026, 11, 9), Status = DayStatus.Miss });
        db.SaveChanges();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT status FROM days";
        Assert.Equal("miss", cmd.ExecuteScalar());
    }

    [Fact]
    public void SameUserSameDate_CannotBeStoredTwice()
    {
        var date = new DateOnly(2026, 11, 9);
        using (var db = NewContext())
        {
            db.Database.EnsureCreated();
            db.Days.Add(new DayRecord { UserId = 1, Date = date, Status = DayStatus.Done });
            db.SaveChanges();
        }

        // Новый контекст ничего не знает о первой записи, значит ошибку даст именно база.
        using var second = NewContext();
        second.Days.Add(new DayRecord { UserId = 1, Date = date, Status = DayStatus.Done });
        Assert.Throws<DbUpdateException>(() => second.SaveChanges());
    }
}
