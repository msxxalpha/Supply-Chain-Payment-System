using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.DashboardView)]
public class HomeController(FinancialReportingService reports, CompanySettingsService companySettings) : Controller
{
    [AllowAnonymous]
    public async Task<IActionResult> Maintenance()
    {
        var settings = await companySettings.GetAsync();
        if (!settings.MaintenanceMode) return RedirectToAction("Login", "Account");
        return View(settings);
    }

    public async Task<IActionResult> Index()
    {
        var data = await reports.GetDashboardAsync();
        return View(data);
    }
}