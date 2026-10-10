using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

public sealed record SupplierPriceMutation(
    int SupplierPartId,
    int SupplierId,
    int PartId,
    decimal PurchasePrice,
    DateTime ValidFrom,
    DateTime ValidTo,
    long? ExistingPriceListItemId = null,
    bool SkipIfIdentical = false,
    int RowNumber = 0);

public sealed record SupplierPriceDebtAdjustmentPreviewRow(
    long InvoiceId,
    string ReceiptNo,
    string Warehouse,
    string PartTitle,
    string SupplierTitle,
    DateTime ReceiptDate,
    decimal ReceiptQuantity,
    decimal PreviousUnitPrice,
    decimal NewUnitPrice,
    decimal PreviousDebtAmount,
    decimal NewDebtAmount,
    decimal AmountChange,
    decimal CurrentBalanceBefore,
    decimal CurrentBalanceAfter);

public sealed record SupplierPriceDebtAdjustmentPreview(
    List<SupplierPriceDebtAdjustmentPreviewRow> Rows,
    decimal NetDebtChange)
{
    public int AffectedReceiptCount => Rows.Count;
}

public sealed record SupplierPriceAdjustmentApplyResult(
    string BatchKey,
    int AffectedReceiptCount,
    decimal NetDebtChange,
    bool AlreadyApplied);

public sealed class SupplierPriceDebtAdjustmentService(AppDbContext db)
{
    public async Task<SupplierPriceDebtAdjustmentPreview> PreviewAsync(IReadOnlyList<SupplierPriceMutation> mutations)
    {
        var context = await SimulateAsync(mutations);
        var invoices = await GetAffectedCurrentInvoicesAsync(context);
        if (invoices.Count == 0)
            return new SupplierPriceDebtAdjustmentPreview([], 0m);

        var invoiceIds = invoices.Select(x => x.Id).Distinct().ToList();
        var adjustments = invoiceIds.Count == 0 ? new List<SupplierPriceDebtAdjustment>() : await db.SupplierPriceDebtAdjustments.AsNoTracking()
            .Where(x => x.PaymentRunInvoiceId.HasValue && invoiceIds.Contains(x.PaymentRunInvoiceId.Value))
            .OrderBy(x => x.Id)
            .ToListAsync();
        var sumByInvoice = adjustments.GroupBy(x => x.PaymentRunInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountChange));
        var latestAdjustmentByInvoice = adjustments.GroupBy(x => x.PaymentRunInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Last());
        var rows = new List<SupplierPriceDebtAdjustmentPreviewRow>();

        foreach (var invoice in invoices)
        {
            var supplierPartId = context.MappingIdByPair[(invoice.PartId!.Value, invoice.SupplierId!.Value)];
            var price = FindPrice(context.SimulatedPrices.GetValueOrDefault(supplierPartId) ?? [], invoice.ReceiptDate);
            if (price is null)
                throw MissingPriceException(invoice.ReceiptNo, invoice.PartTitle, invoice.SupplierTitle, invoice.ReceiptDate);

            if (invoice.ReceiptQuantity <= 0)
                throw new InvalidOperationException($"مقدار رسید «{invoice.ReceiptNo}» برای اصلاح فی باید بزرگ‌تر از صفر باشد.");

            var previousDebt = Math.Round(invoice.OriginalDebt + sumByInvoice.GetValueOrDefault(invoice.Id), 2);
            var newDebt = Math.Round(invoice.ReceiptQuantity * price.PurchasePrice, 2);
            var delta = Math.Round(newDebt - previousDebt, 2);
            if (delta == 0m) continue;

            var previousUnitPrice = latestAdjustmentByInvoice.TryGetValue(invoice.Id, out var previousAdjustment)
                ? previousAdjustment.NewUnitPrice
                : invoice.AppliedUnitPrice;
            var currentAllocation = CurrentAllocation(invoice);
            var previousBalance = Math.Round(previousDebt - invoice.PreviousAllocated - currentAllocation, 2);
            var newBalance = Math.Round(newDebt - invoice.PreviousAllocated - currentAllocation, 2);

            rows.Add(new SupplierPriceDebtAdjustmentPreviewRow(
                invoice.Id,
                invoice.ReceiptNo,
                invoice.Warehouse,
                invoice.PartTitle,
                invoice.SupplierTitle,
                invoice.ReceiptDate,
                invoice.ReceiptQuantity,
                previousUnitPrice,
                price.PurchasePrice,
                previousDebt,
                newDebt,
                delta,
                previousBalance,
                newBalance));
        }

        return new SupplierPriceDebtAdjustmentPreview(
            rows.OrderBy(x => x.ReceiptDate).ThenBy(x => x.SupplierTitle).ThenBy(x => x.ReceiptNo).ToList(),
            Math.Round(rows.Sum(x => x.AmountChange), 2));
    }

    public async Task<SupplierPriceAdjustmentApplyResult> ApplyAsync(
        string batchKey,
        string changeType,
        string description,
        IReadOnlyList<SupplierPriceMutation> mutations,
        int userId,
        string userDisplayName)
    {
        if (!Guid.TryParseExact(batchKey, "N", out _))
            throw new InvalidOperationException("شناسه عملیات معتبر نیست.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var existingBatch = await db.SupplierPriceListChangeBatches.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BatchKey == batchKey);
            if (existingBatch is not null)
            {
                await tx.CommitAsync();
                return new SupplierPriceAdjustmentApplyResult(
                    existingBatch.BatchKey, existingBatch.AffectedReceiptCount, existingBatch.NetDebtChange, true);
            }

            var context = await SimulateAsync(mutations);
            var mappingIds = mutations.Select(x => x.SupplierPartId).Distinct().ToList();
            var trackedPrices = await db.SupplierPriceListItems
                .Where(x => mappingIds.Contains(x.SupplierPartId))
                .ToListAsync();

            var batch = new SupplierPriceListChangeBatch
            {
                BatchKey = batchKey,
                ChangeType = changeType,
                Description = description,
                SupplierId = mutations.Select(x => x.SupplierId).Distinct().Count() == 1 ? mutations[0].SupplierId : null,
                SupplierTitleSnapshot = mutations.Select(x => context.Mappings[x.SupplierPartId].Supplier!.Title).Distinct().Count() == 1
                    ? context.Mappings[mutations[0].SupplierPartId].Supplier!.Title : "چند تامین‌کننده",
                AppliedBy = userId,
                AppliedByNameSnapshot = userDisplayName,
                AppliedAt = DateTime.UtcNow
            };
            db.SupplierPriceListChangeBatches.Add(batch);

            foreach (var mutation in mutations)
            {
                ValidateMutation(mutation);
                if (mutation.ExistingPriceListItemId.HasValue)
                {
                    var row = trackedPrices.SingleOrDefault(x =>
                        x.Id == mutation.ExistingPriceListItemId.Value &&
                        x.SupplierPartId == mutation.SupplierPartId);
                    if (row is null || !row.IsActive)
                        throw new InvalidOperationException("رکورد فی انتخاب‌شده دیگر فعال نیست؛ فهرست بها را تازه‌سازی و دوباره اقدام کنید.");
                    if (await IsUsedAsync(row.Id))
                        throw new InvalidOperationException("این نرخ در سوابق محاسبه استفاده شده است؛ برای اصلاح گذشته، نرخ جایگزین ثبت کنید تا سوابق قبلی محفوظ بماند.");

                    var conflicts = trackedPrices.Where(x => x.SupplierPartId == mutation.SupplierPartId &&
                        x.IsActive && x.Id != row.Id &&
                        x.ValidFrom.Date == mutation.ValidFrom.Date &&
                        x.ValidTo.Date == mutation.ValidTo.Date).ToList();
                    if (conflicts.Any(x => x.PurchasePrice == Math.Round(mutation.PurchasePrice, 2)))
                        throw new InvalidOperationException("رکورد فعالی با همین کالا، تامین‌کننده، بازه و قیمت وجود دارد.");
                    foreach (var conflict in conflicts)
                    {
                        conflict.IsActive = false;
                        conflict.UpdatedAt = DateTime.UtcNow;
                    }

                    row.PurchasePrice = Math.Round(mutation.PurchasePrice, 2);
                    row.ValidFrom = mutation.ValidFrom.Date;
                    row.ValidTo = mutation.ValidTo.Date;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var exact = trackedPrices.Where(x => x.SupplierPartId == mutation.SupplierPartId &&
                        x.IsActive && x.ValidFrom.Date == mutation.ValidFrom.Date &&
                        x.ValidTo.Date == mutation.ValidTo.Date).ToList();
                    var samePrice = exact.FirstOrDefault(x => x.PurchasePrice == Math.Round(mutation.PurchasePrice, 2));
                    if (samePrice is not null && mutation.SkipIfIdentical)
                        continue;
                    if (samePrice is not null)
                        throw new InvalidOperationException("رکوردی با همین کالا، تامین‌کننده، بازه زمانی و قیمت قبلاً فعال است.");

                    foreach (var conflict in exact)
                    {
                        conflict.IsActive = false;
                        conflict.UpdatedAt = DateTime.UtcNow;
                    }

                    var added = new SupplierPriceListItem
                    {
                        SupplierPartId = mutation.SupplierPartId,
                        PurchasePrice = Math.Round(mutation.PurchasePrice, 2),
                        ValidFrom = mutation.ValidFrom.Date,
                        ValidTo = mutation.ValidTo.Date,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.SupplierPriceListItems.Add(added);
                    trackedPrices.Add(added);
                }
            }

            await db.SaveChangesAsync();

            // Re-evaluate the receipt balances against the final set of active prices.
            // This ledger corrects the live balance; approved payment snapshots are not rewritten.
            var invoices = await GetAffectedCurrentInvoicesAsync(context);
            var invoiceIds = invoices.Select(x => x.Id).Distinct().ToList();
            var oldAdjustments = invoiceIds.Count == 0
                ? new List<SupplierPriceDebtAdjustment>()
                : await db.SupplierPriceDebtAdjustments.AsNoTracking()
                    .Where(x => x.PaymentRunInvoiceId.HasValue && invoiceIds.Contains(x.PaymentRunInvoiceId.Value))
                    .OrderBy(x => x.Id)
                    .ToListAsync();
            var sumByInvoice = oldAdjustments.GroupBy(x => x.PaymentRunInvoiceId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountChange));
            var latestAdjustmentByInvoice = oldAdjustments.GroupBy(x => x.PaymentRunInvoiceId!.Value)
                .ToDictionary(g => g.Key, g => g.Last());

            var activePrices = await db.SupplierPriceListItems.AsNoTracking()
                .Where(x => mappingIds.Contains(x.SupplierPartId) && x.IsActive && x.PurchasePrice > 0)
                .OrderBy(x => x.ValidFrom).ThenBy(x => x.Id)
                .ToListAsync();
            var pricesByMapping = activePrices.GroupBy(x => x.SupplierPartId)
                .ToDictionary(g => g.Key, g => g.Select(x => new PriceCandidate(
                    x.Id, x.SupplierPartId, x.PurchasePrice, x.ValidFrom.Date, x.ValidTo.Date)).ToList());

            var savedAdjustments = new List<SupplierPriceDebtAdjustment>();
            foreach (var invoice in invoices)
            {
                var supplierPartId = context.MappingIdByPair[(invoice.PartId!.Value, invoice.SupplierId!.Value)];
                var price = FindPrice(pricesByMapping.GetValueOrDefault(supplierPartId) ?? [], invoice.ReceiptDate);
                if (price is null)
                    throw MissingPriceException(invoice.ReceiptNo, invoice.PartTitle, invoice.SupplierTitle, invoice.ReceiptDate);
                if (invoice.ReceiptQuantity <= 0)
                    throw new InvalidOperationException($"مقدار رسید «{invoice.ReceiptNo}» برای اصلاح فی باید بزرگ‌تر از صفر باشد.");

                var previousDebt = Math.Round(invoice.OriginalDebt + sumByInvoice.GetValueOrDefault(invoice.Id), 2);
                var newDebt = Math.Round(invoice.ReceiptQuantity * price.PurchasePrice, 2);
                var delta = Math.Round(newDebt - previousDebt, 2);
                if (delta == 0m) continue;

                var oldAdjustment = latestAdjustmentByInvoice.GetValueOrDefault(invoice.Id);
                savedAdjustments.Add(new SupplierPriceDebtAdjustment
                {
                    BatchKey = batchKey,
                    PaymentKeyHash = invoice.PaymentKeyHash,
                    PaymentRunInvoiceId = invoice.Id,
                    ReceiptNo = invoice.ReceiptNo,
                    Warehouse = invoice.Warehouse,
                    PartTitle = invoice.PartTitle,
                    SupplierTitle = invoice.SupplierTitle,
                    SupplierId = invoice.SupplierId!.Value,
                    PartId = invoice.PartId!.Value,
                    ReceiptDate = invoice.ReceiptDate.Date,
                    ReceiptQuantity = invoice.ReceiptQuantity,
                    PreviousPriceListItemId = oldAdjustment?.NewPriceListItemId ?? invoice.PriceListItemId,
                    NewPriceListItemId = price.Id,
                    PreviousUnitPrice = oldAdjustment?.NewUnitPrice ?? invoice.AppliedUnitPrice,
                    NewUnitPrice = price.PurchasePrice,
                    PreviousDebtAmount = previousDebt,
                    NewDebtAmount = newDebt,
                    AmountChange = delta,
                    AppliedBy = userId,
                    AppliedAt = DateTime.UtcNow,
                    Reason = description
                });
            }

            db.SupplierPriceDebtAdjustments.AddRange(savedAdjustments);
            batch.AffectedReceiptCount = savedAdjustments.Count;
            batch.NetDebtChange = Math.Round(savedAdjustments.Sum(x => x.AmountChange), 2);
            db.AuditLogs.Add(new AuditLog
            {
                Action = "PRICE_DEBT_ADJUSTMENT",
                Entity = "SupplierPriceListChangeBatch",
                EntityId = batchKey,
                Details = $"{description}؛ تعداد رسید تعدیل‌شده: {batch.AffectedReceiptCount:N0}؛ خالص تغییر بدهی: {batch.NetDebtChange:N0} ریال",
                UserId = userId
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return new SupplierPriceAdjustmentApplyResult(
                batchKey, batch.AffectedReceiptCount, batch.NetDebtChange, false);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> HasFinancialReceiptsInRangeAsync(int supplierPartId, DateTime from, DateTime to)
    {
        var mapping = await db.SupplierParts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == supplierPartId);
        if (mapping is null) return false;
        var rows = await db.PaymentRunInvoices.AsNoTracking()
            .Include(x => x.PaymentRun)
            .Where(x => x.SupplierId == mapping.SupplierId && x.PartId == mapping.PartId &&
                x.ReceiptDate >= from.Date && x.ReceiptDate <= to.Date &&
                x.PaymentRun != null && !x.PaymentRun.IsDeleted &&
                (x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered ||
                 (x.PaymentRun.Status == PaymentRunStatus.Approved && x.PaymentRun.FinancialEffectsAppliedAt.HasValue)))
            .Select(x => new { x.PaymentKeyHash, x.PaymentRunId, x.Id, x.DebtCalculationMethod })
            .ToListAsync();
        var latest = rows.GroupBy(x => x.PaymentKeyHash)
            .Select(g => g.OrderBy(x => x.PaymentRunId).ThenBy(x => x.Id).Last());
        return latest.Any(x => x.DebtCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList);
    }

    public async Task ValidateCurrentPriceSnapshotAsync(
        int? partId, int? supplierId, DateTime receiptDate, long? expectedPriceId,
        decimal expectedUnitPrice, string receiptNo)
    {
        if (!partId.HasValue || !supplierId.HasValue || !expectedPriceId.HasValue)
            throw new InvalidOperationException($"اطلاعات Snapshot فی رسید «{receiptNo}» ناقص است؛ محاسبه را مجدداً انجام دهید.");

        var mappingId = await db.SupplierParts.AsNoTracking()
            .Where(x => x.PartId == partId.Value && x.SupplierId == supplierId.Value && x.IsActive)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync();
        if (!mappingId.HasValue)
            throw new InvalidOperationException($"ارتباط فعال کالا–تامین‌کننده برای رسید «{receiptNo}» یافت نشد؛ محاسبه را مجدداً انجام دهید.");

        var current = await db.SupplierPriceListItems.AsNoTracking()
            .Where(x => x.SupplierPartId == mappingId.Value && x.IsActive &&
                        x.PurchasePrice > 0 && x.ValidFrom <= receiptDate.Date && x.ValidTo >= receiptDate.Date)
            .OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (current is null || current.Id != expectedPriceId.Value ||
            Math.Abs(current.PurchasePrice - expectedUnitPrice) > .005m)
            throw new InvalidOperationException($"فی معتبر رسید «{receiptNo}» پس از محاسبه تغییر کرده است؛ برای جلوگیری از دستور پرداخت با مبلغ قدیمی، محاسبه را از ابتدا انجام دهید.");
    }

    public static decimal CalculateAdjustmentDelta(decimal snapshotDebt, decimal existingAdjustments, decimal quantity, decimal targetUnitPrice)
    {
        var currentEffectiveDebt = Math.Round(snapshotDebt + existingAdjustments, 2);
        var targetDebt = Math.Round(quantity * targetUnitPrice, 2);
        return Math.Round(targetDebt - currentEffectiveDebt, 2);
    }

    public static decimal CalculateOutstandingBalance(decimal snapshotDebt, decimal adjustments, decimal previousAllocations, decimal currentAllocation) =>
        Math.Round(snapshotDebt + adjustments - previousAllocations - currentAllocation, 2);

    private async Task<SimulationContext> SimulateAsync(IReadOnlyList<SupplierPriceMutation> mutations)
    {
        if (mutations is null || mutations.Count == 0)
            throw new InvalidOperationException("هیچ ردیف فهرست بهایی برای اعمال انتخاب نشده است.");
        if (mutations.Count > 5000)
            throw new InvalidOperationException("در هر عملیات حداکثر ۵۰۰۰ ردیف فهرست بها قابل پردازش است.");
        if (mutations.GroupBy(x => (x.SupplierPartId, x.ValidFrom.Date, x.ValidTo.Date)).Any(x => x.Count() > 1))
            throw new InvalidOperationException("در یک عملیات، برای یک کالا–تامین‌کننده و بازه دقیقاً تکراری بیش از یک قیمت ارسال شده است.");

        foreach (var mutation in mutations) ValidateMutation(mutation);

        var mappingIds = mutations.Select(x => x.SupplierPartId).Distinct().ToList();
        var mappings = await db.SupplierParts.AsNoTracking()
            .Include(x => x.Supplier).Include(x => x.Part)
            .Where(x => mappingIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var mutation in mutations)
        {
            if (!mappings.TryGetValue(mutation.SupplierPartId, out var mapping) ||
                mapping.SupplierId != mutation.SupplierId || mapping.PartId != mutation.PartId ||
                !mapping.IsActive || mapping.Supplier?.IsActive != true || mapping.Part?.IsActive != true)
                throw new InvalidOperationException("ارتباط فعال کالا–تامین‌کننده با اطلاعات ارسال‌شده مطابقت ندارد؛ صفحه را تازه‌سازی کنید.");
        }

        var allPrices = await db.SupplierPriceListItems.AsNoTracking()
            .Where(x => mappingIds.Contains(x.SupplierPartId))
            .ToListAsync();
        var activeCandidates = allPrices.Where(x => x.IsActive)
            .Select(x => new PriceCandidate(x.Id, x.SupplierPartId, x.PurchasePrice, x.ValidFrom.Date, x.ValidTo.Date))
            .ToList();
        var scopes = new Dictionary<int, List<DateRange>>();
        foreach (var id in mappingIds) scopes[id] = [];
        long virtualId = long.MaxValue - mutations.Count - 5;

        foreach (var mutation in mutations)
        {
            var newFrom = mutation.ValidFrom.Date;
            var newTo = mutation.ValidTo.Date;
            scopes[mutation.SupplierPartId].Add(new DateRange(newFrom, newTo));

            if (mutation.ExistingPriceListItemId.HasValue)
            {
                var old = allPrices.SingleOrDefault(x =>
                    x.Id == mutation.ExistingPriceListItemId.Value && x.SupplierPartId == mutation.SupplierPartId);
                if (old is null || !old.IsActive)
                    throw new InvalidOperationException("رکورد فی انتخاب‌شده دیگر فعال نیست؛ فهرست بها را تازه‌سازی کنید.");
                if (await IsUsedAsync(old.Id))
                    throw new InvalidOperationException("این نرخ در سوابق محاسبه استفاده شده است؛ برای اصلاح گذشته، نرخ جایگزین ثبت کنید.");
                scopes[mutation.SupplierPartId].Add(new DateRange(old.ValidFrom.Date, old.ValidTo.Date));

                var conflicts = activeCandidates.Where(x => x.SupplierPartId == mutation.SupplierPartId &&
                    x.Id != old.Id && x.ValidFrom == newFrom && x.ValidTo == newTo).ToList();
                if (conflicts.Any(x => x.PurchasePrice == Math.Round(mutation.PurchasePrice, 2)))
                    throw new InvalidOperationException("رکورد فعالی با همین کالا، تامین‌کننده، بازه و قیمت وجود دارد.");
                activeCandidates.RemoveAll(x => conflicts.Any(y => y.Id == x.Id));
                activeCandidates.RemoveAll(x => x.Id == old.Id);
                activeCandidates.Add(new PriceCandidate(old.Id, mutation.SupplierPartId, Math.Round(mutation.PurchasePrice, 2), newFrom, newTo));
            }
            else
            {
                var exact = activeCandidates.Where(x => x.SupplierPartId == mutation.SupplierPartId &&
                    x.ValidFrom == newFrom && x.ValidTo == newTo).ToList();
                var same = exact.FirstOrDefault(x => x.PurchasePrice == Math.Round(mutation.PurchasePrice, 2));
                if (same is not null && mutation.SkipIfIdentical) continue;
                if (same is not null)
                    throw new InvalidOperationException("رکوردی با همین کالا، تامین‌کننده، بازه زمانی و قیمت قبلاً فعال است.");
                activeCandidates.RemoveAll(x => exact.Any(y => y.Id == x.Id));
                activeCandidates.Add(new PriceCandidate(virtualId++, mutation.SupplierPartId, Math.Round(mutation.PurchasePrice, 2), newFrom, newTo));
            }
        }

        return new SimulationContext(
            mappings,
            mappings.Values.ToDictionary(x => (x.PartId, x.SupplierId), x => x.Id),
            scopes,
            activeCandidates.GroupBy(x => x.SupplierPartId).ToDictionary(g => g.Key, g => g.ToList()));
    }

    private async Task<List<PaymentRunInvoice>> GetAffectedCurrentInvoicesAsync(SimulationContext context)
    {
        var mappingIds = context.Scopes.Keys.ToHashSet();
        var mappings = context.Mappings.Values.Where(x => mappingIds.Contains(x.Id)).ToList();
        if (mappings.Count == 0) return [];
        var supplierIds = mappings.Select(x => x.SupplierId).Distinct().ToList();
        var partIds = mappings.Select(x => x.PartId).Distinct().ToList();
        var ranges = context.Scopes.Values.SelectMany(x => x).ToList();
        var minDate = ranges.Min(x => x.From);
        var maxDate = ranges.Max(x => x.To);

        var candidates = await db.PaymentRunInvoices.AsNoTracking()
            .Include(x => x.PaymentRun)
            .Where(x => x.PartId.HasValue && x.SupplierId.HasValue &&
                supplierIds.Contains(x.SupplierId.Value) && partIds.Contains(x.PartId.Value) &&
                x.ReceiptDate >= minDate && x.ReceiptDate <= maxDate &&
                x.PaymentRun != null && !x.PaymentRun.IsDeleted &&
                (x.PaymentRun.Status == PaymentRunStatus.PaymentOrdered ||
                 (x.PaymentRun.Status == PaymentRunStatus.Approved && x.PaymentRun.FinancialEffectsAppliedAt.HasValue)))
            .OrderBy(x => x.PaymentRunId).ThenBy(x => x.Id)
            .ToListAsync();

        var mappingIdByPair = context.Mappings.Values.ToDictionary(x => (x.PartId, x.SupplierId), x => x.Id);
        var latest = candidates.GroupBy(x => x.PaymentKeyHash).Select(g => g.Last()).ToList();
        return latest.Where(x =>
        {
            if (x.DebtCalculationMethod != CurrentClaimCalculationMethod.QuantityBasedPriceList ||
                !x.PartId.HasValue || !x.SupplierId.HasValue ||
                !mappingIdByPair.TryGetValue((x.PartId.Value, x.SupplierId.Value), out var mapId) ||
                !context.Scopes.TryGetValue(mapId, out var receiptRanges))
                return false;
            return receiptRanges.Any(r => x.ReceiptDate.Date >= r.From && x.ReceiptDate.Date <= r.To);
        }).ToList();
    }

    private static PriceCandidate? FindPrice(List<PriceCandidate> prices, DateTime receiptDate) =>
        prices.Where(x => x.ValidFrom <= receiptDate.Date && x.ValidTo >= receiptDate.Date)
            .OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id).FirstOrDefault();

    private static decimal CurrentAllocation(PaymentRunInvoice invoice) =>
        invoice.AllocatedCurrentAmount > 0
            ? invoice.AllocatedCurrentAmount
            : invoice.AllocatedInitialClaimAmount > 0
                ? 0m
                : invoice.AllocatedAmount;

    private async Task<bool> IsUsedAsync(long priceId) =>
        await db.PaymentRunInvoices.AsNoTracking().AnyAsync(x => x.PriceListItemId == priceId);

    private static void ValidateMutation(SupplierPriceMutation mutation)
    {
        if (mutation.SupplierPartId <= 0 || mutation.SupplierId <= 0 || mutation.PartId <= 0)
            throw new InvalidOperationException("ارتباط کالا–تامین‌کننده نامعتبر است.");
        if (mutation.PurchasePrice <= 0)
            throw new InvalidOperationException("قیمت خرید (فی) باید بزرگ‌تر از صفر باشد.");
        if (mutation.ValidTo.Date < mutation.ValidFrom.Date)
            throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.");
    }

    private static InvalidOperationException MissingPriceException(string receipt, string part, string supplier, DateTime date) =>
        new($"پس از تغییر فهرست بها، برای رسید «{receipt}» کالا «{part}» و تامین‌کننده «{supplier}» در تاریخ {PersianDateService.ToJalali(date)} فی معتبر وجود ندارد؛ ابتدا پوشش قیمت آن بازه را تکمیل کنید.");

    private sealed record PriceCandidate(long Id, int SupplierPartId, decimal PurchasePrice, DateTime ValidFrom, DateTime ValidTo);
    private sealed record DateRange(DateTime From, DateTime To);
    private sealed record SimulationContext(
        Dictionary<int, SupplierPart> Mappings,
        Dictionary<(int PartId, int SupplierId), int> MappingIdByPair,
        Dictionary<int, List<DateRange>> Scopes,
        Dictionary<int, List<PriceCandidate>> SimulatedPrices);
}
