using Microsoft.Extensions.DependencyInjection;

namespace FypWeb.Services;

// One server-side sweep, not one timer per mobile user. Deadlines are persisted;
// a restart catches up new-workflow requests without touching legacy notices.
public sealed class ResignationExpiryWorker(IServiceScopeFactory scopes,
    ILogger<ResignationExpiryWorker> logger) : BackgroundService
{
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ResignationWorkflowService>();
        return await workflow.ProcessDueAsync(batchSize: 200, ct: ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Resignation expiry sweep failed. Check the workflow SQL update and database availability; it will retry on the next sweep.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
