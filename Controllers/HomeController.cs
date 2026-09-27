using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.DashboardView)]
public class HomeController(FinancialReportingService reports) : Controller
{
    public async Task<IActionResult> Index()
    {
        var data = await reports.GetDashboardAsync();
        return View(data);
    }
}