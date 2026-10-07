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
        s.SystemParameterValues = settings.ToDictionary(x => x.Key, x => x.Value.Value);
        ApplySystemSettingsToState(s, settings);
        s.CurrentClaimCalculationMethod = (CurrentClaimCalculationMethod)(int)Value(settings, "CURRENT_CLAIM_CALC_METHOD", 1);

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
        var pricesByMapping = new Dictionary<int, List<SupplierPriceListItem>>();
        if (s.CurrentClaimCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList)
        {
            var mappingIds = mappings.Select(x => x.Id).ToList();
            var prices = await db.SupplierPriceListItems.AsNoTracking()
                .Where(x => mappingIds.Contains(x.SupplierPartId))
                .OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id)
                .ToListAsync();
            pricesByMapping = prices.GroupBy(x => x.SupplierPartId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

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
            var debtAmount = x.DebtAmount;
            SupplierPriceListItem? price = null;
            if (s.CurrentClaimCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList)
            {
                if (!pricesByMapping.TryGetValue(mapping.Id, out var candidatePrices))
                    throw new InvalidOperationException($"برای کالا «{part.Title}» و تامین‌کننده «{supplier.Title}» هیچ فهرست بهای ثبت‌شده‌ای وجود ندارد.");
                price = candidatePrices
                    .Where(p => p.ValidFrom.Date <= x.ReceiptDate.Date && p.ValidTo.Date >= x.ReceiptDate.Date)
                    .OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id)
                    .FirstOrDefault();
                if (x.ReceiptQuantity <= 0)
                    throw new InvalidOperationException($"مقدار رسید «{x.ReceiptNo}» برای کالا «{part.Title}» باید بزرگ‌تر از صفر باشد.");
                if (price is null)
                    throw new InvalidOperationException($"برای کالا «{part.Title}» و تامین‌کننده «{supplier.Title}» در تاریخ رسید {PersianDateService.ToJalali(x.ReceiptDate)} قیمت معتبر در فهرست بها یافت نشد.");
                debtAmount = Math.Round(x.ReceiptQuantity * price.PurchasePrice, 2);
            }
            else if (debtAmount <= 0)
            {
                throw new InvalidOperationException($"مبلغ بدهی رسید «{x.ReceiptNo}» باید بزرگ‌تر از صفر باشد.");
            }
            var remaining = Math.Max(0, Math.Round(debtAmount - prior, 2));
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
                ReceiptQuantity = x.ReceiptQuantity,
                DebtCalculationMethod = s.CurrentClaimCalculationMethod,
                AppliedUnitPrice = price?.PurchasePrice ?? 0m,
                PriceListItemId = price?.Id,
                AppliedPriceValidFrom = price?.ValidFrom,
                AppliedPriceValidTo = price?.ValidTo,
                OriginalDebt = debtAmount,
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
        s.ImportedReceipts = imported.ToList();
        s.ImportedDebt = rows.Sum(x => x.OriginalDebt);
        s.RemainingDebt = rows.Sum(x => x.RemainingDebt);
        AllocateCalculatedBudget(s.TotalAllocationBudget, rows, s.CalculatedInitialSharePercent, s.CalculatedCurrentSharePercent, s.MinimumEffectiveDebtAge, s.MinimumAllocationAmount, s.AllocationRounding);
        s.Step = 2;
        return s;
    }

    public async Task RecalculateCurrentDebtsAsync(PaymentWizardState s)
    {
        if (s.Rows.Count == 0) return;

        var imported = Aggregate(s.ImportedReceipts ?? []).ToDictionary(
            x => KeyHash(x.ReceiptNo, x.Warehouse, x.PartTitle, x.SupplierTitle),
            x => x);
        if (imported.Count == 0)
            throw new InvalidOperationException("داده خام رسیدهای این محاسبه در نشست موجود نیست؛ اطلاعات رسیدها را مجدداً دریافت کنید.");

        var previous = await PreviousCurrentAsync();
        var mappingByPair = await db.SupplierParts.AsNoTracking()
            .Where(x => x.IsActive && x.Supplier!.IsActive && x.Part!.IsActive)
            .Select(x => new { x.Id, x.PartId, x.SupplierId })
            .ToListAsync();
        var mappingIdsByPair = mappingByPair.ToDictionary(x => (x.PartId, x.SupplierId), x => x.Id);
        Dictionary<int, List<SupplierPriceListItem>> pricesByMapping = [];
        if (s.CurrentClaimCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList)
        {
            var mappingIds = mappingIdsByPair.Values.Distinct().ToList();
            var prices = await db.SupplierPriceListItems.AsNoTracking()
                .Where(x => mappingIds.Contains(x.SupplierPartId))
                .OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id)
                .ToListAsync();
            pricesByMapping = prices.GroupBy(x => x.SupplierPartId).ToDictionary(g => g.Key, g => g.ToList());
        }

        foreach (var row in s.Rows)
        {
            var key = KeyHash(row.ReceiptNo, row.Warehouse, row.PartTitle, row.SupplierTitle);
            if (!imported.TryGetValue(key, out var source))
                throw new InvalidOperationException($"منبع رسید «{row.ReceiptNo}» در داده خام محاسبه پیدا نشد.");

            row.ReceiptQuantity = source.ReceiptQuantity;
            row.DebtCalculationMethod = s.CurrentClaimCalculationMethod;
            row.PreviousAllocated = previous.GetValueOrDefault(key);

            SupplierPriceListItem? price = null;
            if (s.CurrentClaimCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList)
            {
                if (!row.PartId.HasValue || !row.SupplierId.HasValue || !mappingIdsByPair.TryGetValue((row.PartId.Value, row.SupplierId.Value), out var mappingId))
                    throw new InvalidOperationException($"ارتباط کالا–تامین‌کننده برای رسید «{row.ReceiptNo}» یافت نشد.");
                if (!pricesByMapping.TryGetValue(mappingId, out var candidatePrices))
                    throw new InvalidOperationException($"برای کالا «{row.PartTitle}» و تامین‌کننده «{row.SupplierTitle}» فهرست بها تعریف نشده است.");
                price = candidatePrices
                    .Where(x => x.ValidFrom.Date <= row.ReceiptDate.Date && x.ValidTo.Date >= row.ReceiptDate.Date)
                    .OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id)
                    .FirstOrDefault();
                if (row.ReceiptQuantity <= 0)
                    throw new InvalidOperationException($"مقدار رسید «{row.ReceiptNo}» باید بزرگ‌تر از صفر باشد.");
                if (price is null)
                    throw new InvalidOperationException($"برای کالا «{row.PartTitle}» و تامین‌کننده «{row.SupplierTitle}» در تاریخ رسید {row.ReceiptDateJalali} قیمت معتبر در فهرست بها یافت نشد.");
                row.AppliedUnitPrice = price.PurchasePrice;
                row.PriceListItemId = price.Id;
                row.AppliedPriceValidFrom = price.ValidFrom;
                row.AppliedPriceValidTo = price.ValidTo;
                row.OriginalDebt = Math.Round(row.ReceiptQuantity * price.PurchasePrice, 2);
            }
            else
            {
                row.AppliedUnitPrice = 0;
                row.PriceListItemId = null;
                row.AppliedPriceValidFrom = null;
                row.AppliedPriceValidTo = null;
                row.OriginalDebt = Math.Round(source.DebtAmount, 2);
                if (row.OriginalDebt <= 0)
                    throw new InvalidOperationException($"مبلغ بدهی رسید «{row.ReceiptNo}» باید بزرگ‌تر از صفر باشد.");
            }

            row.RemainingDebt = Math.Max(0, Math.Round(row.OriginalDebt - row.PreviousAllocated, 2));
        }

        var currentBySupplier = s.Rows.Where(x => x.SupplierId.HasValue)
            .GroupBy(x => x.SupplierId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.RemainingDebt));
        var initialBySupplier = s.Rows.Where(x => x.SupplierId.HasValue)
            .GroupBy(x => x.SupplierId!.Value)
            .ToDictionary(g => g.Key, g => g.First().SupplierInitialClaimAmount);
        var totalOutstanding = currentBySupplier.Values.Sum() + initialBySupplier.Values.Sum();

        foreach (var row in s.Rows)
        {
            row.SupplierOutstandingDebt = currentBySupplier.GetValueOrDefault(row.SupplierId ?? 0) + initialBySupplier.GetValueOrDefault(row.SupplierId ?? 0);
            foreach (var score in row.Scores)
            {
                var parameter = s.Parameters.FirstOrDefault(x => x.Id == score.ParameterId);
                if (parameter?.ScoringMethod == ParameterScoringMethod.DebtAmount)
                {
                    score.Score = SupplierDebtAmountScore(row.SupplierOutstandingDebt, totalOutstanding);
                    score.Contribution = Math.Round(score.Score * score.Weight / 100m, 6);
                    score.Source = "محاسبه خودکار";
                }
            }
        }

        s.ImportedDebt = s.Rows.Sum(x => x.OriginalDebt);
        s.RemainingDebt = s.Rows.Sum(x => x.RemainingDebt);
        await RecalculateAllocationAsync(s);
    }

    public Task RecalculateAllocationAsync(PaymentWizardState s)
    {
        // محاسبه مجدد باید با همان پارامترهایی انجام شود که هنگام شروع
        // همین محاسبه در State/Snapshot ثبت شده‌اند، نه با مقادیر جدید اطلاعات پایه.
        ValidateShares(s.CalculatedInitialSharePercent, s.CalculatedCurrentSharePercent, "پرداخت محاسباتی");
        if (s.MinimumEffectiveDebtAge < 0 || s.MinimumAllocationAmount < 0)
            throw new InvalidOperationException("حد سن بدهی موثر و حداقل مبلغ تخصیص نمی‌توانند منفی باشند.");
        EnsureRounding(s.AllocationRounding);

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
        return Task.CompletedTask;
    }

    public async Task<Dictionary<string, decimal>> PreviousCurrentAsync()
    {
        return await db.PaymentRunInvoices
            .Where(x => !x.PaymentRun!.IsDeleted &&
                        (x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered ||
                         (x.PaymentRun.Status == PaymentRunStatus.Approved && x.PaymentRun.FinancialEffectsAppliedAt.HasValue)))
            .GroupBy(x => x.PaymentKeyHash)
            .Select(g => new
            {
                Key = g.Key,
                Amount = g.Sum(x => x.AllocatedCurrentAmount > 0 ? x.AllocatedCurrentAmount :
                    x.AllocatedInitialClaimAmount > 0 ? 0 : x.AllocatedAmount)
            })
            .ToDictionaryAsync(x => x.Key, x => x.Amount);
    }

    public async Task<List<PaymentCurrentReceiptSnapshot>> CurrentReceiptsAsync()
    {
        var invoices = await db.PaymentRunInvoices
            .Where(x => !x.PaymentRun!.IsDeleted &&
                        (x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered ||
                         (x.PaymentRun.Status == PaymentRunStatus.Approved && x.PaymentRun.FinancialEffectsAppliedAt.HasValue)))
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
        s.MinimumAllocationAmount = Value(values, "MIN_ALLOCATION_AMOUNT", 1);
        s.AllocationRounding = Value(values, "ALLOCATION_ROUNDING", 100000);
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
        minAge = Math.Max(0, minAge);
        minAmount = Math.Max(0, minAmount);

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

        var eligibleRows = rows.Where(x => x.DebtAgeDays > minAge && x.SupplierId.HasValue).ToList();
        var initialCapacity = supplierInitial
            .Where(x => eligibleRows.Any(r => r.SupplierId == x.Key))
            .Sum(x => x.Value);
        var currentCapacity = eligibleRows.Sum(x => Math.Max(0, x.RemainingDebt));
        var remainingBudget = FloorToMultiple(
            Math.Min(Math.Max(0, budget), currentCapacity + initialCapacity),
            rounding);

        while (remainingBudget >= rounding)
        {
            var active = eligibleRows
                .Where(x => AvailableCapacity(x, supplierInitial.GetValueOrDefault(x.SupplierId!.Value)) >= rounding)
                .ToList();
            if (active.Count == 0) break;

            var passBudget = remainingBudget;
            var weightSum = active.Sum(x => Math.Max(.000001m, x.WeightedScore));
            var provisional = active.ToDictionary(
                x => x,
                x => FloorToMultiple(passBudget * Math.Max(.000001m, x.WeightedScore) / weightSum, rounding));

            var eligible = active
                .Where(x => provisional[x] >= rounding && provisional[x] > minAmount)
                .ToList();

            if (eligible.Count == 0) break;

            var eligibleWeight = eligible.Sum(x => Math.Max(.000001m, x.WeightedScore));
            var assignedThisPass = 0m;

            foreach (var row in eligible.OrderByDescending(x => x.WeightedScore).ThenBy(x => x.ReceiptDate).ThenBy(x => x.ReceiptNo))
            {
                var requested = FloorToMultiple(passBudget * Math.Max(.000001m, row.WeightedScore) / eligibleWeight, rounding);
                var initialRemaining = supplierInitial.GetValueOrDefault(row.SupplierId!.Value);
                var currentRemaining = Math.Max(0, row.RemainingDebt - row.AllocatedCurrentAmount);
                var capacity = Math.Min(AvailableCapacity(row, initialRemaining), remainingBudget);
                var amount = FloorToMultiple(Math.Min(requested, capacity), rounding);
                if (amount < rounding || amount <= minAmount) continue;

                var split = SplitByShares(amount, initialRemaining, currentRemaining, initialSharePercent, currentSharePercent, rounding);
                if (split.Total <= 0 || split.Total <= minAmount) continue;

                row.AllocatedInitialClaimAmount += split.InitialClaim;
                row.AllocatedCurrentAmount += split.Current;
                row.AllocatedAmount = row.AllocatedInitialClaimAmount + row.AllocatedCurrentAmount;

                // مرحله دوم محاسباتی باید دقیقاً در همین فیلدها snapshot شود؛
                // در مرحله سوم فقط Allocated* تغییر می‌کند.
                row.CalculatedInitialClaimAllocatedAmount = row.AllocatedInitialClaimAmount;
                row.CalculatedCurrentAllocatedAmount = row.AllocatedCurrentAmount;
                row.CalculatedAllocatedAmount = row.AllocatedAmount;

                supplierInitial[row.SupplierId.Value] = Math.Max(0, initialRemaining - split.InitialClaim);
                remainingBudget -= split.Total;
                assignedThisPass += split.Total;

                if (remainingBudget < rounding) break;
            }

            if (assignedThisPass <= 0) break;
        }

        var totalAssigned = rows.Sum(x => x.CalculatedAllocatedAmount);
        if (totalAssigned > 0)
            foreach (var row in rows)
                row.AllocationRatio = Math.Round(row.CalculatedAllocatedAmount / totalAssigned, 8);
    }

    public static void DistributeSupplierAllocation(decimal target, List<PaymentCalculationRow> rows)
        => AllocateBudget(target, rows);

    public static void DistributeSupplierAllocation(decimal target, List<PaymentCalculationRow> rows, decimal initialSharePercent, decimal currentSharePercent, decimal rounding, decimal minAge = 0, decimal minAmount = 0)
    {
        ValidateShares(initialSharePercent, currentSharePercent, "پرداخت محاسباتی");
        EnsureRounding(rounding);
        target = FloorToMultiple(Math.Max(0, target), rounding);

        var stage2 = rows.ToDictionary(x => x, x => (
            x.CalculatedAllocatedAmount,
            x.CalculatedCurrentAllocatedAmount,
            x.CalculatedInitialClaimAllocatedAmount,
            x.WeightedScore,
            x.Warning));

        foreach (var row in rows)
        {
            row.WeightedScore = Math.Max(.000001m, stage2[row].CalculatedAllocatedAmount);
            row.AllocatedAmount = 0;
            row.AllocatedCurrentAmount = 0;
            row.AllocatedInitialClaimAmount = 0;
        }

        AllocateCalculatedBudget(target, rows, initialSharePercent, currentSharePercent, minAge, minAmount, rounding);

        var assigned = Math.Round(rows.Sum(x => x.AllocatedAmount), 2);
        foreach (var row in rows)
        {
            row.CalculatedAllocatedAmount = stage2[row].CalculatedAllocatedAmount;
            row.CalculatedCurrentAllocatedAmount = stage2[row].CalculatedCurrentAllocatedAmount;
            row.CalculatedInitialClaimAllocatedAmount = stage2[row].CalculatedInitialClaimAllocatedAmount;
            row.WeightedScore = stage2[row].WeightedScore;
            row.Warning = stage2[row].Warning;
        }

        if (target > 0 && assigned + .005m < target)
            throw new InvalidOperationException($"مبلغ نهایی تامین‌کننده با محدودیت سن بدهی، مانده مطالبات، حداقل مبلغ یا ضریب مبلغ محاسباتی به طور کامل قابل تسهیم نیست. مبلغ قابل تخصیص {assigned:N0} ریال است.");
    }

    public static ClaimPaymentSplit SplitByShares(decimal total, decimal initialAvailable, decimal currentAvailable, decimal initialSharePercent, decimal currentSharePercent, decimal multiple = 1m)
    {
        ValidateShares(initialSharePercent, currentSharePercent, "تخصیص");
        EnsureRounding(multiple);

        var totalUnits = (long)Math.Floor(Math.Max(0, total) / multiple);
        var initialUnitsAvailable = (long)Math.Floor(Math.Max(0, initialAvailable) / multiple);
        var currentUnitsAvailable = (long)Math.Floor(Math.Max(0, currentAvailable) / multiple);
        if (totalUnits <= 0) return new ClaimPaymentSplit(0, 0);

        var desiredInitialUnits = (long)Math.Round(totalUnits * initialSharePercent / 100m, MidpointRounding.AwayFromZero);
        var desiredCurrentUnits = totalUnits - desiredInitialUnits;

        var initialUnits = Math.Min(desiredInitialUnits, initialUnitsAvailable);
        var currentUnits = Math.Min(desiredCurrentUnits, currentUnitsAvailable);
        var remainder = totalUnits - initialUnits - currentUnits;

        if (remainder > 0)
        {
            var extraCurrent = Math.Min(remainder, Math.Max(0, currentUnitsAvailable - currentUnits));
            currentUnits += extraCurrent;
            remainder -= extraCurrent;
        }
        if (remainder > 0)
        {
            var extraInitial = Math.Min(remainder, Math.Max(0, initialUnitsAvailable - initialUnits));
            initialUnits += extraInitial;
        }

        return new ClaimPaymentSplit(
            Math.Round(initialUnits * multiple, 2),
            Math.Round(currentUnits * multiple, 2));
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
            .Select(g => new ImportedPaymentInvoice(g.Min(x => x.RowNumber), g.First().ReceiptNo, g.First().Warehouse, g.First().PartTitle, g.First().SupplierTitle, g.Sum(x => x.ReceiptQuantity), g.Sum(x => x.DebtAmount), g.Min(x => x.ReceiptDate)));

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
