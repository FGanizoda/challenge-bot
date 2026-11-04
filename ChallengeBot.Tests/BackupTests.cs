using ChallengeBot.Jobs;
using ChallengeBot.Logic;
using ChallengeBot.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChallengeBot.Tests;

public class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "challenge-bot-test-" + Guid.NewGuid().ToString("N"));
    private readonly SimulatedTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(5)));
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly BackupManager _manager;

    public BackupTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "challenge.db");
        _backupDir = Path.Combine(_dir, "backups");

        using (var db = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE t(x TEXT); INSERT INTO t VALUES ('привет');";
            cmd.ExecuteNonQuery();
        }

        _manager = new BackupManager(_dbPath, _backupDir, new ChallengeClock(_time, "Asia/Tashkent"),
            NullLogger<BackupManager>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Backup_IsCreated_AndContainsData()
    {
        Assert.True(_manager.RunIfNeeded());

        var file = Path.Combine(_backupDir, "challenge-2026-11-09.db");
        Assert.True(File.Exists(file));

        using var db = new SqliteConnection($"Data Source={file};Pooling=False");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT x FROM t";
        Assert.Equal("привет", cmd.ExecuteScalar());
    }

    [Fact]
    public void Backup_OnlyOncePerDay_NewFileNextDay()
    {
        Assert.True(_manager.RunIfNeeded());
        Assert.False(_manager.RunIfNeeded());

        _time.Set(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(5)));
        Assert.True(_manager.RunIfNeeded());
        Assert.Equal(2, Directory.GetFiles(_backupDir, "*.db").Length);
    }

    [Fact]
    public void Backup_NoDatabaseYet_DoesNothing()
    {
        File.Delete(_dbPath);
        Assert.False(_manager.RunIfNeeded());
        Assert.False(Directory.Exists(_backupDir));
    }

    [Fact]
    public void Backup_KeepsOnlyLastFourteen()
    {
        // 16 дней подряд: должно остаться 14 самых свежих.
        for (int i = 0; i < 16; i++)
        {
            _time.Set(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(5)).AddDays(i));
            _manager.RunIfNeeded();
        }

        var names = Directory.GetFiles(_backupDir, "*.db").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(14, names.Count);
        Assert.Equal("challenge-2026-11-11.db", names.First()); // два самых старых удалены
        Assert.Equal("challenge-2026-11-24.db", names.Last());
    }

    [Fact]
    public void FilesToDelete_IgnoresForeignFiles_AndKeepsNewest()
    {
        var names = new List<string> { "readme.txt", "challenge-2026-01-01.db", "challenge-2026-01-02.db", "challenge-2026-01-03.db" };
        var toDelete = BackupManager.FilesToDelete(names, keep: 2);
        Assert.Equal(["challenge-2026-01-01.db"], toDelete);
    }
}
