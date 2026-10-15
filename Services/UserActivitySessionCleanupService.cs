using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

/// <summary>
/// Closes stale evaluator attendance sessions. Attendance ends at the last recorded
/// activity, not 30 minutes later, so an idle browser cannot inflate reported time.
/// </summary>
public sealed class UserActivitySessionCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<UserActivitySessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseStaleSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to close stale user activity sessions.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CloseStaleSessionsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - IdleLimit;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await db.UserActivitySessions
            .Where(x => x.LogoutAtUtc == null && x.LastSeenAtUtc <= cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return;

        foreach (var session in stale)
        {
            session.LogoutAtUtc = session.LastSeenAtUtc;
            session.DurationSeconds = Math.Max(0, (int)Math.Min(
                int.MaxValue, (session.LastSeenAtUtc - session.LoginAtUtc).TotalSeconds));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Closed {SessionCount} stale user activity sessions.", stale.Count);
    }
}
