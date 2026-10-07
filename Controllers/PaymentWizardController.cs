using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize]
public class PaymentWizardController(AppDbContext db, ExcelService excel, PaymentCalculationService calc, PaymentOrderPdfService pdf, InputQueryService inputQueries) : Controller
{
    const string SessionKey = "PaymentWizardState";
    int UserId => int.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : 0;
    string UserDisplayName => User.Identity?.Name ?? "کاربر";

    public IActionResult Index() => RedirectToAction(nameof(Step1));

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpGet]
    public IActionResult Step1()
    {
        var s = Load() ?? new PaymentWizardState
        {
            CalculationDateJalali = PersianDateService.ToJalali(DateTime.Today),
            PeriodFromJalali = PersianDateService.ToJalali(DateTime.Today.AddDays(-30)),
            PeriodToJalali = PersianDateService.ToJalali(DateTime.Today),
            Title = "محاسبه تخصیص تامین‌کنندگان"
        };
        return View(s);
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Step1(PaymentWizardState s)
    {
        try
        {
            var cd = PersianDateService.Parse(s.CalculationDateJalali);
            var from = PersianDateService.Parse(s.PeriodFromJalali);
            var to = PersianDateService.Parse(s.PeriodToJalali);
            if (to < from) throw new InvalidOperationException("تاریخ پایان دوره نمی‌تواند قبل از تاریخ شروع باشد.");
            if (s.TotalAllocationBudget < 0) throw new InvalidOperationException("مبلغ کل قابل تخصیص نمی‌تواند منفی باشد.");
            s.CalculationDateJalali = PersianDateService.ToJalali(cd);
            s.PeriodFromJalali = PersianDateService.ToJalali(from);
            s.PeriodToJalali = PersianDateService.ToJalali(to);
            s.Title = string.IsNullOrWhiteSpace(s.Title) ? "محاسبه تخصیص تامین‌کنندگان" : s.Title.Trim();
            if (s.ReceiptSource is not PaymentReceiptSource.Excel and not PaymentReceiptSource.WarehouseSubsystem)
                throw new InvalidOperationException("منبع اطلاعات رسیدها نامعتبر است.");
            s.Step = 2;
            Save(s);
            return RedirectToAction(nameof(Step2));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return View(s);
        }
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpGet]
    public IActionResult Step2()
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        return View(s);
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        if (s.ReceiptSource != PaymentReceiptSource.Excel)
        {
            TempData["Error"] = "در این نوبت، منبع اطلاعات رسیدها «زیرسیستم انبار» انتخاب شده است.";
            return RedirectToAction(nameof(Step2));
        }

        if (file == null || file.Length == 0)
        {
            s.ImportErrorsIgnored = false;
            s.CanIgnoreImportErrors = false;
            s.IgnoredImportRowNumbers = [];
            s.ImportErrorGroups = [new PaymentImportErrorGroup { Key = "file", Title = "فرمت و ساختار فایل", Errors = ["فایل Excel انتخاب نشده است."] }];
            Save(s);
            return View("Step2", s);
        }

        try
        {
            var rows = excel.ReadPaymentInvoices(file.OpenReadStream(), PersianDateService.Parse, out var errors);
            return await ProcessImportedRowsAsync(s, rows, errors, Path.GetFileName(file.FileName), PaymentReceiptSource.Excel);
        }
        catch (Exception ex)
        {
            s.ImportErrorsIgnored = false;
            s.CanIgnoreImportErrors = false;
            s.IgnoredImportRowNumbers = [];
            s.ImportErrorGroups = [new PaymentImportErrorGroup { Key = "system", Title = "خطای پردازش فایل", Errors = [ex.Message] }];
            Save(s);
            return View("Step2", s);
        }
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadWarehouse()
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        if (s.ReceiptSource != PaymentReceiptSource.WarehouseSubsystem)
        {
            TempData["Error"] = "برای دریافت مستقیم، ابتدا در گام اول منبع «زیرسیستم انبار» را انتخاب کنید.";
            return RedirectToAction(nameof(Step1));
        }

        try
        {
            var from = PersianDateService.Parse(s.PeriodFromJalali);
            var to = PersianDateService.Parse(s.PeriodToJalali);
            var result = await inputQueries.ExecuteInventoryReceiptsAsync(from, to);
            if (!result.Success && result.Rows.Count == 0)
            {
                s.ImportErrorsIgnored = false;
                s.CanIgnoreImportErrors = false;
                s.IgnoredImportRowNumbers = [];
                s.ImportErrorGroups = [new PaymentImportErrorGroup { Key = "warehouse-query", Title = "کوئری زیرسیستم انبار", Errors = result.Errors }];
                s.Rows = [];
                s.Parameters = [];
                Save(s);
                return View("Step2", s);
            }

            return await ProcessImportedRowsAsync(
                s,
                result.Rows,
                result.Errors,
                "زیرسیستم انبار؛ اطلاعات رسیدهای خرید انبار",
                PaymentReceiptSource.WarehouseSubsystem);
        }
        catch (Exception ex)
        {
            s.ImportErrorsIgnored = false;
            s.CanIgnoreImportErrors = false;
            s.IgnoredImportRowNumbers = [];
            s.ImportErrorGroups = [new PaymentImportErrorGroup { Key = "warehouse-query", Title = "اجرای کوئری زیرسیستم انبار", Errors = [ex.Message] }];
            Save(s);
            return View("Step2", s);
        }
    }

    async Task<IActionResult> ProcessImportedRowsAsync(
        PaymentWizardState s,
        IReadOnlyList<ImportedPaymentInvoice> imported,
        List<string> errors,
        string sourceName,
        PaymentReceiptSource source)
    {
        s.ImportErrorGroups = [];
        s.ImportErrorsIgnored = false;
        s.IgnoredImportRowNumbers = [];
        s.ReceiptSource = source;
        s.SourceFileName = sourceName;
        s.ImportedReceipts = imported.ToList();
        s.MissingParts = [];
        s.MissingSuppliers = [];

        var from = PersianDateService.Parse(s.PeriodFromJalali);
        var to = PersianDateService.Parse(s.PeriodToJalali);
        var outside = imported.Where(x => x.ReceiptDate.Date < from.Date || x.ReceiptDate.Date > to.Date).ToList();
        if (outside.Count > 0)
            errors.Add($"{outside.Count} رکورد خارج از بازه زمانی تعیین‌شده است؛ {SourceRows(outside.Select(x => x.RowNumber), source)}.");

        var parts = await db.Parts.Where(x => x.IsActive).AsNoTracking().Select(x => new { x.Id, x.Title }).ToListAsync();
        var suppliers = await db.Suppliers.Where(x => x.IsActive).AsNoTracking().Select(x => new { x.Id, x.Title }).ToListAsync();
        var partByName = parts.GroupBy(x => Normalize(x.Title)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var supplierByName = suppliers.GroupBy(x => Normalize(x.Title)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());

        s.MissingParts = imported.Select(x => x.PartTitle.Trim()).Where(x => x != "" && !partByName.ContainsKey(Normalize(x))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        s.MissingSuppliers = imported.Select(x => x.SupplierTitle.Trim()).Where(x => x != "" && !supplierByName.ContainsKey(Normalize(x))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        errors.AddRange(imported
            .Where(x => !string.IsNullOrWhiteSpace(x.PartTitle) && !partByName.ContainsKey(Normalize(x.PartTitle)))
            .GroupBy(x => x.PartTitle.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => $"کالا «{g.Key}» در اطلاعات پایه تعریف نشده یا فعال نیست؛ {SourceRows(g.Select(x => x.RowNumber), source)}."));

        errors.AddRange(imported
            .Where(x => !string.IsNullOrWhiteSpace(x.SupplierTitle) && !supplierByName.ContainsKey(Normalize(x.SupplierTitle)))
            .GroupBy(x => x.SupplierTitle.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => $"تامین‌کننده «{g.Key}» در اطلاعات پایه تعریف نشده یا فعال نیست؛ {SourceRows(g.Select(x => x.RowNumber), source)}."));

        var activeMappings = await db.SupplierParts
            .Where(x => x.IsActive && x.Supplier!.IsActive && x.Part!.IsActive)
            .AsNoTracking()
            .Select(x => new { x.Id, x.PartId, x.SupplierId })
            .ToListAsync();
        var mappingByPair = activeMappings.ToDictionary(x => (x.PartId, x.SupplierId), x => x.Id);
        var mappingKeys = mappingByPair.Keys
            .Select(x => $"{x.PartId}:{x.SupplierId}")
            .ToHashSet();

        var mappingErrors = imported
            .Where(x => partByName.ContainsKey(Normalize(x.PartTitle)) && supplierByName.ContainsKey(Normalize(x.SupplierTitle)))
            .GroupBy(x => new
            {
                Part = x.PartTitle.Trim(),
                Supplier = x.SupplierTitle.Trim(),
                Key = $"{partByName[Normalize(x.PartTitle)].Id}:{supplierByName[Normalize(x.SupplierTitle)].Id}"
            })
            .Where(g => !mappingKeys.Contains(g.Key.Key))
            .Select(g => $"ارتباط فعال بین کالا «{g.Key.Part}» و تامین‌کننده «{g.Key.Supplier}» در اطلاعات پایه تعریف نشده است؛ {SourceRows(g.Select(x => x.RowNumber), source)}.")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        errors.AddRange(mappingErrors);

        if (s.CurrentClaimCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList)
        {
            var partRefs = partByName.ToDictionary(x => x.Key, x => (Id: x.Value.Id, Title: x.Value.Title));
            var supplierRefs = supplierByName.ToDictionary(x => x.Key, x => (Id: x.Value.Id, Title: x.Value.Title));
            var priceListErrors = await FindMissingPriceListErrorsAsync(
                imported, partRefs, supplierRefs, mappingByPair, source == PaymentReceiptSource.Excel);
            errors.AddRange(priceListErrors);
        }

        if (errors.Count > 0)
        {
            s.IgnoredImportRowNumbers = ExtractImportErrorRowNumbers(errors);
            s.ImportErrorsIgnored = false;
            s.CanIgnoreImportErrors = s.IgnoredImportRowNumbers.Count > 0
                && errors
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .All(x => ExtractImportErrorRowNumbers([x]).Count > 0);
            s.Rows = [];
            s.Parameters = [];
            s.ImportedDebt = imported.Sum(x => x.DebtAmount);
            s.RemainingDebt = imported.Sum(x => x.DebtAmount);
            s.ImportErrorGroups = CategorizeImportErrors(errors);
            Save(s);
            return View("Step2", s);
        }

        try
        {
            await calc.CalculateAsync(s, imported);
            s.ImportErrorGroups = [];
            s.MissingParts = [];
            s.MissingSuppliers = [];
            Save(s);
            return RedirectToAction(nameof(Step2));
        }
        catch (Exception ex)
        {
            s.ImportErrorGroups = CategorizeImportErrors([ex.Message]);
            Save(s);
            return View("Step2", s);
        }
    }

    async Task<List<string>> FindMissingPriceListErrorsAsync(
        IReadOnlyList<ImportedPaymentInvoice> imported,
        IReadOnlyDictionary<string, (int Id, string Title)> partByName,
        IReadOnlyDictionary<string, (int Id, string Title)> supplierByName,
        IReadOnlyDictionary<(int PartId, int SupplierId), int> mappingByPair,
        bool isExcel)
    {
        var mappedRows = imported
            .Where(x => partByName.ContainsKey(Normalize(x.PartTitle))
                     && supplierByName.ContainsKey(Normalize(x.SupplierTitle)))
            .Select(x =>
            {
                var part = partByName[Normalize(x.PartTitle)];
                var supplier = supplierByName[Normalize(x.SupplierTitle)];
                var pair = (PartId: (int)part.Id, SupplierId: (int)supplier.Id);
                return new
                {
                    x.RowNumber,
                    x.ReceiptDate,
                    PartId = pair.PartId,
                    SupplierId = pair.SupplierId,
                    PartTitle = part.Title,
                    SupplierTitle = supplier.Title,
                    MappingId = mappingByPair.GetValueOrDefault(pair)
                };
            })
            .Where(x => x.MappingId > 0)
            .ToList();

        if (mappedRows.Count == 0) return [];

        var mappingIds = mappedRows.Select(x => x.MappingId).Distinct().ToList();
        var activePrices = await db.SupplierPriceListItems.AsNoTracking()
            .Where(x => x.IsActive && x.PurchasePrice > 0 && mappingIds.Contains(x.SupplierPartId))
            .Select(x => new { x.SupplierPartId, x.ValidFrom, x.ValidTo })
            .ToListAsync();

        var pricesByMapping = activePrices
            .GroupBy(x => x.SupplierPartId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var errors = new List<string>();
        foreach (var group in mappedRows.GroupBy(x => new
                 {
                     x.MappingId,
                     x.PartId,
                     x.SupplierId,
                     x.PartTitle,
                     x.SupplierTitle
                 }))
        {
            pricesByMapping.TryGetValue(group.Key.MappingId, out var prices);
            prices ??= [];

            var missingDates = group
                .GroupBy(x => x.ReceiptDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    RowNumbers = g.Select(x => x.RowNumber).OrderBy(x => x).ToList()
                })
                .Where(x => !prices.Any(p => p.ValidFrom.Date <= x.Date && p.ValidTo.Date >= x.Date))
                .OrderBy(x => x.Date)
                .ToList();

            if (missingDates.Count == 0) continue;

            var rowsText = string.Join("، ", missingDates.SelectMany(x => x.RowNumbers).Distinct().OrderBy(x => x));
            var sourceText = isExcel ? "سطرهای Excel" : "رکوردهای ورودی";

            if (prices.Count == 0)
            {
                errors.Add(
                    $"برای کالا «{group.Key.PartTitle}» و تامین‌کننده «{group.Key.SupplierTitle}» هیچ قیمت فعالی در فهرست بها ثبت نشده است؛ {sourceText}: {rowsText}.");
            }
            else
            {
                var datesText = string.Join("، ",
                    missingDates.Select(x => PersianDateService.ToJalali(x.Date)));

                errors.Add(
                    $"برای کالا «{group.Key.PartTitle}» و تامین‌کننده «{group.Key.SupplierTitle}» در تاریخ‌های {datesText} قیمت معتبر در فهرست بها وجود ندارد؛ {sourceText}: {rowsText}.");
            }
        }

        return errors
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    static string SourceRows(IEnumerable<int> rowNumbers, PaymentReceiptSource source)
    {
        var rows = string.Join("، ", rowNumbers.Distinct().OrderBy(x => x));
        return source == PaymentReceiptSource.Excel
            ? $"سطرهای Excel: {rows}"
            : $"رکوردهای ورودی: {rows}";
    }

    public static List<int> ExtractImportErrorRowNumbers(IEnumerable<string> errors)
    {
        var result = new HashSet<int>();
        const string pattern = @"(?:سطر\s+|سطرهای\s+Excel:\s*|رکوردهای\s+ورودی:\s*)([0-9۰-۹]+(?:\s*[،,]\s*[0-9۰-۹]+)*)";

        foreach (var error in errors.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            foreach (Match match in Regex.Matches(error, pattern, RegexOptions.CultureInvariant))
            {
                foreach (var token in match.Groups[1].Value.Split(new[] { '،', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var normalized = PersianDateService.ToLatinDigits(token);
                    if (int.TryParse(normalized, out var rowNumber) && rowNumber > 0)
                        result.Add(rowNumber);
                }
            }
        }

        return result.OrderBy(x => x).ToList();
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContinueWithIgnoredErrors(bool ignoreErrors)
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));

        if (!ignoreErrors || !s.CanIgnoreImportErrors || s.IgnoredImportRowNumbers.Count == 0)
        {
            TempData["Error"] = "خطاهای موجود قابل نادیده گرفتن نیستند یا گزینه مربوطه فعال نشده است.";
            return RedirectToAction(nameof(Step2));
        }

        if (s.ImportErrorGroups.Count == 0 || s.ImportErrorGroups.SelectMany(x => x.Errors).Any(e => ExtractImportErrorRowNumbers([e]).Count == 0))
        {
            TempData["Error"] = "همه خطاهای موجود قابل نادیده گرفتن نیستند؛ خطاهای ساختاری یا اتصال باید ابتدا برطرف شوند.";
            return RedirectToAction(nameof(Step2));
        }

        var ignored = s.IgnoredImportRowNumbers.ToHashSet();
        var validRows = s.ImportedReceipts
            .Where(x => !ignored.Contains(x.RowNumber))
            .ToList();

        if (validRows.Count == 0)
        {
            TempData["Error"] = "پس از حذف رکوردهای خطادار، هیچ رکورد سالمی برای محاسبه باقی نمانده است.";
            return RedirectToAction(nameof(Step2));
        }

        try
        {
            s.ImportedReceipts = validRows;
            s.ImportErrorsIgnored = true;
            s.Rows = [];
            s.Parameters = [];
            s.ImportedDebt = validRows.Sum(x => x.DebtAmount);
            s.RemainingDebt = s.ImportedDebt;
            s.ImportErrorGroups = [];
            s.CanIgnoreImportErrors = false;
            await calc.CalculateAsync(s, validRows);
            Save(s);
            TempData["Result"] = $"{ignored.Count:N0} رکورد خطادار نادیده گرفته شد و محاسبه با {validRows.Count:N0} رکورد سالم انجام شد.";
            return RedirectToAction(nameof(Step2));
        }
        catch (Exception ex)
        {
            s.ImportErrorsIgnored = false;
            s.CanIgnoreImportErrors = false;
            s.IgnoredImportRowNumbers = [];
            s.ImportErrorGroups = CategorizeImportErrors([ex.Message]);
            Save(s);
            return View("Step2", s);
        }
    }


    static List<PaymentImportErrorGroup> CategorizeImportErrors(List<string> errors)
    {
        // همه خطاهای یک بارگذاری در یک مجموعه واحد تجمیع می‌شوند.
        // دسته‌بندی صریح است تا هر خطا فقط یک‌بار و در تب مناسب نمایش داده شود.
        var groups = new[]
        {
            new PaymentImportErrorGroup { Key = "format", Title = "فرمت و ساختار فایل", Errors = [] },
            new PaymentImportErrorGroup { Key = "parts", Title = "کالاهای تعریف‌نشده یا غیرفعال", Errors = [] },
            new PaymentImportErrorGroup { Key = "suppliers", Title = "تامین‌کنندگان تعریف‌نشده یا غیرفعال", Errors = [] },
            new PaymentImportErrorGroup { Key = "prices", Title = "فهرست بها؛ قیمت‌های ناموجود", Errors = [] },
            new PaymentImportErrorGroup { Key = "mapping", Title = "ارتباط‌های تعریف‌نشده کالا–تامین‌کننده", Errors = [] },
            new PaymentImportErrorGroup { Key = "other", Title = "سایر خطاهای کنترل اطلاعات", Errors = [] }
        };

        foreach (var error in errors
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ClassifyImportError(error, groups).Errors.Add(error);
        }

        return groups.Where(x => x.Errors.Count > 0).ToList();
    }

    static PaymentImportErrorGroup ClassifyImportError(string error, IReadOnlyList<PaymentImportErrorGroup> groups)
    {
        // اولویت کنترل‌ها مشخص است تا خطاهای ارتباط، کالا و تامین‌کننده
        // به‌اشتباه در گروه فرمت یا خطاهای عمومی قرار نگیرند.
        if (error.Contains("ستون", StringComparison.OrdinalIgnoreCase)
            || error.Contains("فایل Excel", StringComparison.OrdinalIgnoreCase)
            || error.Contains("فرمت", StringComparison.OrdinalIgnoreCase))
            return groups.First(x => x.Key == "format");

        if (error.Contains("فهرست بها", StringComparison.OrdinalIgnoreCase)
            || error.Contains("قیمت معتبر", StringComparison.OrdinalIgnoreCase)
            || error.Contains("قیمت فعالی", StringComparison.OrdinalIgnoreCase))
            return groups.First(x => x.Key == "prices");

        if (error.Contains("ارتباط فعال بین کالا", StringComparison.OrdinalIgnoreCase)
            || error.Contains("قطعه–تامین‌کننده", StringComparison.OrdinalIgnoreCase)
            || error.Contains("قطعه-تامین‌کننده", StringComparison.OrdinalIgnoreCase))
            return groups.First(x => x.Key == "mapping");

        if (error.Contains("کالا «", StringComparison.OrdinalIgnoreCase))
            return groups.First(x => x.Key == "parts");

        if (error.Contains("تامین‌کننده «", StringComparison.OrdinalIgnoreCase))
            return groups.First(x => x.Key == "suppliers");

        return groups.First(x => x.Key == "other");
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScores(List<ScoreEditItem> items)
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));

        foreach (var item in items.Where(x => x.RowIndex >= 0 && x.RowIndex < s.Rows.Count))
        {
            var row = s.Rows[item.RowIndex];
            var score = row.Scores.FirstOrDefault(x => x.ParameterId == item.ParameterId);
            if (score == null || score.Source != "ارزیابی قطعه-تامین‌کننده") continue;
            if (item.Score < 1 || item.Score > 5)
            {
                TempData["Error"] = "امتیاز پارامترهای دستی باید بین 1 تا 5 باشد.";
                return RedirectToAction(nameof(Step2));
            }
            score.Score = item.Score;
            score.Contribution = Math.Round(item.Score * score.Weight / 100m, 6);
        }

        try
        {
            await calc.RecalculateCurrentDebtsAsync(s);
            Save(s);
            TempData["Result"] = "امتیازهای دستی و مبلغ تخصیص‌یافته با قواعد جدید مجدداً محاسبه شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Step2));
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSupplierAllocations(List<SupplierAllocationEditItem> items)
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        try
        {
            var settings = await calc.LoadSystemParametersAsync();
            PaymentCalculationService.ValidateShares(
                settings.GetValueOrDefault("CALC_INITIAL_SHARE")?.Value ?? 50,
                settings.GetValueOrDefault("CALC_CURRENT_SHARE")?.Value ?? 50,
                "پرداخت محاسباتی");
            var rounding = settings.GetValueOrDefault("ALLOCATION_ROUNDING")?.Value ?? 100000;
            var initialShare = settings.GetValueOrDefault("CALC_INITIAL_SHARE")?.Value ?? 50;
            var currentShare = settings.GetValueOrDefault("CALC_CURRENT_SHARE")?.Value ?? 50;
            var minAge = settings.GetValueOrDefault("MIN_EFFECTIVE_DEBT_AGE")?.Value ?? 0;
            var minAmount = settings.GetValueOrDefault("MIN_ALLOCATION_AMOUNT")?.Value ?? 1;
            PaymentCalculationService.ValidateShares(initialShare, currentShare, "پرداخت محاسباتی");
            var expected = s.Rows.Where(x => x.SupplierId.HasValue).GroupBy(x => x.SupplierId!.Value).ToList();

            if (items.Count != expected.Count || items.GroupBy(x => x.SupplierId).Any(g => g.Count() != 1) || expected.Any(g => !items.Any(x => x.SupplierId == g.Key)))
                throw new InvalidOperationException("اطلاعات تخصیص تامین‌کنندگان ناقص یا تکراری است.");

            var submitted = items.ToDictionary(x => x.SupplierId, x => Math.Round(x.Amount, 2));
            foreach (var group in expected)
            {
                var amount = submitted[group.Key];
                var initial = group.First().SupplierInitialClaimAmount;
                var max = Math.Round(group.Sum(x => x.RemainingDebt) + initial, 2);
                if (amount < 0 || amount > max + .005m)
                    throw new InvalidOperationException($"مبلغ تخصیص تامین‌کننده «{group.First().SupplierTitle}» نامعتبر است. سقف مجاز {max:N0} ریال است.");
                if (Math.Abs(Math.Round(amount / rounding, 8) - Math.Round(amount / rounding, 0)) > .00001m)
                    throw new InvalidOperationException($"مبلغ تخصیص تامین‌کننده «{group.First().SupplierTitle}» باید مضربی از {rounding:N0} ریال باشد.");
                PaymentCalculationService.DistributeSupplierAllocation(amount, group.ToList(), initialShare, currentShare, rounding, minAge, minAmount);
            }

            var total = s.Rows.Sum(x => x.AllocatedAmount);
            if (total > s.TotalAllocationBudget + .005m)
                throw new InvalidOperationException("جمع تخصیص تامین‌کنندگان نمی‌تواند از بودجه این نوبت بیشتر باشد.");
            foreach (var row in s.Rows)
                row.AllocationRatio = total > 0 ? Math.Round(row.AllocatedAmount / total, 8) : 0;

            Save(s);
            TempData["Result"] = "تخصیص نهایی تامین‌کنندگان ذخیره شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Step3));
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GoStep3()
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        if (s.Rows.Count == 0)
        {
            TempData["Error"] = "هیچ بدهی باقی‌مانده‌ای برای تخصیص وجود ندارد.";
            return RedirectToAction(nameof(Step2));
        }
        if (s.Rows.Any(r => r.Scores.Any(x => x.Source == "ارزیابی قطعه-تامین‌کننده" && (x.Score < 1 || x.Score > 5))))
        {
            TempData["Error"] = "تمام پارامترهای دستی باید امتیاز معتبر داشته باشند.";
            return RedirectToAction(nameof(Step2));
        }
        try
        {
            // قیمت معتبر باید درست پیش از ورود به مرحله تخصیص نهایی دوباره تعیین شود.
            // بنابراین اگر فهرست بها بین جذب اطلاعات و مرحله سوم تغییر کرده باشد،
            // محاسبه بر مبنای آخرین نرخ معتبر انجام می‌شود.
            await calc.RecalculateCurrentDebtsAsync(s);
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

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpGet]
    public IActionResult Step3()
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        if (s.Rows.Count == 0) return RedirectToAction(nameof(Step2));
        return View(s);
    }

    [Authorize(Policy = SecurityPermissions.PaymentApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve()
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var previous = await calc.PreviousCurrentAsync();
            var supplierIds = s.Rows.Where(x => x.SupplierId.HasValue).Select(x => x.SupplierId!.Value).Distinct().ToList();
            var suppliers = await db.Suppliers.Where(x => supplierIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            foreach (var group in s.Rows.GroupBy(x => x.SupplierId!.Value))
            {
                if (!suppliers.TryGetValue(group.Key, out var supplier))
                    throw new InvalidOperationException($"تامین‌کننده «{group.First().SupplierTitle}» دیگر فعال نیست.");
                var snapshotInitial = group.First().SupplierInitialClaimAmount;
                if (Math.Abs(supplier.InitialClaimAmount - snapshotInitial) > .005m)
                    throw new InvalidOperationException($"مطالبات استقراری تامین‌کننده «{supplier.Title}» در زمان محاسبه تغییر کرده است. محاسبه را مجدداً انجام دهید.");

                if (group.Sum(x => x.AllocatedInitialClaimAmount) > supplier.InitialClaimAmount + .005m)
                    throw new InvalidOperationException($"مبلغ تخصیص استقراری تامین‌کننده «{supplier.Title}» از مانده مطالبات استقراری او بیشتر است.");

                foreach (var row in group)
                {
                    var key = PaymentCalculationService.KeyHash(row.ReceiptNo, row.Warehouse, row.PartTitle, row.SupplierTitle);
                    var livePrevious = previous.GetValueOrDefault(key);
                    if (livePrevious != row.PreviousAllocated)
                        throw new InvalidOperationException($"تخصیص قبلی رسید «{row.ReceiptNo}» تغییر کرده است. لطفاً محاسبه را از ابتدا انجام دهید.");
                    if (row.AllocatedCurrentAmount < 0 || row.AllocatedCurrentAmount > row.RemainingDebt + .005m)
                        throw new InvalidOperationException($"مبلغ جاری تخصیص‌یافته رسید «{row.ReceiptNo}» نامعتبر است.");
                    if (row.AllocatedAmount < 0)
                        throw new InvalidOperationException($"مبلغ تخصیص‌یافته رسید «{row.ReceiptNo}» نامعتبر است.");
                    if (s.AllocationRounding > 0 && Math.Abs(Math.Round(row.AllocatedAmount / s.AllocationRounding, 8) - Math.Round(row.AllocatedAmount / s.AllocationRounding, 0)) > .00001m)
                        throw new InvalidOperationException($"مبلغ تخصیص رسید «{row.ReceiptNo}» باید مضربی از {s.AllocationRounding:N0} ریال باشد.");
                }
            }

            var user = await db.Users.FindAsync(UserId);
            var run = new PaymentRun
            {
                RunType = PaymentRunType.Calculated,
                ReceiptSource = s.ReceiptSource,
                Title = s.Title,
                CalculationDateJalali = s.CalculationDateJalali,
                CalculationDate = s.CalcDate(),
                PeriodFromJalali = s.PeriodFromJalali,
                PeriodToJalali = s.PeriodToJalali,
                PeriodFrom = PersianDateService.Parse(s.PeriodFromJalali),
                PeriodTo = PersianDateService.Parse(s.PeriodToJalali),
                TotalAllocationBudget = s.Rows.Sum(x => x.AllocatedAmount),
                AmountUnit = "ریال",
                ImportedDebt = s.ImportedDebt,
                RemainingDebt = s.RemainingDebt,
                ImportedRowCount = s.Rows.Count,
                SourceFileName = s.SourceFileName,
                Notes = s.Notes,
                Status = PaymentRunStatus.Approved,
                CreatedBy = UserId,
                ApprovedBy = UserId,
                ApprovedAt = DateTime.UtcNow,
                PreparedByNameSnapshot = user?.DisplayName ?? UserDisplayName,
                ConfirmedByNameSnapshot = user?.DisplayName ?? UserDisplayName
            };
            db.PaymentRuns.Add(run);
            await db.SaveChangesAsync();

            if (s.CurrentClaimCalculationMethod == CurrentClaimCalculationMethod.QuantityBasedPriceList)
            {
                var priceIds = s.Rows.Where(x => x.PriceListItemId.HasValue).Select(x => x.PriceListItemId!.Value).Distinct().ToList();
                var currentPrices = await db.SupplierPriceListItems.Where(x => priceIds.Contains(x.Id)).AsNoTracking().ToDictionaryAsync(x => x.Id);
                foreach (var row in s.Rows)
                {
                    if (!row.PriceListItemId.HasValue || !currentPrices.TryGetValue(row.PriceListItemId.Value, out var livePrice))
                        throw new InvalidOperationException($"فهرست بهای استفاده‌شده برای رسید «{row.ReceiptNo}» دیگر در سیستم موجود نیست؛ محاسبه را مجدداً انجام دهید.");
                    if (Math.Abs(livePrice.PurchasePrice - row.AppliedUnitPrice) > .005m ||
                        livePrice.ValidFrom.Date != row.AppliedPriceValidFrom?.Date ||
                        livePrice.ValidTo.Date != row.AppliedPriceValidTo?.Date)
                        throw new InvalidOperationException($"فهرست بهای استفاده‌شده برای رسید «{row.ReceiptNo}» پس از محاسبه تغییر کرده است؛ محاسبه را مجدداً انجام دهید.");
                }
            }

            await SaveSystemParameterSnapshotsAsync(run.Id, s);
            foreach (var p in s.Parameters)
                db.PaymentRunParameterSnapshots.Add(new PaymentRunParameterSnapshot
                {
                    PaymentRunId = run.Id, PaymentParameterId = p.Id, Code = p.Code, Title = p.Title, Type = p.Type,
                    Weight = p.Weight, MaxScore = p.MaxScore, ScoringGuide = p.ScoringGuide,
                    ScoringMethod = p.ScoringMethod, SortOrder = p.SortOrder
                });
            await db.SaveChangesAsync();

            var totalAssigned = s.Rows.Sum(x => x.AllocatedAmount);
            foreach (var group in s.Rows.GroupBy(x => x.SupplierId!.Value))
            {
                var supplier = suppliers[group.Key];
                var initialBefore = supplier.InitialClaimAmount;
                var initialAllocated = group.Sum(x => x.AllocatedInitialClaimAmount);
                var currentAllocated = group.Sum(x => x.AllocatedCurrentAmount);
                var totalAllocated = initialAllocated + currentAllocated;
                db.PaymentRunSupplierSummaries.Add(new PaymentRunSupplierSummary
                {
                    PaymentRunId = run.Id,
                    SupplierId = supplier.Id,
                    SupplierTitle = supplier.Title,
                    InvoiceCount = group.Count(),
                    RemainingDebt = group.Sum(x => x.RemainingDebt) + initialBefore,
                    AllocatedAmount = totalAllocated,
                    InitialClaimAllocatedAmount = initialAllocated,
                    CurrentClaimAllocatedAmount = currentAllocated,
                    InitialClaimBefore = initialBefore,
                    InitialClaimAfter = initialBefore - initialAllocated,
                    RequestedAmount = totalAllocated,
                    AllocationPercent = totalAssigned > 0 ? Math.Round(totalAllocated / totalAssigned, 8) : 0
                });
            }
            await db.SaveChangesAsync();

            foreach (var row in s.Rows)
            {
                var inv = new PaymentRunInvoice
                {
                    PaymentRunId = run.Id,
                    PaymentKeyHash = PaymentCalculationService.KeyHash(row.ReceiptNo, row.Warehouse, row.PartTitle, row.SupplierTitle),
                    ReceiptNo = row.ReceiptNo,
                    Warehouse = row.Warehouse,
                    PartTitle = row.PartTitle,
                    SupplierTitle = row.SupplierTitle,
                    PartId = row.PartId,
                    SupplierId = row.SupplierId,
                    ReceiptQuantity = row.ReceiptQuantity,
                    DebtCalculationMethod = row.DebtCalculationMethod,
                    AppliedUnitPrice = row.AppliedUnitPrice,
                    PriceListItemId = row.PriceListItemId,
                    AppliedPriceValidFrom = row.AppliedPriceValidFrom,
                    AppliedPriceValidTo = row.AppliedPriceValidTo,
                    OriginalDebt = row.OriginalDebt,
                    ReceiptDate = row.ReceiptDate,
                    ReceiptDateJalali = row.ReceiptDateJalali,
                    ContractSettlementDays = row.ContractSettlementDays,
                    SupplyCapacity = row.SupplyCapacity,
                    SupplierInitialClaimAmount = row.SupplierInitialClaimAmount,
                    DebtAgeDays = row.DebtAgeDays,
                    PreviousAllocated = row.PreviousAllocated,
                    RemainingDebt = row.RemainingDebt,
                    SupplierOutstandingDebt = row.SupplierOutstandingDebt,
                    WeightedScore = row.WeightedScore,
                    AllocationRatio = row.AllocationRatio,
                    CalculatedAllocatedAmount = row.CalculatedAllocatedAmount,
                    CalculatedCurrentAllocatedAmount = row.CalculatedCurrentAllocatedAmount,
                    CalculatedInitialClaimAllocatedAmount = row.CalculatedInitialClaimAllocatedAmount,
                    AllocatedCurrentAmount = row.AllocatedCurrentAmount,
                    AllocatedInitialClaimAmount = row.AllocatedInitialClaimAmount,
                    AllocatedAmount = row.AllocatedAmount,
                    SourceRowNumber = row.SourceRowNumber
                };
                db.PaymentRunInvoices.Add(inv);
                await db.SaveChangesAsync();
                foreach (var score in row.Scores)
                {
                    var parameter = s.Parameters.FirstOrDefault(p => p.Id == score.ParameterId);
                    db.PaymentInvoiceScores.Add(new PaymentInvoiceScore
                    {
                        PaymentRunInvoiceId = inv.Id,
                        PaymentParameterId = score.ParameterId,
                        Score = score.Score,
                        Weight = score.Weight,
                        WeightedContribution = score.Contribution,
                        ScoringSource = score.Source,
                        ParameterTitleSnapshot = score.ParameterTitle,
                        ParameterCodeSnapshot = parameter?.Code ?? "",
                        ParameterTypeSnapshot = parameter?.Type ?? PaymentParameterType.Financial,
                        MaxScoreSnapshot = parameter?.MaxScore ?? 5,
                        ScoringGuideSnapshot = parameter?.ScoringGuide ?? "",
                        ScoringMethodSnapshot = parameter?.ScoringMethod ?? ParameterScoringMethod.Manual
                    });
                }
                await db.SaveChangesAsync();
            }

            db.AuditLogs.Add(new AuditLog
            {
                Action = "APPROVE",
                Entity = "PaymentRun",
                EntityId = run.Id.ToString(),
                Details = $"محاسبه محاسباتی؛ تخصیص استقراری: {s.Rows.Sum(x => x.AllocatedInitialClaimAmount):N0}؛ تخصیص جاری: {s.Rows.Sum(x => x.AllocatedCurrentAmount):N0}",
                UserId = UserId
            });
            await db.SaveChangesAsync();

            await tx.CommitAsync();
            HttpContext.Session.Remove(SessionKey);
            TempData["Result"] = $"محاسبه پرداخت شماره {run.Id} با موفقیت تایید و ثبت شد.";
            return RedirectToAction(nameof(History));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Step3));
        }
    }

    async Task SaveSystemParameterSnapshotsAsync(int runId, PaymentWizardState s)
    {
        var values = await db.SystemParameters.Where(x => x.IsActive).AsNoTracking().ToListAsync();
        var captured = s.SystemParameterValues ?? new Dictionary<string, decimal>();

        db.PaymentRunSystemParameterSnapshots.AddRange(values.Select(x => new PaymentRunSystemParameterSnapshot
        {
            PaymentRunId = runId,
            Code = x.Code,
            Title = x.Title,
            ValueType = x.ValueType,
            Value = captured.TryGetValue(x.Code, out var value) ? value : x.Value,
            Unit = x.Unit
        }));
        await db.SaveChangesAsync();
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel()
    {
        HttpContext.Session.Remove(SessionKey);
        TempData["Result"] = "فرایند محاسبه لغو شد.";
        return RedirectToAction(nameof(Step1));
    }

    [Authorize(Policy = SecurityPermissions.PaymentHistory)]
    [HttpGet]
    public async Task<IActionResult> History()
    {
        var rows = await db.PaymentRuns.Where(x => !x.IsDeleted && (x.Status == PaymentRunStatus.Approved || x.Status == PaymentRunStatus.PaymentOrdered))
            .Include(x => x.Invoices).Include(x => x.SupplierSummaries).Include(x => x.SystemParameterSnapshots).OrderByDescending(x => x.Id).ToListAsync();
        return View(rows);
    }

    [Authorize(Policy = SecurityPermissions.PaymentHistory)]
    [HttpGet]
    public async Task<IActionResult> Deleted()
    {
        var rows = await db.PaymentRuns.Where(x => x.IsDeleted).Include(x => x.Invoices).Include(x => x.SupplierSummaries)
            .OrderByDescending(x => x.Id).ToListAsync();
        return View(rows);
    }

    [Authorize(Policy = SecurityPermissions.PaymentHistory)]
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var x = await db.PaymentRuns.Include(r => r.SupplierSummaries).Include(r => r.SystemParameterSnapshots).Include(r => r.NonCalculatedPaymentLines).Include(r => r.Invoices).ThenInclude(i => i.Scores)
            .SingleOrDefaultAsync(r => r.Id == id);
        if (x == null) return NotFound();
        return View(x);
    }

    [Authorize(Policy = SecurityPermissions.PaymentCalculate)]
    [HttpGet]
    public IActionResult ExportPreview()
    {
        var s = Load();
        if (s == null) return RedirectToAction(nameof(Step1));
        return File(excel.PaymentRows(s.Rows, s.Parameters), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "payment-calculation.xlsx");
    }

    [Authorize(Policy = SecurityPermissions.PaymentHistory)]
    [HttpGet]
    public async Task<IActionResult> ExportRun(int id)
    {
        var run = await db.PaymentRuns.Include(x => x.Invoices).ThenInclude(x => x.Scores).SingleOrDefaultAsync(x => x.Id == id);
        if (run == null) return NotFound();

        var rows = run.Invoices.Select(x => new PaymentCalculationRow
        {
            ReceiptNo = x.ReceiptNo, Warehouse = x.Warehouse, PartTitle = x.PartTitle, SupplierTitle = x.SupplierTitle,
            ReceiptQuantity = x.ReceiptQuantity, DebtCalculationMethod = x.DebtCalculationMethod, AppliedUnitPrice = x.AppliedUnitPrice,
            PriceListItemId = x.PriceListItemId, AppliedPriceValidFrom = x.AppliedPriceValidFrom, AppliedPriceValidTo = x.AppliedPriceValidTo,
            OriginalDebt = x.OriginalDebt, PreviousAllocated = x.PreviousAllocated, RemainingDebt = x.RemainingDebt,
            SupplierOutstandingDebt = x.SupplierOutstandingDebt, ContractSettlementDays = x.ContractSettlementDays, DebtAgeDays = x.DebtAgeDays,
            WeightedScore = x.WeightedScore, AllocationRatio = x.AllocationRatio,
            CalculatedAllocatedAmount = x.CalculatedAllocatedAmount,
            CalculatedCurrentAllocatedAmount = x.CalculatedCurrentAllocatedAmount,
            CalculatedInitialClaimAllocatedAmount = x.CalculatedInitialClaimAllocatedAmount,
            AllocatedCurrentAmount = x.AllocatedCurrentAmount, AllocatedInitialClaimAmount = x.AllocatedInitialClaimAmount,
            AllocatedAmount = x.AllocatedAmount, ReceiptDate = x.ReceiptDate, ReceiptDateJalali = x.ReceiptDateJalali,
            Scores = x.Scores.Select(sc => new PaymentScoreResult
            {
                ParameterId = sc.PaymentParameterId, ParameterTitle = sc.ParameterTitleSnapshot, Type = sc.ParameterTypeSnapshot,
                Weight = sc.Weight, Score = sc.Score, Contribution = sc.WeightedContribution, Source = sc.ScoringSource
            }).ToList()
        }).ToList();

        var ps = await db.PaymentRunParameterSnapshots.Where(x => x.PaymentRunId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Title).AsNoTracking().ToListAsync();
        var snapshotParameters = ps.Select(x => new PaymentParameterSnapshot(x.PaymentParameterId, x.Code, x.Title, x.Type, x.Weight, x.MaxScore, x.ScoringGuide, x.ScoringMethod, x.SortOrder)).ToList();
        return File(excel.PaymentRows(rows, snapshotParameters), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"payment-run-{id}.xlsx");
    }

    [Authorize(Policy = SecurityPermissions.PaymentHistory)]
    [HttpGet]
    public async Task<IActionResult> ExportSummary(int id)
    {
        var rows = await db.PaymentRunSupplierSummaries.Where(x => x.PaymentRunId == id).OrderByDescending(x => x.AllocatedAmount)
            .Select(x => new { x.SupplierTitle, x.AllocatedAmount, x.InitialClaimAllocatedAmount, x.CurrentClaimAllocatedAmount, x.InvoiceCount, x.PaymentType }).ToListAsync();
        return File(excel.SupplierSummary(rows.Select(x => (
            x.SupplierTitle, x.AllocatedAmount, x.InitialClaimAllocatedAmount, x.CurrentClaimAllocatedAmount, x.InvoiceCount, PaymentTypeTitle(x.PaymentType)))), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"supplier-summary-{id}.xlsx");
    }

    static string PaymentTypeTitle(NonCalculatedPaymentType? type) => type switch { NonCalculatedPaymentType.Cash => "نقدی", NonCalculatedPaymentType.CheckTransfer => "واگذاری چک", NonCalculatedPaymentType.VehicleTransfer => "واگذاری خودرو", NonCalculatedPaymentType.RawMaterialTransfer => "واگذاری مواداولیه", NonCalculatedPaymentType.IntroductionLetter => "معرفی نامه", NonCalculatedPaymentType.CreditLimit => "حد اعتباری", _ => "" };

    [Authorize(Policy = SecurityPermissions.PaymentOrderCreate)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePaymentOrder(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var run = await db.PaymentRuns
                .Include(x => x.SupplierSummaries)
                .Include(x => x.Invoices)
                .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (run == null) throw new InvalidOperationException("محاسبه یافت نشد.");
            if (run.Status != PaymentRunStatus.Approved)
                throw new InvalidOperationException("فقط پرداخت تاییدشده قابل تبدیل است.");
            if (run.RunType != PaymentRunType.Calculated)
                throw new InvalidOperationException("دستور پرداخت این بخش فقط برای پرداخت‌های محاسباتی صادر می‌شود.");

            var user = await db.Users.FindAsync(UserId);
            var alreadyApplied = run.FinancialEffectsAppliedAt.HasValue;

            if (!alreadyApplied)
            {
                // از آنجا که محاسبه تاییدشده هنوز اثر مالی ندارد، هنگام دستور پرداخت
                // باید آخرین مانده جاری هر رسید و مطالبات استقراری تامین‌کننده دوباره کنترل شود.
                var previous = await calc.PreviousCurrentAsync();
                var supplierIds = run.SupplierSummaries.Select(x => x.SupplierId).Distinct().ToList();
                var suppliers = await db.Suppliers.Where(x => supplierIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

                foreach (var summary in run.SupplierSummaries)
                {
                    if (!suppliers.TryGetValue(summary.SupplierId, out var supplier))
                        throw new InvalidOperationException($"تامین‌کننده «{summary.SupplierTitle}» یافت نشد.");

                    if (Math.Abs(supplier.InitialClaimAmount - summary.InitialClaimBefore) > .005m)
                        throw new InvalidOperationException($"مطالبات استقراری تامین‌کننده «{supplier.Title}» از زمان تایید محاسبه تغییر کرده است؛ ابتدا محاسبه را مجدداً ایجاد کنید.");

                    if (summary.InitialClaimAllocatedAmount < 0 ||
                        summary.InitialClaimAllocatedAmount > supplier.InitialClaimAmount + .005m)
                        throw new InvalidOperationException($"تخصیص استقراری تامین‌کننده «{supplier.Title}» از مانده مطالبات استقراری او بیشتر است.");
                }

                foreach (var row in run.Invoices)
                {
                    var keyPrevious = previous.GetValueOrDefault(row.PaymentKeyHash);
                    if (Math.Abs(keyPrevious - row.PreviousAllocated) > .005m)
                        throw new InvalidOperationException($"مانده جاری رسید «{row.ReceiptNo}» پس از تایید محاسبه تغییر کرده است؛ قبل از دستور پرداخت، محاسبه را مجدداً انجام دهید.");

                    var liveRemaining = Math.Max(0, row.OriginalDebt - keyPrevious);
                    if (row.AllocatedCurrentAmount < 0 || row.AllocatedCurrentAmount > liveRemaining + .005m)
                        throw new InvalidOperationException($"تخصیص جاری رسید «{row.ReceiptNo}» از مانده جاری واقعی آن بیشتر است.");
                }

                foreach (var summary in run.SupplierSummaries.Where(x => x.InitialClaimAllocatedAmount > 0))
                {
                    var supplier = suppliers[summary.SupplierId];
                    var before = supplier.InitialClaimAmount;
                    supplier.InitialClaimAmount = Math.Round(before - summary.InitialClaimAllocatedAmount, 2);

                    db.SupplierClaimHistories.Add(new SupplierClaimHistory
                    {
                        SupplierId = supplier.Id,
                        ClaimType = SupplierClaimType.Initial,
                        AmountBefore = before,
                        AmountChange = -summary.InitialClaimAllocatedAmount,
                        AmountAfter = supplier.InitialClaimAmount,
                        PaymentRunId = run.Id,
                        Reference = $"کاهش مطالبات استقراری بابت دستور پرداخت محاسباتی شماره {run.Id}",
                        EffectiveDateJalali = run.CalculationDateJalali,
                        UserId = UserId
                    });
                }

                run.FinancialEffectsAppliedAt = DateTime.UtcNow;
            }

            run.Status = PaymentRunStatus.PaymentOrdered;
            run.PaymentOrderNumber ??= $"DP-{run.Id:000000}";
            run.PaymentOrderedAt ??= DateTime.UtcNow;
            run.PaymentOrderedBy = UserId;
            run.PaymentOrderApproverNameSnapshot = user?.DisplayName ?? UserDisplayName;

            db.AuditLogs.Add(new AuditLog
            {
                Action = "PAYMENT_ORDER",
                Entity = "PaymentRun",
                EntityId = run.Id.ToString(),
                Details = $"تبدیل پرداخت شماره {run.Id} به دستور پرداخت {run.PaymentOrderNumber}؛ اثر مالی در زمان دستور پرداخت اعمال شد.",
                UserId = UserId
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Result"] = $"پرداخت شماره {run.Id} به دستور پرداخت {run.PaymentOrderNumber} تبدیل شد؛ مانده مطالبات استقراری و جاری از این لحظه در محاسبات بعدی لحاظ خواهد شد.";
            return RedirectToAction(nameof(History));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(History));
        }
    }

    [Authorize(Policy = SecurityPermissions.PaymentOrderDownload)]
    [HttpGet]
    public async Task<IActionResult> DownloadPaymentOrder(int id)
    {
        var run = await db.PaymentRuns.Include(x => x.SupplierSummaries).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (run == null) return NotFound();
        if (run.Status != PaymentRunStatus.PaymentOrdered) throw new InvalidOperationException("این پرداخت هنوز به دستور پرداخت تبدیل نشده است.");
        var company = await db.CompanySettings.SingleOrDefaultAsync(x => x.Id == 1) ?? new CompanySettings();
        var path = await pdf.GenerateAndSaveAsync(company, run, run.SupplierSummaries);
        var fileName = Path.GetFileName(path);
        return PhysicalFile(path, "application/pdf", fileName, enableRangeProcessing: true);
    }

    [Authorize(Policy = SecurityPermissions.PaymentDelete)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var run = await db.PaymentRuns.Include(x => x.SupplierSummaries).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (run == null) throw new InvalidOperationException("پرداخت یافت نشد.");
            var isPaymentOrdered = run.Status == PaymentRunStatus.PaymentOrdered;
            if (run.Status != PaymentRunStatus.Approved && !isPaymentOrdered)
                throw new InvalidOperationException("فقط پرداخت تاییدشده یا دستورپرداخت‌شده قابل حذف منطقی است.");
            if (isPaymentOrdered && !User.HasClaim("IsAdmin", "1"))
                throw new InvalidOperationException("حذف دستور پرداخت فقط برای مدیر سامانه مجاز است.");

            if (run.FinancialEffectsAppliedAt.HasValue)
            {
                var keys = await db.PaymentRunInvoices
                    .Where(x => x.PaymentRunId == id && x.AllocatedCurrentAmount > 0)
                    .Select(x => x.PaymentKeyHash).ToListAsync();

                if (keys.Count > 0)
                {
                    var dependent = await db.PaymentRunInvoices
                        .Where(x => !x.PaymentRun!.IsDeleted && x.PaymentRunId > id &&
                                    keys.Contains(x.PaymentKeyHash) && x.PreviousAllocated > 0)
                        .Select(x => new { x.PaymentRunId, x.ReceiptNo }).Take(10).ToListAsync();
                    if (dependent.Count > 0)
                        throw new InvalidOperationException("این پرداخت قبلاً اثر مالی داشته و در محاسبات بعدی مورد استفاده قرار گرفته است؛ ابتدا محاسبات بعدی وابسته را حذف کنید.");
                }

                var initialSupplierIds = run.SupplierSummaries
                    .Where(x => x.InitialClaimAllocatedAmount > 0)
                    .Select(x => x.SupplierId).Distinct().ToList();
                if (initialSupplierIds.Count > 0)
                {
                    var laterInitial = await db.PaymentRunSupplierSummaries
                        .Where(x => !x.PaymentRun!.IsDeleted && x.PaymentRunId > id &&
                                    initialSupplierIds.Contains(x.SupplierId) && x.InitialClaimAllocatedAmount > 0)
                        .Select(x => x.PaymentRunId).Take(10).ToListAsync();
                    if (laterInitial.Count > 0)
                        throw new InvalidOperationException("این پرداخت از مطالبات استقراری استفاده کرده و در پرداخت‌های بعدی همان تامین‌کننده نیز استفاده شده است؛ ابتدا پرداخت‌های بعدی وابسته را حذف کنید.");
                }

                foreach (var summary in run.SupplierSummaries.Where(x => x.InitialClaimAllocatedAmount > 0))
                {
                    var supplier = await db.Suppliers.SingleAsync(x => x.Id == summary.SupplierId);
                    var before = supplier.InitialClaimAmount;
                    supplier.InitialClaimAmount = Math.Round(before + summary.InitialClaimAllocatedAmount, 2);
                    db.SupplierClaimHistories.Add(new SupplierClaimHistory
                    {
                        SupplierId = supplier.Id,
                        ClaimType = SupplierClaimType.Initial,
                        AmountBefore = before,
                        AmountChange = summary.InitialClaimAllocatedAmount,
                        AmountAfter = supplier.InitialClaimAmount,
                        PaymentRunId = run.Id,
                        Reference = $"برگشت مطالبات استقراری بابت حذف منطقی پرداخت شماره {run.Id}",
                        EffectiveDateJalali = run.CalculationDateJalali,
                        UserId = UserId
                    });
                }
            }

            run.IsDeleted = true;
            run.Status = PaymentRunStatus.Cancelled;
            run.DeletedAt = DateTime.UtcNow;
            run.DeletedBy = UserId;
            run.DeleteReason = "حذف منطقی توسط کاربر";
            db.AuditLogs.Add(new AuditLog
            {
                Action = "DELETE_LOGICAL", Entity = "PaymentRun", EntityId = run.Id.ToString(),
                Details = $"پرداخت شماره {run.Id} حذف منطقی شد و مطالبات استقراری تخصیص‌یافته آن برگشت داده شد.", UserId = UserId
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            TempData["Result"] = $"پرداخت شماره {run.Id} حذف منطقی شد؛ سابقه آن حفظ و آثار مالی فعال آن از محاسبات بعدی خارج شد.";
            return RedirectToAction(nameof(History));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(History));
        }
    }

    PaymentWizardState? Load()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PaymentWizardState>(json);
    }

    void Save(PaymentWizardState s) => HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(s));
    static string Normalize(string v) => (v ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک").ToLowerInvariant();

    public class ScoreEditItem { public int RowIndex { get; set; } public int ParameterId { get; set; } public decimal Score { get; set; } }
}
