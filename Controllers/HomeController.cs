using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Controllers;
[Authorize(Policy=SecurityPermissions.DashboardView)]
public class HomeController(AppDbContext db):Controller{
 public async Task<IActionResult> Index(){var activeRuns=db.PaymentRuns.Where(x=>!x.IsDeleted&&(x.Status==PaymentRunStatus.Approved||x.Status==PaymentRunStatus.PaymentOrdered));return View(new HomeVm{
  Name=User.Identity?.Name??"کاربر",Parts=await db.Parts.CountAsync(x=>x.IsActive),Suppliers=await db.Suppliers.CountAsync(x=>x.IsActive),Mappings=await db.SupplierParts.CountAsync(x=>x.IsActive),Parameters=await db.PaymentParameters.CountAsync(x=>x.IsActive),
  ApprovedRuns=await activeRuns.CountAsync(),
  TotalAllocated=await db.PaymentRunInvoices.Where(x=>!x.PaymentRun!.IsDeleted&&(x.PaymentRun.Status==PaymentRunStatus.Approved||x.PaymentRun.Status==PaymentRunStatus.PaymentOrdered)).SumAsync(x=>(decimal?)x.AllocatedAmount)??0
 });}
 public record HomeVm{public string Name{get;init;}="";public int Parts{get;init;}public int Suppliers{get;init;}public int Mappings{get;init;}public int Parameters{get;init;}public int ApprovedRuns{get;init;}public decimal TotalAllocated{get;init;}}
}