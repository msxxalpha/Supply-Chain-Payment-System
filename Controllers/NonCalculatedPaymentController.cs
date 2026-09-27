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
        if (existing != null && existing.Suppliers.Count > 0)
        {
            existing.AvailableSuppliers = await priority.GetAsync();
            return View(existing);
        }

        var s = new NonCalculatedPaymentWizardState
        {
            PaymentDateJalali = PersianDateService.ToJalali(DateTime.Today),
            AvailableSuppliers = await priority.GetAsync()
        };
        return View(s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Step1(string title, string paymentDateJalali, string? notes, int[] supplierIds)
    {
        try
        {
            var date = PersianDateService.Parse(paymentDateJalali);
            var ids = (supplierIds ?? []).Distinct().ToList();
            if (ids.Count == 0) throw new InvalidOperationException("حداقل یک تامین‌کننده را انتخاب کنید.");
            var all = await priority.GetAsync();
            var selected = all.Where(x => ids.Contains(x.SupplierId)).OrderByDescending(x => x.Priority).ThenBy(x => x.SupplierTitle).ToList();
            if (selected.Count != ids.Count) throw new InvalidOperationException("یکی از تامین‌کنندگان انتخاب‌شده دیگر فعال یا دارای مطالبات قابل پرداخت نیست.");

            var state = new NonCalculatedPaymentWizardState
            {
                Step = 2,
                Title = string.IsNullOrWhiteSpace(title) ? "پرداخت غیرمحاسباتی" : title.Trim(),
                PaymentDateJalali = PersianDateService.ToJalali(date),
                Notes = notes?.Trim(),
                AvailableSuppliers = all,
                Suppliers = selected.Select(x => new NonCalculatedSupplierRow
                {
                    SupplierId = x.SupplierId,
                    SupplierTitle = x.SupplierTitle,
                    InitialClaimAmount = x.InitialClaimAmount,
                    CurrentDebt = x.CurrentDebt,
                    PaymentType = NonCalculatedPaymentType.Cash,
                    Amount = 0
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
                Title = title?.Trim() ?? "پرداخت غیرمحاسباتی",
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
        if (s == null || s.Suppliers.Count == 0) return RedirectToAction(nameof(Step1));
        return View(s);
    }

    [Authorize(Policy = SecurityPermissions.PaymentApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(List<NonCalculatedSupplierEditItem> items)
    {
        var s = Load();
        if (s == null || s.Suppliers.Count == 0) return RedirectToAction(nameof(Step1));

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var selectedIds = s.Suppliers.Select(x => x.SupplierId).ToHashSet();
            if (items.Count != selectedIds.Count || items.Select(x => x.SupplierId).Distinct().Count() != selectedIds.Count || items.Any(x => !selectedIds.Contains(x.SupplierId)))
                throw new InvalidOperationException("فهرست تامین‌کنندگان پرداخت غیرمحاسباتی ناقص یا تکراری است.");

            var settings = await calc.LoadSystemParametersAsync();
            var initialShare = settings.GetValueOrDefault("NONCALC_INITIAL_SHARE")?.Value ?? 50;
            var currentShare = settings.GetValueOrDefault("NONCALC_CURRENT_SHARE")?.Value ?? 50;
            PaymentCalculationService.ValidateShares(initialShare, currentShare, "پرداخت غیرمحاسباتی");

            var receipts = await calc.CurrentReceiptsAsync();
            var supplierIds = selectedIds.ToList();
            var suppliers = await db.Suppliers.Where(x => supplierIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var user = await db.Users.FindAsync(UserId);
            var runDate = PersianDateService.Parse(s.PaymentDateJalali);
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
                TotalAllocationBudget = 0,
                AmountUnit = "ریال",
                ImportedDebt = 0,
                RemainingDebt = 0,
                ImportedRowCount = s.Suppliers.Count,
                Notes = s.Notes,
                Status = PaymentRunStatus.Approved,
                FinancialEffectsAppliedAt = DateTime.UtcNow,
                CreatedBy = UserId,
                ApprovedBy = UserId,
                ApprovedAt = DateTime.UtcNow,
                PreparedByNameSnapshot = user?.DisplayName ?? UserDisplayName,
                ConfirmedByNameSnapshot = user?.DisplayName ?? UserDisplayName
            };
            db.PaymentRuns.Add(run);
            await db.SaveChangesAsync();

            var systemValues = await db.SystemParameters.Where(x => x.IsActive).AsNoTracking().ToListAsync();
            db.PaymentRunSystemParameterSnapshots.AddRange(systemValues.Select(x => new PaymentRunSystemParameterSnapshot
            {
                PaymentRunId = run.Id, Code = x.Code, Title = x.Title, ValueType = x.ValueType, Value = x.Value, Unit = x.Unit
            }));
            await db.SaveChangesAsync();

            decimal totalAllocated = 0, totalBasis = 0;

            foreach (var selected in s.Suppliers)
            {
                var item = items.Single(x => x.SupplierId == selected.SupplierId);
                if (!suppliers.TryGetValue(selected.SupplierId, out var supplier))
                    throw new InvalidOperationException($"تامین‌کننده «{selected.SupplierTitle}» یافت نشد.");
                if (item.Amount <= 0)
                    throw new InvalidOperationException($"مبلغ پرداخت تامین‌کننده «{supplier.Title}» باید بیشتر از صفر باشد.");

                var supplierReceipts = receipts.Where(x => x.SupplierId == supplier.Id).OrderBy(x => x.ReceiptDate).ThenBy(x => x.ReceiptNo).ToList();
                var liveCurrent = supplierReceipts.Sum(x => x.RemainingDebt);
                var initialBefore = supplier.InitialClaimAmount;
                var capacity = initialBefore + liveCurrent;
                if (capacity <= 0) throw new InvalidOperationException($"تامین‌کننده «{supplier.Title}» مانده مطالبات قابل پرداخت ندارد.");

                var requested = Math.Round(item.Amount, 2);
                var effectiveTotal = Math.Min(requested, capacity);
                var split = PaymentCalculationService.SplitByShares(effectiveTotal, initialBefore, liveCurrent, initialShare, currentShare);
                var currentLeft = split.Current;
                var currentAllocated = 0m;
                var touched = new List<(PaymentCurrentReceiptSnapshot Receipt, decimal Amount)>();

                foreach (var receipt in supplierReceipts)
                {
                    if (currentLeft <= 0) break;
                    var take = Math.Round(Math.Min(currentLeft, receipt.RemainingDebt), 2);
                    if (take <= 0) continue;
                    touched.Add((receipt, take));
                    currentAllocated += take;
                    currentLeft -= take;
                }

                var actual = split.InitialClaim + currentAllocated;
                if (actual <= 0) throw new InvalidOperationException($"پرداخت تامین‌کننده «{supplier.Title}» قابل ثبت نیست.");

                db.PaymentRunSupplierSummaries.Add(new PaymentRunSupplierSummary
                {
                    PaymentRunId = run.Id,
                    SupplierId = supplier.Id,
                    SupplierTitle = supplier.Title,
                    InvoiceCount = touched.Count,
                    RemainingDebt = capacity,
                    AllocatedAmount = actual,
                    InitialClaimAllocatedAmount = split.InitialClaim,
                    CurrentClaimAllocatedAmount = currentAllocated,
                    InitialClaimBefore = initialBefore,
                    InitialClaimAfter = initialBefore - split.InitialClaim,
                    PaymentType = item.PaymentType,
                    RequestedAmount = requested,
                    AllocationPercent = 0
                });

                if (split.InitialClaim > 0)
                {
                    supplier.InitialClaimAmount = Math.Round(initialBefore - split.InitialClaim, 2);
                    db.SupplierClaimHistories.Add(new SupplierClaimHistory
                    {
                        SupplierId = supplier.Id,
                        ClaimType = SupplierClaimType.Initial,
                        AmountBefore = initialBefore,
                        AmountChange = -split.InitialClaim,
                        AmountAfter = supplier.InitialClaimAmount,
                        PaymentRunId = run.Id,
                        Reference = $"کاهش مطالبات استقراری بابت پرداخت غیرمحاسباتی شماره {run.Id}",
                        EffectiveDateJalali = s.PaymentDateJalali,
                        UserId = UserId
                    });
                }

                var first = true;
                foreach (var pair in touched)
                {
                    var receipt = pair.Receipt;
                    var prior = Math.Max(0, receipt.OriginalDebt - receipt.RemainingDebt);
                    var initialForRow = first ? split.InitialClaim : 0;
                    var totalForRow = pair.Amount + initialForRow;
                    db.PaymentRunInvoices.Add(new PaymentRunInvoice
                    {
                        PaymentRunId = run.Id,
                        PaymentKeyHash = receipt.PaymentKeyHash,
                        ReceiptNo = receipt.ReceiptNo,
                        Warehouse = receipt.Warehouse,
                        PartTitle = receipt.PartTitle,
                        SupplierTitle = receipt.SupplierTitle,
                        PartId = receipt.PartId,
                        SupplierId = receipt.SupplierId,
                        OriginalDebt = receipt.OriginalDebt,
                        ReceiptDate = receipt.ReceiptDate,
                        ReceiptDateJalali = PersianDateService.ToJalali(receipt.ReceiptDate),
                        ContractSettlementDays = receipt.ContractSettlementDays,
                        SupplierInitialClaimAmount = initialBefore,
                        PreviousAllocated = prior,
                        RemainingDebt = receipt.RemainingDebt,
                        SupplierOutstandingDebt = capacity,
                        WeightedScore = 0,
                        AllocationRatio = 0,
                        CalculatedAllocatedAmount = 0,
                        CalculatedCurrentAllocatedAmount = 0,
                        CalculatedInitialClaimAllocatedAmount = 0,
                        AllocatedCurrentAmount = pair.Amount,
                        AllocatedInitialClaimAmount = initialForRow,
                        AllocatedAmount = totalForRow,
                        SourceRowNumber = 0
                    });
                    first = false;
                }

                totalAllocated += actual;
                totalBasis += capacity;
            }

            var summaries = await db.PaymentRunSupplierSummaries.Where(x => x.PaymentRunId == run.Id).ToListAsync();
            foreach (var summary in summaries)
                summary.AllocationPercent = totalAllocated > 0 ? Math.Round(summary.AllocatedAmount / totalAllocated, 8) : 0;
            run.TotalAllocationBudget = totalAllocated;
            run.ImportedDebt = totalBasis;
            run.RemainingDebt = totalBasis;
            await db.SaveChangesAsync();

            db.AuditLogs.Add(new AuditLog
            {
                Action = "APPROVE",
                Entity = "PaymentRun",
                EntityId = run.Id.ToString(),
                Details = $"پرداخت غیرمحاسباتی؛ استقراری: {summaries.Sum(x => x.InitialClaimAllocatedAmount):N0}؛ جاری: {summaries.Sum(x => x.CurrentClaimAllocatedAmount):N0}",
                UserId = UserId
            });
            await db.SaveChangesAsync();

            await tx.CommitAsync();
            HttpContext.Session.Remove(SessionKey);
            TempData["Result"] = $"پرداخت غیرمحاسباتی شماره {run.Id} با موفقیت ثبت و تایید شد.";
            return RedirectToAction("History", "PaymentWizard");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Step2));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel()
    {
        HttpContext.Session.Remove(SessionKey);
        TempData["Result"] = "پرداخت غیرمحاسباتی لغو شد.";
        return RedirectToAction(nameof(Step1));
    }

    NonCalculatedPaymentWizardState? Load()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<NonCalculatedPaymentWizardState>(json);
    }

    void Save(NonCalculatedPaymentWizardState s) => HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(s));
}
