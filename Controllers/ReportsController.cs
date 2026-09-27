using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.ReportsView)]
public class ReportsController(FinancialReportingService reports) : Controller
{
    public async Task<IActionResult> Index()
    {
        var data = await reports.GetDashboardAsync();
        return View(data);
    }
}