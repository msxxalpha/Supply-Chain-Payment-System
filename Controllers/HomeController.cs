using Indamin.Payment.Data;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Controllers;
[Authorize]public class HomeController(AppDbContext db):Controller{
 public IActionResult Error()=>View();
 public async Task<IActionResult> Index()=>View(new HomeVm{
  Name=User.Identity?.Name??"کاربر",Parts=await db.Parts.CountAsync(x=>x.IsActive),Suppliers=await db.Suppliers.CountAsync(x=>x.IsActive),
  Mappings=await db.SupplierParts.CountAsync(x=>x.IsActive),Parameters=await db.PaymentParameters.CountAsync(x=>x.IsActive),
  ApprovedRuns=await db.PaymentRuns.CountAsync(x=>x.Status==PaymentRunStatus.Approved),
  TotalAllocated=await db.PaymentRunInvoices.Where(x=>x.PaymentRun!.Status==PaymentRunStatus.Approved).SumAsync(x=>(decimal?)x.AllocatedAmount)??0
 });}
 public record HomeVm{public string Name{get;init;}="";public int Parts{get;init;}public int Suppliers{get;init;}public int Mappings{get;init;}public int Parameters{get;init;}public int ApprovedRuns{get;init;}public decimal TotalAllocated{get;init;}}
}
