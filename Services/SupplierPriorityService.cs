using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

public class SupplierPriorityService(AppDbContext db)
{
    public async Task<List<NonCalculatedAvailableSupplierRow>> GetAsync()
    {
        var invoices = await db.PaymentRunInvoices.AsNoTracking()
            .Where(x => !x.PaymentRun!.IsDeleted && (x.PaymentRun.Status == PaymentRunStatus.Approved || x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered))
            .OrderBy(x => x.PaymentRunId).ThenBy(x => x.Id).ToListAsync();
        var latest = invoices.GroupBy(x => x.PaymentKeyHash).Select(g => g.Last()).ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Title).ToListAsync();
        var maxRemaining = suppliers.Select(s => s.InitialClaimAmount + latest.Where(x => x.SupplierId == s.Id).Sum(x => Math.Max(0, x.RemainingDebt - CurrentAllocated(x)))).DefaultIfEmpty(1).Max();
        var rows = new List<NonCalculatedAvailableSupplierRow>();

        foreach (var supplier in suppliers)
        {
            var current = latest.Where(x => x.SupplierId == supplier.Id).ToList();
            var currentDebt = current.Sum(x => Math.Max(0, x.RemainingDebt - CurrentAllocated(x)));
            var open = current.Count(x => x.RemainingDebt - CurrentAllocated(x) > 0);
            var ageRatio = open > 0 ? current.Where(x => x.RemainingDebt - CurrentAllocated(x) > 0)
                .Average(x => x.ContractSettlementDays > 0 ? (decimal)x.DebtAgeDays / x.ContractSettlementDays : 1) : 0;
            var overdue = current.Count(x => x.RemainingDebt - CurrentAllocated(x) > 0 && x.ContractSettlementDays > 0 && x.DebtAgeDays > x.ContractSettlementDays);
            var paid = await db.PaymentRunSupplierSummaries.AsNoTracking().Where(x => x.SupplierId == supplier.Id && !x.PaymentRun!.IsDeleted && (x.PaymentRun.Status == PaymentRunStatus.Approved || x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered)).SumAsync(x => (decimal?)x.AllocatedAmount) ?? 0;
            var basis = supplier.InitialClaimAmount + current.Sum(x => x.OriginalDebt);
            var coverage = basis > 0 ? Math.Clamp(paid / basis, 0, 1) : 0;
            var remaining = supplier.InitialClaimAmount + currentDebt;
            var priority = PriorityScore(remaining, maxRemaining, ageRatio, coverage, overdue, open);
            if (remaining > 0)
                rows.Add(new NonCalculatedAvailableSupplierRow
                {
                    SupplierId = supplier.Id,
                    SupplierTitle = supplier.Title,
                    InitialClaimAmount = supplier.InitialClaimAmount,
                    CurrentDebt = currentDebt,
                    Priority = priority,
                    OpenReceiptCount = open
                });
        }

        return rows.OrderByDescending(x => x.Priority).ThenByDescending(x => x.TotalDebt).ThenBy(x => x.SupplierTitle).ToList();
    }

    public static decimal PriorityScore(decimal remaining, decimal maxRemaining, decimal ageRatio, decimal coverage, int overdue, int count)
    {
        if (remaining <= 0) return 0;
        var debt = maxRemaining > 0 ? remaining / maxRemaining : 0;
        var age = Math.Clamp(ageRatio, 0, 2) / 2;
        var overdueRate = count > 0 ? (decimal)overdue / count : 0;
        var under = 1 - Math.Clamp(coverage, 0, 1);
        var volume = Math.Min(1, count / 20m);
        return Math.Round(100 * (debt * .30m + age * .30m + overdueRate * .20m + under * .15m + volume * .05m), 1);
    }

    static decimal CurrentAllocated(PaymentRunInvoice invoice) =>
        invoice.AllocatedCurrentAmount > 0
            ? invoice.AllocatedCurrentAmount
            : invoice.AllocatedInitialClaimAmount > 0
                ? 0
                : invoice.AllocatedAmount;
}
