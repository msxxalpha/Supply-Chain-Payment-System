using Indamin.Payment.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize]
public class ReportsController(AppDbContext db):Controller
{
 public async Task<IActionResult> Index()
 {
  var runs=await db.PaymentRuns.AsNoTracking().OrderByDescending(x=>x.Id).ToListAsync();
  var approved=await db.PaymentRunInvoices.AsNoTracking().Where(x=>x.PaymentRun!.Status==PaymentRunStatus.Approved)
   .Select(x=>new InvoiceFact(x.SupplierId,x.SupplierTitle,x.PartId,x.PartTitle,x.OriginalDebt,x.AllocatedAmount,x.DebtAgeDays,x.ContractSettlementDays)).ToListAsync();
  var suppliers=approved.GroupBy(x=>new{x.SupplierId,x.SupplierTitle}).Select(g=>{var o=g.Sum(x=>x.OriginalDebt);var a=g.Sum(x=>x.AllocatedAmount);var rem=Math.Max(0,o-a);var ar=g.Count()==0?0:g.Average(x=>x.ContractSettlementDays>0?(decimal)x.DebtAgeDays/x.ContractSettlementDays:1);var od=g.Count(x=>x.ContractSettlementDays>0&&x.DebtAgeDays>x.ContractSettlementDays);return new SupplierRow(g.Key.SupplierId??0,g.Key.SupplierTitle,g.Count(),o,a,rem,o>0?a/o:0,od,PriorityScore(rem,ar,o>0?a/o:0,od,g.Count()));}).OrderByDescending(x=>x.Priority).ToList();
  var parts=approved.GroupBy(x=>new{x.PartId,x.PartTitle}).Select(g=>{var o=g.Sum(x=>x.OriginalDebt);var a=g.Sum(x=>x.AllocatedAmount);var rem=Math.Max(0,o-a);var ar=g.Count()==0?0:g.Average(x=>x.ContractSettlementDays>0?(decimal)x.DebtAgeDays/x.ContractSettlementDays:1);var od=g.Count(x=>x.ContractSettlementDays>0&&x.DebtAgeDays>x.ContractSettlementDays);return new PartRow(g.Key.PartId??0,g.Key.PartTitle,g.Count(),o,a,rem,o>0?a/o:0,od,PriorityScore(rem,ar,o>0?a/o:0,od,g.Count()));}).OrderByDescending(x=>x.Priority).ToList();
  var original=approved.Sum(x=>x.OriginalDebt);var allocated=approved.Sum(x=>x.AllocatedAmount);var remaining=Math.Max(0,original-allocated);
  var recent=runs.Where(x=>x.Status==PaymentRunStatus.Approved).Take(8).Select(x=>new RunRow(x.Id,x.Title,x.CalculationDateJalali,x.ImportedDebt,x.TotalAllocationBudget,x.Invoices.Count,x.Invoices.Sum(i=>i.AllocatedAmount))).ToList();
  var trend=runs.Where(x=>x.Status==PaymentRunStatus.Approved).OrderBy(x=>x.Id).TakeLast(12).Select(x=>new TrendRow(x.CalculationDateJalali,x.TotalAllocationBudget,x.Invoices.Sum(i=>i.AllocatedAmount))).ToList();
  var status=runs.GroupBy(x=>x.Status).ToDictionary(g=>g.Key,g=>g.Count());
  return View(new ReportsVm(runs.Count,runs.Count(x=>x.Status==PaymentRunStatus.Approved),runs.Count(x=>x.Status==PaymentRunStatus.Cancelled),approved.Count,original,allocated,remaining,original>0?allocated/original:0,suppliers,parts,status,trend,recent));
 }
 static decimal PriorityScore(decimal remaining,decimal ageRatio,decimal coverage,int overdue,int count){if(remaining<=0)return 0;var debt=Math.Min(1,remaining/1000000000m);var age=Math.Clamp(ageRatio,0,2)/2;var overdueRate=count>0?(decimal)overdue/count:0;var under=1-Math.Clamp(coverage,0,1);var volume=Math.Min(1,count/20m);return Math.Round(100*(debt*.30m+age*.30m+overdueRate*.20m+under*.15m+volume*.05m),1);}
 record InvoiceFact(int? SupplierId,string SupplierTitle,int? PartId,string PartTitle,decimal OriginalDebt,decimal AllocatedAmount,int DebtAgeDays,int ContractSettlementDays);
 public record SupplierRow(int Id,string Title,int Count,decimal Original,decimal Allocated,decimal Remaining,decimal Coverage,int Overdue,decimal Priority);
 public record PartRow(int Id,string Title,int Count,decimal Original,decimal Allocated,decimal Remaining,decimal Coverage,int Overdue,decimal Priority);
 public record RunRow(int Id,string Title,string Date,decimal Imported,decimal Budget,int Count,decimal Allocated);
 public record TrendRow(string Date,decimal Budget,decimal Allocated);
 public record ReportsVm(int RunCount,int ApprovedRuns,int CancelledRuns,int InvoiceCount,decimal Original,decimal Allocated,decimal Remaining,decimal Coverage,List<SupplierRow> Suppliers,List<PartRow> Parts,Dictionary<PaymentRunStatus,int> Status,List<TrendRow> Trend,List<RunRow> Recent);
}