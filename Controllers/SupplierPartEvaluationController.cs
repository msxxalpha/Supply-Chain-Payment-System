using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.SupplierPartEvaluationView)]
public class SupplierPartEvaluationController(AppDbContext db) : Controller
{
    int UserId => int.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : 0;

    public async Task<IActionResult> Dashboard()
    {
        var parameters = await GetPermittedParametersAsync();
        var mappings = await db.SupplierParts.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Part)
            .Where(x => x.IsActive && x.Supplier!.IsActive && x.Part!.IsActive)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        var mappingIds = mappings.Select(x => x.Id).ToList();
        var parameterIds = parameters.Select(x => x.Id).ToList();
        var myScores = mappingIds.Count == 0 || parameterIds.Count == 0
            ? new List<SupplierPartAssessorEvaluation>()
            : await db.SupplierPartAssessorEvaluations.AsNoTracking()
                .Where(x => x.AssessorUserId == UserId && !x.IsDefault &&
                            mappingIds.Contains(x.SupplierPartId) &&
                            parameterIds.Contains(x.PaymentParameterId))
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync();

        var scoredKeys = myScores.Select(x => (x.SupplierPartId, x.PaymentParameterId)).ToHashSet();
        var scoreCountsByMapping = myScores.GroupBy(x => x.SupplierPartId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PaymentParameterId).Distinct().Count());

        var pending = new List<EvaluatorPendingMapping>();
        if (parameters.Count > 0)
        {
            foreach (var mapping in mappings)
            {
                var missing = parameters.Where(p => !scoredKeys.Contains((mapping.Id, p.Id))).ToList();
                if (missing.Count == 0) continue;
                pending.Add(new EvaluatorPendingMapping(
                    mapping.Id,
                    mapping.Supplier!.Code,
                    mapping.Supplier.Title,
                    mapping.Part!.Code,
                    mapping.Part.Title,
                    missing.Count,
                    string.Join("، ", missing.Take(3).Select(x => x.Title)) + (missing.Count > 3 ? " و موارد دیگر" : "")));
            }
        }

        var progress = parameters.Select(parameter =>
        {
            var rows = myScores.Where(x => x.PaymentParameterId == parameter.Id).ToList();
            var assessed = rows.Select(x => x.SupplierPartId).Distinct().Count();
            var percent = mappings.Count == 0 ? 0m : Math.Round(assessed * 100m / mappings.Count, 1);
            return new EvaluatorParameterProgress(
                parameter.Id,
                parameter.Title,
                parameter.Weight,
                assessed,
                mappings.Count,
                percent,
                rows.Count == 0 ? null : Math.Round(rows.Average(x => x.Score), 2, MidpointRounding.AwayFromZero));
        }).ToList();

        var mappingById = mappings.ToDictionary(x => x.Id);
        var parameterById = parameters.ToDictionary(x => x.Id);
        var recent = myScores.OrderByDescending(x => x.UpdatedAt).Take(10)
            .Where(x => mappingById.ContainsKey(x.SupplierPartId) && parameterById.ContainsKey(x.PaymentParameterId))
            .Select(x =>
            {
                var mapping = mappingById[x.SupplierPartId];
                return new EvaluatorRecentScore(
                    mapping.Supplier!.Title,
                    mapping.Part!.Code + " - " + mapping.Part.Title,
                    parameterById[x.PaymentParameterId].Title,
                    x.Score,
                    PersianDateService.ToJalali(x.UpdatedAt.ToLocalTime()) + " " + x.UpdatedAt.ToLocalTime().ToString("HH:mm"));
            }).ToList();

        var expectedScores = mappings.Count * parameters.Count;
        var completion = expectedScores == 0 ? 0m : Math.Round(myScores.Count * 100m / expectedScores, 1);
        var assessedMappings = scoreCountsByMapping.Count;
        var completedMappings = parameters.Count == 0 ? 0 : mappings.Count(x =>
            parameters.All(p => scoredKeys.Contains((x.Id, p.Id))));
        return View(new EvaluatorDashboardVm(
            User.Identity?.Name ?? "ارزیاب",
            mappings.Count,
            assessedMappings,
            completedMappings,
            pending.Count,
            parameters.Count,
            myScores.Count,
            expectedScores,
            completion,
            progress,
            pending.Take(12).ToList(),
            recent));
    }

    public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = 25)
    {
        pageSize = NormalizePageSize(pageSize);
        page = Math.Max(1, page);
        q = (q ?? "").Trim();

        var parameters = await GetPermittedParametersAsync();
        if (parameters.Count == 0)
            return View(new EvaluatorVm([], [], [], [], 0, page, pageSize, q));

        var query = db.SupplierParts
            .Include(x => x.Supplier)
            .Include(x => x.Part)
            .Where(x => x.IsActive && x.Supplier!.IsActive && x.Part!.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x =>
                x.Supplier!.Code.Contains(q) ||
                x.Supplier.Title.Contains(q) ||
                x.Part!.Code.Contains(q) ||
                x.Part.Title.Contains(q));

        var total = await query.CountAsync();
        var mappings = await query
            .OrderBy(x => x.Supplier!.Title)
            .ThenBy(x => x.Part!.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ids = mappings.Select(x => x.Id).ToHashSet();
        var parameterIds = parameters.Select(x => x.Id).ToHashSet();

        var myScores = await db.SupplierPartAssessorEvaluations.AsNoTracking()
            .Where(x => ids.Contains(x.SupplierPartId) &&
                        parameterIds.Contains(x.PaymentParameterId) &&
                        x.AssessorUserId == UserId && !x.IsDefault)
            .ToListAsync();

        var allScores = await db.SupplierPartAssessorEvaluations.AsNoTracking()
            .Where(x => ids.Contains(x.SupplierPartId) &&
                        parameterIds.Contains(x.PaymentParameterId) && !x.IsDefault)
            .ToListAsync();

        return View(new EvaluatorVm(mappings, parameters, myScores, allScores, total, page, pageSize, q));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int supplierPartId, List<EvaluatorScoreEditItem> items, string? q, int page = 1, int pageSize = 25)
    {
        var mapping = await db.SupplierParts
            .Include(x => x.Supplier)
            .Include(x => x.Part)
            .SingleOrDefaultAsync(x => x.Id == supplierPartId && x.IsActive);

        if (mapping == null)
            return NotFound();

        var parameters = await GetPermittedParametersAsync();
        var allowed = parameters.ToDictionary(x => x.Id);
        var posted = (items ?? []).GroupBy(x => x.ParameterId).Select(g => g.Last()).ToList();

        if (posted.Count == 0)
        {
            TempData["Error"] = "حداقل یک امتیاز قابل ثبت انتخاب کنید.";
            return RedirectToAction(nameof(Index), new { q, page, pageSize });
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in posted)
            {
                if (!allowed.TryGetValue(item.ParameterId, out var parameter))
                    throw new InvalidOperationException($"دسترسی ثبت امتیاز پارامتر «شناسه {item.ParameterId}» برای شما فعال نیست.");

                if (item.Score < 1 || item.Score > parameter.MaxScore)
                    throw new InvalidOperationException($"امتیاز «{parameter.Title}» باید بین ۱ تا {parameter.MaxScore:0.##} باشد.");

                var row = await db.SupplierPartAssessorEvaluations
                    .SingleOrDefaultAsync(x =>
                        x.SupplierPartId == supplierPartId &&
                        x.PaymentParameterId == parameter.Id &&
                        x.AssessorUserId == UserId);

                if (row == null)
                {
                    db.SupplierPartAssessorEvaluations.Add(new SupplierPartAssessorEvaluation
                    {
                        SupplierPartId = supplierPartId,
                        PaymentParameterId = parameter.Id,
                        AssessorUserId = UserId,
                        Score = Math.Round(item.Score, 2),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    row.Score = Math.Round(item.Score, 2);
                    row.IsDefault = false;
                    row.UpdatedAt = DateTime.UtcNow;
                }

            }

            // Persist all evaluator scores before calculating their averages.
            await db.SaveChangesAsync();
            foreach (var item in posted)
                await RefreshAggregateAsync(supplierPartId, item.ParameterId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            db.AuditLogs.Add(new AuditLog
            {
                Action = "ASSESS",
                Entity = "SupplierPartAssessorEvaluation",
                EntityId = supplierPartId.ToString(),
                Details = $"ارزیابی قطعه «{mapping.Part?.Title}» / تامین‌کننده «{mapping.Supplier?.Title}» توسط {User.Identity?.Name ?? "کاربر"} ثبت شد.",
                UserId = UserId
            });
            await db.SaveChangesAsync();

            TempData["Result"] = "امتیازهای شما با موفقیت ثبت شد و میانگین جدول مبنای محاسبه به‌روزرسانی شد.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { q, page, pageSize });
    }

    async Task<List<PaymentParameter>> GetPermittedParametersAsync()
    {
        var parameters = await db.PaymentParameters
            .Where(x => x.IsActive && x.Weight > 0 && x.ScoringMethod == ParameterScoringMethod.Manual)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync();

        return parameters
            .Where(x => User.HasClaim(SecurityPermissions.Claim, $"SupplierPartEvaluation.Parameter.{x.Id}")
                     || User.HasClaim("IsAdmin", "1"))
            .ToList();
    }

    async Task RefreshAggregateAsync(int supplierPartId, int parameterId)
    {
        var scores = await db.SupplierPartAssessorEvaluations
            .Where(x => x.SupplierPartId == supplierPartId && x.PaymentParameterId == parameterId && !x.IsDefault)
            .Select(x => x.Score)
            .ToListAsync();

        var average = scores.Count == 0 ? 1m : Math.Round(scores.Average(), 2, MidpointRounding.AwayFromZero);

        var aggregate = await db.SupplierPartEvaluations
            .SingleOrDefaultAsync(x => x.SupplierPartId == supplierPartId && x.PaymentParameterId == parameterId);

        if (aggregate == null)
        {
            db.SupplierPartEvaluations.Add(new SupplierPartEvaluation
            {
                SupplierPartId = supplierPartId,
                PaymentParameterId = parameterId,
                Score = average,
                IsActive = true,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            aggregate.Score = average;
            aggregate.IsActive = true;
            aggregate.UpdatedAt = DateTime.UtcNow;
        }

    }

    static int NormalizePageSize(int value) => value is 50 or 75 or 100 ? value : 25;


    public record EvaluatorDashboardVm(
        string EvaluatorName,
        int TotalMappings,
        int AssessedMappings,
        int CompletedMappings,
        int PendingMappingCount,
        int RequiredParameterCount,
        int CompletedScores,
        int ExpectedScores,
        decimal CompletionPercent,
        List<EvaluatorParameterProgress> ParameterProgress,
        List<EvaluatorPendingMapping> PendingItems,
        List<EvaluatorRecentScore> RecentScores);

    public record EvaluatorParameterProgress(
        int ParameterId,
        string ParameterTitle,
        decimal Weight,
        int EvaluatedMappings,
        int TotalMappings,
        decimal CompletionPercent,
        decimal? AverageScore);

    public record EvaluatorPendingMapping(
        int SupplierPartId,
        string SupplierCode,
        string SupplierTitle,
        string PartCode,
        string PartTitle,
        int MissingParameterCount,
        string MissingParameterTitles);

    public record EvaluatorRecentScore(
        string SupplierTitle,
        string PartTitle,
        string ParameterTitle,
        decimal Score,
        string UpdatedAtJalali);

    public record EvaluatorScoreEditItem(int ParameterId, decimal Score);
    public record EvaluatorVm(
        List<SupplierPart> Mappings,
        List<PaymentParameter> Parameters,
        List<SupplierPartAssessorEvaluation> MyScores,
        List<SupplierPartAssessorEvaluation> AllScores,
        int TotalCount,
        int Page,
        int PageSize,
        string Search);
}
