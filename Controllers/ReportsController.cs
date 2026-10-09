using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize]
public class ReportsController(FinancialReportingService reports, AppDbContext db) : Controller
{
    [Authorize(Policy = SecurityPermissions.ReportsView)]
    public async Task<IActionResult> Index()
    {
        var data = await reports.GetDashboardAsync();
        return View(data);
    }

    [Authorize(Policy = SecurityPermissions.AssessorPerformance)]
    public async Task<IActionResult> AssessorPerformance(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var start = from?.Date.ToUniversalTime();
        var end = to?.Date.AddDays(1).ToUniversalTime();

        var sessionsQuery = db.UserActivitySessions.AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.RoleCode == "SUPPLIER_PART_ASSESSOR");
        if (start.HasValue) sessionsQuery = sessionsQuery.Where(x => x.LoginAtUtc >= start.Value);
        if (end.HasValue) sessionsQuery = sessionsQuery.Where(x => x.LoginAtUtc < end.Value);

        var sessions = await sessionsQuery.OrderByDescending(x => x.LoginAtUtc).ToListAsync();
        var scoreQuery = db.SupplierPartAssessorEvaluations.AsNoTracking()
            .Include(x => x.AssessorUser)
            .Include(x => x.SupplierPart).ThenInclude(x => x!.Supplier)
            .Include(x => x.SupplierPart).ThenInclude(x => x!.Part)
            .Include(x => x.PaymentParameter).AsQueryable();
        if (start.HasValue) scoreQuery = scoreQuery.Where(x => x.UpdatedAt >= start.Value);
        if (end.HasValue) scoreQuery = scoreQuery.Where(x => x.UpdatedAt < end.Value);
        var scores = await scoreQuery.ToListAsync();

        var assessorIds = sessions.Select(x => x.UserId).Concat(scores.Select(x => x.AssessorUserId)).Distinct().ToHashSet();
        var sessionRows = sessions.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => new
        {
            Count = g.Count(),
            Seconds = g.Sum(s => s.LogoutAtUtc.HasValue
                ? Math.Max(0, s.DurationSeconds)
                : Math.Max(Math.Max(0, s.DurationSeconds), (int)Math.Min(int.MaxValue, (now - s.LoginAtUtc).TotalSeconds))),
            LastSeen = g.Max(s => s.LastSeenAtUtc),
            Active = g.Any(s => !s.LogoutAtUtc.HasValue)
        });
        var scoreRows = scores.GroupBy(x => x.AssessorUserId).ToDictionary(g => g.Key, g => new
        {
            Count = g.Count(),
            MappingCount = g.Select(x => x.SupplierPartId).Distinct().Count(),
            ParameterCount = g.Select(x => x.PaymentParameterId).Distinct().Count(),
            Average = g.Average(x => x.Score),
            LastScored = g.Max(x => x.UpdatedAt)
        });
        var users = await db.Users.AsNoTracking().Where(x => assessorIds.Contains(x.Id)).OrderBy(x => x.DisplayName).ToListAsync();
        var rows = users.Select(u =>
        {
            sessionRows.TryGetValue(u.Id, out var session);
            scoreRows.TryGetValue(u.Id, out var score);
            return new AssessorPerformanceRow(
                u.Id, u.UserName, u.DisplayName,
                session?.Count ?? 0,
                session?.Seconds ?? 0,
                session?.LastSeen,
                session?.Active ?? false,
                score?.Count ?? 0,
                score?.MappingCount ?? 0,
                score?.ParameterCount ?? 0,
                score?.Average,
                score?.LastScored);
        }).OrderByDescending(x => x.TotalScoringSeconds).ThenBy(x => x.DisplayName).ToList();

        var vm = new AssessorPerformanceVm(rows, from, to, rows.Sum(x => x.SessionCount),
            rows.Sum(x => x.ScoreCount), rows.Sum(x => x.TotalScoringSeconds));
        return View(vm);
    }

    public record AssessorPerformanceRow(
        int UserId, string UserName, string DisplayName,
        int SessionCount, int TotalScoringSeconds, DateTime? LastSeenUtc, bool HasActiveSession,
        int ScoreCount, int SupplierPartCount, int ParameterCount, decimal? AverageScore, DateTime? LastScoredUtc);
    public record AssessorPerformanceVm(
        List<AssessorPerformanceRow> Rows, DateTime? From, DateTime? To,
        int TotalSessions, int TotalScores, int TotalSeconds);
}
