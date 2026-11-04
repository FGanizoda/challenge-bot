using ChallengeBot.Config;
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

var host = builder.Build();

// Токен: переменная окружения BOT_TOKEN или BotToken из appsettings.Local.json.
// Само значение нигде не печатаем.
var token = builder.Configuration["BOT_TOKEN"] ?? builder.Configuration["BotToken"];
var options = host.Services.GetRequiredService<IOptions<ChallengeOptions>>().Value;
Console.WriteLine($"ChallengeBot: старт {options.StartDate}, дней {options.Days}, штраф {options.Fine} {options.Currency}.");
Console.WriteLine(string.IsNullOrWhiteSpace(token) ? "Токен не задан." : "Токен найден.");

await host.RunAsync();
