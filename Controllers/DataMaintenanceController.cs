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
            AuditLogs = await db.AuditLogs.CountAsync(),
            Parts = await db.Parts.CountAsync(),
            Suppliers = await db.Suppliers.CountAsync(),
            SupplierParts = await db.SupplierParts.CountAsync(),
            SupplierPartEvaluations = await db.SupplierPartEvaluations.CountAsync(),
            SupplierPartAssessorEvaluations = await db.SupplierPartAssessorEvaluations.CountAsync(),
            SupplierPriceListItems = await db.SupplierPriceListItems.CountAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSection(string? section, string? confirmation)
    {
        var expectedConfirmation = string.Equals(section, "reset-test-data", StringComparison.Ordinal)
            ? "پاک‌سازی کامل اطلاعات آزمایشی"
            : ConfirmationPhrase;
        if (!string.Equals(confirmation?.Trim(), expectedConfirmation, StringComparison.Ordinal))
        {
            TempData["Error"] = $"برای جلوگیری از حذف ناخواسته، عبارت «{expectedConfirmation}» را دقیقاً وارد کنید.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(section) ||
            !new[] { "calculated-payments", "non-calculated-payments", "claim-history", "assessor-activity", "audit-logs", "reset-test-data" }.Contains(section, StringComparer.Ordinal))
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
                case "reset-test-data":
                    deleted = await ResetTestDataAsync();
                    details.Add("پاک‌سازی یکپارچه اطلاعات آزمایشی شامل کالاها، تامین‌کنندگان، ارتباط‌ها، ارزیابی‌ها، فهرست بها و پرداخت‌های وابسته");
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

    private async Task<int> ResetTestDataAsync()
    {
        var deleted = 0;

        // A full test reset also removes payment history that references the
        // supplier/part master data, so production data starts without test records.
        deleted += await DeletePaymentRunsAsync(PaymentRunType.Calculated);
        deleted += await DeletePaymentRunsAsync(PaymentRunType.NonCalculated);

        // Delete dependents before their principals to satisfy SQL Server FKs.
        deleted += await db.SupplierClaimHistories.ExecuteDeleteAsync();
        deleted += await db.SupplierPartAssessorEvaluations.ExecuteDeleteAsync();
        deleted += await db.SupplierPartEvaluations.ExecuteDeleteAsync();
        deleted += await db.SupplierPriceListItems.ExecuteDeleteAsync();
        deleted += await db.SupplierParts.ExecuteDeleteAsync();
        deleted += await db.SupplierActivities.ExecuteDeleteAsync();
        deleted += await db.Parts.ExecuteDeleteAsync();
        deleted += await db.Suppliers.ExecuteDeleteAsync();

        return deleted;
    }

    private async Task<int> DeletePaymentRunsAsync(PaymentRunType runType)
    {
        var deleted = 0;

        // Delete with SQL subqueries rather than loading all run IDs into memory.
        deleted += await db.PaymentInvoiceScores
            .Where(x => db.PaymentRunInvoices.Any(i =>
                i.Id == x.PaymentRunInvoiceId &&
                db.PaymentRuns.Any(r => r.Id == i.PaymentRunId && r.RunType == runType)))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunInvoices
            .Where(x => db.PaymentRuns.Any(r => r.Id == x.PaymentRunId && r.RunType == runType))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunSupplierSummaries
            .Where(x => db.PaymentRuns.Any(r => r.Id == x.PaymentRunId && r.RunType == runType))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunParameterSnapshots
            .Where(x => db.PaymentRuns.Any(r => r.Id == x.PaymentRunId && r.RunType == runType))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRunSystemParameterSnapshots
            .Where(x => db.PaymentRuns.Any(r => r.Id == x.PaymentRunId && r.RunType == runType))
            .ExecuteDeleteAsync();
        deleted += await db.NonCalculatedPaymentLines
            .Where(x => db.PaymentRuns.Any(r => r.Id == x.PaymentRunId && r.RunType == runType))
            .ExecuteDeleteAsync();
        deleted += await db.SupplierClaimHistories
            .Where(x => x.PaymentRunId.HasValue &&
                db.PaymentRuns.Any(r => r.Id == x.PaymentRunId.Value && r.RunType == runType))
            .ExecuteDeleteAsync();
        deleted += await db.PaymentRuns
            .Where(x => x.RunType == runType)
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
        public int Parts { get; set; }
        public int Suppliers { get; set; }
        public int SupplierParts { get; set; }
        public int SupplierPartEvaluations { get; set; }
        public int SupplierPartAssessorEvaluations { get; set; }
        public int SupplierPriceListItems { get; set; }
    }
}
