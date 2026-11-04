using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Handlers;
using ChallengeBot.Jobs;
using ChallengeBot.Logic;
using ChallengeBot.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// Режим проверки: dotnet run -- --simulate. Бот в Telegram не подключается, база в памяти.
if (args.Contains("--simulate"))
    return await SimulationRunner.RunAsync();

// Как служба Windows программа стартует в папке System32. Переходим в папку программы,
// чтобы appsettings.json, challenge.db, logs/ и backups/ искались рядом с ChallengeBot.exe.
if (WindowsServiceHelpers.IsWindowsService())
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}
else if (!File.Exists("appsettings.json"))
{
    // Запуск из разработки не из папки проекта (например, dotnet run --project ChallengeBot из корня решения):
    // ищем папку с ChallengeBot.csproj выше bin/Debug/... и работаем в ней.
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ChallengeBot.csproj")))
        {
            Directory.SetCurrentDirectory(dir.FullName);
            break;
        }
    }
}

var builder = Host.CreateApplicationBuilder(args);

// Файл с токеном: лежит рядом с программой, в git не попадает.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Запуск как служба Windows (при обычном запуске из консоли ничего не меняется).
builder.Services.AddWindowsService(o => o.ServiceName = "ChallengeBot");

// Логи: в консоль и в файл logs/challenge-ГГГГММДД.log, новый файл каждый день, хранится 14 последних.
builder.Logging.AddSerilog(new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.File(Path.Combine("logs", "challenge-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true)
    .CreateLogger(), dispose: true);

builder.Services.Configure<ChallengeOptions>(
    builder.Configuration.GetSection(ChallengeOptions.SectionName));

// Подменяемые часы: в тестах и в режиме симуляции сюда подставят свои.
builder.Services.AddSingleton(TimeProvider.System);

// Часы челленджа: местное время с учётом часового пояса.
builder.Services.AddSingleton(sp => new ChallengeClock(
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IOptions<ChallengeOptions>>().Value.TimeZone));

// База данных: один файл challenge.db.
var connectionString = builder.Configuration.GetConnectionString("Challenge") ?? "Data Source=challenge.db";
builder.Services.AddDbContextFactory<ChallengeDbContext>(o => o.UseSqlite(connectionString));

// Резервные копии базы: папка backups/ рядом с challenge.db.
builder.Services.AddSingleton(sp =>
{
    var dbPath = Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);
    var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
    return new BackupManager(dbPath, backupDir, sp.GetRequiredService<ChallengeClock>(),
        sp.GetRequiredService<ILogger<BackupManager>>());
});

builder.Services.AddSingleton<ChallengeStore>();
builder.Services.AddSingleton<ReportHandler>();
builder.Services.AddSingleton<AdminCommandHandler>();
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddSingleton<BotUpdateHandler>();
builder.Services.AddSingleton<BotClientHolder>();
builder.Services.AddSingleton<IChatSender, TelegramChatSender>();
builder.Services.AddSingleton<ChallengeJobs>();
builder.Services.AddHostedService<BotService>();
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHostedService<BackupService>();

var host = builder.Build();

// Схему создаём без миграций.
using (var db = host.Services.GetRequiredService<IDbContextFactory<ChallengeDbContext>>().CreateDbContext())
    db.Database.EnsureCreated();

var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ChallengeBot");
var options = host.Services.GetRequiredService<IOptions<ChallengeOptions>>().Value;
log.LogInformation("ChallengeBot запускается. Старт челленджа {Start:dd.MM.yyyy}, дней {Days}, штраф {Fine} {Currency}, часовой пояс {Zone}.",
    options.StartDate, options.Days, options.Fine, options.Currency, options.TimeZone);

await host.RunAsync();
return 0;
