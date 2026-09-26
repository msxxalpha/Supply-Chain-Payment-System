using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy=SecurityPermissions.ReportsView)]
public class ReportsController(AppDbContext db):Controller
{
 public async Task<IActionResult> Index()
 {
  var runs=await db.PaymentRuns.AsNoTracking().Include(x=>x.Invoices).OrderByDescending(x=>x.Id).ToListAsync();
  var suppliersMaster=await db.Suppliers.AsNoTracking().ToListAsync();
  var approved=await db.PaymentRunInvoices.AsNoTracking().Where(x=>!x.PaymentRun!.IsDeleted&&(x.PaymentRun.Status==PaymentRunStatus.Approved||x.PaymentRun.Status==PaymentRunStatus.PaymentOrdered))
   .OrderBy(x=>x.PaymentRunId).ToListAsync();

  var latestByKey=approved.GroupBy(x=>x.PaymentKeyHash).Select(g=>g.Last()).ToList();
  var suppliers=new List<SupplierRow>();
  foreach(var sm in suppliersMaster){
   var hist=approved.Where(x=>x.SupplierId==sm.Id).ToList();
   var latest=latestByKey.Where(x=>x.SupplierId==sm.Id).ToList();
   var currentClaims=latest.Sum(x=>x.OriginalDebt);
   var outstanding=latest.Sum(x=>Math.Max(0,x.RemainingDebt-x.AllocatedAmount));
   var paid=hist.Sum(x=>x.AllocatedAmount);
   var open=latest.Count(x=>x.RemainingDebt-x.AllocatedAmount>0);
   var ageRatio=open>0?latest.Where(x=>x.RemainingDebt-x.AllocatedAmount>0).Average(x=>x.ContractSettlementDays>0?(decimal)x.DebtAgeDays/x.ContractSettlementDays:1):0;
   var overdue=latest.Count(x=>x.RemainingDebt-x.AllocatedAmount>0&&x.ContractSettlementDays>0&&x.DebtAgeDays>x.ContractSettlementDays);
   suppliers.Add(new SupplierRow(sm.Id,sm.Title,open,sm.InitialClaimAmount,currentClaims,paid,sm.InitialClaimAmount+outstanding,sm.InitialClaimAmount+currentClaims>0?Math.Clamp(paid/(sm.InitialClaimAmount+currentClaims),0,1):0,ageRatio,overdue,0));
  }
  suppliers=suppliers.Where(x=>x.InitialClaim>0||x.CurrentClaims>0||x.Paid>0||x.Remaining>0).OrderByDescending(x=>x.Remaining).ToList();

  var parts=latestByKey.GroupBy(x=>new{x.PartId,x.PartTitle}).Select(g=>{var hist=approved.Where(x=>x.PartId==g.Key.PartId).ToList();var current=g.ToList();var o=current.Sum(x=>x.OriginalDebt);var a=hist.Sum(x=>x.AllocatedAmount);var rem=current.Sum(x=>Math.Max(0,x.RemainingDebt-x.AllocatedAmount));var open=current.Count(x=>x.RemainingDebt-x.AllocatedAmount>0);var ar=open>0?current.Where(x=>x.RemainingDebt-x.AllocatedAmount>0).Average(x=>x.ContractSettlementDays>0?(decimal)x.DebtAgeDays/x.ContractSettlementDays:1):0;var od=current.Count(x=>x.RemainingDebt-x.AllocatedAmount>0&&x.ContractSettlementDays>0&&x.DebtAgeDays>x.ContractSettlementDays);return new PartRow(g.Key.PartId??0,g.Key.PartTitle,open,o,a,rem,o>0?a/o:0,ar,od,0);}).OrderByDescending(x=>x.Remaining).ToList();

  var supplierParts=latestByKey.GroupBy(x=>new{x.SupplierId,x.SupplierTitle,x.PartId,x.PartTitle}).Select(g=>{var hist=approved.Where(x=>x.SupplierId==g.Key.SupplierId&&x.PartId==g.Key.PartId).ToList();var current=g.ToList();var o=current.Sum(x=>x.OriginalDebt);var a=hist.Sum(x=>x.AllocatedAmount);var rem=current.Sum(x=>Math.Max(0,x.RemainingDebt-x.AllocatedAmount));return new SupplierPartRow(g.Key.SupplierTitle,g.Key.PartTitle,current.Count,o,a,rem,o>0?a/o:0);}).OrderByDescending(x=>x.Remaining).ToList();

  var original= suppliers.Sum(x=>x.InitialClaim+x.CurrentClaims);
  var allocated= suppliers.Sum(x=>x.Paid);
  var remaining=suppliers.Sum(x=>x.Remaining);
  var maxRemainingSupplier=suppliers.Select(x=>x.Remaining).DefaultIfEmpty(1).Max();
  var maxRemainingPart=parts.Select(x=>x.Remaining).DefaultIfEmpty(1).Max();
  suppliers=suppliers.Select(x=>x with{Priority=PriorityScore(x.Remaining,maxRemainingSupplier,x.AgeRatio,x.Coverage,x.Overdue,x.Count)}).ToList();
  parts=parts.Select(x=>x with{Priority=PriorityScore(x.Remaining,maxRemainingPart,x.AgeRatio,x.Coverage,x.Overdue,x.Count)}).ToList();

  var activeRuns=runs.Where(x=>!x.IsDeleted).ToList();var status=activeRuns.GroupBy(x=>x.Status).ToDictionary(g=>g.Key,g=>g.Count());
  var recent=activeRuns.Where(x=>x.Status==PaymentRunStatus.Approved||x.Status==PaymentRunStatus.PaymentOrdered).Take(8).Select(x=>new RunRow(x.Id,x.Title,x.CalculationDateJalali,x.ImportedDebt,x.TotalAllocationBudget,x.Invoices.Count,x.Invoices.Sum(i=>i.AllocatedAmount))).ToList();
  var trend=activeRuns.Where(x=>x.Status==PaymentRunStatus.Approved||x.Status==PaymentRunStatus.PaymentOrdered).OrderBy(x=>x.Id).TakeLast(12).Select(x=>new TrendRow(x.CalculationDateJalali,x.TotalAllocationBudget,x.Invoices.Sum(i=>i.AllocatedAmount))).ToList();

  return View(new ReportsVm(activeRuns.Count,activeRuns.Count(x=>x.Status==PaymentRunStatus.Approved||x.Status==PaymentRunStatus.PaymentOrdered),activeRuns.Count(x=>x.Status==PaymentRunStatus.Cancelled),latestByKey.Count,original,allocated,remaining,original>0?allocated/original:0,suppliers,parts,supplierParts,status,trend,recent));
 }
 static decimal PriorityScore(decimal remaining,decimal maxRemaining,decimal ageRatio,decimal coverage,int overdue,int count){if(remaining<=0)return 0;var debt=maxRemaining>0?remaining/maxRemaining:0;var age=Math.Clamp(ageRatio,0,2)/2;var overdueRate=count>0?(decimal)overdue/count:0;var under=1-Math.Clamp(coverage,0,1);var volume=Math.Min(1,count/20m);return Math.Round(100*(debt*.30m+age*.30m+overdueRate*.20m+under*.15m+volume*.05m),1);}
 public record SupplierRow(int Id,string Title,int Count,decimal InitialClaim,decimal CurrentClaims,decimal Paid,decimal Remaining,decimal Coverage,decimal AgeRatio,int Overdue,decimal Priority);
 public record PartRow(int Id,string Title,int Count,decimal Original,decimal Allocated,decimal Remaining,decimal Coverage,decimal AgeRatio,int Overdue,decimal Priority);
 public record SupplierPartRow(string Supplier,string Part,int Count,decimal Original,decimal Allocated,decimal Remaining,decimal Coverage);
 public record RunRow(int Id,string Title,string Date,decimal Imported,decimal Budget,int Count,decimal Allocated);
 public record TrendRow(string Date,decimal Budget,decimal Allocated);
 public record ReportsVm(int RunCount,int ApprovedRuns,int CancelledRuns,int InvoiceCount,decimal Original,decimal Allocated,decimal Remaining,decimal Coverage,List<SupplierRow> Suppliers,List<PartRow> Parts,List<SupplierPartRow> SupplierParts,Dictionary<PaymentRunStatus,int> Status,List<TrendRow> Trend,List<RunRow> Recent);
}