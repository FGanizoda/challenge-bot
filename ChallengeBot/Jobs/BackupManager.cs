using ChallengeBot.Logic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChallengeBot.Jobs;

/// <summary>
/// Раз в сутки копирует challenge.db в папку backups/ и хранит последние 14 копий.
/// Копия делается средствами SQLite (безопасно, даже если бот в этот момент пишет в базу).
/// </summary>
public class BackupManager
{
    public const int KeepCount = 14;
    private const string Prefix = "challenge-";
    private const string Extension = ".db";

    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly ChallengeClock _clock;
    private readonly ILogger<BackupManager> _log;

    public BackupManager(string dbPath, string backupDir, ChallengeClock clock, ILogger<BackupManager> log)
    {
        _dbPath = dbPath;
        _backupDir = backupDir;
        _clock = clock;
        _log = log;
    }

    /// <summary>Имя файла копии за дату, например challenge-2026-11-09.db.</summary>
    public static string FileNameFor(DateOnly date) => $"{Prefix}{date:yyyy-MM-dd}{Extension}";

    /// <summary>Какие старые копии удалить, чтобы осталось не больше keep самых свежих. Имена сортируются по дате.</summary>
    public static List<string> FilesToDelete(IEnumerable<string> fileNames, int keep) =>
        fileNames
            .Where(n => n.StartsWith(Prefix) && n.EndsWith(Extension))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

    /// <summary>Делает копию за сегодня, если её ещё нет. Возвращает true, если копия создана.</summary>
    public bool RunIfNeeded()
    {
        if (!File.Exists(_dbPath))
            return false; // базы ещё нет (первый запуск)

        Directory.CreateDirectory(_backupDir);
        var target = Path.Combine(_backupDir, FileNameFor(_clock.Today));
        if (File.Exists(target))
            return false;

        // Сначала пишем во временный файл: недокопированный файл не должен выглядеть как готовая копия.
        var temp = target + ".tmp";
        File.Delete(temp);

        using (var source = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        using (var destination = new SqliteConnection($"Data Source={temp};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        File.Move(temp, target);

        _log.LogInformation("Резервная копия базы: {File}", Path.GetFileName(target));
        DeleteOld();
        return true;
    }

    private void DeleteOld()
    {
        var names = Directory.GetFiles(_backupDir).Select(Path.GetFileName).OfType<string>();
        foreach (var name in FilesToDelete(names, KeepCount))
        {
            File.Delete(Path.Combine(_backupDir, name));
            _log.LogInformation("Удалена старая копия: {File}", name);
        }
    }
}

/// <summary>Проверяет раз в час, сделана ли копия за сегодня. Первая проверка сразу при старте.</summary>
public class BackupService : BackgroundService
{
    private readonly BackupManager _manager;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupService> _log;

    public BackupService(BackupManager manager, TimeProvider time, ILogger<BackupService> log)
    {
        _manager = manager;
        _time = time;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), _time);
        try
        {
            do
            {
                try
                {
                    _manager.RunIfNeeded();
                }
                catch (Exception ex)
                {
                    // Сбой копии не должен ронять бота.
                    _log.LogError(ex, "Не удалось сделать резервную копию");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // обычная остановка
        }
    }
}
