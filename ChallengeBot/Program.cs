using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Handlers;
using ChallengeBot.Jobs;
using ChallengeBot.Logic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Файл с токеном: лежит рядом с программой, в git не попадает.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.Configure<ChallengeOptions>(
    builder.Configuration.GetSection(ChallengeOptions.SectionName));

// Подменяемые часы: в тестах и в режиме симуляции сюда подставят свои.
builder.Services.AddSingleton(TimeProvider.System);

// Часы челленджа: местное время с учётом часового пояса.
builder.Services.AddSingleton(sp => new ChallengeClock(
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IOptions<ChallengeOptions>>().Value.TimeZone));

// База данных: один файл challenge.db.
builder.Services.AddDbContextFactory<ChallengeDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Challenge")));

builder.Services.AddSingleton<ChallengeStore>();
builder.Services.AddSingleton<ReportHandler>();
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddSingleton<BotUpdateHandler>();
builder.Services.AddSingleton<BotClientHolder>();
builder.Services.AddSingleton<IChatSender, TelegramChatSender>();
builder.Services.AddSingleton<ChallengeJobs>();
builder.Services.AddHostedService<BotService>();
builder.Services.AddHostedService<SchedulerService>();

var host = builder.Build();

// Схему создаём без миграций.
using (var db = host.Services.GetRequiredService<IDbContextFactory<ChallengeDbContext>>().CreateDbContext())
    db.Database.EnsureCreated();

// Токен: переменная окружения BOT_TOKEN или BotToken из appsettings.Local.json.
// Само значение нигде не печатаем.
var token = builder.Configuration["BOT_TOKEN"] ?? builder.Configuration["BotToken"];
var options = host.Services.GetRequiredService<IOptions<ChallengeOptions>>().Value;
Console.WriteLine($"ChallengeBot: старт {options.StartDate}, дней {options.Days}, штраф {options.Fine} {options.Currency}.");
Console.WriteLine(string.IsNullOrWhiteSpace(token) ? "Токен не задан." : "Токен найден.");

await host.RunAsync();
