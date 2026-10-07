using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.SetupSupplierPriceList)]
public class SupplierPriceListController(AppDbContext db, ExcelService excel) : Controller
{
    static int NormalizePageSize(int value) => value is 50 or 75 or 100 ? value : 25;
    int UserId => int.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : 0;

    public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = 25)
    {
        pageSize = NormalizePageSize(pageSize);
        page = Math.Max(1, page);
        q = (q ?? "").Trim();

        var query = db.Suppliers.AsNoTracking().Where(x => x.IsActive);
        if (q != "")
            query = query.Where(x => x.Code.Contains(q) || x.Title.Contains(q));

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(x => x.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SupplierPriceListSummaryVm(
                x.Id, x.Code, x.Title,
                x.SupplierParts.Count(p => p.IsActive)))
            .ToListAsync();

        return View(new SupplierPriceListIndexVm(rows, total, page, pageSize, q));
    }

    [HttpGet]
    public async Task<IActionResult> Manage(int supplierId, string? q, int page = 1, int pageSize = 25)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == supplierId);
        if (supplier == null) return NotFound();

        pageSize = NormalizePageSize(pageSize);
        page = Math.Max(1, page);
        q = (q ?? "").Trim();

        var mappings = await db.SupplierParts.AsNoTracking()
            .Include(x => x.Part)
            .Where(x => x.SupplierId == supplierId && x.IsActive && x.Part!.IsActive)
            .OrderBy(x => x.Part!.Code)
            .ToListAsync();

        var priceQuery = db.SupplierPriceListItems.AsNoTracking()
            .Include(x => x.SupplierPart).ThenInclude(x => x!.Part)
            .Where(x => x.SupplierPart!.SupplierId == supplierId);

        if (q != "")
            priceQuery = priceQuery.Where(x =>
                x.SupplierPart!.Part!.Code.Contains(q) ||
                x.SupplierPart.Part.Title.Contains(q));

        var total = await priceQuery.CountAsync();
        var priceRows = await priceQuery
            .OrderBy(x => x.SupplierPart!.Part!.Code)
            .ThenByDescending(x => x.ValidFrom)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var usedIds = priceRows.Count == 0
            ? []
            : await db.PaymentRunInvoices.AsNoTracking()
                .Where(x => x.PriceListItemId.HasValue && priceRows.Select(p => p.Id).Contains(x.PriceListItemId.Value))
                .Select(x => x.PriceListItemId!.Value)
                .Distinct()
                .ToHashSetAsync();

        return View(new SupplierPriceListManageVm(
            supplier, mappings, priceRows, usedIds, total, page, pageSize, q));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPrice(int supplierId, int supplierPartId, decimal purchasePrice, string validFrom, string validTo)
    {
        try
        {
            var mapping = await db.SupplierParts.Include(x => x.Supplier)
                .Include(x => x.Part)
                .SingleOrDefaultAsync(x => x.Id == supplierPartId && x.SupplierId == supplierId && x.IsActive);

            if (mapping == null) throw new InvalidOperationException("ارتباط کالا–تامین‌کننده انتخاب‌شده معتبر یا فعال نیست.");
            var from = PersianDateService.Parse(validFrom).Date;
            var to = PersianDateService.Parse(validTo).Date;
            ValidatePrice(purchasePrice, from, to);

            db.SupplierPriceListItems.Add(new SupplierPriceListItem
            {
                SupplierPartId = mapping.Id,
                PurchasePrice = Math.Round(purchasePrice, 2),
                ValidFrom = from,
                ValidTo = to,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await LogAsync("CREATE", "SupplierPriceListItem", supplierPartId.ToString(), $"قیمت {purchasePrice:N2} برای {mapping.Part!.Title} / {mapping.Supplier!.Title}");
            TempData["Result"] = "رکورد فهرست بها ثبت شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Manage), new { supplierId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPrice(long id, decimal purchasePrice, string validFrom, string validTo)
    {
        var row = await db.SupplierPriceListItems.Include(x => x.SupplierPart)
            .ThenInclude(x => x!.Supplier)
            .Include(x => x.SupplierPart).ThenInclude(x => x!.Part)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (row == null) return NotFound();

        var supplierId = row.SupplierPart!.SupplierId;
        try
        {
            if (await IsUsedAsync(id))
                throw new InvalidOperationException("این نرخ قبلاً در محاسبه پرداخت استفاده شده است و قابل ویرایش نیست.");

            var from = PersianDateService.Parse(validFrom).Date;
            var to = PersianDateService.Parse(validTo).Date;
            ValidatePrice(purchasePrice, from, to);

            row.PurchasePrice = Math.Round(purchasePrice, 2);
            row.ValidFrom = from;
            row.ValidTo = to;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await LogAsync("UPDATE", "SupplierPriceListItem", id.ToString(), $"ویرایش نرخ {row.SupplierPart!.Part!.Title} / {row.SupplierPart.Supplier!.Title}");
            TempData["Result"] = "رکورد فهرست بها به‌روزرسانی شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Manage), new { supplierId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePrice(long id)
    {
        var row = await db.SupplierPriceListItems.Include(x => x.SupplierPart).SingleOrDefaultAsync(x => x.Id == id);
        if (row == null) return NotFound();
        var supplierId = row.SupplierPart!.SupplierId;

        try
        {
            if (await IsUsedAsync(id))
                throw new InvalidOperationException("این نرخ قبلاً در محاسبه پرداخت استفاده شده است و قابل حذف نیست.");

            db.SupplierPriceListItems.Remove(row);
            await db.SaveChangesAsync();
            await LogAsync("DELETE", "SupplierPriceListItem", id.ToString(), "حذف رکورد فهرست بها");
            TempData["Result"] = "رکورد فهرست بها حذف شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Manage), new { supplierId });
    }

    [HttpPost, Authorize(Policy = "AdminOnly"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "فایل Excel انتخاب نشده است.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var suppliers = await db.Suppliers.Where(x => x.IsActive)
                .AsNoTracking().ToDictionaryAsync(x => Normalize(x.Code), x => x.Id);
            var parts = await db.Parts.Where(x => x.IsActive)
                .AsNoTracking().ToDictionaryAsync(x => Normalize(x.Code), x => x.Id);

            var rows = excel.ReadSupplierPriceList(
                file.OpenReadStream(), suppliers, parts, PersianDateService.Parse, out var errors);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in rows)
            {
                if (!seen.Add($"{item.SupplierId}:{item.PartId}:{item.ValidFrom:yyyyMMdd}:{item.ValidTo:yyyyMMdd}"))
                    errors.Add($"سطر {item.RowNumber}: رکورد با همین تامین‌کننده، کالا و بازه زمانی در فایل تکراری است.");

                var mapping = await db.SupplierParts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.SupplierId == item.SupplierId && x.PartId == item.PartId && x.IsActive);
                if (mapping == null)
                    errors.Add($"سطر {item.RowNumber}: ارتباط فعال بین تامین‌کننده و کالا در اطلاعات پایه وجود ندارد.");
            }

            if (errors.Count > 0)
            {
                TempData["Error"] = $"ورود فهرست بها انجام نشد. {string.Join(" | ", errors.Distinct())}";
                return RedirectToAction(nameof(Index));
            }

            var supplierIds = rows.Select(x => x.SupplierId).Distinct().ToList();
            var partIds = rows.Select(x => x.PartId).Distinct().ToList();
            var mappingRows = await db.SupplierParts
                .Where(x => supplierIds.Contains(x.SupplierId) && partIds.Contains(x.PartId))
                .ToListAsync();
            var mappings = mappingRows
                .Where(x => x.IsActive)
                .ToDictionary(x => $"{x.SupplierId}:{x.PartId}", x => x.Id);

            foreach (var item in rows)
            {
                if (!mappings.TryGetValue($"{item.SupplierId}:{item.PartId}", out var mappingId))
                    throw new InvalidOperationException($"ارتباط فعال تامین‌کننده و کالا برای سطر {item.RowNumber} پیدا نشد.");

                var current = await db.SupplierPriceListItems.FirstOrDefaultAsync(x =>
                    x.SupplierPartId == mappingId &&
                    x.ValidFrom == item.ValidFrom &&
                    x.ValidTo == item.ValidTo);

                if (current != null)
                {
                    if (await IsUsedAsync(current.Id))
                        throw new InvalidOperationException($"رکورد فهرست بها برای سطر {item.RowNumber} قبلاً در محاسبه استفاده شده است و قابل جایگزینی نیست.");

                    current.PurchasePrice = item.PurchasePrice;
                    current.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    db.SupplierPriceListItems.Add(new SupplierPriceListItem
                    {
                        SupplierPartId = mappingId,
                        PurchasePrice = item.PurchasePrice,
                        ValidFrom = item.ValidFrom,
                        ValidTo = item.ValidTo,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }


            await db.SaveChangesAsync();
            await LogAsync("IMPORT", "SupplierPriceListItem", "EXCEL", $"ورود تجمیعی {rows.Count} ردیف فهرست بها");
            TempData["Result"] = $"{rows.Count:N0} ردیف فهرست بها با موفقیت وارد/به‌روزرسانی شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "ورود فهرست بها انجام نشد: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel()
    {
        var rows = await db.SupplierPriceListItems
            .Include(x => x.SupplierPart).ThenInclude(x => x!.Supplier)
            .Include(x => x.SupplierPart).ThenInclude(x => x!.Part)
            .OrderBy(x => x.SupplierPart!.Supplier!.Code)
            .ThenBy(x => x.SupplierPart.Part!.Code)
            .ThenByDescending(x => x.ValidFrom)
            .ToListAsync();
        return File(excel.SupplierPriceList(rows),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "supplier-price-list.xlsx");
    }

    async Task<bool> IsUsedAsync(long id) =>
        await db.PaymentRunInvoices.AsNoTracking().AnyAsync(x => x.PriceListItemId == id);

    async Task LogAsync(string action, string entity, string id, string details)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action, Entity = entity, EntityId = id, Details = details, UserId = UserId
        });
        await db.SaveChangesAsync();
    }

    static void ValidatePrice(decimal price, DateTime from, DateTime to)
    {
        if (price <= 0) throw new InvalidOperationException("قیمت خرید (فی) باید بزرگ‌تر از صفر باشد.");
        if (to < from) throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.");
    }

    static string Normalize(string s) => (s ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک").ToLowerInvariant();

    public record SupplierPriceListSummaryVm(int SupplierId, string Code, string Title, int LinkedPartCount);
    public record SupplierPriceListIndexVm(List<SupplierPriceListSummaryVm> Rows, int TotalCount, int Page, int PageSize, string Search);
    public record SupplierPriceListManageVm(
        Supplier Supplier, List<SupplierPart> Mappings, List<SupplierPriceListItem> Rows,
        HashSet<long> UsedIds, int TotalCount, int Page, int PageSize, string Search);
}
