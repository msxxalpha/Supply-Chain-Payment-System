using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.SupplierOverview)]
public class SupplierOverviewController(FinancialReportingService reports, AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(int? id)
    {
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Title)
            .Select(x => new SupplierSelectorRow(x.Id, x.Code, x.Title))
            .ToListAsync();

        if (suppliers.Count == 0)
            return View(new SupplierOverviewVm(suppliers, null));

        var selectedId = id.GetValueOrDefault(suppliers[0].Id);
        if (!suppliers.Any(x => x.Id == selectedId))
            selectedId = suppliers[0].Id;

        var data = await reports.GetSupplierAsync(selectedId);
        return View(new SupplierOverviewVm(suppliers, data));
    }

    public record SupplierOverviewVm(List<SupplierSelectorRow> Suppliers, SupplierDashboardData? Data);
    public record SupplierSelectorRow(int Id, string Code, string Title);
}