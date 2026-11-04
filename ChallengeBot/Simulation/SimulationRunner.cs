using System.Net;
using System.Text.RegularExpressions;
using ChallengeBot.Config;
using ChallengeBot.Data;
using ChallengeBot.Handlers;
using ChallengeBot.Jobs;
using ChallengeBot.Logic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChallengeBot.Simulation;

/// <summary>
/// Режим проверки: <c>dotnet run -- --simulate</c>.
/// Прогоняет неделю челленджа со всеми командами, дедлайнами и «выключением компьютера» без ожидания.
/// Работают настоящие обработчики и планировщик, но время подменено, а вместо Telegram стоит заглушка.
/// База в памяти, настоящая challenge.db не трогается. Каждая проверка печатается как ✔ или ✘.
/// </summary>
public class SimulationRunner
{
    private const long ChatId = -1001;
    private const int ReportsThread = 7;
    private static readonly TimeSpan Tashkent = TimeSpan.FromHours(5);
    private static readonly string[] DayNames = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];

    private readonly ChallengeOptions _options = new();
    private readonly SimulatedTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, Tashkent));
    private readonly SimulationDbFactory _db = new();
    private readonly FakeTelegramHandler _telegram = new();
    private readonly TelegramBotClient _bot;
    private readonly bool _quiet;

    // Всё это пересоздаётся при «перезапуске программы».
    private ChallengeStore _store = null!;
    private ChallengeJobs _jobs = null!;
    private BotUpdateHandler _updates = null!;

    private int _checks, _failed, _messageCounter = 1;
    private bool _printBotMessages = true;

    private readonly User _admin = new() { Id = 1000, FirstName = "Админ", Username = "admin" };
    private readonly User _anna = new() { Id = 1, FirstName = "Анна", Username = "anna" };
    private readonly User _bobur = new() { Id = 2, FirstName = "Бобур", Username = "bobur" };
    private readonly User _karim = new() { Id = 3, FirstName = "Карим", Username = "karim" };
    private readonly User _diloza = new() { Id = 4, FirstName = "Дилноза", Username = "diloza" };
    private readonly User _outsider = new() { Id = 99, FirstName = "Посторонний", Username = "outsider" };

    public SimulationRunner(bool quiet = false)
    {
        _quiet = quiet;
        _bot = _telegram.CreateClient();
        _telegram.ChatAdmins.Add(_admin.Id);
        _telegram.OnMessageSent = text =>
        {
            if (_printBotMessages) Print("      🤖 " + Plain(text).Replace("\n", "\n         "));
        };
    }

    public static async Task<int> RunAsync(bool quiet = false)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var runner = new SimulationRunner(quiet);
        return await runner.RunScenarioAsync();
    }

    // ---------- Сценарий ----------

    private async Task<int> RunScenarioAsync()
    {
        Print("Симуляция: неделя челленджа (старт 09.11.2026, часовой пояс Asia/Tashkent). База в памяти.\n");
        Boot();

        await Setup();
        await Monday();
        await Tuesday();
        await Wednesday();
        await Thursday();
        await Friday();
        await SaturdayAndSunday();
        await AdminCommands();
        await DowntimeAndRestart();

        Print($"\nИтог: проверок {_checks}, успешно {_checks - _failed}, не прошло {_failed}.");
        Print(_failed == 0 ? "Симуляция пройдена." : "Симуляция НЕ пройдена.");
        return _failed == 0 ? 0 : 1;
    }

    private async Task Setup()
    {
        Section("Воскресенье 08.11: настройка и вступление");
        SetLocal(8, 12, 0);

        await Say(_anna, "/join");
        Check(Last().Contains("/setup"), "до /setup бот просит админа настроить тему");

        await Say(_admin, "/setup");
        Check(await _store.GetReportsTargetAsync() == (ChatId, ReportsThread), "/setup запомнил чат и тему «Отчёты»");

        await Say(_anna, "/setup");
        Check(Last().Contains("только для админов"), "/setup от обычного участника отклонён");

        foreach (var u in new[] { _anna, _bobur, _karim, _diloza })
            await Say(u, "/join");

        var all = await _store.GetActiveParticipantsAsync();
        Check(all.Count == 4 && all.All(p => p.StartDate == new DateOnly(2026, 11, 9)),
            "4 участника, у всех день 1 — понедельник 09.11");

        await Say(_anna, "/join");
        Check(Last().Contains("уже в челлендже"), "повторный /join не создаёт дубль");

        await Tick();
        all = await _store.GetActiveParticipantsAsync();
        Check(all.All(p => p.PartnerUserId is not null), "каждому назначен напарник");
    }

    private async Task Monday()
    {
        Section("Понедельник 09.11 (день 1)");
        SetLocal(9, 8, 0);
        await Tick();
        Check(Sent("Доброе утро").Any(m => m.Contains("День 1/60")), "08:00: утреннее сообщение «День 1/60»");

        SetLocal(9, 10, 0);
        await Video(_anna, "День 1/60 - готово. Самочувствие 8/10");
        Check(await Status(_anna, 9) == DayStatus.Done, "видео Анны засчитано (д)");
        Check(_telegram.Count("setMessageReaction") == 1, "на видео поставлена реакция");

        var before = (await _store.GetDaysAsync(_anna.Id)).Count;
        await Video(_anna, "День 1/60 - готово. Самочувствие 8/10");
        Check(Last().Contains("уже засчитан") && (await _store.GetDaysAsync(_anna.Id)).Count == before,
            "повторное видео в тот же день не задваивает день");

        SetLocal(9, 10, 30);
        await Video(_bobur, "День 1/60 - готово", seconds: 5);
        Check(Last().Contains("короче") && await Status(_bobur, 9) is null, "слишком короткое видео не засчитано");
        await Video(_bobur, "День 1/60 - готово", forwarded: true);
        Check(Last().Contains("Пересланное") && await Status(_bobur, 9) is null, "пересланное видео не засчитано");
        await Video(_bobur, "День 1/60 - готово. Самочувствие 9/10");
        Check(await Status(_bobur, 9) == DayStatus.Done, "нормальное видео Бобура засчитано");

        SetLocal(9, 11, 0);
        await Video(_diloza, "День 1/60 - готово. Самочувствие 3/10");
        Check(await Status(_diloza, 9) == DayStatus.Done && Last().Contains("щадящий"),
            "самочувствие 3/10: засчитано и мягко предложен щадящий режим");

        await Video(_outsider, "привет");
        Check(Last().Contains("/join"), "видео от незарегистрированного: просьба вступить");

        var calls = _telegram.Calls.Count;
        await Video(_anna, "другая тема", thread: 555);
        Check(_telegram.Calls.Count == calls, "видео вне темы «Отчёты» игнорируется");

        SetLocal(9, 18, 0);
        await Tick();
        var reminder = Sent("Напоминание").Single();
        Check(reminder.Contains($"user?id={_karim.Id}\"") && !reminder.Contains($"user?id={_anna.Id}\""),
            "18:00: напоминание упоминает только того, кто ещё не отчитался");
        SetLocal(9, 21, 0);
        await Tick();
        Check(Sent("Напоминание").Count() == 2, "21:00: второе напоминание");

        SetLocal(9, 21, 59);
        await Tick();
        Check(await Status(_karim, 9) is null, "21:59: день ещё не закрыт");
        SetLocal(9, 22, 0);
        await Tick();
        Check(await Status(_karim, 9) == DayStatus.Miss, "22:00: у Карима пропуск (п)");
        var deadline = Sent("Время отчёта вышло").Single();
        Check(deadline.Contains($"user?id={_karim.Id}\"") && deadline.Contains($"user?id={_diloza.Id}\""),
            "в сообщении о дедлайне упомянут Карим и его напарник Дилноза");

        SetLocal(9, 22, 1);
        await Tick();
        Check(Sent("Время отчёта вышло").Count() == 1, "повторная проверка не дублирует дедлайн");

        SetLocal(9, 22, 5);
        await Video(_karim, "поздно");
        Check(Last().Contains("прошло") && await Status(_karim, 9) == DayStatus.Miss, "видео после дедлайна не отменяет пропуск");
    }

    private async Task Tuesday()
    {
        Section("Вторник 10.11 (день 2)");
        SetLocal(10, 0, 0, seconds: 30);
        await Video(_anna, "День 2/60 - готово. Самочувствие 8/10");
        Check(await Status(_anna, 10) == DayStatus.Done, "видео в 00:00:30 засчитано уже на новый день");

        await DayWithTicks(10, [_bobur, _diloza], sick: [_karim], messageHour: 9);
        Check(await Status(_karim, 10) == DayStatus.Sick, "/sick: у Карима больничный (б)");
        Check(Sent("Больничный на сегодня оформлен").Any(m => m.Contains("Осталось больничных: 2 из 3")),
            "осталось 2 больничных из 3");
        Check(await NoMisses(10), "во вторник пропусков нет");
    }

    private async Task Wednesday()
    {
        Section("Среда 11.11 (день 3)");
        await DayWithTicks(11, [_anna, _bobur, _diloza], sick: [_karim], messageHour: 9);
        Check(await NoMisses(11), "в среду пропусков нет");
    }

    private async Task Thursday()
    {
        Section("Четверг 12.11 (день 4)");
        await DayWithTicks(12, [_anna, _bobur], sick: [_karim, _diloza], messageHour: 9);
        var days = await _store.GetDaysAsync(_karim.Id);
        Check(days.Count(d => d.Status == DayStatus.Sick) == 3, "Карим использовал все 3 больничных");
        Check(await NoMisses(12), "в четверг пропусков нет");
    }

    private async Task Friday()
    {
        Section("Пятница 13.11 (день 5): больничный сверх лимита");
        SetLocal(13, 8, 0);
        await Tick();
        SetLocal(13, 10, 0);
        await Say(_karim, "/sick");
        Check(Last().Contains("использованы"), "4-й больничный не оформлен, бот объясняет про лимит");
        Check(await Status(_karim, 13) is null, "штраф и пропуск автоматически не поставлены");

        foreach (var u in new[] { _anna, _bobur, _diloza })
            await Video(u, "День 5/60 - готово. Самочувствие 7/10");

        SetLocal(13, 22, 0);
        await Tick();
        Check(await Status(_karim, 13) is null, "в 22:00 Кариму пропуск не ставится, решает группа");
    }

    private async Task SaturdayAndSunday()
    {
        Section("Суббота 14.11 и воскресенье 15.11");
        await DayWithTicks(14, [_anna, _bobur, _karim, _diloza], sick: [], messageHour: 9);

        SetLocal(15, 8, 0);
        await Tick();
        SetLocal(15, 9, 0);
        foreach (var u in new[] { _anna, _bobur, _karim, _diloza })
            await Video(u, "День 7/60 - готово. Самочувствие 8/10");

        SetLocal(15, 20, 0);
        await Tick();
        var weekly = Sent("Итоги недели").SingleOrDefault();
        Check(weekly is not null, "воскресенье 20:00: итоги недели");
        Check(weekly!.Contains("Анна: 7/7") && weekly.Contains("больничных осталось 0") && weekly.Contains("В банке: 50 сомони"),
            "в итогах: у Анны 7/7, у Карима больничных 0, в банке 50 сомони");
        SetLocal(15, 20, 1);
        await Tick();
        Check(Sent("Итоги недели").Count() == 1, "итоги недели не дублируются");

        SetLocal(15, 21, 0);
        await Tick();
        SetLocal(15, 22, 0);
        await Tick();
        Check(await NoMisses(15), "в воскресенье пропусков нет");
    }

    private async Task AdminCommands()
    {
        Section("Воскресенье вечером: /status, /table, /bank, админ-команды, /export");
        SetLocal(15, 22, 30);

        await Say(_anna, "/status");
        Check(Last().Contains("День 7/60") && Last().Contains("Выполнено: 7"), "/status: день 7/60, выполнено 7");

        await Say(_karim, "/status");
        Check(Last().Contains("Больничные: использовано 3, осталось 0") && Last().Contains("штраф: 50 сомони"),
            "/status Карима: больничных 0, штраф 50 сомони");

        await Say(_bobur, "/table");
        Check(Last().Contains("<pre>") && Last().Contains("Анна") && Last().Contains("Карим"), "/table показывает таблицу группы");

        await Say(_bobur, "/bank");
        Check(Last().Contains("Взносов нет") && Last().Contains("Всего в банке: 50 сомони"), "/bank: взносов нет, в банке 50 сомони");

        await Say(_bobur, "/mark @karim 2026-11-13 щ");
        Check(Last().Contains("только для админов"), "/mark от обычного участника отклонён");

        await Say(_admin, "/mark @karim 2026-11-13 щ");
        Check(await Status(_karim, 13) == DayStatus.Gentle, "/mark: админ поставил Кариму 13.11 щадящий день");

        await Say(_admin, "/mark @karim 2026-11-30 д");
        Check(Last().Contains("вне челленджа"), "/mark на будущую дату отклонён");

        await Say(_admin, "/pair @anna @karim");
        var anna = await _store.GetParticipantAsync(_anna.Id);
        var karim = await _store.GetParticipantAsync(_karim.Id);
        var bobur = await _store.GetParticipantAsync(_bobur.Id);
        Check(anna!.PartnerUserId == _karim.Id && karim!.PartnerUserId == _anna.Id, "/pair: Анна и Карим теперь напарники");
        Check(bobur!.PartnerUserId == _diloza.Id, "бывшие напарники Бобур и Дилноза стали парой");

        await Say(_admin, "/setstart @nobody 2026-11-16");
        Check(Last().Contains("Не нашёл"), "/setstart для неизвестного участника: понятная ошибка");

        await Say(_anna, "/export");
        Check(_telegram.Count("sendDocument") == 0, "/export от обычного участника отклонён");
        await Say(_admin, "/export");
        Check(_telegram.Count("sendDocument") == 1 && _telegram.Calls.Last().ContentLength > 2000, "/export: админу отправлен xlsx");
    }

    private async Task DowntimeAndRestart()
    {
        Section("Компьютер выключен с вс 22:00 до ср 09:00, потом программа запускается снова");
        SetLocal(18, 9, 0);
        Boot(); // «перезапуск»: все объекты новые, память пуста, данные только в базе

        var messagesBefore = _telegram.SentTexts.Count();
        await Tick();

        Check(await Status(_anna, 16) == DayStatus.Miss && await Status(_anna, 17) == DayStatus.Miss,
            "при старте дозакрыты пропущенные дедлайны за пн и вт");
        Check(await Status(_anna, 18) is null, "сегодняшний день (ср) не закрыт: до дедлайна далеко");
        Check(Sent("Закрыт день").Count() == 2, "в группе по одному сообщению за каждый дозакрытый день");
        Check(Sent("Доброе утро").Any(m => m.Contains("День 10/60")), "утреннее сообщение за ср отправлено после запуска");
        var anna = await _store.GetParticipantAsync(_anna.Id);
        Check(anna!.PartnerUserId == _karim.Id, "пары и участники на месте после перезапуска");

        var messagesAfter = _telegram.SentTexts.Count();
        Boot();
        await Tick();
        Check(_telegram.SentTexts.Count() == messagesAfter, "второй перезапуск ничего не дублирует");
        Check(messagesAfter > messagesBefore, "(контроль) после первого запуска сообщения были");

        await Say(_admin, "/mark @anna 2026-11-16 д");
        Check(await Status(_anna, 16) == DayStatus.Done, "админ исправил пропуск через /mark");
        await Say(_anna, "/bank");
        Check(Last().Contains("Анна: 1 × 50"), "/bank пересчитан: у Анны один штраф");
    }

    // ---------- Помощники сценария ----------

    /// <summary>Типичный день: тики по расписанию, видео участников и больничные.</summary>
    private async Task DayWithTicks(int day, User[] reporters, User[] sick, int messageHour)
    {
        SetLocal(day, 8, 0);
        await Tick();

        SetLocal(day, messageHour, 0);
        foreach (var user in sick)
            await Say(user, "/sick");
        foreach (var user in reporters)
            await Video(user, $"День {day - 8}/60 - готово. Самочувствие 7/10");

        foreach (var (h, m) in new[] { (18, 0), (21, 0), (22, 0) })
        {
            SetLocal(day, h, m);
            await Tick();
        }
    }

    private void Boot()
    {
        var options = Options.Create(_options);
        var clock = new ChallengeClock(_time, _options.TimeZone);
        _store = new ChallengeStore(_db);
        var sender = new SimulationSender(_store, _bot);

        _jobs = new ChallengeJobs(_store, clock, options, sender, NullLogger<ChallengeJobs>.Instance);
        var admin = new AdminCommandHandler(_store, clock, options);
        var commands = new CommandHandler(_store, clock, options, admin);
        var reports = new ReportHandler(_store, clock, options, NullLogger<ReportHandler>.Instance);
        _updates = new BotUpdateHandler(commands, reports, NullLogger<BotUpdateHandler>.Instance) { BotUsername = "sim_bot" };
    }

    private Task Tick() => _jobs.TickAsync(default);

    private async Task<DayStatus?> Status(User user, int day) =>
        (await _store.GetDayAsync(user.Id, new DateOnly(2026, 11, day)))?.Status;

    private async Task<bool> NoMisses(int day)
    {
        var date = new DateOnly(2026, 11, day);
        return (await _store.GetAllDaysAsync()).All(d => d.Date != date || d.Status != DayStatus.Miss);
    }

    private IEnumerable<string> Sent(string startsWith) =>
        _telegram.SentTexts.Where(t => t.StartsWith(startsWith));

    private string Last() => _telegram.SentTexts.LastOrDefault() ?? "";

    private void SetLocal(int day, int hour, int minute, int seconds = 0) =>
        _time.Set(new DateTimeOffset(2026, 11, day, hour, minute, seconds, Tashkent));

    private Task Say(User from, string text)
    {
        LogUser(from, text);
        return Deliver(from, new Message { Text = text }, ReportsThread);
    }

    private Task Video(User from, string caption, int seconds = 30, bool forwarded = false, int thread = ReportsThread)
    {
        LogUser(from, $"[видео {seconds} с] {caption}{(forwarded ? " (пересланное)" : "")}");
        var message = new Message
        {
            Caption = caption,
            Video = new Video { FileId = "f", FileUniqueId = "u", Width = 640, Height = 480, Duration = seconds },
        };
        if (forwarded)
            message.ForwardOrigin = new MessageOriginHiddenUser { SenderUserName = "someone", Date = DateTime.UtcNow };
        return Deliver(from, message, thread);
    }

    private async Task Deliver(User from, Message message, int thread)
    {
        message.Id = _messageCounter++;
        message.Date = _time.GetUtcNow().UtcDateTime;
        message.From = from;
        message.Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup, IsForum = true };
        message.MessageThreadId = thread;
        message.IsTopicMessage = true;

        await _updates.HandleUpdateAsync(_bot, new Update { Message = message }, default);
    }

    // ---------- Вывод ----------

    private void Check(bool ok, string description)
    {
        _checks++;
        if (!ok) _failed++;
        Print($"   {(ok ? "✔" : "✘ НЕ ПРОШЛО:")} {description}");
    }

    private void Section(string title) => Print($"\n== {title} ==");

    private void LogUser(User user, string text) =>
        Print($"[{DayNames[(int)_time.GetUtcNow().ToOffset(Tashkent).DayOfWeek]} {_time.GetUtcNow().ToOffset(Tashkent):HH:mm:ss}] {user.FirstName}: {text}");

    private void Print(string text)
    {
        if (!_quiet) Console.WriteLine(text);
    }

    /// <summary>Читаемый вид сообщения: без HTML-тегов.</summary>
    private static string Plain(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", ""));
}

/// <summary>Отправитель планировщика для симуляции: шлёт через ту же фейковую «сеть», что и обработчики.</summary>
public class SimulationSender : IChatSender
{
    private readonly ChallengeStore _store;
    private readonly ITelegramBotClient _bot;

    public SimulationSender(ChallengeStore store, ITelegramBotClient bot)
    {
        _store = store;
        _bot = bot;
    }

    public async Task SendAsync(string htmlText, CancellationToken ct)
    {
        var target = await _store.GetReportsTargetAsync();
        if (target is null) return;
        await _bot.SendMessage(target.Value.ChatId, htmlText, parseMode: ParseMode.Html,
            messageThreadId: target.Value.ThreadId, cancellationToken: ct);
    }
}
