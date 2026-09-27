using System.Data;
using System.Text.Json;
using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.PaymentNonCalculated)]
public class NonCalculatedPaymentController(AppDbContext db, SupplierPriorityService priority, PaymentCalculationService calc) : Controller
{
    const string SessionKey = "NonCalculatedPaymentWizardState";
    int UserId => int.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : 0;
    string UserDisplayName => User.Identity?.Name ?? "کاربر";

    public IActionResult Index() => RedirectToAction(nameof(Step1));

    [HttpGet]
    public async Task<IActionResult> Step1()
    {
        var existing = Load();
        existing ??= new NonCalculatedPaymentWizardState
        {
            PaymentDateJalali = PersianDateService.ToJalali(DateTime.Today),
            Title = "پرداخت غیرمحاسباتی"
        };
        existing.AvailableSuppliers = await priority.GetAsync();
        return View(existing);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Step1(string title, string paymentDateJalali, string? notes, int[] supplierIds)
    {
        try
        {
            var date = PersianDateService.Parse(paymentDateJalali);
            var ids = supplierIds ?? [];
            if (ids.Length == 0)
                throw new InvalidOperationException("حداقل یک ردیف تامین‌کننده را انتخاب کنید.");

            var settings = await calc.LoadSystemParametersAsync();
            var initialShare = settings.GetValueOrDefault("NONCALC_INITIAL_SHARE")?.Value ?? 50m;
            var currentShare = settings.GetValueOrDefault("NONCALC_CURRENT_SHARE")?.Value ?? 50m;
            PaymentCalculationService.ValidateShares(initialShare, currentShare, "پرداخت غیرمحاسباتی");

            var all = await priority.GetAsync();
            var catalog = all.ToDictionary(x => x.SupplierId);
            if (ids.Any(id => !catalog.ContainsKey(id)))
                throw new InvalidOperationException("یکی از تامین‌کنندگان انتخاب‌شده دیگر فعال یا دارای مطالبات قابل پرداخت نیست.");

            var state = new NonCalculatedPaymentWizardState
            {
                Step = 2,
                Title = string.IsNullOrWhiteSpace(title) ? "پرداخت غیرمحاسباتی" : title.Trim(),
                PaymentDateJalali = PersianDateService.ToJalali(date),
                Notes = notes?.Trim(),
                NonCalculatedInitialSharePercent = initialShare,
                NonCalculatedCurrentSharePercent = currentShare,
                SystemParameterValues = settings.ToDictionary(x => x.Key, x => x.Value.Value),
                AvailableSuppliers = all,
                Suppliers = ids.Select(id =>
                {
                    var x = catalog[id];
                    return new NonCalculatedSupplierRow
                    {
                        SupplierId = x.SupplierId,
                        SupplierTitle = x.SupplierTitle,
                        InitialClaimAmount = x.InitialClaimAmount,
                        CurrentDebt = x.CurrentDebt,
                        PaymentType = NonCalculatedPaymentType.Cash,
                        Amount = 0,
                        Description = ""
                    };
                }).ToList()
            };

            Save(state);
            return RedirectToAction(nameof(Step2));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            var s = new NonCalculatedPaymentWizardState
            {
                Step = 1,
                Title = string.IsNullOrWhiteSpace(title) ? "پرداخت غیرمحاسباتی" : title.Trim(),
                PaymentDateJalali = paymentDateJalali,
                Notes = notes?.Trim(),
                AvailableSuppliers = await priority.GetAsync()
            };
            return View(s);
        }
    }

    [HttpGet]
    public IActionResult Step2()
    {
        var s = Load();
        if (s == null || s.Suppliers.Count == 0)
            return RedirectToAction(nameof(Step1));
        return View(s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(List<NonCalculatedSupplierEditItem> items)
    {
        var s = Load();
        if (s == null || s.Suppliers.Count == 0)
            return RedirectToAction(nameof(Step1));

        try
        {
            ValidateWizardItems(s, items);
            for (var i = 0; i < s.Suppliers.Count; i++)
            {
                s.Suppliers[i].PaymentType = items[i].PaymentType;
                s.Suppliers[i].Amount = Math.Round(items[i].Amount, 2);
                s.Suppliers[i].Description = (items[i].Description ?? "").Trim();
            }

            await PreparePreviewAsync(s);
            s.Step = 3;
            Save(s);
            return RedirectToAction(nameof(Step3));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            Save(s);
            return RedirectToAction(nameof(Step2));
        }
    }

    [HttpGet]
    public IActionResult Step3()
    {
        var s = Load();
        if (s == null || s.Suppliers.Count == 0)
            return RedirectToAction(nameof(Step1));
        return View(s);
    }

    [Authorize(Policy = SecurityPermissions.PaymentOrderCreate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize()
    {
        var s = Load();
        if (s == null || s.Suppliers.Count == 0)
            return RedirectToAction(nameof(Step1));

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            PaymentCalculationService.ValidateShares(
                s.NonCalculatedInitialSharePercent,
                s.NonCalculatedCurrentSharePercent,
                "پرداخت غیرمحاسباتی");

            ValidateWizardItems(s, s.Suppliers.Select(x => new NonCalculatedSupplierEditItem
            {
                SupplierId = x.SupplierId,
                PaymentType = x.PaymentType,
                Amount = x.Amount,
                Description = x.Description
            }).ToList());

            var supplierIds = s.Suppliers.Select(x => x.SupplierId).Distinct().ToList();
            var suppliers = await db.Suppliers
                .Where(x => supplierIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            var receipts = await calc.CurrentReceiptsAsync();
            foreach (var group in s.Suppliers.GroupBy(x => x.SupplierId))
            {
                if (!suppliers.TryGetValue(group.Key, out var supplier) || !supplier.IsActive)
                    throw new InvalidOperationException($"تامین‌کننده با شناسه {group.Key} دیگر فعال نیست.");

                var snapshotInitial = group.First().InitialClaimAtPreview;
                var liveCurrent = receipts.Where(x => x.SupplierId == group.Key).Sum(x => x.RemainingDebt);
                if (Math.Abs(supplier.InitialClaimAmount - snapshotInitial) > .005m ||
                    Math.Abs(liveCurrent - group.First().CurrentDebtAtPreview) > .005m)
                    throw new InvalidOperationException($"مانده مطالبات تامین‌کننده «{supplier.Title}» از زمان پیش‌نمایش تغییر کرده است؛ پیش‌نمایش را مجدداً ایجاد کنید.");

                var requested = Math.Round(group.Sum(x => x.Amount), 2);
                var capacity = Math.Round(supplier.InitialClaimAmount + liveCurrent, 2);
                if (requested > capacity + .005m)
                    throw new InvalidOperationException($"مجموع مبالغ درخواست‌شده برای «{supplier.Title}» از ظرفیت مطالبات قابل پرداخت بیشتر است.");

                var split = PaymentCalculationService.SplitByShares(
                    requested,
                    supplier.InitialClaimAmount,
                    liveCurrent,
                    s.NonCalculatedInitialSharePercent,
                    s.NonCalculatedCurrentSharePercent,
                    .01m);

                if (Math.Abs(split.InitialClaim - group.Sum(x => x.CalculatedInitialClaimAmount)) > .005m ||
                    Math.Abs(split.Current - group.Sum(x => x.CalculatedCurrentClaimAmount)) > .005m)
                    throw new InvalidOperationException($"تفکیک سهم مطالبات تامین‌کننده «{supplier.Title}» دیگر با پیش‌نمایش یکسان نیست؛ پیش‌نمایش را مجدداً ایجاد کنید.");
            }

            var runDate = PersianDateService.Parse(s.PaymentDateJalali);
            var now = DateTime.UtcNow;
            var user = await db.Users.FindAsync(UserId);
            var totalRequested = Math.Round(s.Suppliers.Sum(x => x.Amount), 2);
            var totalCapacity = Math.Round(
                s.Suppliers.GroupBy(x => x.SupplierId)
                    .Sum(g => suppliers[g.Key].InitialClaimAmount +
                              receipts.Where(x => x.SupplierId == g.Key).Sum(x => x.RemainingDebt)),
                2);

            var run = new PaymentRun
            {
                RunType = PaymentRunType.NonCalculated,
                Title = string.IsNullOrWhiteSpace(s.Title) ? "پرداخت غیرمحاسباتی" : s.Title.Trim(),
                CalculationDateJalali = s.PaymentDateJalali,
                CalculationDate = runDate,
                PeriodFromJalali = s.PaymentDateJalali,
                PeriodToJalali = s.PaymentDateJalali,
                PeriodFrom = runDate,
                PeriodTo = runDate,
                TotalAllocationBudget = totalRequested,
                AmountUnit = "ریال",
                ImportedDebt = totalCapacity,
                RemainingDebt = totalCapacity,
                ImportedRowCount = s.Suppliers.Count,
                Notes = s.Notes,
                Status = PaymentRunStatus.PaymentOrdered,
                FinancialEffectsAppliedAt = now,
                CreatedBy = UserId,
                CreatedAt = now,
                ApprovedBy = UserId,
                ApprovedAt = now,
                PaymentOrderNumber = "",
                PaymentOrderedAt = now,
                PaymentOrderedBy = UserId,
                PaymentOrderApproverNameSnapshot = user?.DisplayName ?? UserDisplayName,
                PreparedByNameSnapshot = user?.DisplayName ?? UserDisplayName,
                ConfirmedByNameSnapshot = user?.DisplayName ?? UserDisplayName
            };

            db.PaymentRuns.Add(run);
            await db.SaveChangesAsync();
            run.PaymentOrderNumber = $"DP-{run.Id:000000}";

            var systemValues = await db.SystemParameters.Where(x => x.IsActive).AsNoTracking().ToListAsync();
            db.PaymentRunSystemParameterSnapshots.AddRange(systemValues.Select(x => new PaymentRunSystemParameterSnapshot
            {
                PaymentRunId = run.Id,
                Code = x.Code,
                Title = x.Title,
                ValueType = x.ValueType,
                Value = s.SystemParameterValues.TryGetValue(x.Code, out var value) ? value : x.Value,
                Unit = x.Unit
            }));

            var totalAssigned = totalRequested;

            foreach (var group in s.Suppliers.GroupBy(x => x.SupplierId))
            {
                var supplier = suppliers[group.Key];
                var initialBefore = supplier.InitialClaimAmount;
                var currentBefore = receipts.Where(x => x.SupplierId == group.Key).Sum(x => x.RemainingDebt);
                var initialAllocated = Math.Round(group.Sum(x => x.CalculatedInitialClaimAmount), 2);
                var currentAllocated = Math.Round(group.Sum(x => x.CalculatedCurrentClaimAmount), 2);
                var totalAllocated = Math.Round(initialAllocated + currentAllocated, 2);
                var lineTypes = group.Select(x => x.PaymentType).Distinct().ToList();

                var affectedReceiptList = receipts
                    .Where(x => x.SupplierId == group.Key)
                    .OrderBy(x => x.ReceiptDate)
                    .ThenBy(x => x.ReceiptNo)
                    .ToList();

                var currentLeft = currentAllocated;
                var touched = new List<(PaymentCurrentReceiptSnapshot Receipt, decimal Amount)>();
                foreach (var receipt in affectedReceiptList)
                {
                    if (currentLeft <= .005m) break;
                    var take = Math.Round(Math.Min(currentLeft, receipt.RemainingDebt), 2);
                    if (take <= 0) continue;
                    touched.Add((receipt, take));
                    currentLeft = Math.Round(currentLeft - take, 2);
                }
                if (currentLeft > .005m)
                    throw new InvalidOperationException($"مانده جاری تامین‌کننده «{supplier.Title}» برای ثبت پرداخت کافی نیست.");

                db.PaymentRunSupplierSummaries.Add(new PaymentRunSupplierSummary
                {
                    PaymentRunId = run.Id,
                    SupplierId = supplier.Id,
                    SupplierTitle = supplier.Title,
                    InvoiceCount = touched.Count,
                    RemainingDebt = initialBefore + currentBefore,
                    AllocatedAmount = totalAllocated,
                    InitialClaimAllocatedAmount = initialAllocated,
                    CurrentClaimAllocatedAmount = currentAllocated,
                    InitialClaimBefore = initialBefore,
                    InitialClaimAfter = initialBefore - initialAllocated,
                    PaymentType = lineTypes.Count == 1 ? lineTypes[0] : null,
                    RequestedAmount = group.Sum(x => x.Amount),
                    AllocationPercent = totalAssigned > 0 ? Math.Round(totalAllocated / totalAssigned, 8) : 0
                });

                if (initialAllocated > 0)
                {
                    supplier.InitialClaimAmount = Math.Round(initialBefore - initialAllocated, 2);
                    db.SupplierClaimHistories.Add(new SupplierClaimHistory
                    {
                        SupplierId = supplier.Id,
                        ClaimType = SupplierClaimType.Initial,
                        AmountBefore = initialBefore,
                        AmountChange = -initialAllocated,
                        AmountAfter = supplier.InitialClaimAmount,
                        PaymentRunId = run.Id,
                        Reference = $"کاهش مطالبات استقراری بابت دستور پرداخت غیرمحاسباتی شماره {run.Id}",
                        EffectiveDateJalali = s.PaymentDateJalali,
                        UserId = UserId
                    });
                }

                foreach (var receipt in touched)
                {
                    var prior = Math.Max(0, Math.Round(receipt.OriginalDebt - receipt.Receipt.RemainingDebt, 2));
                    var initialForRow = receipt.Equals(touched.First()) ? initialAllocated : 0;
                    db.PaymentRunInvoices.Add(new PaymentRunInvoice
                    {
                        PaymentRunId = run.Id,
                        PaymentKeyHash = receipt.Receipt.PaymentKeyHash,
                        ReceiptNo = receipt.Receipt.ReceiptNo,
                        Warehouse = receipt.Receipt.Warehouse,
                        PartTitle = receipt.Receipt.PartTitle,
                        SupplierTitle = receipt.Receipt.SupplierTitle,
                        PartId = receipt.Receipt.PartId,
                        SupplierId = receipt.Receipt.SupplierId,
                        OriginalDebt = receipt.Receipt.OriginalDebt,
                        ReceiptDate = receipt.Receipt.ReceiptDate,
                        ReceiptDateJalali = PersianDateService.ToJalali(receipt.Receipt.ReceiptDate),
                        ContractSettlementDays = receipt.Receipt.ContractSettlementDays,
                        SupplierInitialClaimAmount = initialBefore,
                        DebtAgeDays = 0,
                        PreviousAllocated = prior,
                        RemainingDebt = receipt.Receipt.RemainingDebt,
                        SupplierOutstandingDebt = initialBefore + currentBefore,
                        WeightedScore = 0,
                        AllocationRatio = 0,
                        CalculatedAllocatedAmount = 0,
                        CalculatedCurrentAllocatedAmount = 0,
                        CalculatedInitialClaimAllocatedAmount = 0,
                        AllocatedCurrentAmount = receipt.Amount,
                        AllocatedInitialClaimAmount = initialForRow,
                        AllocatedAmount = receipt.Amount + initialForRow,
                        SourceRowNumber = 0
                    });
                }

                if (touched.Count == 0 && initialAllocated <= 0 && totalAllocated > .005m)
                    throw new InvalidOperationException($"پرداخت تامین‌کننده «{supplier.Title}» قابل ثبت نیست.");

                var lineNumber = 1;
                foreach (var line in group)
                {
                    db.NonCalculatedPaymentLines.Add(new NonCalculatedPaymentLineSnapshot
                    {
                        PaymentRunId = run.Id,
                        LineNumber = lineNumber++,
                        SupplierId = supplier.Id,
                        SupplierTitleSnapshot = line.SupplierTitle,
                        PaymentType = line.PaymentType,
                        RequestedAmount = line.Amount,
                        Description = line.Description ?? "",
                        SupplierInitialClaimBefore = initialBefore,
                        SupplierCurrentDebtBefore = currentBefore,
                        SupplierTotalDebtBefore = initialBefore + currentBefore,
                        InitialClaimAllocatedAmount = line.CalculatedInitialClaimAmount,
                        CurrentClaimAllocatedAmount = line.CalculatedCurrentClaimAmount,
                        AllocatedAmount = line.CalculatedAllocatedAmount,
                        PaymentDateJalali = s.PaymentDateJalali,
                        PaymentDate = runDate
                    });
                }
            }

            await db.SaveChangesAsync();

            db.AuditLogs.Add(new AuditLog
            {
                Action = "PAYMENT_ORDER",
                Entity = "PaymentRun",
                EntityId = run.Id.ToString(),
                Details = $"پرداخت غیرمحاسباتی و دستور پرداخت {run.PaymentOrderNumber} ثبت شد؛ استقراری: {run.SupplierSummaries.Sum(x => x.InitialClaimAllocatedAmount):N0}؛ جاری: {run.SupplierSummaries.Sum(x => x.CurrentClaimAllocatedAmount):N0}",
                UserId = UserId
            });
            await db.SaveChangesAsync();

            await tx.CommitAsync();
            HttpContext.Session.Remove(SessionKey);
            TempData["Result"] = $"پرداخت غیرمحاسباتی شماره {run.Id} با دستور پرداخت {run.PaymentOrderNumber} ثبت شد و آثار مالی آن در محاسبات بعدی لحاظ خواهد شد.";
            return RedirectToAction("History", "PaymentWizard");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Step3));
        }
    }

    async Task PreparePreviewAsync(NonCalculatedPaymentWizardState s)
    {
        PaymentCalculationService.ValidateShares(
            s.NonCalculatedInitialSharePercent,
            s.NonCalculatedCurrentSharePercent,
            "پرداخت غیرمحاسباتی");

        var supplierIds = s.Suppliers.Select(x => x.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers
            .Where(x => supplierIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id);
        var receipts = await calc.CurrentReceiptsAsync();

        foreach (var group in s.Suppliers.GroupBy(x => x.SupplierId))
        {
            if (!suppliers.TryGetValue(group.Key, out var supplier))
                throw new InvalidOperationException($"تامین‌کننده «{group.First().SupplierTitle}» دیگر فعال یا موجود نیست.");

            var current = receipts.Where(x => x.SupplierId == supplier.Id).ToList();
            var initialBefore = Math.Round(supplier.InitialClaimAmount, 2);
            var currentBefore = Math.Round(current.Sum(x => x.RemainingDebt), 2);
            var requested = Math.Round(group.Sum(x => x.Amount), 2);
            var capacity = Math.Round(initialBefore + currentBefore, 2);

            if (requested <= 0)
                throw new InvalidOperationException($"مجموع مبلغ پرداخت تامین‌کننده «{supplier.Title}» باید بیشتر از صفر باشد.");
            if (requested > capacity + .005m)
                throw new InvalidOperationException($"مجموع مبلغ‌های تامین‌کننده «{supplier.Title}» از مجموع مطالبات قابل پرداخت او بیشتر است.");

            var split = PaymentCalculationService.SplitByShares(
                requested,
                initialBefore,
                currentBefore,
                s.NonCalculatedInitialSharePercent,
                s.NonCalculatedCurrentSharePercent,
                .01m);

            var initialAssigned = 0m;
            var lines = group.ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var lineInitial = i == lines.Count - 1
                    ? Math.Round(split.InitialClaim - initialAssigned, 2)
                    : Math.Round(split.InitialClaim * line.Amount / requested, 2);
                var lineCurrent = Math.Round(line.Amount - lineInitial, 2);
                if (lineInitial < 0 || lineCurrent < 0)
                    throw new InvalidOperationException($"تفکیک مبلغ تامین‌کننده «{supplier.Title}» نامعتبر است.");

                line.InitialClaimAmount = initialBefore;
                line.CurrentDebt = currentBefore;
                line.InitialClaimAtPreview = initialBefore;
                line.CurrentDebtAtPreview = currentBefore;
                line.CalculatedInitialClaimAmount = lineInitial;
                line.CalculatedCurrentClaimAmount = lineCurrent;
                line.CalculatedAllocatedAmount = lineInitial + lineCurrent;
                initialAssigned += lineInitial;
            }

            if (Math.Abs(initialAssigned - split.InitialClaim) > .005m)
                throw new InvalidOperationException($"تفکیک مطالبات تامین‌کننده «{supplier.Title}» نامعتبر است.");
        }
    }

    static void ValidateWizardItems(NonCalculatedPaymentWizardState s, List<NonCalculatedSupplierEditItem> items)
    {
        if (items.Count != s.Suppliers.Count)
            throw new InvalidOperationException("اطلاعات آیتم‌های پرداخت ناقص یا نامعتبر است.");

        for (var i = 0; i < s.Suppliers.Count; i++)
        {
            if (items[i].SupplierId != s.Suppliers[i].SupplierId)
                throw new InvalidOperationException("ترتیب آیتم‌های پرداخت تغییر کرده است؛ لطفاً اطلاعات را مجدداً وارد کنید.");

            if (!Enum.IsDefined(typeof(NonCalculatedPaymentType), items[i].PaymentType))
                throw new InvalidOperationException($"نوع پرداخت در ردیف {i + 1} نامعتبر است.");

            if (items[i].Amount <= 0)
                throw new InvalidOperationException($"مبلغ ردیف {i + 1} باید بیشتر از صفر باشد.");

            if (!string.IsNullOrWhiteSpace(items[i].Description) && items[i].Description.Length > 2000)
                throw new InvalidOperationException($"توضیحات ردیف {i + 1} نباید بیشتر از ۲۰۰۰ کاراکتر باشد.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel()
    {
        HttpContext.Session.Remove(SessionKey);
        TempData["Result"] = "فرایند پرداخت غیرمحاسباتی لغو شد.";
        return RedirectToAction(nameof(Step1));
    }

    NonCalculatedPaymentWizardState? Load()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<NonCalculatedPaymentWizardState>(json);
    }

    void Save(NonCalculatedPaymentWizardState s)
        => HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(s));
}