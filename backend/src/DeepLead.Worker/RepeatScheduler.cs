using DeepLead.Data;

namespace DeepLead.Worker;

/// <summary>
/// Starts repeating sessions (Weekly/Monthly) when due by cloning them into a new Pending run;
/// the SearchRunner then picks the new run up like any other session.
/// </summary>
public sealed class RepeatScheduler(SearchRepository searches, ILogger<RepeatScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var id in await searches.GetDueRepeatsAsync(stoppingToken))
                {
                    var newId = await searches.CloneAsync(id);
                    logger.LogInformation("Repeat due: session {SearchId} -> new run {NewId}", id, newId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Repeat scheduler check failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
