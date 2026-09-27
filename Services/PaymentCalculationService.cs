using System.Security.Cryptography;
using System.Text;
using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

public class PaymentCalculationService(AppDbContext db)
{
    public const decimal WeightTolerance = .0001m;

    public async Task<PaymentWizardState> CalculateAsync(PaymentWizardState s, IReadOnlyList<ImportedPaymentInvoice> imported)
    {
        if (s.TotalAllocationBudget < 0)
            throw new InvalidOperationException("مبلغ قابل تخصیص نمی‌تواند منفی باشد.");

        var settings = await LoadSystemParametersAsync();
        ApplySystemSettingsToState(s, settings);

        var parameters = await db.PaymentParameters.Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).AsNoTracking().ToListAsync();

        var totalWeight = parameters.Sum(x => x.Weight);
        if (Math.Abs(totalWeight - 100m) > WeightTolerance)
            throw new InvalidOperationException($"جمع وزن پارامترهای فعال باید دقیقاً 100٪ باشد؛ مقدار فعلی {totalWeight:0.####}٪ است.");

        var parts = await db.Parts.Where(x => x.IsActive).AsNoTracking().ToListAsync();
        var suppliers = await db.Suppliers.Where(x => x.IsActive).AsNoTracking().ToListAsync();
        var mappings = await db.SupplierParts.Where(x => x.IsActive && x.Supplier!.IsActive && x.Part!.IsActive)
            .AsNoTracking().ToListAsync();
        var evaluations = await db.SupplierPartEvaluations.AsNoTracking().ToListAsync();

        var partByName = parts.GroupBy(x => Normalize(x.Title)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var supplierByName = suppliers.GroupBy(x => Normalize(x.Title)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var mappingByPair = mappings.ToDictionary(x => (x.PartId, x.SupplierId));
        var activeEvaluation = evaluations.Where(x => x.IsActive)
            .GroupBy(x => (x.SupplierPartId, x.PaymentParameterId)).ToDictionary(g => g.Key, g => g.First().Score);
        var anyEvaluation = evaluations
            .GroupBy(x => (x.SupplierPartId, x.PaymentParameterId)).ToDictionary(g => g.Key, g => g.First());

        var previousCurrent = await PreviousCurrentAsync();
        var aggregated = Aggregate(imported).ToList();
        var remainingByKey = aggregated.ToDictionary(
            x => KeyHash(x.ReceiptNo, x.Warehouse, x.PartTitle, x.SupplierTitle),
            x => Math.Max(0, Math.Round(x.DebtAmount - previousCurrent.GetValueOrDefault(KeyHash(x.ReceiptNo, x.Warehouse, x.PartTitle, x.SupplierTitle)), 2)));

        var currentBySupplier = aggregated.GroupBy(x => Normalize(x.SupplierTitle)).ToDictionary(
            g => g.Key,
            g => g.Sum(x => remainingByKey[KeyHash(x.ReceiptNo, x.Warehouse, x.PartTitle, x.SupplierTitle)]));

        var initialBySupplier = suppliers.ToDictionary(x => x.Id, x => x.InitialClaimAmount);
        var currentBySupplierId = supplierByName.Where(x => initialBySupplier.ContainsKey(x.Value.Id))
            .ToDictionary(x => x.Value.Id, x => currentBySupplier.GetValueOrDefault(x.Key));
        var totalCurrentOutstanding = currentBySupplierId.Values.Sum();
        var totalOutstandingForScore = totalCurrentOutstanding + suppliers.Sum(x => x.InitialClaimAmount);

        var rows = new List<PaymentCalculationRow>();
        var totalImported = imported.Sum(x => x.DebtAmount);

        foreach (var x in aggregated)
        {
            if (!partByName.TryGetValue(Normalize(x.PartTitle), out var part))
                throw new InvalidOperationException($"کالا «{x.PartTitle}» در اطلاعات پایه وجود ندارد.");
            if (!supplierByName.TryGetValue(Normalize(x.SupplierTitle), out var supplier))
                throw new InvalidOperationException($"تامین‌کننده «{x.SupplierTitle}» در اطلاعات پایه وجود ندارد.");
            if (!mappingByPair.TryGetValue((part.Id, supplier.Id), out var mapping))
                throw new InvalidOperationException($"ارتباط فعال قطعه «{part.Title}» و تامین‌کننده «{supplier.Title}» تعریف نشده است.");

            var key = KeyHash(x.ReceiptNo, x.Warehouse, part.Title, supplier.Title);
            var prior = previousCurrent.GetValueOrDefault(key);
            var remaining = Math.Max(0, Math.Round(x.DebtAmount - prior, 2));
            var age = (s.CalcDate() - x.ReceiptDate.Date).Days;
            var supplierCurrent = currentBySupplierId.GetValueOrDefault(supplier.Id);
            var supplierInitial = supplier.InitialClaimAmount;

            var row = new PaymentCalculationRow
            {
                SourceRowNumber = x.RowNumber,
                ReceiptNo = x.ReceiptNo,
                Warehouse = x.Warehouse,
                PartTitle = part.Title,
                SupplierTitle = supplier.Title,
                PartId = part.Id,
                SupplierId = supplier.Id,
                OriginalDebt = x.DebtAmount,
                ReceiptDate = x.ReceiptDate.Date,
                ReceiptDateJalali = PersianDateService.ToJalali(x.ReceiptDate),
                ContractSettlementDays = mapping.ContractSettlementDays,
                SupplyCapacity = mapping.SupplyCapacity,
                SupplierInitialClaimAmount = supplierInitial,
                DebtAgeDays = age,
                PreviousAllocated = prior,
                RemainingDebt = remaining,
                SupplierOutstandingDebt = supplierCurrent + supplierInitial
            };

            foreach (var parameter in parameters)
            {
                var inactiveManual = parameter.ScoringMethod == ParameterScoringMethod.Manual &&
                                     anyEvaluation.TryGetValue((mapping.Id, parameter.Id), out var oldEval) &&
                                     !oldEval.IsActive;

                decimal raw;
                var source = "ارزیابی قطعه-تامین‌کننده";
                if (inactiveManual)
                {
                    raw = 0;
                    source = "ارزیابی غیرفعال";
                }
                else if (parameter.ScoringMethod == ParameterScoringMethod.DebtAgeEffective)
                {
                    raw = AgeScore(age, mapping.ContractSettlementDays);
                    source = "محاسبه خودکار";
                }
                else if (parameter.ScoringMethod == ParameterScoringMethod.DebtAmount)
                {
                    raw = SupplierDebtAmountScore(supplierCurrent + supplierInitial, totalOutstandingForScore);
                    source = "محاسبه خودکار";
                }
                else
                {
                    raw = activeEvaluation.GetValueOrDefault((mapping.Id, parameter.Id), 0);
                    if (raw < 1 || raw > parameter.MaxScore)
                        throw new InvalidOperationException($"امتیاز پارامتر «{parameter.Title}» برای قطعه «{part.Title}» و تامین‌کننده «{supplier.Title}» ثبت نشده است.");
                }

                var score = inactiveManual ? 0 : Math.Clamp(raw, 1, parameter.MaxScore);
                row.Scores.Add(new PaymentScoreResult
                {
                    ParameterId = parameter.Id,
                    ParameterTitle = parameter.Title,
                    Type = parameter.Type,
                    Weight = parameter.Weight,
                    Score = score,
                    Contribution = inactiveManual ? 0 : Math.Round(score * parameter.Weight / 100m, 6),
                    Source = source
                });
            }

            row.WeightedScore = row.Scores.Sum(x => x.Contribution);
            if (remaining <= 0)
                row.Warning = "این بدهی قبلاً به‌طور کامل از محل مطالبات جاری تخصیص یافته و در این نوبت مبلغی دریافت نمی‌کند.";

            rows.Add(row);
        }

        s.Parameters = Snapshot(parameters);
        s.Rows = rows;
        s.ImportedDebt = totalImported;
        s.RemainingDebt = rows.Sum(x => x.RemainingDebt);
        AllocateCalculatedBudget(s.TotalAllocationBudget, rows, s.CalculatedInitialSharePercent, s.CalculatedCurrentSharePercent, s.MinimumEffectiveDebtAge, s.MinimumAllocationAmount, s.AllocationRounding);
        s.Step = 2;
        return s;
    }

    public async Task RecalculateAllocationAsync(PaymentWizardState s)
    {
        var settings = await LoadSystemParametersAsync();
        ApplySystemSettingsToState(s, settings);

        foreach (var row in s.Rows)
        {
            row.WeightedScore = row.Scores.Sum(x => x.Contribution);
            row.CalculatedAllocatedAmount = 0;
            row.CalculatedCurrentAllocatedAmount = 0;
            row.CalculatedInitialClaimAllocatedAmount = 0;
            row.AllocatedAmount = 0;
            row.AllocatedCurrentAmount = 0;
            row.AllocatedInitialClaimAmount = 0;
            row.AllocationRatio = 0;
            row.Warning = row.RemainingDebt <= 0
                ? "این بدهی قبلاً به‌طور کامل از محل مطالبات جاری تخصیص یافته و در این نوبت مبلغی دریافت نمی‌کند."
                : null;
        }

        AllocateCalculatedBudget(s.TotalAllocationBudget, s.Rows, s.CalculatedInitialSharePercent, s.CalculatedCurrentSharePercent, s.MinimumEffectiveDebtAge, s.MinimumAllocationAmount, s.AllocationRounding);
    }

    public async Task<Dictionary<string, decimal>> PreviousCurrentAsync()
    {
        return await db.PaymentRunInvoices
            .Where(x => !x.PaymentRun!.IsDeleted && (x.PaymentRun.Status == PaymentRunStatus.Approved || x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered))
            .GroupBy(x => x.PaymentKeyHash)
            .Select(g => new
            {
                Key = g.Key,
                Amount = g.Sum(x => x.AllocatedCurrentAmount > 0
                    ? x.AllocatedCurrentAmount
                    : x.AllocatedInitialClaimAmount > 0
                        ? 0
                        : x.AllocatedAmount)
            })
            .ToDictionaryAsync(x => x.Key, x => x.Amount);
    }

    public async Task<List<PaymentCurrentReceiptSnapshot>> CurrentReceiptsAsync()
    {
        var invoices = await db.PaymentRunInvoices
            .Where(x => !x.PaymentRun!.IsDeleted && (x.PaymentRun.Status == PaymentRunStatus.Approved || x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered))
            .OrderBy(x => x.PaymentRunId).ThenBy(x => x.Id).AsNoTracking().ToListAsync();

        var latest = invoices.GroupBy(x => x.PaymentKeyHash).Select(g => g.Last()).ToList();
        return latest.Select(x =>
        {
            var currentAllocation = x.AllocatedCurrentAmount > 0
                ? x.AllocatedCurrentAmount
                : x.AllocatedInitialClaimAmount > 0
                    ? 0
                    : x.AllocatedAmount;
            return new PaymentCurrentReceiptSnapshot(
                x.PaymentKeyHash,
                x.ReceiptNo,
                x.Warehouse,
                x.PartTitle,
                x.SupplierTitle,
                x.PartId,
                x.SupplierId,
                x.OriginalDebt,
                x.ReceiptDate,
                Math.Max(0, x.RemainingDebt - currentAllocation),
                x.ContractSettlementDays);
        }).Where(x => x.RemainingDebt > 0 && x.SupplierId.HasValue).ToList();
    }

    public async Task<Dictionary<string, SystemParameter>> LoadSystemParametersAsync()
    {
        var rows = await db.SystemParameters.Where(x => x.IsActive).AsNoTracking().ToListAsync();
        return rows.ToDictionary(x => x.Code, x => x);
    }

    static void ApplySystemSettingsToState(PaymentWizardState s, IReadOnlyDictionary<string, SystemParameter> values)
    {
        s.CalculatedInitialSharePercent = Value(values, "CALC_INITIAL_SHARE", 50);
        s.CalculatedCurrentSharePercent = Value(values, "CALC_CURRENT_SHARE", 50);
        s.MinimumEffectiveDebtAge = Value(values, "MIN_EFFECTIVE_DEBT_AGE", 0);
        s.MinimumAllocationAmount = Value(values, "MIN_ALLOCATION_AMOUNT", 0);
        s.AllocationRounding = Value(values, "ALLOCATION_ROUNDING", 1000000);
        ValidateShares(s.CalculatedInitialSharePercent, s.CalculatedCurrentSharePercent, "پرداخت محاسباتی");
    }

    public static void ValidateShares(decimal initialShare, decimal currentShare, string title)
    {
        if (initialShare < 0 || currentShare < 0 || initialShare > 100 || currentShare > 100 || Math.Abs(initialShare + currentShare - 100m) > WeightTolerance)
            throw new InvalidOperationException($"مجموع درصد سهم مطالبات استقراری و جاری در {title} باید دقیقاً 100٪ باشد.");
    }

    static decimal Value(IReadOnlyDictionary<string, SystemParameter> values, string code, decimal fallback) =>
        values.TryGetValue(code, out var x) ? x.Value : fallback;

    public static decimal AgeScore(int age, int contract)
    {
        if (contract <= 0) return 5;
        var a = Math.Max(0, age);
        if (a < contract * .25m) return 1;
        if (a <= contract * .50m) return 2;
        if (a <= contract * .75m) return 1;
        if (a <= contract) return 4;
        if (a <= contract + 30) return 4;
        return 5;
    }

    public static decimal DebtAmountScore(decimal debt, decimal total)
    {
        if (total <= 0) return 1;
        var p = debt / total * 100m;
        if (p < 3) return 1;
        if (p <= 10) return 2;
        if (p <= 20) return 3;
        if (p <= 35) return 4;
        return 5;
    }

    public static decimal SupplierDebtAmountScore(decimal supplierOutstandingDebt, decimal totalOutstandingDebt) =>
        DebtAmountScore(supplierOutstandingDebt, totalOutstandingDebt);

    public static void AllocateCalculatedBudget(decimal budget, List<PaymentCalculationRow> rows, decimal initialSharePercent, decimal currentSharePercent, decimal minAge, decimal minAmount, decimal rounding)
    {
        ValidateShares(initialSharePercent, currentSharePercent, "پرداخت محاسباتی");
        EnsureRounding(rounding);

        foreach (var row in rows)
        {
            row.AllocatedAmount = 0;
            row.AllocatedCurrentAmount = 0;
            row.AllocatedInitialClaimAmount = 0;
            row.CalculatedAllocatedAmount = 0;
            row.CalculatedCurrentAllocatedAmount = 0;
            row.CalculatedInitialClaimAllocatedAmount = 0;
            row.AllocationRatio = 0;
        }

        var supplierInitial = rows.Where(x => x.SupplierId.HasValue)
            .GroupBy(x => x.SupplierId!.Value)
            .ToDictionary(g => g.Key, g => Math.Max(0, g.First().SupplierInitialClaimAmount));
        var remainingBudget = FloorToMultiple(Math.Min(Math.Max(0, budget), rows.Where(x => x.DebtAgeDays > minAge).Sum(x => x.RemainingDebt)
            + supplierInitial.Where(x => rows.Any(r => r.SupplierId == x.Key && r.DebtAgeDays > minAge)).Sum(x => x.Value)), rounding);

        while (remainingBudget >= rounding)
        {
            var active = rows.Where(x => x.DebtAgeDays > minAge && x.SupplierId.HasValue &&
                                         AvailableCapacity(x, supplierInitial.GetValueOrDefault(x.SupplierId!.Value)) >= rounding)
                .ToList();
            if (active.Count == 0) break;

            var weightSum = active.Sum(x => Math.Max(.000001m, x.WeightedScore));
            var desired = active.ToDictionary(x => x, x => remainingBudget * Math.Max(.000001m, x.WeightedScore) / weightSum);
            var eligible = active.Where(x => desired[x] > minAmount && desired[x] >= rounding).ToList();
            if (eligible.Count == 0) break;

            var assignedThisPass = 0m;
            var eligibleWeight = eligible.Sum(x => Math.Max(.000001m, x.WeightedScore));

            foreach (var row in eligible.OrderByDescending(x => x.WeightedScore).ThenBy(x => x.ReceiptDate))
            {
                var amount = FloorToMultiple(remainingBudget * Math.Max(.000001m, row.WeightedScore) / eligibleWeight, rounding);
                var initialRemaining = supplierInitial.GetValueOrDefault(row.SupplierId!.Value);
                var capacity = AvailableCapacity(row, initialRemaining);
                amount = Math.Min(amount, FloorToMultiple(capacity, rounding));
                if (amount < rounding) continue;

                var split = SplitByShares(amount, initialRemaining, Math.Max(0, row.RemainingDebt - row.AllocatedCurrentAmount), initialSharePercent, currentSharePercent);
                if (split.Total <= 0) continue;

                row.AllocatedInitialClaimAmount += split.InitialClaim;
                row.AllocatedCurrentAmount += split.Current;
                row.AllocatedAmount = row.AllocatedInitialClaimAmount + row.AllocatedCurrentAmount;
                row.CalculatedInitialClaimAllocatedAmount = row.AllocatedInitialClaimAmount;
                row.CalculatedCurrentAllocatedAmount = row.AllocatedCurrentAmount;
                row.CalculatedAllocatedAmount = row.AllocatedAmount;
                supplierInitial[row.SupplierId.Value] = Math.Max(0, initialRemaining - split.InitialClaim);

                assignedThisPass += split.Total;
                remainingBudget -= split.Total;
                if (remainingBudget < rounding) break;
            }

            if (assignedThisPass <= 0) break;
        }

        var totalAssigned = rows.Sum(x => x.CalculatedAllocatedAmount);
        if (totalAssigned > 0)
            foreach (var row in rows)
                row.AllocationRatio = Math.Round(row.CalculatedAllocatedAmount / totalAssigned, 8);
    }

    public static void DistributeSupplierAllocation(decimal target, List<PaymentCalculationRow> rows, decimal initialSharePercent, decimal currentSharePercent, decimal rounding)
    {
        ValidateShares(initialSharePercent, currentSharePercent, "پرداخت محاسباتی");
        EnsureRounding(rounding);
        target = Math.Round(Math.Max(0, target), 2);
        if (Math.Abs(FloorToMultiple(target, rounding) - target) > .005m)
            throw new InvalidOperationException($"مبلغ تخصیص تامین‌کننده باید مضربی از {rounding:N0} ریال باشد.");

        var originalWeights = rows.ToDictionary(x => x, x => x.CalculatedAllocatedAmount);
        foreach (var row in rows)
        {
            row.WeightedScore = Math.Max(.000001m, originalWeights[row]);
            row.AllocatedAmount = 0;
            row.AllocatedCurrentAmount = 0;
            row.AllocatedInitialClaimAmount = 0;
        }

        AllocateCalculatedBudget(target, rows, initialSharePercent, currentSharePercent, decimal.MinValue, 0, rounding);
        foreach (var row in rows) row.WeightedScore = originalWeights[row];
    }

    public static ClaimPaymentSplit SplitByShares(decimal total, decimal initialAvailable, decimal currentAvailable, decimal initialSharePercent, decimal currentSharePercent)
    {
        ValidateShares(initialSharePercent, currentSharePercent, "تخصیص");
        total = Math.Max(0, total);
        initialAvailable = Math.Max(0, initialAvailable);
        currentAvailable = Math.Max(0, currentAvailable);

        var desiredInitial = Math.Round(total * initialSharePercent / 100m, 2);
        var desiredCurrent = total - desiredInitial;
        var initial = Math.Min(desiredInitial, initialAvailable);
        var current = Math.Min(desiredCurrent, currentAvailable);
        var remainder = total - initial - current;

        if (remainder > 0)
        {
            var extraInitial = Math.Min(remainder, initialAvailable - initial);
            initial += Math.Max(0, extraInitial);
            remainder -= Math.Max(0, extraInitial);
        }
        if (remainder > 0)
        {
            var extraCurrent = Math.Min(remainder, currentAvailable - current);
            current += Math.Max(0, extraCurrent);
        }

        return new ClaimPaymentSplit(Math.Round(initial, 2), Math.Round(current, 2));
    }

    public static void AllocateBudget(decimal budget, List<PaymentCalculationRow> rows)
    {
        var total = Math.Min(Math.Max(0, budget), rows.Sum(x => x.RemainingDebt));
        var rem = total;
        var active = rows.Where(x => x.RemainingDebt > 0).ToList();
        foreach (var x in rows)
        {
            x.AllocatedAmount = 0;
            x.AllocationRatio = 0;
        }

        for (var guard = 0; guard < rows.Count + 5 && rem > .005m && active.Count > 0; guard++)
        {
            var sw = active.Sum(x => Math.Max(.000001m, x.WeightedScore));
            foreach (var x in active.ToList())
            {
                var share = rem * Math.Max(.000001m, x.WeightedScore) / sw;
                var room = x.RemainingDebt - x.AllocatedAmount;
                x.AllocatedAmount = Math.Round(x.AllocatedAmount + Math.Min(share, room), 2);
            }
            var assigned = rows.Sum(x => x.AllocatedAmount);
            rem = Math.Round(total - assigned, 2);
            active = active.Where(x => x.RemainingDebt - x.AllocatedAmount > .005m).ToList();
        }

        var assigned2 = rows.Sum(x => x.AllocatedAmount);
        var delta = Math.Round(total - assigned2, 2);
        if (delta != 0 && delta > 0)
        {
            var t = rows.Where(x => x.RemainingDebt - x.AllocatedAmount >= delta).OrderByDescending(x => x.WeightedScore).FirstOrDefault();
            if (t != null) t.AllocatedAmount = Math.Round(t.AllocatedAmount + delta, 2);
        }
        else if (delta < 0)
        {
            var t = rows.Where(x => x.AllocatedAmount >= -delta).OrderByDescending(x => x.WeightedScore).FirstOrDefault();
            if (t != null) t.AllocatedAmount = Math.Round(t.AllocatedAmount + delta, 2);
        }

        if (total > 0)
            foreach (var x in rows)
                x.AllocationRatio = Math.Round(x.AllocatedAmount / total, 8);
    }

    public static decimal DistributeOldestCurrentAmount(decimal amount, IEnumerable<PaymentCurrentReceiptSnapshot> receipts)
    {
        var left = Math.Max(0, amount);
        var allocated = 0m;
        foreach (var receipt in receipts.Where(x => x.RemainingDebt > 0).OrderBy(x => x.ReceiptDate).ThenBy(x => x.ReceiptNo))
        {
            if (left <= 0) break;
            var take = Math.Min(left, receipt.RemainingDebt);
            allocated += take;
            left -= take;
        }
        return allocated;
    }

    static decimal AvailableCapacity(PaymentCalculationRow row, decimal supplierInitialRemaining) =>
        Math.Max(0, row.RemainingDebt - row.AllocatedCurrentAmount) + Math.Max(0, supplierInitialRemaining);

    static decimal FloorToMultiple(decimal value, decimal multiple) =>
        Math.Floor(Math.Max(0, value) / multiple) * multiple;

    static void EnsureRounding(decimal rounding)
    {
        if (rounding <= 0) throw new InvalidOperationException("رندینگ مبلغ تخصیص باید بزرگ‌تر از صفر باشد.");
    }

    public static string Key(string receipt, string warehouse, string part, string supplier) =>
        string.Join("|", new[] { receipt, warehouse, part, supplier }.Select(Normalize));

    public static string KeyHash(string receipt, string warehouse, string part, string supplier)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(Key(receipt, warehouse, part, supplier))));
    }

    static IEnumerable<ImportedPaymentInvoice> Aggregate(IReadOnlyList<ImportedPaymentInvoice> rows) =>
        rows.GroupBy(x => new { R = Normalize(x.ReceiptNo), W = Normalize(x.Warehouse), P = Normalize(x.PartTitle), S = Normalize(x.SupplierTitle) })
            .Select(g => new ImportedPaymentInvoice(g.Min(x => x.RowNumber), g.First().ReceiptNo, g.First().Warehouse, g.First().PartTitle, g.First().SupplierTitle, g.Sum(x => x.DebtAmount), g.Min(x => x.ReceiptDate)));

    static string Normalize(string value) => (value ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک").ToLowerInvariant();

    static List<PaymentParameterSnapshot> Snapshot(List<PaymentParameter> rows) =>
        rows.Select(x => new PaymentParameterSnapshot(x.Id, x.Code, x.Title, x.Type, x.Weight, x.MaxScore, x.ScoringGuide, x.ScoringMethod, x.SortOrder)).ToList();
}

public record ClaimPaymentSplit(decimal InitialClaim, decimal Current)
{
    public decimal Total => InitialClaim + Current;
}

public record PaymentCurrentReceiptSnapshot(
    string PaymentKeyHash,
    string ReceiptNo,
    string Warehouse,
    string PartTitle,
    string SupplierTitle,
    int? PartId,
    int? SupplierId,
    decimal OriginalDebt,
    DateTime ReceiptDate,
    decimal RemainingDebt,
    int ContractSettlementDays);

public static class PaymentWizardStateExtensions
{
    public static DateTime CalcDate(this PaymentWizardState x) => PersianDateService.Parse(x.CalculationDateJalali);
}
