using Indamin.Payment.Data;

namespace Indamin.Payment.Services;

public class SupplierPriorityService(FinancialReportingService reporting)
{
    public async Task<List<NonCalculatedAvailableSupplierRow>> GetAsync()
    {
        var dashboard = await reporting.GetDashboardAsync();
        return dashboard.Suppliers
            .Where(x => x.Remaining > 0)
            .Select(x => new NonCalculatedAvailableSupplierRow
            {
                SupplierId = x.SupplierId,
                SupplierTitle = x.SupplierTitle,
                InitialClaimAmount = x.InitialClaim,
                CurrentDebt = x.CurrentClaims,
                Priority = x.Priority,
                OpenReceiptCount = x.OpenReceiptCount
            })
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.TotalDebt)
            .ThenBy(x => x.SupplierTitle)
            .ToList();
    }

    public static decimal PriorityScore(
        decimal remaining, decimal maxRemaining, decimal ageRatio,
        decimal coverage, int overdue, int count)
    {
        if (remaining <= 0) return 0;

        var debt = maxRemaining > 0 ? remaining / maxRemaining : 0;
        var age = Math.Clamp(ageRatio, 0, 2) / 2;
        var overdueRate = count > 0 ? (decimal)overdue / count : 0;
        var under = 1 - Math.Clamp(coverage, 0, 1);
        var volume = Math.Min(1, count / 20m);

        return Math.Round(
            100 * (debt * .30m + age * .30m + overdueRate * .20m +
                    under * .15m + volume * .05m), 1);
    }
}