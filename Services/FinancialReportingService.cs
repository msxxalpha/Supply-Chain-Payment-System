using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

public class FinancialReportingService(AppDbContext db)
{
    public async Task<FinancialDashboardData> GetDashboardAsync()
    {
        var context = await LoadContextAsync();

        var supplierRows = BuildSupplierRows(context.Suppliers, context.FinancialRuns, context.LatestInvoices, context.ActiveInvoices);
        supplierRows = ApplyPriorityAndSort(supplierRows);

        var parts = BuildPartRows(context.LatestInvoices, context.ActiveInvoices);
        var totals = BuildPaymentTypeTotals(context.FinancialRuns);

        var original = supplierRows.Sum(x => x.InitialClaim + x.CurrentClaims);
        var paid = supplierRows.Sum(x => x.TotalPaid);
        var remaining = supplierRows.Sum(x => x.Remaining);
        var coverage = original > 0 ? Math.Clamp(paid / original, 0, 1) : 0;

        var trend = context.FinancialRuns
            .OrderByDescending(x => x.Id)
            .Take(12)
            .OrderBy(x => x.Id)
            .Select(x => new FinancialRunRow(
                x.Id, x.Title, x.CalculationDateJalali, x.RunType, x.Status,
                x.SupplierSummaries.Sum(s => s.AllocatedAmount)))
            .ToList();

        return new FinancialDashboardData(
            context.WorkflowRuns.Count,
            context.WorkflowRuns.Count(x => x.Status == PaymentRunStatus.PaymentOrdered),
            context.WorkflowRuns.Count(x => x.Status == PaymentRunStatus.Approved),
            original, paid, remaining, coverage,
            totals, supplierRows, parts,
            BuildSupplierPartRows(context.LatestInvoices, context.ActiveInvoices),
            trend);
    }

    public async Task<SupplierDashboardData?> GetSupplierAsync(int supplierId)
    {
        var context = await LoadContextAsync();
        var supplier = context.Suppliers.SingleOrDefault(x => x.Id == supplierId);
        if (supplier == null) return null;

        var latest = context.LatestInvoices.Where(x => x.SupplierId == supplierId).ToList();
        var activeCurrentInvoices = context.ActiveInvoices.Where(x => x.SupplierId == supplierId).ToList();
        var paidSummary = context.FinancialRuns.SelectMany(x => x.SupplierSummaries)
            .Where(x => x.SupplierId == supplierId).ToList();
        var nonCalculatedLines = context.FinancialRuns
            .Where(x => x.RunType == PaymentRunType.NonCalculated)
            .SelectMany(x => x.NonCalculatedPaymentLines)
            .Where(x => x.SupplierId == supplierId)
            .ToList();

        var calculatedPaid = context.FinancialRuns.Where(x => x.RunType == PaymentRunType.Calculated)
            .SelectMany(x => x.SupplierSummaries).Where(x => x.SupplierId == supplierId)
            .Sum(x => x.AllocatedAmount);

        var initialPaid = paidSummary.Sum(x => x.InitialClaimAllocatedAmount);
        var currentPaid = paidSummary.Sum(x => x.CurrentClaimAllocatedAmount);
        var typeTotals = BuildSupplierTypeTotals(context.FinancialRuns, supplierId);

        var currentClaims = latest.Sum(CurrentRemaining);
        var remaining = supplier.InitialClaimAmount + currentClaims;
        var totalPaid = calculatedPaid + typeTotals.NonCalculatedTotal;
        var historicalInitial = supplier.InitialClaimAmount + initialPaid;
        var historicalCurrent = currentClaims + currentPaid;
        var basis = historicalInitial + historicalCurrent;
        var coverage = basis > 0 ? Math.Clamp(totalPaid / basis, 0, 1) : 0;

        var overdue = latest.Count(x => CurrentRemaining(x) > 0 &&
            x.ContractSettlementDays > 0 && x.DebtAgeDays > x.ContractSettlementDays);

        var ageRatio = latest.Count(x => CurrentRemaining(x) > 0) > 0
            ? latest.Where(x => CurrentRemaining(x) > 0)
                .Average(x => x.ContractSettlementDays > 0
                    ? (decimal)x.DebtAgeDays / x.ContractSettlementDays
                    : 1)
            : 0;

        var paymentHistory = BuildSupplierPaymentHistory(
            context.FinancialRuns, supplierId, nonCalculatedLines);

        var partRows = latest
            .GroupBy(x => new { x.PartId, x.PartTitle })
            .Select(g =>
            {
                var ids = g.Select(x => x.PaymentKeyHash).ToHashSet();
                var currentPaidForPart = activeCurrentInvoices
                    .Where(x => ids.Contains(x.PaymentKeyHash))
                    .Sum(CurrentAllocation);

                var originalForPart = g.Sum(x => x.OriginalDebt);
                var remainingForPart = g.Sum(CurrentRemaining);
                return new SupplierPartFinanceRow(
                    supplier.Title, g.Key.PartTitle, g.Count(x => CurrentRemaining(x) > 0),
                    remainingForPart,
                    currentPaidForPart,
                    originalForPart,
                    originalForPart > 0 ? Math.Clamp(currentPaidForPart / originalForPart, 0, 1) : 0,
                    g.Any(x => x.ContractSettlementDays > 0 && x.DebtAgeDays > x.ContractSettlementDays));
            })
            .OrderByDescending(x => x.Remaining)
            .ThenBy(x => x.PartTitle)
            .ToList();

        var claims = await db.SupplierClaimHistories.AsNoTracking()
            .Where(x => x.SupplierId == supplierId && x.ClaimType == SupplierClaimType.Initial)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new SupplierClaimFinanceRow(
                x.EffectiveDateJalali, x.AmountBefore, x.AmountChange, x.AmountAfter,
                x.Reference, x.PaymentRunId))
            .ToListAsync();

        var activities = await db.SupplierActivities.AsNoTracking()
            .Where(x => x.SupplierId == supplierId)
            .OrderBy(x => x.Activity!.Title)
            .Select(x => x.Activity!.Title)
            .ToListAsync();

        var mappingCount = await db.SupplierParts.AsNoTracking()
            .CountAsync(x => x.SupplierId == supplierId && x.IsActive);

        var openReceiptCount = latest.Count(x => CurrentRemaining(x) > 0);

        return new SupplierDashboardData(
            new SupplierFinanceProfile(
                supplier.Id, supplier.Code, supplier.Title, activities,
                supplier.InitialClaimAmount, currentClaims, remaining,
                historicalInitial, historicalCurrent,
                calculatedPaid,
                typeTotals.Cash, typeTotals.Check, typeTotals.Vehicle,
                typeTotals.RawMaterial, typeTotals.IntroductionLetter, typeTotals.CreditLimit,
                typeTotals.NonCalculatedTotal, totalPaid, coverage,
                openReceiptCount, mappingCount, overdue, ageRatio),
            paymentHistory, partRows, claims);
    }

    async Task<ReportingContext> LoadContextAsync()
    {
        var workflowRuns = await db.PaymentRuns.AsNoTracking()
            .Include(x => x.Invoices)
            .Include(x => x.SupplierSummaries)
            .Include(x => x.NonCalculatedPaymentLines)
            .Where(x => !x.IsDeleted &&
                (x.Status == PaymentRunStatus.Approved ||
                 x.Status == PaymentRunStatus.PaymentOrdered))
            .OrderBy(x => x.Id)
            .ToListAsync();

        // صرفاً تایید شدن پرداخت، اثر مالی ایجاد نمی‌کند.
        // فقط دستور پرداخت‌شده یا رکورد legacy تاییدشده‌ای که اثر مالی آن ثبت شده،
        // باید در دریافتی‌ها و مانده‌های پرداخت‌شده وارد شود.
        var financialRuns = workflowRuns
            .Where(IsFinanciallyEffective)
            .ToList();

        var activeInvoices = financialRuns.SelectMany(x => x.Invoices)
            .OrderBy(x => x.PaymentRunId).ThenBy(x => x.Id)
            .ToList();

        var latestInvoices = activeInvoices
            .GroupBy(x => x.PaymentKeyHash)
            .Select(g => g.Last())
            .ToList();

        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Title)
            .ToListAsync();

        return new ReportingContext(workflowRuns, financialRuns, activeInvoices, latestInvoices, suppliers);
    }

    static bool IsFinanciallyEffective(PaymentRun x) =>
        x.Status == PaymentRunStatus.PaymentOrdered ||
        (x.Status == PaymentRunStatus.Approved && x.FinancialEffectsAppliedAt.HasValue);

    static List<SupplierFinanceRow> BuildSupplierRows(
        List<Supplier> suppliers,
        List<PaymentRun> activeRuns,
        List<PaymentRunInvoice> latestInvoices,
        List<PaymentRunInvoice> activeInvoices)
    {
        var result = new List<SupplierFinanceRow>();

        foreach (var supplier in suppliers)
        {
            var latest = latestInvoices.Where(x => x.SupplierId == supplier.Id).ToList();
            var summary = activeRuns.SelectMany(x => x.SupplierSummaries)
                .Where(x => x.SupplierId == supplier.Id).ToList();

            var typeTotals = BuildSupplierTypeTotals(activeRuns, supplier.Id);
            var calculatedPaid = activeRuns
                .Where(x => x.RunType == PaymentRunType.Calculated)
                .SelectMany(x => x.SupplierSummaries)
                .Where(x => x.SupplierId == supplier.Id)
                .Sum(x => x.AllocatedAmount);

            var currentClaims = latest.Sum(CurrentRemaining);
            var totalPaid = calculatedPaid + typeTotals.NonCalculatedTotal;
            var initialPaid = summary.Sum(x => x.InitialClaimAllocatedAmount);
            var currentPaid = summary.Sum(x => x.CurrentClaimAllocatedAmount);

            var historicalInitial = supplier.InitialClaimAmount + initialPaid;
            var historicalCurrent = currentClaims + currentPaid;
            var original = historicalInitial + historicalCurrent;
            var remaining = supplier.InitialClaimAmount + currentClaims;
            var coverage = original > 0 ? Math.Clamp(totalPaid / original, 0, 1) : 0;

            var open = latest.Count(x => CurrentRemaining(x) > 0);
            var overdue = latest.Count(x => CurrentRemaining(x) > 0 &&
                x.ContractSettlementDays > 0 &&
                x.DebtAgeDays > x.ContractSettlementDays);

            var ageRatio = open > 0
                ? latest.Where(x => CurrentRemaining(x) > 0)
                    .Average(x => x.ContractSettlementDays > 0
                        ? (decimal)x.DebtAgeDays / x.ContractSettlementDays : 1)
                : 0;

            result.Add(new SupplierFinanceRow(
                supplier.Id, supplier.Code, supplier.Title,
                open, supplier.InitialClaimAmount, currentClaims,
                calculatedPaid, typeTotals.Cash, typeTotals.Check, typeTotals.Vehicle,
                typeTotals.RawMaterial, typeTotals.IntroductionLetter, typeTotals.CreditLimit,
                typeTotals.NonCalculatedTotal, totalPaid,
                remaining, coverage, ageRatio, overdue, 0));
        }

        return result.Where(x => x.Remaining > 0 || x.TotalPaid > 0).ToList();
    }

    static List<SupplierFinanceRow> ApplyPriorityAndSort(List<SupplierFinanceRow> rows)
    {
        var maxRemaining = rows.Select(x => x.Remaining).DefaultIfEmpty(1).Max();
        return rows.Select(x => x with
        {
            Priority = SupplierPriorityService.PriorityScore(
                x.Remaining, maxRemaining, x.AgeRatio, x.Coverage, x.Overdue, x.OpenReceiptCount)
        })
        .OrderByDescending(x => x.Priority)
        .ThenByDescending(x => x.Remaining)
        .ThenBy(x => x.SupplierTitle)
        .ToList();
    }

    static List<PartFinanceRow> BuildPartRows(
        List<PaymentRunInvoice> latestInvoices,
        List<PaymentRunInvoice> activeInvoices)
    {
        var result = new List<PartFinanceRow>();
        foreach (var group in latestInvoices.GroupBy(x => new { x.PartId, x.PartTitle }))
        {
            var keys = group.Select(x => x.PaymentKeyHash).ToHashSet();
            var currentPaid = activeInvoices.Where(x => keys.Contains(x.PaymentKeyHash)).Sum(CurrentAllocation);
            var current = group.Sum(CurrentRemaining);
            var open = group.Count(x => CurrentRemaining(x) > 0);
            var overdue = group.Count(x => CurrentRemaining(x) > 0 &&
                x.ContractSettlementDays > 0 &&
                x.DebtAgeDays > x.ContractSettlementDays);
            var original = group.Sum(x => x.OriginalDebt);

            result.Add(new PartFinanceRow(
                group.Key.PartId ?? 0, group.Key.PartTitle, open,
                original, currentPaid, current,
                original > 0 ? Math.Clamp(currentPaid / original, 0, 1) : 0,
                overdue));
        }
        return result.OrderByDescending(x => x.Remaining).ThenBy(x => x.PartTitle).ToList();
    }

    static List<SupplierPartFinanceRow> BuildSupplierPartRows(
        List<PaymentRunInvoice> latestInvoices,
        List<PaymentRunInvoice> activeInvoices)
    {
        return latestInvoices.GroupBy(x => new { x.SupplierId, x.SupplierTitle, x.PartId, x.PartTitle })
            .Select(g =>
            {
                var keys = g.Select(x => x.PaymentKeyHash).ToHashSet();
                var paid = activeInvoices.Where(x => keys.Contains(x.PaymentKeyHash)).Sum(CurrentAllocation);
                var original = g.Sum(x => x.OriginalDebt);
                var remaining = g.Sum(CurrentRemaining);
                return new SupplierPartFinanceRow(
                    g.Key.SupplierTitle, g.Key.PartTitle,
                    g.Count(x => CurrentRemaining(x) > 0),
                    remaining, paid, original,
                    original > 0 ? Math.Clamp(paid / original, 0, 1) : 0,
                    g.Any(x => x.ContractSettlementDays > 0 &&
                               x.DebtAgeDays > x.ContractSettlementDays));
            })
            .OrderByDescending(x => x.Remaining).ThenBy(x => x.SupplierTitle).ThenBy(x => x.PartTitle).ToList();
    }

    static SupplierPaymentTypeTotals BuildSupplierTypeTotals(List<PaymentRun> runs, int supplierId)
    {
        var calc = runs.Where(x => x.RunType == PaymentRunType.Calculated)
            .SelectMany(x => x.SupplierSummaries)
            .Where(x => x.SupplierId == supplierId)
            .Sum(x => x.AllocatedAmount);

        var lines = runs.Where(x => x.RunType == PaymentRunType.NonCalculated)
            .SelectMany(x => x.NonCalculatedPaymentLines)
            .Where(x => x.SupplierId == supplierId)
            .ToList();

        decimal legacy(NonCalculatedPaymentType type) =>
            runs.Where(x => x.RunType == PaymentRunType.NonCalculated)
                .SelectMany(x => x.SupplierSummaries)
                .Where(x => x.SupplierId == supplierId && x.PaymentType == type)
                .Sum(x => x.AllocatedAmount);

        var cash = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.Cash).Sum(x => x.AllocatedAmount);
        var check = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.CheckTransfer).Sum(x => x.AllocatedAmount);
        var vehicle = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.VehicleTransfer).Sum(x => x.AllocatedAmount);
        var raw = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.RawMaterialTransfer).Sum(x => x.AllocatedAmount);
        var intro = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.IntroductionLetter).Sum(x => x.AllocatedAmount);
        var credit = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.CreditLimit).Sum(x => x.AllocatedAmount);

        if (lines.Count == 0)
        {
            cash = legacy(NonCalculatedPaymentType.Cash);
            check = legacy(NonCalculatedPaymentType.CheckTransfer);
            vehicle = legacy(NonCalculatedPaymentType.VehicleTransfer);
            raw = legacy(NonCalculatedPaymentType.RawMaterialTransfer);
            intro = legacy(NonCalculatedPaymentType.IntroductionLetter);
            credit = legacy(NonCalculatedPaymentType.CreditLimit);
        }

        return new SupplierPaymentTypeTotals(
            calc, cash, check, vehicle, raw, intro, credit,
            cash + check + vehicle + raw + intro + credit);
    }

    static PaymentTypeTotals BuildPaymentTypeTotals(List<PaymentRun> runs)
    {
        var calc = runs.Where(x => x.RunType == PaymentRunType.Calculated)
            .SelectMany(x => x.SupplierSummaries)
            .Sum(x => x.AllocatedAmount);

        var lines = runs.Where(x => x.RunType == PaymentRunType.NonCalculated)
            .SelectMany(x => x.NonCalculatedPaymentLines)
            .ToList();

        decimal legacy(NonCalculatedPaymentType type) =>
            runs.Where(x => x.RunType == PaymentRunType.NonCalculated)
                .SelectMany(x => x.SupplierSummaries)
                .Where(x => x.PaymentType == type)
                .Sum(x => x.AllocatedAmount);

        var cash = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.Cash).Sum(x => x.AllocatedAmount);
        var check = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.CheckTransfer).Sum(x => x.AllocatedAmount);
        var vehicle = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.VehicleTransfer).Sum(x => x.AllocatedAmount);
        var raw = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.RawMaterialTransfer).Sum(x => x.AllocatedAmount);
        var intro = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.IntroductionLetter).Sum(x => x.AllocatedAmount);
        var credit = lines.Where(x => x.PaymentType == NonCalculatedPaymentType.CreditLimit).Sum(x => x.AllocatedAmount);

        if (lines.Count == 0)
        {
            cash = legacy(NonCalculatedPaymentType.Cash);
            check = legacy(NonCalculatedPaymentType.CheckTransfer);
            vehicle = legacy(NonCalculatedPaymentType.VehicleTransfer);
            raw = legacy(NonCalculatedPaymentType.RawMaterialTransfer);
            intro = legacy(NonCalculatedPaymentType.IntroductionLetter);
            credit = legacy(NonCalculatedPaymentType.CreditLimit);
        }

        var nonCalculated = cash + check + vehicle + raw + intro + credit;
        return new PaymentTypeTotals(calc, cash, check, vehicle, raw, intro, credit, nonCalculated, calc + nonCalculated);
    }

    static List<SupplierPaymentHistoryRow> BuildSupplierPaymentHistory(
        List<PaymentRun> runs, int supplierId, List<NonCalculatedPaymentLineSnapshot> lines)
    {
        var result = new List<SupplierPaymentHistoryRow>();

        foreach (var run in runs.Where(x => x.RunType == PaymentRunType.Calculated))
        {
            var summary = run.SupplierSummaries.FirstOrDefault(x => x.SupplierId == supplierId);
            if (summary != null && summary.AllocatedAmount > 0)
            {
                result.Add(new SupplierPaymentHistoryRow(
                    run.Id, run.CalculationDateJalali, "محاسباتی", "تخصیص مبلغ",
                    summary.AllocatedAmount, summary.InitialClaimAllocatedAmount, summary.CurrentClaimAllocatedAmount,
                    run.Status));
            }
        }

        foreach (var group in lines.GroupBy(x => new { x.PaymentRunId, x.PaymentType }))
        {
            var run = runs.FirstOrDefault(x => x.Id == group.Key.PaymentRunId);
            var amount = group.Sum(x => x.AllocatedAmount);
            if (run != null && amount > 0)
            {
                result.Add(new SupplierPaymentHistoryRow(
                    run.Id, run.CalculationDateJalali, "غیرمحاسباتی",
                    PaymentTypeTitle(group.Key.PaymentType),
                    amount,
                    group.Sum(x => x.InitialClaimAllocatedAmount),
                    group.Sum(x => x.CurrentClaimAllocatedAmount),
                    run.Status));
            }
        }

        return result.OrderByDescending(x => x.RunId).ThenBy(x => x.PaymentTypeTitle).ToList();
    }

    static string PaymentTypeTitle(NonCalculatedPaymentType type) => type switch
    {
        NonCalculatedPaymentType.Cash => "نقدی",
        NonCalculatedPaymentType.CheckTransfer => "واگذاری چک",
        NonCalculatedPaymentType.VehicleTransfer => "واگذاری خودرو",
        NonCalculatedPaymentType.RawMaterialTransfer => "واگذاری مواداولیه",
        NonCalculatedPaymentType.IntroductionLetter => "معرفی نامه",
        NonCalculatedPaymentType.CreditLimit => "حد اعتباری",
        _ => "نامشخص"
    };

    static decimal CurrentAllocation(PaymentRunInvoice x) =>
        x.AllocatedCurrentAmount > 0
            ? x.AllocatedCurrentAmount
            : x.AllocatedInitialClaimAmount > 0 ? 0 : x.AllocatedAmount;

    static decimal CurrentRemaining(PaymentRunInvoice x) =>
        Math.Max(0, x.RemainingDebt - CurrentAllocation(x));

    record ReportingContext(
        List<PaymentRun> WorkflowRuns,
        List<PaymentRun> FinancialRuns,
        List<PaymentRunInvoice> ActiveInvoices,
        List<PaymentRunInvoice> LatestInvoices,
        List<Supplier> Suppliers);
}

public record FinancialDashboardData(
    int RunCount, int PaymentOrderedRuns, int ApprovedRuns,
    decimal Original, decimal TotalPaid, decimal Remaining, decimal Coverage,
    PaymentTypeTotals PaymentTypes,
    List<SupplierFinanceRow> Suppliers,
    List<PartFinanceRow> Parts,
    List<SupplierPartFinanceRow> SupplierParts,
    List<FinancialRunRow> Trend);

public record PaymentTypeTotals(
    decimal Calculated, decimal Cash, decimal Check, decimal Vehicle,
    decimal RawMaterial, decimal IntroductionLetter, decimal CreditLimit,
    decimal NonCalculatedTotal, decimal GrandTotal);

public record SupplierPaymentTypeTotals(
    decimal Calculated, decimal Cash, decimal Check, decimal Vehicle,
    decimal RawMaterial, decimal IntroductionLetter, decimal CreditLimit,
    decimal NonCalculatedTotal);

public record SupplierFinanceRow(
    int SupplierId, string SupplierCode, string SupplierTitle, int OpenReceiptCount,
    decimal InitialClaim, decimal CurrentClaims, decimal CalculatedPaid,
    decimal CashPaid, decimal CheckPaid, decimal VehiclePaid, decimal RawMaterialPaid,
    decimal IntroductionLetterPaid, decimal CreditLimitPaid, decimal NonCalculatedTotal,
    decimal TotalPaid, decimal Remaining, decimal Coverage, decimal AgeRatio,
    int Overdue, decimal Priority)
{
    public decimal GrandPayableBasis => InitialClaim + CurrentClaims;
}

public record PartFinanceRow(
    int PartId, string PartTitle, int OpenReceiptCount, decimal Original,
    decimal Paid, decimal Remaining, decimal Coverage, int Overdue);

public record SupplierPartFinanceRow(
    string SupplierTitle, string PartTitle, int OpenReceiptCount,
    decimal Remaining, decimal Paid, decimal Original, decimal Coverage,
    bool HasOverdue);

public record FinancialRunRow(
    int RunId, string Title, string Date, PaymentRunType RunType,
    PaymentRunStatus Status, decimal Amount);

public record SupplierDashboardData(
    SupplierFinanceProfile Profile,
    List<SupplierPaymentHistoryRow> PaymentHistory,
    List<SupplierPartFinanceRow> PartBreakdown,
    List<SupplierClaimFinanceRow> ClaimHistory);

public record SupplierFinanceProfile(
    int SupplierId, string SupplierCode, string SupplierTitle,
    List<string> Activities, decimal InitialClaim, decimal CurrentClaims,
    decimal Remaining, decimal HistoricalInitial, decimal HistoricalCurrent,
    decimal CalculatedPaid, decimal CashPaid, decimal CheckPaid, decimal VehiclePaid,
    decimal RawMaterialPaid, decimal IntroductionLetterPaid, decimal CreditLimitPaid,
    decimal NonCalculatedTotal, decimal TotalPaid, decimal Coverage,
    int OpenReceiptCount, int MappingCount, int Overdue, decimal AgeRatio)
{
    public decimal GrandPayableBasis => InitialClaim + CurrentClaims;
}

public record SupplierPaymentHistoryRow(
    int RunId, string Date, string RunTypeTitle, string PaymentTypeTitle,
    decimal Amount, decimal InitialAmount, decimal CurrentAmount,
    PaymentRunStatus Status);

public record SupplierClaimFinanceRow(
    string Date, decimal Before, decimal Change, decimal After,
    string Reference, int? PaymentRunId);
