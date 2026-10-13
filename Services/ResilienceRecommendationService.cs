using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Services;
public sealed class ResilienceRecommendationScore
{
 public int SupplierId { get; init; }
 public decimal? SupplyImportance { get; init; }
 public decimal? ResilienceScore { get; init; }
}
/// <summary>Optional non-calculated guidance; it does not change financial payment calculations.</summary>
public sealed class ResilienceRecommendationService(AppDbContext db, SupplierPriorityService priority)
{
 public async Task<List<ResilienceRecommendationScore>> GetScoresAsync()
 {
  var suppliers=await priority.GetAsync();
  var parameters=await db.PaymentParameters.AsNoTracking().Where(p=>p.IsActive&&p.ParticipatesInNonCalculatedRecommendation).Select(p=>new{p.Id,p.MaxScore}).ToListAsync();
  if(parameters.Count==0)return suppliers.Select(s=>new ResilienceRecommendationScore{SupplierId=s.SupplierId}).ToList();
  var ids=parameters.Select(p=>p.Id).ToArray();
  var maxScores=parameters.ToDictionary(p=>p.Id,p=>p.MaxScore);
  var evaluations=await db.SupplierPartEvaluations.AsNoTracking()
   .Where(e=>e.IsActive&&ids.Contains(e.PaymentParameterId)&&e.SupplierPart!=null&&e.SupplierPart.IsActive&&e.SupplierPart.Supplier!=null&&e.SupplierPart.Supplier.IsActive&&!e.SupplierPart.Supplier.IsNonParticipating&&e.SupplierPart.Part!=null&&e.SupplierPart.Part.IsActive)
   .Select(e=>new{e.SupplierPart!.SupplierId,e.PaymentParameterId,e.Score}).ToListAsync();
  var importance=evaluations.Where(e=>maxScores.ContainsKey(e.PaymentParameterId)&&maxScores[e.PaymentParameterId]>0&&e.Score>0&&e.Score<=maxScores[e.PaymentParameterId])
   .GroupBy(e=>e.SupplierId).ToDictionary(g=>g.Key,g=>(decimal?)Math.Round(g.Average(e=>e.Score/maxScores[e.PaymentParameterId])*100m,1));
  var financial=suppliers.ToDictionary(s=>s.SupplierId,s=>Math.Clamp(s.Priority,0m,100m));
  return suppliers.Select(s=>{
   importance.TryGetValue(s.SupplierId,out var supply);
   // Provisional 65/35 weighting must be calibrated against historical outcomes.
   decimal? score=supply.HasValue?Math.Round(financial[s.SupplierId]*.65m+supply.Value*.35m,1):null;
   return new ResilienceRecommendationScore{SupplierId=s.SupplierId,SupplyImportance=supply,ResilienceScore=score};
  }).ToList();
 }
}
