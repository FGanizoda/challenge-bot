using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChallengeBot.Jobs;

/// <summary>
/// Раз в минуту сверяет местное время с расписанием (см. ChallengeJobs).
/// Первая проверка идёт сразу при старте: так дозакрывается дедлайн, пропущенный за время простоя.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly ChallengeJobs _jobs;
    private readonly TimeProvider _time;
    private readonly ILogger<SchedulerService> _log;

    public SchedulerService(ChallengeJobs jobs, TimeProvider time, ILogger<SchedulerService> log)
    {
        _jobs = jobs;
        _time = time;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _time);
        try
        {
            do
            {
                await _jobs.TickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // обычная остановка программы
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Планировщик остановился из-за ошибки");
        }
    }
}
