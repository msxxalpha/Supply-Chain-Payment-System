using Indamin.Payment.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = "AdminOnly")]
public class DataMaintenanceController(AppDbContext db) : Controller
{
    private const string ConfirmationPhrase = "حذف کامل";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new DataMaintenanceVm
        {
            CalculatedRuns = await db.PaymentRuns.CountAsync(x => x.RunType == PaymentRunType.Calculated),
            NonCalculatedRuns = await db.PaymentRuns.CountAsync(x => x.RunType == PaymentRunType.NonCalculated),
            ClaimHistories = await db.SupplierClaimHistories.CountAsync(),
            AssessorScores = await db.SupplierPartAssessorEvaluations.CountAsync(),
            ActivitySessions = await db.UserActivitySessions.CountAsync(),
            AuditLogs = await db.AuditLogs.CountAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSection(string? section, string? confirmation)
    {
        if (!string.Equals(confirmation?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))
        {
            TempData["Error"] = $"برای جلوگیری از حذف ناخواسته، عبارت «{ConfirmationPhrase}» را دقیقاً وارد کنید.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(section) ||
            !new[] { "calculated-payments", "non-calculated-payments", "claim-history", "assessor-activity", "audit-logs" }.Contains(section, StringComparer.Ordinal))
        {
            TempData["Error"] = "بخش انتخاب‌شده معتبر نیست؛ هیچ اطلاعاتی حذف نشد.";
            return RedirectToAction(nameof(Index));
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var deleted = 0;
            var details = new List<string>();

            switch (section)
            {
                case "calculated-payments":
                    deleted = await DeletePaymentRunsAsync(PaymentRunType.Calculated);
                    details.Add("محاسبات و جزئیات پرداخت محاسباتی");
                    break;
                case "non-calculated-payments":
                    deleted = await DeletePaymentRunsAsync(PaymentRunType.NonCalculated);
                    details.Add("پرداخت‌های غیرمحاسباتی و جزئیات آن‌ها");
                    break;
                case "claim-history":
                    deleted = await db.SupplierClaimHistories.ExecuteDeleteAsync();
                    details.Add("سوابق تغییر مطالبات تامین‌کنندگان");
                    break;
                case "assessor-activity":
                    var scoreCount = await db.SupplierPartAssessorEvaluations.ExecuteDeleteAsync();
                    var sessionCount = await db.UserActivitySessions.ExecuteDeleteAsync();
                    deleted = scoreCount + sessionCount;
                    details.Add($"امتیازهای ارزیابان: {scoreCount:N0}");
                    details.Add($"سوابق نشست و فعالیت کاربران: {sessionCount:N0}");
                    break;
                case "audit-logs":
                    deleted = await db.AuditLogs.ExecuteDeleteAsync();
                    details.Add("گزارش رویدادها و عملیات سیستم");
                    break;
            }

            await transaction.CommitAsync();
            TempData["Result"] = $"حذف کامل بخش «{string.Join("، ", details)}» انجام شد. تعداد رکوردهای حذف‌شده: {deleted:N0}.";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            TempData["Error"] = $"حذف انجام نشد و تغییرات این عملیات به‌طور کامل بازگردانی شد. جزئیات: {ex.GetBaseException().Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<int> DeletePaymentRunsAsync(PaymentRunType runType)
    {
        var runIds = await db.PaymentRuns
            .Where(x => x.RunType == runType)
            .Select(x => x.Id)
            .ToListAsync();

        if (runIds.Count == 0)
            return 0;

        var deleted = 0;

        // Remove dependent rows first to respect SQL Server foreign-key constraints.
        deleted += await db.PaymentInvoiceScores
            .Where(x => runIds.Contains(x.Invoice!.PaymentRunId))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunInvoices
            .Where(x => runIds.Contains(x.PaymentRunId))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunSupplierSummaries
            .Where(x => runIds.Contains(x.PaymentRunId))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunParameterSnapshots
            .Where(x => runIds.Contains(x.PaymentRunId))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunSystemParameterSnapshots
            .Where(x => runIds.Contains(x.PaymentRunId))
            .ExecuteDeleteAsync();
        deleted += await db.NonCalculatedPaymentLines
            .Where(x => runIds.Contains(x.PaymentRunId))
            .ExecuteDeleteAsync();
        deleted += await db.SupplierClaimHistories
            .Where(x => x.PaymentRunId.HasValue && runIds.Contains(x.PaymentRunId.Value))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRuns
            .Where(x => runIds.Contains(x.Id))
            .ExecuteDeleteAsync();

        return deleted;
    }

    public sealed class DataMaintenanceVm
    {
        public int CalculatedRuns { get; set; }
        public int NonCalculatedRuns { get; set; }
        public int ClaimHistories { get; set; }
        public int AssessorScores { get; set; }
        public int ActivitySessions { get; set; }
        public int AuditLogs { get; set; }
    }
}
